using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FluentScrobbler.Models.Proxy;
using FluentScrobbler.Services;
using FluentScrobbler.Services.Proxy;

namespace FluentScrobbler.Views.Proxy
{
    /// <summary>
    /// Раздел "Proxy" на вкладке Account (четвертый раздел). Все состояние хранится в
    /// ProxyService и настройках приложения, поэтому настройки не сбрасываются при
    /// переключении вкладок.
    /// </summary>
    public sealed partial class ProxySection : UserControl
    {
        private bool _syncing;
        private int _lastListVersion = -1;

        public ProxySection()
        {
            this.InitializeComponent();
            this.Loaded += ProxySection_Loaded;
            this.Unloaded += ProxySection_Unloaded;
        }

        private void ProxySection_Loaded(object sender, RoutedEventArgs e)
        {
            // Защита от двойной подписки при повторных показах страницы.
            ProxyService.Instance.StateChanged -= OnProxyStateChanged;
            ProxyService.Instance.StateChanged += OnProxyStateChanged;
            SyncFromService();

            // В режиме Auto список мог быть еще не загружен (например, если загрузка не удалась при старте).
            if (ProxyService.Instance.Mode == ProxyConstants.PROXY_MODE_AUTO &&
                ProxyService.Instance.GetProxyList().Count == 0)
            {
                _ = ProxyService.Instance.EnsureProxyListAsync();
            }
        }

        private void ProxySection_Unloaded(object sender, RoutedEventArgs e)
        {
            ProxyService.Instance.StateChanged -= OnProxyStateChanged;
        }

        private void OnProxyStateChanged()
        {
            _ = DispatcherQueue.TryEnqueue(SyncFromService);
        }

        /// <summary>
        /// Синхронизирует UI с состоянием ProxyService. Вызывается при загрузке раздела,
        /// изменении настроек и любых событиях сервиса.
        /// </summary>
        private void SyncFromService()
        {
            var service = ProxyService.Instance;
            _syncing = true;
            try
            {
                string mode = service.Mode;
                ModeComboBox.SelectedIndex = mode switch
                {
                    ProxyConstants.PROXY_MODE_MANUAL => 1,
                    ProxyConstants.PROXY_MODE_AUTO => 2,
                    _ => 0
                };

                ManualCard.Visibility = mode == ProxyConstants.PROXY_MODE_MANUAL ? Visibility.Visible : Visibility.Collapsed;
                AutoCard.Visibility = mode == ProxyConstants.PROXY_MODE_AUTO ? Visibility.Visible : Visibility.Collapsed;

                ModeDescriptionText.Text = mode switch
                {
                    ProxyConstants.PROXY_MODE_MANUAL =>
                        "Manual mode: traffic goes through the SOCKS5 proxy you specify below.",
                    ProxyConstants.PROXY_MODE_AUTO =>
                        "Auto mode: traffic goes through a server selected from the list below. If the current server becomes unavailable, another server from the list is applied automatically.",
                    _ =>
                        "Disabled (default): the app connects to Last.fm directly, without a proxy."
                };

                var current = service.CurrentProxy;
                string host = current?.Host ?? string.Empty;
                string port = current is null ? string.Empty : current.Port.ToString();

                // Не затираем текст, который пользователь прямо сейчас редактирует.
                if (ManualHostTextBox.FocusState == FocusState.Unfocused) ManualHostTextBox.Text = host;
                if (ManualPortTextBox.FocusState == FocusState.Unfocused) ManualPortTextBox.Text = port;
                AutoHostTextBox.Text = host;
                AutoPortTextBox.Text = port;

                var list = service.GetProxyList();
                int version = service.ProxyListVersion;
                if (version != _lastListVersion)
                {
                    _lastListVersion = version;
                    ProxyListListBox.ItemsSource = list;
                }
                ProxyListStatusText.Text = service.ListStatusText;

                int selectedIndex = -1;
                if (current is not null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].Host.Equals(current.Host, StringComparison.OrdinalIgnoreCase) &&
                            list[i].Port == current.Port)
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }
                ProxyListListBox.SelectedIndex = selectedIndex;

                string status = BuildStatusText(service, current);
                ManualStatusText.Text = status;
                AutoStatusText.Text = status;
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>Формирует текст статуса: адрес текущего прокси, состояние подключения и время последнего пинга.</summary>
        private static string BuildStatusText(ProxyService service, ProxyInfo? current)
        {
            if (current is null)
            {
                return "No proxy server is currently configured.";
            }

            string state = service.LastPingOk switch
            {
                true => "Connected",
                false => "Unreachable",
                null => "Not checked yet"
            };

            string lastPing = service.LastPingTime is { } time
                ? time.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
                : "never";

            string text = $"{current.Host}:{current.Port} ({current.Type}) - status: {state}, last ping: {lastPing}";
            string note = service.StatusNote;
            return string.IsNullOrEmpty(note) ? text : text + Environment.NewLine + note;
        }

        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            if (ModeComboBox.SelectedItem is not ComboBoxItem { Tag: string mode }) return;

            ProxyService.Instance.SetMode(mode);
            SyncFromService();
        }

        private void ManualApplyButton_Click(object sender, RoutedEventArgs e)
        {
            var host = ManualHostTextBox.Text?.Trim() ?? string.Empty;
            if (!int.TryParse(ManualPortTextBox.Text?.Trim(), out int port))
            {
                ManualStatusText.Text = "Enter a valid port (1-65535).";
                return;
            }

            if (!ProxyService.Instance.TryApplyProxy(host, port, out string error))
            {
                ManualStatusText.Text = error;
                return;
            }

            SyncFromService();
        }

        private async void ManualPingButton_Click(object sender, RoutedEventArgs e)
        {
            await PingCurrentProxyAsync();
        }

        private async void AutoPingButton_Click(object sender, RoutedEventArgs e)
        {
            await PingCurrentProxyAsync();
        }

        private async Task PingCurrentProxyAsync()
        {
            ManualPingButton.IsEnabled = false;
            AutoPingButton.IsEnabled = false;
            try
            {
                await ProxyService.Instance.PingCurrentAsync();
            }
            catch (Exception ex)
            {
                LogService.LogError("[Proxy UI] Ping failed", ex);
            }
            finally
            {
                ManualPingButton.IsEnabled = true;
                AutoPingButton.IsEnabled = true;
                SyncFromService();
            }
        }

        private void ProxyListListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            if (ProxyListListBox.SelectedItem is not ProxyInfo proxy) return;

            // Выбор сервера из списка сразу применяет прокси (кнопка Apply не нужна)
            // и запускает ping-проверку.
            ProxyService.Instance.SelectProxy(proxy);
            SyncFromService();
        }

        private async void RefreshListButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshListButton.IsEnabled = false;
            RefreshListButton.Content = "Loading...";
            try
            {
                await ProxyService.Instance.LoadProxyListAsync();
            }
            catch (Exception ex)
            {
                LogService.LogError("[Proxy UI] Failed to refresh the proxy list", ex);
            }
            finally
            {
                RefreshListButton.IsEnabled = true;
                RefreshListButton.Content = "Refresh";
                SyncFromService();
            }
        }
    }
}


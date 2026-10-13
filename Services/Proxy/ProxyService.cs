using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentScrobbler.Models.Proxy;

namespace FluentScrobbler.Services.Proxy
{
    /// <summary>
    /// Единая точка интеграции прокси-функционала форка (для стран, где Last.fm заблокирован).
    /// Отвечает за: хранение настроек режима и текущего прокси-сервера, SOCKS5-туннель для
    /// общего HttpClient, загрузку удаленного списка серверов, ping-проверку текущего сервера
    /// (каждый час) и аварийное переключение на доступный сервер из списка.
    /// Код намеренно вынесен в отдельную папку Services/Proxy для легкого переноса
    /// в новые версии приложения FluentScrobbler.
    /// </summary>
    public sealed class ProxyService
    {
        private static readonly Lazy<ProxyService> LazyInstance = new(() => new ProxyService());

        /// <summary>
        /// Глобальный экземпляр сервиса прокси.
        /// </summary>
        public static ProxyService Instance => LazyInstance.Value;

        private readonly object _lock = new();
        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private readonly HttpClient _remoteListClient;
        private readonly Timer _hourlyTimer;

        private string _mode;
        private string _configuredAddress;
        private ProxyInfo? _currentProxy;
        private readonly List<ProxyInfo> _proxyList = new();
        private int _proxyListVersion;
        private bool _proxyListLoaded;
        private string _listStatusText = "The proxy list has not been loaded yet.";
        private bool? _lastPingOk;
        private DateTimeOffset? _lastPingTime;
        private string _statusNote = string.Empty;

        /// <summary>
        /// Общий HttpClient, который направляет запросы через текущий SOCKS5 прокси-сервер.
        /// Если прокси отключен - запросы идут напрямую. Текущий прокси читается при каждом
        /// новом соединении, поэтому смена прокси применяется без перезапуска приложения.
        /// </summary>
        public HttpClient HttpClient { get; }

        /// <summary>
        /// Срабатывает при любом изменении состояния прокси (режим, сервер, статус, список).
        /// Может вызываться из фонового потока - подписчик должен сам перейти на UI-поток.
        /// </summary>
        public event Action? StateChanged;

        private ProxyService()
        {
            _mode = NormalizeMode(SettingsService.GetSetting(ProxyConstants.SETTING_PROXY_MODE));
            _configuredAddress = SettingsService.GetSetting(ProxyConstants.SETTING_PROXY_ADDRESS) ?? string.Empty;
            _currentProxy = _mode == ProxyConstants.PROXY_MODE_DISABLED ? null : ParseAddress(_configuredAddress);

            _remoteListClient = HttpClientHelper.CreateRemoteListClient();
            _remoteListClient.Timeout = TimeSpan.FromSeconds(30);

            var handler = new SocketsHttpHandler
            {
                ConnectCallback = ConnectCallbackAsync,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                ConnectTimeout = TimeSpan.FromSeconds(10)
            };
            HttpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

            // Ежечасная проверка текущего прокси-сервера. Если он недоступен -
            // применяется другой сервер из удаленного списка.
            _hourlyTimer = new Timer(
                _ => HourlyCheckSafe(),
                null,
                TimeSpan.FromMilliseconds(ProxyConstants.PING_INTERVAL_MS),
                TimeSpan.FromMilliseconds(ProxyConstants.PING_INTERVAL_MS));
        }

        /// <summary>Текущий режим: Disabled (по умолчанию) / Manual / Auto.</summary>
        public string Mode
        {
            get { lock (_lock) { return _mode; } }
        }

        /// <summary>
        /// Текущий примененный прокси-сервер, либо null если прокси отключен или не настроен.
        /// </summary>
        public ProxyInfo? CurrentProxy
        {
            get { lock (_lock) { return _currentProxy; } }
        }

        /// <summary>Последний результат ping-проверки текущего сервера: true/false, null - еще не проверялся.</summary>
        public bool? LastPingOk
        {
            get { lock (_lock) { return _lastPingOk; } }
        }

        /// <summary>Время последнего ping-проверки текущего сервера (null - еще не проверялся).</summary>
        public DateTimeOffset? LastPingTime
        {
            get { lock (_lock) { return _lastPingTime; } }
        }

        /// <summary>Дополнительная заметка о состоянии (например, о переключении на резервный сервер).</summary>
        public string StatusNote
        {
            get { lock (_lock) { return _statusNote; } }
        }

        /// <summary>Статус загрузки удаленного списка прокси-серверов.</summary>
        public string ListStatusText
        {
            get { lock (_lock) { return _listStatusText; } }
        }

        /// <summary>Версия (номер обновления) текущего загруженного списка серверов.</summary>
        public int ProxyListVersion
        {
            get { lock (_lock) { return _proxyListVersion; } }
        }

        /// <summary>Список серверов из удаленного файла (копия для безопасного чтения).</summary>
        public List<ProxyInfo> GetProxyList()
        {
            lock (_lock)
            {
                return _proxyList.ToList();
            }
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }


        /// <summary>
        /// Устанавливает режим работы прокси и сохраняет его в настройках.
        /// В режиме Auto при отсутствии выбранного сервера автоматически загружается список
        /// и выбирается первый доступный сервер.
        /// </summary>
        public void SetMode(string mode)
        {
            mode = NormalizeMode(mode);

            lock (_lock)
            {
                if (_mode == mode) return;
                _mode = mode;
                _currentProxy = mode == ProxyConstants.PROXY_MODE_DISABLED ? null : ParseAddress(_configuredAddress);
                _lastPingOk = null;
                _lastPingTime = null;
                _statusNote = string.Empty;
            }

            SettingsService.SetSetting(ProxyConstants.SETTING_PROXY_MODE, mode);
            RaiseStateChanged();

            if (mode == ProxyConstants.PROXY_MODE_AUTO)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await EnsureProxyListAsync().ConfigureAwait(false);
                        if (CurrentProxy is null)
                        {
                            await EnsureAutoSelectionAsync().ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("[Proxy] Failed to initialize Auto mode", ex);
                    }
                });
            }
        }

        /// <summary>
        /// Применяет прокси-сервер, заданный вручную (режим Manual). Возвращает false и текст
        /// ошибки, если введенные данные некорректны.
        /// </summary>
        public bool TryApplyProxy(string host, int port, out string error)
        {
            error = string.Empty;

            if (Mode == ProxyConstants.PROXY_MODE_DISABLED)
            {
                error = "Proxy is disabled. Select the \"Manual\" proxy mode first.";
                return false;
            }

            host = (host ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(host))
            {
                error = "Enter the proxy host (host or IP address).";
                return false;
            }

            if (port < 1 || port > 65535)
            {
                error = "Enter a valid port (1-65535).";
                return false;
            }

            ApplyProxy(host, port);
            _ = PingCurrentAsync();
            return true;
        }

        /// <summary>
        /// Выбирает прокси-сервер из списка (режим Auto): применяет его сразу, сохраняет в
        /// настройках и запускает ping-проверку.
        /// </summary>
        public void SelectProxy(ProxyInfo proxy)
        {
            if (proxy is null) return;
            if (Mode == ProxyConstants.PROXY_MODE_DISABLED) return;

            ApplyProxy(proxy.Host, proxy.Port);
            _ = PingCurrentAsync();
        }

        /// <summary>Сохраняет текущий прокси-сервер в настройках и сбрасывает результат последнего ping.</summary>
        private void ApplyProxy(string host, int port)
        {
            lock (_lock)
            {
                _configuredAddress = $"{host}:{port}";
                _currentProxy = new ProxyInfo
                {
                    Host = host,
                    Port = port,
                    Type = ProxyConstants.PROXY_TYPE_SOCKS5
                };
                _lastPingOk = null;
                _lastPingTime = null;
                _statusNote = string.Empty;
            }

            SettingsService.SetSetting(ProxyConstants.SETTING_PROXY_ADDRESS, $"{host}:{port}");
            RaiseStateChanged();
        }

        /// <summary>
        /// Загружает удаленный список SOCKS5 прокси-серверов (файл SOCKS5.txt по ссылке из
        /// ProxyConstants.PROXY_LIST_URL) на устройство пользователя и парсит его.
        /// </summary>
        public async Task<bool> LoadProxyListAsync()
        {
            if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false))
            {
                return false;
            }

            try
            {
                return await LoadProxyListUnsafeAsync().ConfigureAwait(false);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// Загружает список серверов, если он еще не был загружен.
        /// </summary>
        public async Task<bool> EnsureProxyListAsync()
        {
            if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false))
            {
                return false;
            }

            try
            {
                return await EnsureProxyListUnsafeAsync().ConfigureAwait(false);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>Версия метода без захвата _operationLock (вызывается под уже захваченной блокировкой).</summary>
        private async Task<bool> EnsureProxyListUnsafeAsync()
        {
            lock (_lock)
            {
                if (_proxyListLoaded && _proxyList.Count > 0) return true;
            }
            return await LoadProxyListUnsafeAsync().ConfigureAwait(false);
        }

        /// <summary>Версия метода без захвата _operationLock (вызывается под уже захваченной блокировкой).</summary>
        private async Task<bool> LoadProxyListUnsafeAsync()
        {
            SetListStatus("Loading the proxy list...");

            try
            {
                using var response = await _remoteListClient.GetAsync(
                    ProxyConstants.PROXY_LIST_URL,
                    HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var parsed = new List<ProxyInfo>();
                string? line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
                {
                    if (TryParseProxyLine(line, out var proxy) && proxy is not null)
                    {
                        parsed.Add(proxy);
                    }
                }

                if (parsed.Count == 0)
                {
                    throw new InvalidDataException("No valid SOCKS5 servers found in the proxy list.");
                }

                lock (_lock)
                {
                    _proxyList.Clear();
                    _proxyList.AddRange(parsed);
                    _proxyListLoaded = true;
                    _proxyListVersion++;
                }

                SetListStatus($"Loaded {parsed.Count} proxy servers.");
                RaiseStateChanged();
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogError("[Proxy] Failed to download the proxy list", ex);
                SetListStatus("Failed to load the proxy list. Press Refresh to try again.");
                RaiseStateChanged();
                return false;
            }
        }

        private void SetListStatus(string status)
        {
            lock (_lock)
            {
                _listStatusText = status;
            }
        }

        /// <summary>
        /// Парсит строку удаленного списка. Формат: socks5://host:port #country
        /// "#country" (перед ним пробел) - информативный код страны, который игнорируется
        /// при подключении, но отображается в интерфейсе.
        /// </summary>
        public static bool TryParseProxyLine(string line, out ProxyInfo? proxy)
        {
            proxy = null;

            if (string.IsNullOrWhiteSpace(line)) return false;

            var value = line.Trim();

            // Отделяем информативный код страны: "#nl"
            string countryCode = string.Empty;
            int hashIndex = value.IndexOf('#');
            if (hashIndex >= 0)
            {
                countryCode = value[(hashIndex + 1)..].Trim().TrimStart('#').Trim();
                value = value[..hashIndex].Trim();
            }

            // Отделяем схему: поддерживается только socks5://
            int schemeIndex = value.IndexOf("://", StringComparison.OrdinalIgnoreCase);
            if (schemeIndex >= 0)
            {
                string scheme = value[..schemeIndex];
                if (!scheme.Equals("socks5", StringComparison.OrdinalIgnoreCase)) return false;
                value = value[(schemeIndex + 3)..];
            }

            // Убираем учетные данные вида user:pass@ (если вдруг присутствуют)
            int atIndex = value.LastIndexOf('@');
            if (atIndex >= 0)
            {
                value = value[(atIndex + 1)..];
            }

            value = value.Trim();

            // Разделяем на host и порт (порт - последнее значение после ":")
            int colonIndex = value.LastIndexOf(':');
            if (colonIndex <= 0 || colonIndex == value.Length - 1) return false;

            string host = value[..colonIndex].Trim();
            string portText = value[(colonIndex + 1)..].Trim();

            // Поддержка IPv6 вида [::1]:8080
            if (host.StartsWith('[') && host.EndsWith(']'))
            {
                host = host[1..^1];
            }

            if (string.IsNullOrEmpty(host)) return false;
            if (host.Any(char.IsWhiteSpace)) return false;
            if (!int.TryParse(portText, out int port) || port < 1 || port > 65535) return false;

            proxy = new ProxyInfo
            {
                Host = host,
                Port = port,
                Type = ProxyConstants.PROXY_TYPE_SOCKS5,
                CountryCode = countryCode
            };
            return true;
        }

        /// <summary>
        /// Выполняет ping-проверку текущего прокси-сервера (SOCKS5 handshake) и обновляет
        /// статус подключения и время последнего пинга.
        /// </summary>
        public async Task<bool> PingCurrentAsync()
        {
            var proxy = CurrentProxy;
            if (proxy is null) return false;

            if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false))
            {
                return false;
            }

            try
            {
                bool reachable = await PingSocks5Async(proxy).ConfigureAwait(false);
                RecordPing(reachable);
                return reachable;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private void RecordPing(bool reachable)
        {
            lock (_lock)
            {
                _lastPingOk = reachable;
                _lastPingTime = DateTimeOffset.Now;
            }
            RaiseStateChanged();
        }

        /// <summary>
        /// Ежечасная проверка: пингуем текущий прокси-сервер; если он недоступен - применяем
        /// другой сервер из удаленного списка (требование форка).
        /// </summary>
        private void HourlyCheckSafe()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await HourlyCheckAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogService.LogError("[Proxy] Hourly proxy check failed", ex);
                }
            });
        }

        private async Task HourlyCheckAsync()
        {
            var mode = Mode;
            if (mode == ProxyConstants.PROXY_MODE_DISABLED) return;

            if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                return; // другая операция уже выполняется - проверим в следующий час
            }

            try
            {
                var current = CurrentProxy;
                if (current is null)
                {
                    // В режиме Auto еще не выбран сервер - выбираем первый доступный.
                    if (mode == ProxyConstants.PROXY_MODE_AUTO)
                    {
                        await EnsureProxyListUnsafeAsync().ConfigureAwait(false);
                        await SelectFirstReachableUnsafeAsync().ConfigureAwait(false);
                    }
                    return;
                }

                bool reachable = await PingSocks5Async(current).ConfigureAwait(false);
                RecordPing(reachable);
                if (reachable)
                {
                    lock (_lock) { _statusNote = string.Empty; }
                    RaiseStateChanged();
                    return;
                }

                // Текущий сервер недоступен - ищем замену в удаленном списке.
                lock (_lock)
                {
                    _statusNote = $"Proxy {current.Host}:{current.Port} is unreachable. Looking for an available server in the proxy list...";
                }
                RaiseStateChanged();

                if (!await EnsureProxyListUnsafeAsync().ConfigureAwait(false))
                {
                    lock (_lock)
                    {
                        _statusNote = $"Proxy {current.Host}:{current.Port} is unreachable and the proxy list could not be downloaded. Will retry in an hour.";
                    }
                    RaiseStateChanged();
                    return;
                }

                var candidates = GetProxyList()
                    .Where(p => !(p.Host.Equals(current.Host, StringComparison.OrdinalIgnoreCase) && p.Port == current.Port))
                    .ToList();

                int attempts = 0;
                foreach (var candidate in candidates)
                {
                    if (++attempts > ProxyConstants.FALLBACK_MAX_ATTEMPTS) break;

                    if (await PingSocks5Async(candidate).ConfigureAwait(false))
                    {
                        ApplyProxy(candidate.Host, candidate.Port);
                        RecordPing(true);
                        lock (_lock)
                        {
                            _statusNote = $"Proxy {current.Host}:{current.Port} was unreachable - switched to {candidate.Host}:{candidate.Port} from the proxy list.";
                        }
                        RaiseStateChanged();
                        return;
                    }
                }

                lock (_lock)
                {
                    _statusNote = $"Proxy {current.Host}:{current.Port} is unreachable and no available server was found in the proxy list. Will retry in an hour.";
                }
                RaiseStateChanged();
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// Гарантирует, что в режиме Auto выбран прокси-сервер: загружает список (при
        /// необходимости) и применяет первый доступный сервер.
        /// </summary>
        public async Task EnsureAutoSelectionAsync()
        {
            if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                if (CurrentProxy != null) return;
                await EnsureProxyListUnsafeAsync().ConfigureAwait(false);
                await SelectFirstReachableUnsafeAsync().ConfigureAwait(false);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>Выбирает первый доступный сервер из загруженного списка. Вызывается под захваченным _operationLock.</summary>
        private async Task SelectFirstReachableUnsafeAsync()
        {
            var list = GetProxyList();
            if (list.Count == 0)
            {
                lock (_lock) { _statusNote = "The proxy list is empty. Press Refresh to load it."; }
                RaiseStateChanged();
                return;
            }

            int attempts = 0;
            foreach (var candidate in list)
            {
                if (++attempts > ProxyConstants.AUTO_SELECT_MAX_ATTEMPTS) break;

                if (await PingSocks5Async(candidate).ConfigureAwait(false))
                {
                    ApplyProxy(candidate.Host, candidate.Port);
                    RecordPing(true);
                    lock (_lock)
                    {
                        _statusNote = $"Auto-selected {candidate.Host}:{candidate.Port} from the proxy list.";
                    }
                    RaiseStateChanged();
                    return;
                }
            }

            lock (_lock)
            {
                _statusNote = "None of the proxy servers in the list responded. Will retry in an hour.";
            }
            RaiseStateChanged();
        }

        /// <summary>
        /// Ping-проверка SOCKS5: устанавливаем TCP-соединение и выполняем SOCKS5 greeting
        /// (05 01 00), ожидая успешный ответ (05 00).
        /// </summary>
        private static async Task<bool> PingSocks5Async(ProxyInfo proxy)
        {
            try
            {
                using var cts = new CancellationTokenSource(ProxyConstants.PING_TIMEOUT_MS);
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(proxy.Host, proxy.Port, cts.Token).ConfigureAwait(false);

                var stream = tcp.GetStream();
                await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, cts.Token).ConfigureAwait(false);

                var reply = new byte[2];
                await stream.ReadExactlyAsync(reply, cts.Token).ConfigureAwait(false);
                return reply[0] == 0x05 && reply[1] == 0x00;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (Exception ex)
            {
                LogService.LogError("[Proxy] Ping failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Callback соединения SocketsHttpHandler: если прокси включен - открываем SOCKS5-туннель
        /// до целевого хоста, иначе - прямое соединение. Текущий прокси читается на каждое новое
        /// соединение, поэтому смена прокси применяется сразу.
        /// </summary>
        private async ValueTask<Stream> ConnectCallbackAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var proxy = CurrentProxy;
            if (proxy is null)
            {
                var socket = await ConnectSocketAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }

            return await Socks5Tunnel.ConnectAsync(proxy.Host, proxy.Port, context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Установка TCP-соединения с асинхронным разрешением DNS и перебором адресов хоста.</summary>
        internal static async Task<Socket> ConnectSocketAsync(string host, int port, CancellationToken cancellationToken)
        {
            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new HttpRequestException($"DNS resolution failed for {host}.", ex);
            }

            if (addresses.Length == 0)
            {
                throw new HttpRequestException($"No DNS addresses found for {host}.");
            }

            Exception? lastError = null;
            foreach (var address in addresses)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
                    return socket;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    socket.Dispose();
                }
            }

            throw new HttpRequestException($"Could not connect to {host}:{port}.", lastError);
        }

        /// <summary>
        /// Инициализация при запуске приложения: в режиме Auto загружает список серверов и
        /// выбирает первый доступный, если сервер еще не выбран.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                if (Mode == ProxyConstants.PROXY_MODE_AUTO)
                {
                    await EnsureProxyListAsync().ConfigureAwait(false);
                    if (CurrentProxy is null)
                    {
                        await EnsureAutoSelectionAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[Proxy] Initialization failed", ex);
            }
        }

        /// <summary>
        /// Приводит значение настройки к одному из поддерживаемых режимов.
        /// Значение по умолчанию (а также устаревшее "Off") - Disabled.
        /// </summary>
        private static string NormalizeMode(string? mode)
        {
            if (string.Equals(mode, ProxyConstants.PROXY_MODE_MANUAL, StringComparison.OrdinalIgnoreCase))
            {
                return ProxyConstants.PROXY_MODE_MANUAL;
            }

            if (string.Equals(mode, ProxyConstants.PROXY_MODE_AUTO, StringComparison.OrdinalIgnoreCase))
            {
                return ProxyConstants.PROXY_MODE_AUTO;
            }

            return ProxyConstants.PROXY_MODE_DISABLED;
        }

        /// <summary>Разбирает адрес из настройки вида "host:port".</summary>
        private static ProxyInfo? ParseAddress(string? address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            var value = address.Trim();
            int colonIndex = value.LastIndexOf(':');
            if (colonIndex <= 0 || colonIndex == value.Length - 1) return null;

            string host = value[..colonIndex].Trim();
            string portText = value[(colonIndex + 1)..].Trim();

            if (string.IsNullOrEmpty(host)) return null;
            if (!int.TryParse(portText, out int port) || port < 1 || port > 65535) return null;

            return new ProxyInfo
            {
                Host = host,
                Port = port,
                Type = ProxyConstants.PROXY_TYPE_SOCKS5
            };
        }
    }
}







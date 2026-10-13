namespace FluentScrobbler.Services.Proxy
{
    /// <summary>
    /// Константы для работы с прокси-серверами (форк для стран, где Last.fm заблокирован).
    /// Вынесены отдельно, чтобы код прокси легко переносился в новые версии FluentScrobbler.
    /// </summary>
    public static class ProxyConstants
    {
        /// <summary>
        /// Ключ настройки с адресом текущего примененного прокси-сервера (формат "host:port").
        /// </summary>
        public const string SETTING_PROXY_ADDRESS = "ProxyAddress";

        /// <summary>
        /// Ключ настройки режима работы прокси (Disabled / Manual / Auto).
        /// </summary>
        public const string SETTING_PROXY_MODE = "ProxyMode";

        /// <summary>
        /// Режим: прокси отключен. Значение по умолчанию для приложения.
        /// </summary>
        public const string PROXY_MODE_DISABLED = "Disabled";

        /// <summary>
        /// Режим: прокси настраивается вручную (Host и Port задаются пользователем).
        /// </summary>
        public const string PROXY_MODE_MANUAL = "Manual";

        /// <summary>
        /// Режим: прокси-сервера получают из удаленного списка SOCKS5.txt.
        /// </summary>
        public const string PROXY_MODE_AUTO = "Auto";

        /// <summary>
        /// URL удаленного текстового файла со списком SOCKS5 прокси-серверов.
        /// Формат каждой строки: socks5://host:port #country
        /// </summary>
        public const string PROXY_LIST_URL = "https://raw.githubusercontent.com/HankNovic/ProxyClean/refs/heads/main/SOCKS5.txt";

        /// <summary>
        /// Интервал автоматической проверки текущего прокси-сервера: 1 час (в миллисекундах).
        /// </summary>
        public const int PING_INTERVAL_MS = 60 * 60 * 1000;

        /// <summary>
        /// Таймаут ping-проверки SOCKS5 прокси-сервера (в миллисекундах).
        /// </summary>
        public const int PING_TIMEOUT_MS = 5000;

        /// <summary>
        /// Максимальное количество серверов из списка, проверяемых при аварийном переключении
        /// на резервный прокси-сервер.
        /// </summary>
        public const int FALLBACK_MAX_ATTEMPTS = 20;

        /// <summary>
        /// Максимальное количество серверов из списка, проверяемых при первичном автовыборе в режиме Auto.
        /// </summary>
        public const int AUTO_SELECT_MAX_ATTEMPTS = 10;

        /// <summary>
        /// Тип прокси-серверов, поддерживаемых приложением.
        /// </summary>
        public const string PROXY_TYPE_SOCKS5 = "SOCKS5";
    }
}

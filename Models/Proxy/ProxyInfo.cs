using System.Text.Json.Serialization;

namespace FluentScrobbler.Models.Proxy
{
    public class ProxyInfo
    {
        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "SOCKS5";

        /// <summary>
        /// Код страны из комментария удаленного списка (например, "nl" для строки "socks5://host:port #nl").
        /// Носит исключительно информативный характер и не участвует в подключении.
        /// </summary>
        [JsonPropertyName("country")]
        public string CountryCode { get; set; } = string.Empty;

        /// <summary>
        /// Адрес прокси-сервера в формате "host:port".
        /// </summary>
        [JsonIgnore]
        public string HostPort => $"{Host}:{Port}";

        /// <summary>
        /// Строка для отображения в списке: "host:port #country".
        /// </summary>
        [JsonIgnore]
        public string DisplayText => string.IsNullOrEmpty(CountryCode) ? HostPort : $"{HostPort} #{CountryCode}";

        public override string ToString() => DisplayText;
    }
}

using System; 
using System.Net.Http; 

namespace FluentScrobbler.Services 
{
    /// <summary>
    /// Фабрика HttpClient для служб, которые не должны зависеть от пользовательского прокси.
    /// </summary>
    public static class HttpClientHelper 
    {
        /// <summary>
        /// Создает HttpClient для загрузки удаленных списков прокси-серверов.
        /// Профиль HTTP для этих запросов настраивается отдельно и не включает пользовательский прокси.
        /// </summary>
        public static HttpClient CreateRemoteListClient() 
        {
            var handler = new HttpClientHandler(); 

            return new HttpClient(handler); 
        }

        /// <summary>
        /// Создает обычный HttpClient. Профиль HTTP настраивается вызывающим кодом.
        /// </summary>
        public static HttpClient CreateDefaultClient() 
        {
            return new HttpClient(new HttpClientHandler()); 
        }
    }
}

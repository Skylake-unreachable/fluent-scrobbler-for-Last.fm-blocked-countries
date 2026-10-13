using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FluentScrobbler.Services.Proxy
{
    /// <summary>
    /// Реализация SOCKS5-туннеля для HttpClient: устанавливает TCP-соединение с SOCKS5-сервером,
    /// выполняет handshake и команду CONNECT до целевого хоста, после чего возвращает поток
    /// NetworkStream, через который прозрачно проходит HTTPS-трафик приложения.
    /// </summary>
    internal static class Socks5Tunnel
    {
        private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Открывает SOCKS5-туннель от прокси-сервера (proxyHost:proxyPort) до целевого адреса.
        /// </summary>
        public static async ValueTask<Stream> ConnectAsync(
            string proxyHost,
            int proxyPort,
            DnsEndPoint destination,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(HandshakeTimeout);
            var token = timeoutCts.Token;

            Socket? socket = null;
            try
            {
                socket = await ProxyService.ConnectSocketAsync(proxyHost, proxyPort, token).ConfigureAwait(false);

                // 1. SOCKS5 greeting: VER=5, NMETHODS=1, METHOD=0 (без аутентификации).
                await SendAllAsync(socket, new byte[] { 0x05, 0x01, 0x00 }, token).ConfigureAwait(false);

                var greetingReply = new byte[2];
                await ReceiveExactAsync(socket, greetingReply, token).ConfigureAwait(false);
                if (greetingReply[0] != 0x05 || greetingReply[1] != 0x00)
                {
                    throw new IOException($"SOCKS5 proxy {proxyHost}:{proxyPort} rejected the handshake (reply {greetingReply[1]:X2}).");
                }

                // 2. Команда CONNECT до целевого хоста.
                var connectRequest = BuildConnectRequest(destination.Host, destination.Port);
                await SendAllAsync(socket, connectRequest, token).ConfigureAwait(false);

                var header = new byte[4];
                await ReceiveExactAsync(socket, header, token).ConfigureAwait(false);
                if (header[1] != 0x00)
                {
                    throw new IOException($"SOCKS5 proxy {proxyHost}:{proxyPort} could not connect to {destination.Host}:{destination.Port} (reply code {header[1]}).");
                }

                // 3. Пропускаем адрес и порт, назначенные прокси-сервером (BND.ADDR, BND.PORT).
                await SkipBoundAddressAsync(socket, header[3], token).ConfigureAwait(false);

                var stream = new NetworkStream(socket, ownsSocket: true);
                socket = null; // поток забирает владение сокетом
                return stream;
            }
            finally
            {
                socket?.Dispose();
            }
        }

        /// <summary>Собирает SOCKS5-запрос CONNECT для адреса назначения (IPv4/IPv6/домен).</summary>
        private static byte[] BuildConnectRequest(string host, int port)
        {
            byte addressType;
            byte[] addressBytes;

            if (IPAddress.TryParse(host, out var ip))
            {
                addressBytes = ip.GetAddressBytes();
                addressType = ip.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x04 : (byte)0x01;
            }
            else
            {
                string asciiHost = host;
                try
                {
                    asciiHost = new IdnMapping().GetAscii(host);
                }
                catch
                {
                    // если имя уже в ASCII/Punycode - используем как есть
                }

                addressBytes = Encoding.ASCII.GetBytes(asciiHost);
                if (addressBytes.Length == 0 || addressBytes.Length > 255)
                {
                    throw new IOException($"Destination host name is invalid: {host}");
                }
                addressType = 0x03;
            }

            using var ms = new MemoryStream();
            ms.WriteByte(0x05); // VER
            ms.WriteByte(0x01); // CMD = CONNECT
            ms.WriteByte(0x00); // RSV
            ms.WriteByte(addressType);
            if (addressType == 0x03)
            {
                ms.WriteByte((byte)addressBytes.Length); // длина доменного имени
            }
            ms.Write(addressBytes, 0, addressBytes.Length);
            ms.WriteByte((byte)(port >> 8));   // PORT, старший байт
            ms.WriteByte((byte)(port & 0xFF)); // PORT, младший байт
            return ms.ToArray();
        }

        /// <summary>Читает адрес и порт, назначенные прокси-сервером (BND.ADDR + BND.PORT).</summary>
        private static async Task SkipBoundAddressAsync(Socket socket, byte addressType, CancellationToken token)
        {
            switch (addressType)
            {
                case 0x01: // IPv4
                    await ReceiveExactAsync(socket, new byte[4], token).ConfigureAwait(false);
                    break;
                case 0x04: // IPv6
                    await ReceiveExactAsync(socket, new byte[16], token).ConfigureAwait(false);
                    break;
                case 0x03: // доменное имя
                    var lengthBuffer = new byte[1];
                    await ReceiveExactAsync(socket, lengthBuffer, token).ConfigureAwait(false);
                    await ReceiveExactAsync(socket, new byte[lengthBuffer[0]], token).ConfigureAwait(false);
                    break;
                default:
                    throw new IOException($"SOCKS5 proxy returned an unknown address type ({addressType}).");
            }

            await ReceiveExactAsync(socket, new byte[2], token).ConfigureAwait(false); // BND.PORT
        }

        private static async Task SendAllAsync(Socket socket, byte[] buffer, CancellationToken token)
        {
            int sent = 0;
            while (sent < buffer.Length)
            {
                int count = await socket.SendAsync(buffer.AsMemory(sent), SocketFlags.None, token).ConfigureAwait(false);
                if (count == 0) throw new IOException("SOCKS5 connection closed while sending data.");
                sent += count;
            }
        }

        private static async Task ReceiveExactAsync(Socket socket, byte[] buffer, CancellationToken token)
        {
            int received = 0;
            while (received < buffer.Length)
            {
                int count = await socket.ReceiveAsync(buffer.AsMemory(received), SocketFlags.None, token).ConfigureAwait(false);
                if (count == 0) throw new IOException("SOCKS5 connection closed by the proxy server.");
                received += count;
            }
        }
    }
}



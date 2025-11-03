using Org.Mentalis.Security.Certificates;
using Org.Mentalis.Security.Ssl;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace FixedSsl
{
    public static class SslSocket
    {
        static SslSocket()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        private const int SSLv3 = 0x0300;
        private const int TLSv1 = 0x0301;
        private static SecureProtocol legacyProtocols = SecureProtocol.Ssl3 | SecureProtocol.Tls1;
        public static async Task<Stream?> AuthenticateAsServerAsync(Socket socket, X509Certificate? certificate, bool forceSsl)
        {
            //no certificate, no ssl
            if (certificate == null)
                return new NetworkStream(socket, true);

            //content type - 1 byte
            //version - 2 bytes
            //length - 2 bytes
            //handshake type - 1 byte
            //length - 3 bytes
            //max version - 2 bytes (this is the actual ssl version we want to check)

            //total 11 bytes

            //read first 11 bytes, but do not consume them.
            byte[] buffer = new byte[11];
            int received = await socket.ReceiveAsync(buffer, SocketFlags.Peek).ConfigureAwait(false);
            
            // Log what we received for debugging
            if (received > 0)
            {
                var hexPreview = received >= 3 
                    ? $"{buffer[0]:X2} {buffer[1]:X2} {buffer[2]:X2}"
                    : string.Join(" ", buffer.Take(received).Select(b => $"{b:X2}"));
                System.Diagnostics.Debug.WriteLine($"SslSocket.AuthenticateAsServerAsync: Received {received} bytes, first bytes: {hexPreview}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"SslSocket.AuthenticateAsServerAsync: Received {received} bytes (socket may be closed)");
            }
            
            if (received < 3) // Need at least 3 bytes to detect protocol
            {
                // If socket closed (0 bytes) or forceSsl, return null. Otherwise allow plain TCP fallback
                if (received == 0 || forceSsl)
                {
                    System.Diagnostics.Debug.WriteLine($"SslSocket.AuthenticateAsServerAsync: Returning null (received={received}, forceSsl={forceSsl})");
                    return null;
                }
                // Client connected but hasn't sent enough data yet - allow plain TCP fallback when forceSsl=false
                System.Diagnostics.Debug.WriteLine($"SslSocket.AuthenticateAsServerAsync: Returning NetworkStream (plain TCP fallback, received={received})");
                return new NetworkStream(socket, true);
            }
            if (received < buffer.Length)
            {
                // Not enough data for full TLS detection, but check what we have
                // If it's clearly HTTP ("GET"), return plain TCP
                if (received >= 3 && buffer[0] == 0x47 && buffer[1] == 0x45 && buffer[2] == 0x54) // "GET"
                {
                    return new NetworkStream(socket, true);
                }
                // Not enough data - wait for more or return null if forceSsl
                if (forceSsl)
                    return null;
                return new NetworkStream(socket, true);
            }

            //content type needs to be handshake (0x16) and handshake type needs to be client hello (0x01)
            bool ssl = buffer[0] == 0x16 && buffer[5] == 0x01;

            if (!ssl)
            {
                if (forceSsl)
                    return null;
                return new NetworkStream(socket, true);
            }

            // Parse TLS version from buffer
            // Bytes 1-2: Protocol version (e.g., 0x0300 = SSL 3.0, 0x0301 = TLS 1.0, 0x0302 = TLS 1.1, etc.)
            int protocolVersion = (buffer[1] << 8) | buffer[2];
            // Bytes 9-10: Maximum SSL version the client supports (in ClientHello)
            int maxSslVersion = buffer[9] << 8 | buffer[10];
            
            // For legacy games (like NHL Legacy), they typically use SSL 3.0 or TLS 1.0
            // The SecureSocket implementation supports these legacy protocols better than modern SslStream
            // Check if it's a TLS/SSL handshake (0x03XX format indicates TLS/SSL protocol)
            bool isTlsFormat = (protocolVersion & 0xFF00) == 0x0300;
            
            // Use SecureSocket for legacy protocols or if max version suggests legacy support
            // This is safer for older games that may not properly negotiate modern TLS
            if (protocolVersion == SSLv3 || protocolVersion == TLSv1 || 
                maxSslVersion == SSLv3 || maxSslVersion == TLSv1 ||
                (isTlsFormat && maxSslVersion <= 0x0303)) // TLS 1.0, 1.1, or 1.2
            {
                // Use legacy SecureSocket which supports SSL 3.0 and TLS 1.0 properly
                SecurityOptions options = new SecurityOptions(legacyProtocols, new Certificate(certificate), ConnectionEnd.Server);
                SecureSocket ss = new SecureSocket(socket, options);
                return new SecureNetworkStream(ss, true);
            }
            
            // Fallback to SslStream for very modern TLS (1.3+), though unlikely for legacy games
            // Note: Modern .NET may not support TLS 1.0/1.1, so SecureSocket is preferred
            SslStream sslStream = new SslStream(new NetworkStream(socket, true), false);
            await sslStream.AuthenticateAsServerAsync(certificate).ConfigureAwait(false);
            return sslStream;
        }

        public static Stream? AuthenticateAsServer(Socket socket, X509Certificate? certificate, bool forceSsl)
        {
            //no certificate, no ssl
            if (certificate == null)
                return new NetworkStream(socket, true);

            //content type - 1 byte
            //version - 2 bytes
            //length - 2 bytes
            //handshake type - 1 byte
            //length - 3 bytes
            //max version - 2 bytes (this is the actual ssl version we want to check)

            //total 11 bytes

            //read first 11 bytes, but do not consume them.
            byte[] buffer = new byte[11];
            int received = socket.Receive(buffer, SocketFlags.Peek);
            
            if (received > 0)
            {
                var hexPreview = received >= 3 
                    ? $"{buffer[0]:X2} {buffer[1]:X2} {buffer[2]:X2}"
                    : string.Join(" ", buffer.Take(received).Select(b => $"{b:X2}"));
                System.Diagnostics.Debug.WriteLine($"SslSocket.AuthenticateAsServer: Received {received} bytes, first bytes: {hexPreview}");
            }
            
            if (received < 3) // Need at least 3 bytes to detect protocol
            {
                // If socket closed (0 bytes) or forceSsl, return null. Otherwise allow plain TCP fallback
                if (received == 0 || forceSsl)
                {
                    System.Diagnostics.Debug.WriteLine($"SslSocket.AuthenticateAsServer: Returning null (received={received}, forceSsl={forceSsl})");
                    return null;
                }
                // Client connected but hasn't sent enough data yet - allow plain TCP fallback when forceSsl=false
                System.Diagnostics.Debug.WriteLine($"SslSocket.AuthenticateAsServer: Returning NetworkStream (plain TCP fallback, received={received})");
                return new NetworkStream(socket, true);
            }
            
            // If we received less than 11 bytes, check what we have
            if (received < buffer.Length)
            {
                // If it's clearly HTTP ("GET"), return plain TCP
                if (received >= 3 && buffer[0] == 0x47 && buffer[1] == 0x45 && buffer[2] == 0x54) // "GET"
                {
                    return new NetworkStream(socket, true);
                }
                // Not enough data - wait for more or return null if forceSsl
                if (forceSsl)
                    return null;
                return new NetworkStream(socket, true);
            }

            //content type needs to be handshake (0x16) and handshake type needs to be client hello (0x01)
            bool ssl = buffer[0] == 0x16 && buffer[5] == 0x01;

            if (!ssl)
            {
                if (forceSsl)
                    return null;
                return new NetworkStream(socket, true);
            }

            // Parse TLS version from buffer
            int protocolVersion = (buffer[1] << 8) | buffer[2];
            int maxSslVersion = buffer[9] << 8 | buffer[10];
            
            // For legacy games, use SecureSocket which supports SSL 3.0 and TLS 1.0
            bool useLegacySsl = (protocolVersion == SSLv3 || protocolVersion == TLSv1 || 
                                maxSslVersion == SSLv3 || maxSslVersion == TLSv1);
            bool isTlsFormat = (protocolVersion & 0xFF00) == 0x0300;
            
            if (useLegacySsl || (isTlsFormat && maxSslVersion <= 0x0303))
            {
                SecurityOptions options = new SecurityOptions(legacyProtocols, new Certificate(certificate), ConnectionEnd.Server);
                SecureSocket ss = new SecureSocket(socket, options);
                return new SecureNetworkStream(ss, true);
            }

            SslStream sslStream = new SslStream(new NetworkStream(socket, true), false);
            sslStream.AuthenticateAsServer(certificate);
            return sslStream;
        }

        public static IAsyncResult BeginAuthenticateAsServer(Socket socket, X509Certificate? certificate, bool forceSsl, AsyncCallback? callback, object? state)
        {
            return AuthenticateAsServerAsync(socket, certificate, forceSsl).AsApm(callback, state);
        }

        public static Stream? EndAuthenticateAsServer(IAsyncResult result)
        {
            return ((Task<Stream?>)result).Result;
        }

        #region Helpers
        private static IAsyncResult AsApm<T>(this Task<T> task,
                                    AsyncCallback? callback,
                                    object? state)
        {
            if (task == null)
                throw new ArgumentNullException("task");

            var tcs = new TaskCompletionSource<T>(state);
            task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null && t.Exception.InnerExceptions != null)
                    tcs.TrySetException(t.Exception.InnerExceptions);
                else if (t.IsCanceled)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetResult(t.Result);

                if (callback != null)
                    callback(tcs.Task);
            }, TaskScheduler.Default);
            return tcs.Task;
        }
        #endregion
    }
}
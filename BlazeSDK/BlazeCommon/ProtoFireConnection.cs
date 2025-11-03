using Org.Mentalis.Security.Ssl;
using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using NLog;

namespace BlazeCommon
{
    public class ProtoFireConnection
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        public long ID { get; }
        public ProtoFireServer? Owner { get; }
        public Socket Socket { get; }
        public Stream? Stream { get; private set; }
        public bool Connected { get; private set; }

        static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);
        public ProtoFireConnection(long id, ProtoFireServer owner, Socket socket)
        {
            ID = id;
            Owner = owner;
            Socket = socket;
            Stream = null;
            Connected = true;
        }

        public ProtoFireConnection(Socket socket)
        {
            ID = 0;
            Owner = null;
            Socket = socket;
            Stream = null;
            Connected = true;
        }

        public void SetStream(Stream stream)
        {
            if (Stream != null)
                throw new InvalidOperationException("Stream is already set");
            Stream = stream;
        }

        public void Disconnect()
        {
            if (!Connected)
                return;

            Connected = false;

            //stream owns the socket, so no need to close the socket
            try { Stream?.Close(); } catch { }

            Owner?.KillConnection(this); //remove from connection list
        }


        public async Task<ProtoFirePacket?> ReadPacketAsync()
        {
            if (!Connected)
            {
                _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Connection is not connected.", ID);
                return null;
            }

            if (Stream == null)
            {
                _logger.Error("Connection({ConnectionId}) ReadPacketAsync: Stream is not set.", ID);
                throw new InvalidOperationException("Stream is not set");
            }

            try
            {
                _logger.Trace("Connection({ConnectionId}) ReadPacketAsync: Attempting to read FireFrame header ({HeaderSize} bytes)...", ID, FireFrame.MIN_HEADER_SIZE);
                _logger.Trace("Connection({ConnectionId}) ReadPacketAsync: Stream type: {StreamType}, CanRead: {CanRead}, DataAvailable: {DataAvailable}", 
                    ID, Stream.GetType().Name, Stream.CanRead, Stream is Org.Mentalis.Security.Ssl.SecureNetworkStream sns ? sns.DataAvailable : "N/A");
                
                FireFrame frame = new FireFrame();
                
                // For SecureNetworkStream, wait a bit for data to arrive after SSL handshake
                if (Stream is Org.Mentalis.Security.Ssl.SecureNetworkStream secureStream)
                {
                    if (!secureStream.DataAvailable)
                    {
                        _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: SecureNetworkStream reports no data available yet. Waiting briefly after SSL handshake...", ID);
                        // Check if socket is still connected
                        bool socketConnected = Socket?.Connected ?? false;
                        _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Socket.Connected={SocketConnected}", ID, socketConnected);
                        
                        // Give the client a moment - sometimes there's a delay after SSL handshake
                        // Some clients may close immediately if certificate validation fails, so don't wait too long
                        await Task.Delay(300).ConfigureAwait(false);
                        
                        // Check again
                        socketConnected = Socket?.Connected ?? false;
                        if (!secureStream.DataAvailable)
                        {
                            _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Still no data after wait. Socket.Connected={SocketConnected}. Client may have disconnected or rejected certificate.", ID, socketConnected);
                            // If socket is closed, client disconnected (may be normal after receiving response)
                            if (!socketConnected)
                            {
                                _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Socket is closed - client disconnected. This may be normal if client received expected response.", ID);
                                return null;
                            }
                            // Try one more brief wait
                            await Task.Delay(200).ConfigureAwait(false);
                            socketConnected = Socket?.Connected ?? false;
                            if (!socketConnected)
                            {
                                _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Socket closed during final wait - client disconnected.", ID);
                                return null;
                            }
                        }
                    }
                }
                
                // Use ReadAllAsync which handles partial reads better
                // Wrap in try-catch to handle SecureSocket exceptions gracefully
                try
                {
                    if (!await Stream.ReadAllAsync(frame.Frame, 0, FireFrame.MIN_HEADER_SIZE).ConfigureAwait(false))
                    {
                        _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: ReadAllAsync returned false - no data read. Stream may be closed.", ID);
                        return null;
                    }
                }
                catch (System.IO.IOException ioEx) when (ioEx.InnerException is System.Net.Sockets.SocketException)
                {
                    var sockEx = ioEx.InnerException as System.Net.Sockets.SocketException;
                    _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: SocketException during read (ErrorCode: {ErrorCode}). Connection may have closed during SSL handshake or immediately after.", 
                        ID, sockEx?.SocketErrorCode.ToString() ?? "Unknown");
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Connection({ConnectionId}) ReadPacketAsync: Exception while reading header: {Message}", ID, ex.Message);
                    return null;
                }
                
               // Log raw header bytes for debugging
               var headerHex = BitConverter.ToString(frame.Frame, 0, FireFrame.MIN_HEADER_SIZE).Replace("-", " ");
               _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Read header bytes: {HeaderHex}", ID, headerHex);
               
               // Detect HTTP requests (common mistake - someone connecting with browser/client expecting HTTP)
               if (frame.Frame[0] == 0x47 && frame.Frame[1] == 0x45 && frame.Frame[2] == 0x54) // "GET"
               {
                   string httpRequest = System.Text.Encoding.ASCII.GetString(frame.Frame, 0, Math.Min(FireFrame.MIN_HEADER_SIZE, frame.Frame.Length));
                   _logger.Warn("Connection({ConnectionId}) ReadPacketAsync: Received HTTP request instead of Blaze packet: {HttpRequest}", ID, httpRequest.Trim());
                   _logger.Warn("Connection({ConnectionId}) This port expects Blaze protocol packets, not HTTP. Client may be misconfigured or this is a health check/probe.", ID);
                   return null;
               }
                
                // Parse and log header details
                ushort sizeLow = (ushort)((frame.Frame[0] << 8) | frame.Frame[1]);
                ushort component = (ushort)((frame.Frame[2] << 8) | frame.Frame[3]);
                ushort command = (ushort)((frame.Frame[4] << 8) | frame.Frame[5]);
                byte msgTypeAndUserIndex = frame.Frame[8];
                byte msgType = (byte)((msgTypeAndUserIndex >> 4) & 0xF);
                byte userIndex = (byte)(msgTypeAndUserIndex & 0xF);
                
                _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Parsed header - SizeLow={SizeLow}, Component=0x{Component:X4}, Command=0x{Command:X4}, MsgType={MsgType}, UserIndex={UserIndex}", 
                    ID, sizeLow, component, command, msgType, userIndex);

                ushort extraFrameBytesNeeded = frame.ExtraHeaderSize;
                uint fullSize = frame.Size; // Get size before reading extra header
                
                if (extraFrameBytesNeeded > 0)
                {
                    _logger.Trace("Connection({ConnectionId}) ReadPacketAsync: Reading extra header bytes ({ExtraBytes} bytes)...", ID, extraFrameBytesNeeded);
                    if (!await Stream.ReadAllAsync(frame.Frame, FireFrame.MIN_HEADER_SIZE, extraFrameBytesNeeded).ConfigureAwait(false))
                    {
                        _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Failed to read extra header bytes.", ID);
                        return null;
                    }
                    
                    // Size might need to be recalculated after reading extra header
                    fullSize = frame.Size;
                    _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: After extra header, fullSize={FullSize}", ID, fullSize);
                }
                
                // IMPORTANT: Check if Size includes header or is just payload
                // Looking at WriteToAsync, Size is set to Data.Length, suggesting it's just payload
                // But let's check both possibilities
                uint headerTotalSize = (uint)(FireFrame.MIN_HEADER_SIZE + extraFrameBytesNeeded);
                uint dataSizeAsPayload = fullSize; // Assume Size is payload only
                uint dataSizeAsTotal = fullSize > headerTotalSize ? fullSize - headerTotalSize : 0; // Assume Size includes header
                
                _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Size analysis - FullSize={FullSize}, HeaderSize={HeaderSize}, IfPayloadOnly={PayloadSize}, IfTotalSize={TotalSize}", 
                    ID, fullSize, headerTotalSize, dataSizeAsPayload, dataSizeAsTotal);
                
                // Try payload-only interpretation first (as per WriteToAsync)
                _logger.Trace("Connection({ConnectionId}) ReadPacketAsync: Reading packet data as payload ({DataSize} bytes)...", ID, fullSize);
                byte[] data = new byte[fullSize];
                
                int totalRead = 0;
                int lastRead = 0;
                DateTime startTime = DateTime.UtcNow;
                
                while (totalRead < data.Length && Connected)
                {
                    int bytesToRead = data.Length - totalRead;
                    int bytesRead = await Stream.ReadAsync(data, totalRead, bytesToRead).ConfigureAwait(false);
                    
                    if (bytesRead == 0)
                    {
                        TimeSpan elapsed = DateTime.UtcNow - startTime;
                        _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Read 0 bytes after reading {TotalRead}/{TotalBytes} bytes. Elapsed: {Elapsed}ms. Connection still open: {Connected}", 
                            ID, totalRead, data.Length, elapsed.TotalMilliseconds, Connected);
                        
                        // Dump what we received so far for debugging
                        if (totalRead > 0)
                        {
                            var receivedHex = BitConverter.ToString(data, 0, totalRead).Replace("-", " ");
                            _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Partial data received (first 256 bytes or all if less): {DataHex}", 
                                ID, totalRead > 256 ? BitConverter.ToString(data, 0, 256).Replace("-", " ") + "..." : receivedHex);
                            
                            // Try to interpret as text for debugging
                            var textPreview = System.Text.Encoding.UTF8.GetString(data, 0, Math.Min(totalRead, 256)).Replace("\0", ".");
                            _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Data as text (first 256): {TextPreview}", ID, textPreview);
                        }
                        
                        return null;
                    }
                    
                    totalRead += bytesRead;
                    lastRead = bytesRead;
                    
                    // Log progress for large packets
                    if (frame.Size > 1000 && totalRead % 1000 == 0)
                    {
                        _logger.Trace("Connection({ConnectionId}) ReadPacketAsync: Progress: {TotalRead}/{TotalBytes} bytes read ({Percent:F1}%)", 
                            ID, totalRead, data.Length, (totalRead * 100.0 / data.Length));
                    }
                }
                
                if (totalRead < data.Length)
                {
                    _logger.Debug("Connection({ConnectionId}) ReadPacketAsync: Only read {TotalRead}/{TotalBytes} bytes before connection closed.", ID, totalRead, data.Length);
                    return null;
                }
                
                TimeSpan totalElapsed = DateTime.UtcNow - startTime;
                _logger.Trace("Connection({ConnectionId}) ReadPacketAsync: Successfully read all {TotalBytes} bytes in {Elapsed}ms", 
                    ID, data.Length, totalElapsed.TotalMilliseconds);

                _logger.Trace("Connection({ConnectionId}) ReadPacketAsync: Successfully read packet. Component=0x{Component:X4}, Command=0x{Command:X4}, MsgType={MsgType}", 
                    ID, frame.Component, frame.Command, frame.MsgType);
                return new ProtoFirePacket(frame, data);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Connection({ConnectionId}) ReadPacketAsync: Exception while reading packet: {Message}", ID, ex.Message);
                return null;
            }
        }

        public ProtoFirePacket? ReadPacket()
        {
            if (!Connected)
                return null;

            if (Stream == null)
                throw new InvalidOperationException("Stream is not set");

            try
            {

                FireFrame frame = new FireFrame();
                if (!Stream.ReadAll(frame.Frame, 0, FireFrame.MIN_HEADER_SIZE))
                    return null;

                ushort extraFrameBytesNeeded = frame.ExtraHeaderSize;
                if (!Stream.ReadAll(frame.Frame, FireFrame.MIN_HEADER_SIZE, extraFrameBytesNeeded))
                    return null;

                byte[] data = new byte[frame.Size];
                if (!Stream.ReadAll(data, 0, data.Length))
                    return null;

                return new ProtoFirePacket(frame, data);
            }
            catch (Exception)
            {
                return null;
            }

        }

        public bool Send(ProtoFirePacket packet)
        {
            if (!Connected)
                return false;

            if (Stream == null)
                throw new InvalidOperationException("Stream is not set");

            bool success = false;

            semaphoreSlim.Wait();
            try
            {
                packet.WriteTo(Stream);
                Stream.Flush();
                success = true;
            }
            catch (ObjectDisposedException)
            {
                success = false;
            }
            catch (IOException)
            {
                success = false;
            }
            finally
            {
                semaphoreSlim.Release();
            }
            return success;
        }

        public async Task<bool> SendAsync(ProtoFirePacket packet)
        {
            if (!Connected)
                return false;

            if (Stream == null)
                throw new InvalidOperationException("Stream is not set");

            bool success = false;
            await semaphoreSlim.WaitAsync();
            try
            {
                await packet.WriteToAsync(Stream).ConfigureAwait(false);
                await Stream.FlushAsync().ConfigureAwait(false);
                success = true;
            }
            catch (ObjectDisposedException)
            {
                success = false;
            }
            catch (IOException)
            {
                success = false;
            }
            finally
            {
                semaphoreSlim.Release();
            }
            return success;
        }



        private static async Task<Socket?> ConnectToAsync(string hostname, int port)
        {
            IPHostEntry host = Dns.GetHostEntry(hostname);
            if (host.AddressList.Length == 0)
                return null;

            IPAddress ipAddress = host.AddressList[0];
            IPEndPoint remoteEP = new IPEndPoint(ipAddress, port);

            // Create a TCP/IP  socket.
            Socket sock = new Socket(ipAddress.AddressFamily,
                SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await sock.ConnectAsync(remoteEP).ConfigureAwait(false);
                return sock;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static async Task<ProtoFireConnection?> ConnectAsync(string hostname, int port, bool ssl = true)
        {
            Socket? sock = await ConnectToAsync(hostname, port).ConfigureAwait(false);
            if (sock == null)
                return null;

            Stream stream = new NetworkStream(sock, true);
            if (ssl)
            {
                SslStream sslStream = new SslStream(stream, false, RemoteCertificateVerify);
                await sslStream.AuthenticateAsClientAsync(hostname, null, System.Security.Authentication.SslProtocols.Tls, false).ConfigureAwait(false);
                stream = sslStream;
            }

            var ret = new ProtoFireConnection(sock);
            ret.SetStream(stream);
            return ret;
        }

        public static ProtoFireConnection? ConnectSsl3(string hostname, int port)
        {
            IPHostEntry host = Dns.GetHostEntry(hostname);
            if (host.AddressList.Length == 0)
                return null;

            SecurityOptions options = new SecurityOptions(
                SecureProtocol.Ssl3 | SecureProtocol.Tls1,  // use SSL3 or TLS1
                null!,                                       // do not use client authentication
                ConnectionEnd.Client,                       // this is the client side
                CredentialVerification.None,                // do not check the certificate -- this should not be used in a real-life application :-)
                null!,                                       // not used with automatic certificate verification
                hostname,                        // this is the common name of the Microsoft web server
                SecurityFlags.Default,                      // use the default security flags
                SslAlgorithms.ALL,               // only use secure ciphers
                null!);										// do not process certificate requests.

            SecureSocket s = new SecureSocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, options);
            // connect to the remote host
            s.Connect(new IPEndPoint(host.AddressList[0], port));


            ProtoFireConnection connection = new ProtoFireConnection(null!);
            connection.SetStream(new SecureNetworkStream(s, true));
            return connection;
        }

        public static ProtoFireConnection? ConnectSsl3(long address, int port)
        {
            SecurityOptions options = new SecurityOptions(
                SecureProtocol.Ssl3 | SecureProtocol.Tls1,  // use SSL3 or TLS1
                null!,                                       // do not use client authentication
                ConnectionEnd.Client,                       // this is the client side
                CredentialVerification.None,                // do not check the certificate -- this should not be used in a real-life application :-)
                null!,                                       // not used with automatic certificate verification
                null!,                        // this is the common name of the Microsoft web server
                SecurityFlags.Default,                      // use the default security flags
                SslAlgorithms.SECURE_CIPHERS,               // only use secure ciphers
                null!);										// do not process certificate requests.

            SecureSocket s = new SecureSocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, options);
            // connect to the remote host
            s.Connect(new IPEndPoint(address, port));


            ProtoFireConnection connection = new ProtoFireConnection(null!);
            connection.SetStream(new SecureNetworkStream(s, true));
            return connection;
        }

        private static bool RemoteCertificateVerify(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
    }
}

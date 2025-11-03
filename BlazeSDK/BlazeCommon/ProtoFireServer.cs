using FixedSsl;
using NLog;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace BlazeCommon
{
    public abstract class ProtoFireServer
    {
        public string Name { get; private set; }
        public IPEndPoint LocalEP { get; private set; }
        public bool IsRunning { get; private set; }
        public X509Certificate? Certificate { get; private set; }
        public bool ForceSsl { get; private set; }

        [MemberNotNullWhen(true, nameof(Certificate))]
        public bool Secure { get => Certificate != null; }

        private Socket? _listenSocket;
        private long _nextConnectionId;
        private ConcurrentDictionary<long, ProtoFireConnection> _connections;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private static Logger _logger = LogManager.GetCurrentClassLogger();

        public ProtoFireServer(string name, IPEndPoint localEP, X509Certificate? cert, bool forceSsl)
        {
            Name = name;
            LocalEP = localEP;
            IsRunning = false;
            Certificate = cert;
            ForceSsl = forceSsl;

            _connections = new ConcurrentDictionary<long, ProtoFireConnection>();
            _cancellationTokenSource = new CancellationTokenSource();
            _nextConnectionId = 0;
        }

        public void KillConnection(ProtoFireConnection connection)
        {
            if (connection.Connected)
                connection.Disconnect(); //will call this method again after disconnect
            else
                OnProtoFireDisconnectInternalAsync(connection).GetAwaiter().GetResult();
        }

        public void Stop()
        {
            IsRunning = false;
            _cancellationTokenSource.Cancel();
        }

        public async Task Start(int backlog)
        {
            //check if already running or is cancelled
            if (IsRunning)
                return;

            if (_cancellationTokenSource.IsCancellationRequested)
                _cancellationTokenSource = new CancellationTokenSource();

            //server not running, start it
            try
            {
                _logger.Info($"Starting {(Secure ? "secure" : "insecure")} ProtoFireServer({{Name}}) on port {{Port}}...", Name, LocalEP.Port);
                if (Secure)
                {
                    _logger.Info($"ProtoFireServer({Name}): Certificate is present (Subject: {Certificate?.Subject ?? "unknown"}), ForceSsl={ForceSsl}");
                }
                else
                {
                    _logger.Warn($"ProtoFireServer({Name}): No certificate - will operate in plain TCP mode");
                }
                _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _listenSocket.Bind(LocalEP);
                _listenSocket.Listen(backlog);
                IsRunning = true;
                _logger.Info($"ProtoFireServer({{Name}}) started on port {{Port}} (Secure: {Secure}).", Name, LocalEP.Port);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to start {(Secure ? "secure" : "insecure")} ProtoFireServer({{Name}}) on port {{Port}}. Perhaps the port is already in use.", Name, LocalEP.Port);
                IsRunning = false;
                return;
            }

            try
            {
                //start accepting connections
                _logger.Info("ProtoFireServer({ServerName}) is now accepting connections on {LocalEP}", Name, LocalEP);
                _logger.Debug("ProtoFireServer({ServerName}) AcceptAsync loop started. Waiting for incoming connections...", Name);
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    Socket socket = await _listenSocket.AcceptAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
                    long clientId = Interlocked.Increment(ref _nextConnectionId);

                    _logger.Info("ProtoFireServer({ServerName}) accepted new socket connection from {RemoteEP} on port {Port}", 
                        Name, socket.RemoteEndPoint, LocalEP.Port);
                    _logger.Debug("ProtoFireServer({ServerName}) Connection({ClientId}): Socket.Connected={Connected}, Socket.LocalEndPoint={LocalEP}, Socket.RemoteEndPoint={RemoteEP}",
                        Name, clientId, socket.Connected, socket.LocalEndPoint, socket.RemoteEndPoint);
                    
                    ProtoFireConnection connection = new ProtoFireConnection(clientId, this, socket);
                    // Don't await - process connections concurrently
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await OnProtoFireConnectInternalAsync(connection).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "ProtoFireServer({ServerName}) Exception processing connection({ClientId})", Name, connection.ID);
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.Error(ex, "ProtoFireServer({ServerName}) exception in accept loop", Name);
            }

            IsRunning = false;

            _listenSocket.Close();
            _nextConnectionId = 0;
            //kill all server connections
            foreach (var connection in _connections.Values)
                connection.Disconnect();
            _connections.Clear();

        }

        public async void AuthenticateAsServerCallback(IAsyncResult result)
        {
            ProtoFireConnection connection = (ProtoFireConnection)result.AsyncState!;

            try
            {
                _logger.Debug("Connection({ClientId}) AuthenticateAsServerCallback: Starting SSL authentication check...", connection.ID);
                Stream? stream = SslSocket.EndAuthenticateAsServer(result);
                if (stream == null)
                {
                    if (ForceSsl)
                    {
                        _logger.Warn("Connection({ClientId}) SSL detection failed: Client did not send TLS handshake.", connection.ID);
                        _logger.Warn("Connection({ClientId}) rejected: SSL is required (forceSsl=true) but client did not send TLS.", connection.ID);
                        _logger.Debug("Connection({ClientId}) Socket state before disconnect: Connected={Connected}, RemoteEndPoint={RemoteEP}", 
                            connection.ID, connection.Socket.Connected, connection.Socket.RemoteEndPoint);
                        connection.Disconnect();
                        return;
                    }
                    else
                    {
                        // ForceSsl == false: Allow plain TCP fallback when SSL detection fails
                        _logger.Info("Connection({ClientId}) SSL detection returned null, but forceSsl=false. Falling back to plain TCP.", connection.ID);
                        _logger.Debug("Connection({ClientId}) Creating NetworkStream for plain TCP. Socket.Connected={Connected}", 
                            connection.ID, connection.Socket.Connected);
                        stream = new NetworkStream(connection.Socket, true);
                        // Continue below to set stream and start packet reading
                    }
                }

                connection.SetStream(stream);

                // Log connection details - distinguish SSL from plain TCP fallback
                bool isSecure = stream.GetType().Name.Contains("Secure") || stream.GetType().Name.Contains("Ssl");
                if (isSecure)
                {
                    _logger.Info("Authenticated as server for connection({ClientId}). Stream type: {StreamType} (SSL/TLS enabled)", connection.ID, stream.GetType().Name);
                    _logger.Debug("Connection({ClientId}) SSL handshake completed. Waiting for first Blaze packet...", connection.ID);
                }
                else
                {
                    _logger.Warn("Connection({ClientId}) using plain TCP fallback (Stream type: {StreamType}). Client did not send TLS handshake, but forceSsl=false so allowing plain TCP.", connection.ID, stream.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to authenticate as server for connection({ClientId}).", connection.ID);
                connection.Disconnect();
                return;
            }

            _logger.Debug("Connection({ClientId}) starting packet reading loop after SSL handshake.", connection.ID);
            int packetCount = 0;
            while (IsRunning)
            {
                ProtoFirePacket? packet = await connection.ReadPacketAsync().ConfigureAwait(false);

                //disconnected
                if (packet == null)
                {
                    if (packetCount > 0)
                    {
                        _logger.Info("Connection({ClientId}) ReadPacketAsync returned null after processing {PacketCount} packet(s). Client may have disconnected after receiving response (this is normal behavior).", connection.ID, packetCount);
                    }
                    else
                    {
                        _logger.Warn("Connection({ClientId}) ReadPacketAsync returned null before receiving any packets. Client may have rejected the certificate and closed the connection.", connection.ID);
                        _logger.Warn("Connection({ClientId}) This is expected if using a self-signed certificate without client-side SSL patching.", connection.ID);
                    }
                    break;
                }
                
                packetCount++;
                _logger.Debug("Connection({ClientId}) received first packet after SSL handshake (packet #{PacketNum}).", connection.ID, packetCount);

                try { await OnProtoFirePacketReceivedAsync(connection, packet).ConfigureAwait(false); }
                catch (Exception ex) { await OnProtoFireErrorInternalAsync(connection, ex).ConfigureAwait(false); }
            }

            connection.Disconnect();
        }

        public ValueTask KillConnectionAsync(ProtoFireConnection connection)
        {
            if (connection.Connected)
            {
                connection.Disconnect(); //will call this method again after disconnect
                return ValueTask.CompletedTask;
            }

            return OnProtoFireDisconnectInternalAsync(connection);
        }

        private async ValueTask OnProtoFireConnectInternalAsync(ProtoFireConnection connection)
        {
            if (!_connections.TryAdd(connection.ID, connection))
            {
                connection.Disconnect();
                return;
            }

            _logger.Info("Connection({ClientId}) accepted from {RemoteEP} on {ServerName} (port {Port}).", 
                connection.ID, connection.Socket.RemoteEndPoint, Name, LocalEP.Port);
            _logger.Debug("Connection({ClientId}) Socket local endpoint: {LocalEP}", connection.ID, connection.Socket.LocalEndPoint);

            try
            {
                await OnProtoFireConnectAsync(connection).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await OnProtoFireErrorInternalAsync(connection, ex).ConfigureAwait(false);
            }

            if (connection.Connected)
            {
                // Check if client wants SSL - if so, negotiate SSL. Otherwise use plain TCP.
                if (Secure)
                {
                    // We have a certificate - check if client is sending TLS
                    _logger.Info("Authenticating as server for connection({ClientId}).", connection.ID);
                    SslSocket.BeginAuthenticateAsServer(connection.Socket, Certificate, ForceSsl, AuthenticateAsServerCallback, connection);
                }
                else
                {
                    // No certificate - plain TCP only
                    var stream = new NetworkStream(connection.Socket, true);
                    connection.SetStream(stream);
                    _logger.Info("Connection({ClientId}) using plain TCP (no SSL certificate available).", connection.ID);
                    
                    // Start reading packets in background
                    _ = Task.Run(async () =>
                    {
                        _logger.Debug("Connection({ClientId}) started packet reading loop.", connection.ID);
                        int packetCount = 0;
                        while (IsRunning && connection.Connected)
                        {
                            try
                            {
                                ProtoFirePacket? packet = await connection.ReadPacketAsync().ConfigureAwait(false);
                                if (packet == null)
                                {
                                    if (connection.Connected)
                                    {
                                        _logger.Debug("Connection({ClientId}) ReadPacketAsync returned null but connection is still connected. This may indicate the client closed the connection or sent invalid data.", connection.ID);
                                    }
                                    else
                                    {
                                        _logger.Debug("Connection({ClientId}) ReadPacketAsync returned null and connection is disconnected.", connection.ID);
                                    }
                                    break;
                                }
                                
                                packetCount++;
                                _logger.Debug("Connection({ClientId}) received packet #{PacketNum}, Component=0x{Component:X4}, Command=0x{Command:X4}, MsgType={MsgType}, Size={Size} bytes", 
                                    connection.ID, packetCount, packet.Frame.Component, packet.Frame.Command, packet.Frame.MsgType, packet.Data.Length);
                                
                                try 
                                { 
                                    await OnProtoFirePacketReceivedAsync(connection, packet).ConfigureAwait(false); 
                                }
                                catch (Exception ex) 
                                { 
                                    await OnProtoFireErrorInternalAsync(connection, ex).ConfigureAwait(false); 
                                }
                            }
                            catch (Exception readEx)
                            {
                                _logger.Error(readEx, "Connection({ClientId}) exception while reading packet: {Message}", connection.ID, readEx.Message);
                                break;
                            }
                        }
                        _logger.Debug("Connection({ClientId}) packet reading loop ended. Total packets received: {PacketCount}", connection.ID, packetCount);
                        connection.Disconnect();
                    });
                }
            }
        }

        private async ValueTask OnProtoFireDisconnectInternalAsync(ProtoFireConnection connection)
        {
            if (!_connections.TryRemove(connection.ID, out _))
                return;

            _logger.Info("Connection({ClientId}) disconnected.", connection.ID);

            try
            {
                await OnProtoFireDisconnectAsync(connection).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await OnProtoFireErrorInternalAsync(connection, ex).ConfigureAwait(false);
            }
        }


        private async Task OnProtoFireErrorInternalAsync(ProtoFireConnection connection, Exception exception)
        {
            try
            {
                await OnProtoFireErrorAsync(connection, exception).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                //an error occured while handling an error, doesnt sound good...
                await OnProtoFireErrorInternalAsync(connection, ex).ConfigureAwait(false);
            }
        }

        public abstract Task OnProtoFireConnectAsync(ProtoFireConnection connection);
        public abstract Task OnProtoFirePacketReceivedAsync(ProtoFireConnection connection, ProtoFirePacket packet);
        public abstract Task OnProtoFireDisconnectAsync(ProtoFireConnection connection);
        public abstract Task OnProtoFireErrorAsync(ProtoFireConnection connection, Exception exception);
    }
}

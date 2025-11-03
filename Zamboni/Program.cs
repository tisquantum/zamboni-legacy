using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Blaze2SDK;
using BlazeCommon;
using NLog;
using NLog.Layouts;
using Tdf;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Zamboni.Components.Blaze;
using Zamboni.Components.NHL10;

namespace Zamboni;

//((ip.src == 127.0.0.1) && (ip.dst == 127.0.0.1)) && tcp.port == 42127 || tcp.port == 13337 || tcp.port == 8999 || tcp.port == 9946 || tcp.port == 17502 || tcp.port == 17501 || tcp.port == 17500 || tcp.port == 17499
// (ip.dst == 192.168.100.178 && ip.src == 192.168.1.79) || (ip.src == 192.168.100.178 && ip.dst == 192.168.1.79)
//tcp.port == 8999 || tcp.port == 9946 || tcp.port == 17502 || tcp.port == 17501 || tcp.port == 17500 || tcp.port == 17499 || udp.port == 8999 || udp.port == 9946 || udp.port == 17502 || udp.port == 17501 || udp.port == 17500 || udp.port == 17499
//tcp.port == 17499 || udp.port == 17499 || tcp.port == 3659|| udp.port == 3659
// tcp.port == 17499 || udp.port == 17499 || tcp.port == 3659 || udp.port == 3659  || tcp.port == 17500 || udp.port == 17500 || tcp.port == 17501 || udp.port == 17501 || tcp.port == 17502 || udp.port == 17502 || tcp.port == 17503 || udp.port == 17503
internal class Program
{
    public const string Version = "1.3";

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static ZamboniConfig ZamboniConfig;
    public static Database Database;

    private static readonly string PublicIp = new HttpClient().GetStringAsync("https://checkip.amazonaws.com/").GetAwaiter().GetResult().Trim();
    public static string GameServerIp;
    public static X509Certificate2? CoreServerCertificate; // Track if game server has SSL certificate

    private static async Task Main(string[] args)
    {
        InitConfig();
        StartLogger();
        InitDatabase();
        
        // Handle GameServerIp configuration
        if (ZamboniConfig.GameServerIp.Equals("auto"))
        {
            GameServerIp = PublicIp;
        }
        else if (ZamboniConfig.GameServerIp.Equals("wsl2"))
        {
            // Auto-detect WSL2 IP address for Windows RPCS3 compatibility
            try
            {
                // Get the first non-loopback IP address (WSL2 IP)
                var hostEntry = await Dns.GetHostEntryAsync(Dns.GetHostName());
                var wsl2Ip = hostEntry.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));
                GameServerIp = wsl2Ip?.ToString() ?? "127.0.0.1";
                Logger.Info($"Auto-detected WSL2 IP: {GameServerIp}");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to auto-detect WSL2 IP, using 127.0.0.1");
                GameServerIp = "127.0.0.1";
            }
        }
        else
        {
            GameServerIp = ZamboniConfig.GameServerIp;
        }

        var commandTask = Task.Run(StartCommandListener);
        var redirectorTask = StartRedirectorServer();
        var coreTask = StartCoreServer();
        var apiTask = new RestApi().StartAsync();
        Logger.Warn("Zamboni server " + Version + " started");
        await Task.WhenAll(redirectorTask, coreTask, commandTask, apiTask);
    }

    private static void StartLogger()
    {
        var logLevel = LogLevel.FromString(ZamboniConfig.LogLevel);
        var layout = new SimpleLayout("[${longdate}][${callsite-filename:includeSourcePath=false}(${callsite-linenumber})][${level:uppercase=true}]: ${message:withexception=true}");
        LogManager.Setup().LoadConfiguration(builder =>
        {
            builder.ForLogger().FilterMinLevel(logLevel)
                .WriteToConsole(layout)
                .WriteToFile("logs/server-${shortdate}.log", layout);
        });
    }

    private static void InitConfig()
    {
        const string configFile = "zamboni-config.yml";
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();
        var serializer = new SerializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();

        if (!File.Exists(configFile))
        {
            ZamboniConfig = new ZamboniConfig();
            var yaml = serializer.Serialize(ZamboniConfig);

            const string comments = "# GameServerIp: 'auto' = automatically detect public IP or specify a manual IP address, where GameServer is run on\n" +
                                    "# GameServerPort: Port for GameServer to listen on. (Redirector server lives on 42127, clients request there)\n" +
                                    "# LogLevel: Valid values: Trace, Debug, Info, Warn, Error, Fatal, Off.\n" +
                                    "# DatabaseConnectionString: Connection string to PostgreSQL, for saving data. (Not required)\n\n";
            File.WriteAllText(configFile, comments + yaml);
            Logger.Warn("Config file created: " + configFile);
            return;
        }

        var yamlText = File.ReadAllText(configFile);
        ZamboniConfig = deserializer.Deserialize<ZamboniConfig>(yamlText);
    }

    private static void InitDatabase()
    {
        Database = new Database();
    }

    private static X509Certificate2 LoadOrCreateCertificate()
    {
        // Try to load existing certificate first (using Bug_OldProtoSSL method certificate if available)
        // Priority: 1) gosredirector.pfx (Bug_OldProtoSSL), 2) gosredirector.ea.com.pfx (fallback)
        const string certPath = "certificates/gosredirector.pfx";
        const string fallbackCertPath = "certificates/gosredirector.ea.com.pfx";
        // Try Bug_OldProtoSSL certificate first
        if (File.Exists(certPath))
        {
            try
            {
                Logger.Info($"Loading Bug_OldProtoSSL certificate from {certPath}");
                return new X509Certificate2(certPath, "password");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to load certificate from {certPath}, trying fallback...");
            }
        }
        
        // Try fallback certificate path
        if (File.Exists(fallbackCertPath))
        {
            try
            {
                Logger.Info($"Loading SSL certificate from {fallbackCertPath}");
                return new X509Certificate2(fallbackCertPath, "password");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to load certificate from {fallbackCertPath}, will generate a new one");
            }
        }

        // Generate a self-signed certificate for local testing
        Logger.Warn("No SSL certificate found. Generating self-signed certificate for local testing...");
        Logger.Warn("NOTE: The client may need SSL certificate validation patched to accept this certificate.");
        
        try
        {
            var certificate = GenerateSelfSignedCertificate("gosredirector.ea.com");
            
            // Save it for future use
            Directory.CreateDirectory("certificates");
            byte[] pfxBytes = certificate.Export(X509ContentType.Pfx, "password");
            File.WriteAllBytes(certPath, pfxBytes);
            Logger.Info($"Generated and saved certificate to {certPath}");
            
            return certificate;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to generate SSL certificate. SSL will be disabled.");
            // Return a dummy certificate that won't actually work, but won't cause null ref
            // In practice, this should never happen unless there's a system-level issue
            throw new InvalidOperationException("Unable to create SSL certificate. Server cannot start without SSL for redirector.", ex);
        }
    }

    private static X509Certificate2 GenerateSelfSignedCertificate(string subjectName)
    {
        using (RSA rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest($"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            
            // Add Subject Alternative Names
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);
            sanBuilder.AddDnsName(subjectName);
            sanBuilder.AddDnsName("localhost");
            
            // Try to add WSL2 IP if we can detect it (will be done at certificate generation time)
            // For now, we'll add common WSL2 IP ranges - actual IP will be added when we know it
            try
            {
                var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
                var wsl2Ip = hostEntry.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));
                if (wsl2Ip != null)
                {
                    sanBuilder.AddIpAddress(wsl2Ip);
                    Logger.Info($"Added WSL2 IP {wsl2Ip} to certificate SAN");
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Could not detect WSL2 IP for certificate SAN: {ex.Message}");
            }
            
            request.CertificateExtensions.Add(sanBuilder.Build());

            // Create certificate valid for 10 years
            var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(10));
            
            return certificate;
        }
    }

    private static async Task StartRedirectorServer()
    {
        // Client sends TLS handshake, so we need to negotiate SSL.
        // Client will need Bug_OldProtoSSL patch to accept our self-signed certificate.
        X509Certificate2 cert = LoadOrCreateCertificate();
        bool forceSsl = false; // Don't force, but negotiate if client sends TLS
        
        Logger.Info($"SSL certificate loaded: {cert.Subject}");
        Logger.Info("Redirector server will negotiate SSL when client sends TLS handshake.");
        Logger.Info("NOTE: Client (RPCS3) may need Bug_OldProtoSSL patch to accept self-signed certificate.");
        
        var redirector = Blaze2.CreateBlazeServer("RedirectorServer", new IPEndPoint(IPAddress.Any, 42127), certificate: cert, forceSsl: forceSsl);
        redirector.AddComponent<RedirectorComponent>();
        await redirector.Start(-1).ConfigureAwait(false);
    }

    private static async Task StartCoreServer()
    {
        var tdfFactory = new TdfFactory();
        
        // Try enabling SSL on game server - NHL Legacy may expect SSL on both redirector and game server
        X509Certificate2? gameServerCert = null;
        bool gameServerForceSsl = false;
        
        try
        {
            // Use same certificate for game server (can use different one if needed)
            gameServerCert = LoadOrCreateCertificate();
            CoreServerCertificate = gameServerCert; // Store for RedirectorComponent to check
            Logger.Info($"Game server SSL certificate loaded: {gameServerCert.Subject}");
            Logger.Warn("Game server will negotiate SSL (forceSsl=false) but prefer SSL. If client doesn't send TLS, we'll allow plain TCP for debugging.");
            Logger.Warn("NOTE: If client keeps sending HTTP/plain TCP, it may be resolving gosredirector.ea.com via DNS instead of using provided IP.");
            gameServerForceSsl = false; // Allow plain TCP fallback to see what client actually sends
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to load SSL certificate for game server. Game server will use plain TCP only.");
            CoreServerCertificate = null;
            gameServerCert = null;
        }
        
        var config = new BlazeServerConfiguration("CoreServer", new IPEndPoint(IPAddress.Any, ZamboniConfig.GameServerPort), tdfFactory.CreateLegacyEncoder(), tdfFactory.CreateLegacyDecoder())
        {
            Certificate = gameServerCert, // X509Certificate2 inherits from X509Certificate, so this should work
            ForceSsl = gameServerForceSsl
        };
        
        // Verify certificate is set
        if (config.Certificate != null)
        {
            Logger.Info($"CoreServer configuration: Certificate is set (Subject: {config.Certificate.Subject}). Secure mode: true");
        }
        else
        {
            Logger.Warn("CoreServer configuration: Certificate is NULL. Secure mode: false");
        }
        
        var core = new ZamboniCoreServer(config);
        core.AddComponent<UtilComponent>();
        core.AddComponent<AuthenticationComponent>();
        core.AddComponent<UserSessionsComponent>();
        core.AddComponent<MessagingComponent>();
        core.AddComponent<CensusDataComponent>();
        core.AddComponent<RoomsComponent>();
        core.AddComponent<LeagueComponent>();
        core.AddComponent<ClubsComponent>();
        core.AddComponent<StatsComponent>();
        core.AddComponent<GameManagerComponent>();
        core.AddComponent<GameReportingComponent>();

        core.AddComponent<DynamicMessagingComponent>(); // Seems to be NHL10 Specific Components
        core.AddComponent<OsdkSettingsComponent>(); // Seems to be NHL10 Specific Components

        Logger.Info($"Starting CoreServer (ZamboniCoreServer) - will call Start(-1) with certificate: {(gameServerCert != null ? "Present" : "NULL")}");
        await core.Start(-1).ConfigureAwait(false);
        Logger.Info("CoreServer (ZamboniCoreServer) Start(-1) completed - server should now be listening.");
    }

    private static void StartCommandListener()
    {
        Logger.Info("Type 'help' or 'status'.");

        while (true)
        {
            var input = ReadLine.Read();
            if (string.IsNullOrWhiteSpace(input))
                continue;

            switch (input.Trim().ToLowerInvariant())
            {
                case "help":
                    Logger.Warn("Available commands: help, status");
                    break;

                case "status":
                    Logger.Info("Zamboni " + Version);
                    Logger.Info("Server running on ip: " + GameServerIp + " (" + PublicIp + ")");
                    Logger.Info("GameServerPort port: " + ZamboniConfig.GameServerPort);
                    Logger.Info("Redirector port: 42127");
                    Logger.Info("Online Users: " + Manager.ZamboniUsers.Count);
                    foreach (var user in Manager.ZamboniUsers) Logger.Info(user.Username);
                    Logger.Info("Queued Total Users: " + (Manager.QueuedMatchZamboniUsers.Count + Manager.QueuedShootoutZamboniUsers.Count));
                    foreach (var qum in Manager.QueuedMatchZamboniUsers) Logger.Info(qum.Username + " (Ranked Match Queue)");
                    foreach (var qus in Manager.QueuedShootoutZamboniUsers) Logger.Info(qus.Username + " (Ranked Shootout Queue)");
                    Logger.Info("Zamboni Games: " + Manager.ZamboniGames.Count);
                    foreach (var zg in Manager.ZamboniGames) Logger.Info(zg);
                    break;

                default:
                    Logger.Info($"Unknown command: {input}");
                    break;
            }
        }
    }
}
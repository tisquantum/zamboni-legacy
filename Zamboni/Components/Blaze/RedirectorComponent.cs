using System;
using System.Threading.Tasks;
using Blaze2SDK.Blaze.Redirector;
using Blaze2SDK.Components;
using BlazeCommon;
using NLog;

namespace Zamboni.Components.Blaze;

internal class RedirectorComponent : RedirectorComponentBase.Server
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override Task<ServerInstanceInfo> GetServerInstanceAsync(ServerInstanceRequest request, BlazeRpcContext context)
    {
        try
        {
            Logger.Info("=== GetServerInstance Request Received ===");
            Logger.Info($"Connection ID: {context.Connection.ID}");
            Logger.Info($"Remote EndPoint: {context.Connection.Socket?.RemoteEndPoint}");
            Logger.Info($"Service Name (mName): {request.mName ?? "(null)"}");
            Logger.Info($"Client Name: {request.mClientName ?? "(null)"}");
            Logger.Info($"Client Version: {request.mClientVersion ?? "(null)"}");
            Logger.Info($"Client SkuId: {request.mClientSkuId ?? "(null)"}");
            Logger.Info($"Platform: {request.mPlatform ?? "(null)"}");
            Logger.Info($"Environment: {request.mEnvironment ?? "(null)"}");
            Logger.Info($"Connection Profile: {request.mConnectionProfile ?? "(null)"}");
            Logger.Info($"BlazeSDK Version: {request.mBlazeSDKVersion ?? "(null)"}");
            Logger.Info($"BlazeSDK Build Date: {request.mBlazeSDKBuildDate ?? "(null)"}");
            Logger.Info($"DirtySDK Version: {request.mDirtySDKVersion ?? "(null)"}");
            Logger.Info($"Client Locale: {request.mClientLocale} (0x{request.mClientLocale:X8})");
            Logger.Info($"FirstPartyId: {request.mFirstPartyId}");

            // Check if game server has SSL enabled
            bool gameServerSecure = Program.CoreServerCertificate != null;
            
            // Extract hostname from certificate CN for SSL validation
            // The client uses the hostname for SSL certificate validation, but connects using the IP address
            // This avoids DNS resolution while ensuring certificate validation passes
            string hostname = Program.GameServerIp; // Default to IP
            if (gameServerSecure && Program.CoreServerCertificate != null)
            {
                // Extract CN from certificate subject (format: "CN=gosredirector.ea.com,...")
                string subject = Program.CoreServerCertificate.Subject;
                int cnStart = subject.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
                if (cnStart >= 0)
                {
                    cnStart += 3; // Skip "CN="
                    int cnEnd = subject.IndexOf(',', cnStart);
                    if (cnEnd < 0) cnEnd = subject.Length;
                    string cn = subject.Substring(cnStart, cnEnd - cnStart).Trim();
                    if (!string.IsNullOrEmpty(cn))
                    {
                        hostname = cn;
                        Logger.Info($"Using certificate CN as hostname: {hostname} (IP: {Program.GameServerIp} will be used for connection)");
                    }
                }
            }
            
            var responseData = new ServerInstanceInfo
            {
                mAddress = new ServerAddress
                {
                    IpAddress = new IpAddress
                    {
                        mHostname = hostname, // Use hostname matching certificate if SSL
                        mIp = Util.GetIPAddressAsUInt(Program.GameServerIp), // IP client will connect to
                        mPort = Program.ZamboniConfig.GameServerPort
                    }
                },
                mSecure = gameServerSecure, // Tell client if game server uses SSL
                mDefaultDnsAddress = 0
            };

            Logger.Info("=== GetServerInstance Response ===");
            if (responseData.mAddress.IpAddress != null)
            {
                Logger.Info($"Server Address - Hostname: {responseData.mAddress.IpAddress.Value.mHostname}");
                Logger.Info($"Server Address - IP: {responseData.mAddress.IpAddress.Value.mIp} (0x{responseData.mAddress.IpAddress.Value.mIp:X8})");
                Logger.Info($"Server Address - Port: {responseData.mAddress.IpAddress.Value.mPort}");
            }
            else
            {
                Logger.Info("Server Address - IpAddress is null");
            }
            Logger.Info($"Secure: {responseData.mSecure}");
            Logger.Info($"DefaultDnsAddress: {responseData.mDefaultDnsAddress}");
            Logger.Info("=== GetServerInstance Complete ===");
            Logger.Warn($"=== CLIENT SHOULD NOW CONNECT TO CORESERVER ===");
            Logger.Warn($"Expected connection: Hostname={responseData.mAddress.IpAddress.Value.mHostname}, IP={Program.GameServerIp}, Port={responseData.mAddress.IpAddress.Value.mPort} (SSL: {responseData.mSecure})");
            Logger.Warn($"Note: Client should use IP ({Program.GameServerIp}) for connection, hostname ({responseData.mAddress.IpAddress.Value.mHostname}) for SSL validation");
            Logger.Warn($"Watch for log: ProtoFireServer(\"CoreServer\") accepted new socket connection");
            Logger.Warn($"If no connection appears, possible causes:");
            Logger.Warn($"  1. Client resolving hostname '{responseData.mAddress.IpAddress.Value.mHostname}' via DNS instead of using IP {Program.GameServerIp}");
            Logger.Warn($"     → Solution: Ensure hosts file points {responseData.mAddress.IpAddress.Value.mHostname} to {Program.GameServerIp}");
            Logger.Warn($"  2. SSL certificate validation failing (hostname mismatch or certificate rejection)");
            Logger.Warn($"     → Solution: Ensure Bug_OldProtoSSL patch is enabled to skip certificate validation");
            Logger.Warn($"  3. Windows Firewall blocking port {Program.ZamboniConfig.GameServerPort}");
            Logger.Warn($"  4. WSL2 port forwarding missing for {Program.ZamboniConfig.GameServerPort}");
            Logger.Warn($"  5. Client-side connection error (check RPCS3 logs/TTY for errors)");

            return Task.FromResult(responseData);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "=== GetServerInstance Exception ===");
            Logger.Error($"Exception Type: {ex.GetType().Name}");
            Logger.Error($"Exception Message: {ex.Message}");
            Logger.Error($"Stack Trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Logger.Error($"Inner Exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                Logger.Error($"Inner Stack Trace: {ex.InnerException.StackTrace}");
            }
            Logger.Error("=== GetServerInstance Exception End ===");
            throw;
        }
    }
}
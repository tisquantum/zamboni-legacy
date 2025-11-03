using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Blaze2SDK.Blaze;
using Blaze2SDK.Blaze.Authentication;
using Blaze2SDK.Components;
using BlazeCommon;
using NLog;
using XI5;

namespace Zamboni.Components.Blaze;

public class AuthenticationComponent : AuthenticationComponentBase.Server
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override Task<ConsoleLoginResponse> Ps3LoginAsync(PS3LoginRequest request, BlazeRpcContext context)
    {
        try
        {
            Logger.Info("=== PS3Login Request Received ===");
            Logger.Info($"Connection ID: {context.Connection.ID}");
            Logger.Info($"Remote EndPoint: {context.Connection.Socket?.RemoteEndPoint}");
            Logger.Info($"Request Email: {request.mEmail ?? "(null)"}");
            Logger.Info($"PS3 Ticket Length: {request.mPS3Ticket?.Length ?? 0} bytes");
            
            if (request.mPS3Ticket != null && request.mPS3Ticket.Length > 0)
            {
                Logger.Debug($"PS3 Ticket (first 32 bytes hex): {BitConverter.ToString(request.mPS3Ticket.Take(32).ToArray())}");
            }

            var ticket = new XI5Ticket(request.mPS3Ticket);
            
            Logger.Info($"Parsed Ticket - OnlineId: {ticket.OnlineId}, UserId: {ticket.UserId}, Domain: {ticket.Domain}, Region: {ticket.Region}");

            //Still unsure what EXBB is. Research concluded its
            //`externalblob` binary(36) DEFAULT NULL COMMENT 'sizeof(SceNpId)==36',
            //"SceNpId", Its 36 bytes long, it starts with PSN Username and suffixed with other data in the end
            //This taken straight from https://github.com/hallofmeat/Skateboard3Server/blob/master/src/Skateboard3Server.Blaze/Handlers/Authentication/LoginHandler.cs
            var externalBlob = new List<byte>();
            externalBlob.AddRange(Encoding.ASCII.GetBytes(ticket.OnlineId.PadRight(20, '\0')));
            externalBlob.AddRange(Encoding.ASCII.GetBytes(ticket.Domain));
            externalBlob.AddRange(Encoding.ASCII.GetBytes(ticket.Region));
            externalBlob.AddRange(Encoding.ASCII.GetBytes("ps3"));
            externalBlob.Add(0x0);
            externalBlob.Add(0x1);
            externalBlob.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

            Logger.Warn(ticket.OnlineId + " connected");
            foreach (var zamboniUser in Manager.ZamboniUsers.ToList().Where(zamboniUser => zamboniUser.Username.Equals(ticket.OnlineId))) Manager.ZamboniUsers.Remove(zamboniUser);
            var user = new ZamboniUser(context.BlazeConnection, ticket.UserId, ticket.OnlineId, externalBlob.ToArray());

            Task.Run(async () =>
            {
                await Task.Delay(100);
            UserSessionsBase.Server.NotifyUserAddedAsync(user.BlazeServerConnection, new UserIdentification
            {
                mAccountLocale = 1701729619,
                mExternalId = ticket.UserId,
                mBlazeId = (uint)ticket.UserId,
                mName = ticket.OnlineId,
                mPersonaId = ticket.OnlineId,
                mExternalBlob = externalBlob.ToArray()
            });
        });

            Task.Run(async () =>
            {
                await Task.Delay(200);
                UserSessionsBase.Server.NotifyUserSessionExtendedDataUpdateAsync(user.BlazeServerConnection,
                    new UserSessionExtendedDataUpdate
                    {
                        mExtendedData = new UserSessionExtendedData
                        {
                            mAddress = null!,
                            mBestPingSiteAlias = "qos",
                            mClientAttributes = new SortedDictionary<uint, int>(),
                            mCountry = "",
                            mDataMap = new SortedDictionary<uint, int>(),
                            mHardwareFlags = HardwareFlags.None,
                            mLatencyList = new List<int>
                            {
                                10
                            },
                            mQosData = default,
                            mUserInfoAttribute = 0,
                            mBlazeObjectIdList = new List<ulong>()
                        },
                        mUserId = (uint)ticket.UserId
                    });
            });

            var response = new ConsoleLoginResponse
            {
                mSessionInfo = new SessionInfo
                {
                    mBlazeUserId = (uint)ticket.UserId,
                    mSessionKey = ticket.UserId.ToString(),
                    mEmail = "",
                    mPersonaDetails = new PersonaDetails
                    {
                        mDisplayName = ticket.OnlineId,
                        mLastAuthenticated = 0,
                        mPersonaId = (long)ticket.UserId,
                        mExtId = ticket.UserId,
                        mExtType = ExternalRefType.PS3
                    },
                    mUserId = (long)ticket.UserId
                },
                mTosHost = "",
                mTosUri = ""
            };

            Logger.Info("=== PS3Login Response ===");
            Logger.Info($"SessionInfo - BlazeUserId: {response.mSessionInfo.mBlazeUserId}, UserId: {response.mSessionInfo.mUserId}, SessionKey: {response.mSessionInfo.mSessionKey}");
            Logger.Info($"PersonaDetails - DisplayName: {response.mSessionInfo.mPersonaDetails.mDisplayName}, PersonaId: {response.mSessionInfo.mPersonaDetails.mPersonaId}, ExtId: {response.mSessionInfo.mPersonaDetails.mExtId}, ExtType: {response.mSessionInfo.mPersonaDetails.mExtType}");
            Logger.Info($"TosHost: {response.mTosHost}, TosUri: {response.mTosUri}");
            Logger.Info("=== PS3Login Complete ===");

            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "=== PS3Login Exception ===");
            Logger.Error($"Exception Type: {ex.GetType().Name}");
            Logger.Error($"Exception Message: {ex.Message}");
            Logger.Error($"Stack Trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Logger.Error($"Inner Exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                Logger.Error($"Inner Stack Trace: {ex.InnerException.StackTrace}");
            }
            Logger.Error("=== PS3Login Exception End ===");
            throw;
        }
    }


    public override Task<NullStruct> LogoutAsync(NullStruct request, BlazeRpcContext context)
    {
        var leaver = Manager.GetZamboniUser(context.BlazeConnection);
        if (leaver != null) Manager.ZamboniUsers.Remove(leaver);
        if (leaver != null) Manager.QueuedMatchZamboniUsers.Remove(leaver);
        if (leaver != null) Manager.QueuedShootoutZamboniUsers.Remove(leaver);
        return Task.FromResult(new NullStruct());
    }

    public override Task<NullStruct> CreateWalUserSessionAsync(NullStruct request, BlazeRpcContext context)
    {
        return Task.FromResult(new NullStruct());
    }
}
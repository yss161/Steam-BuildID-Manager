using System.Text.Json;
using SteamBuildIdManager.Core.Models;
using SteamKit2;

namespace SteamBuildIdManager.Core.Services;

/// <summary>
/// Queries public product information straight from Steam with an anonymous PICS session.
/// Each call uses its own connection, a hard timeout and an ephemeral machine identity;
/// no credentials are read and no hardware identifiers leave the process.
/// </summary>
public sealed class SteamLiveAppInfoService : ISteamAppInfoService
{
    private readonly TimeSpan _timeout;

    public SteamLiveAppInfoService(TimeSpan? timeout = null) => _timeout = timeout ?? TimeSpan.FromSeconds(25);

    public Task<SteamAppInfo> GetAppInfoAsync(string appId, CancellationToken cancellationToken = default)
    {
        if (!SteamAppInfoService.TryNormalizeAppId(appId, out var normalized))
            throw new SteamAppInfoException("AppID 必须为 1 到 4294967295 之间的整数。");
        return Task.Run(() => Query(normalized, cancellationToken), cancellationToken);
    }

    private SteamAppInfo Query(string appId, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);

        var client = new SteamClient(SteamConfiguration.Create(builder => builder
            .WithProtocolTypes(ProtocolTypes.WebSocket | ProtocolTypes.Tcp)
            .WithMachineInfoProvider(AnonymousQueryIdentity.Instance)));
        var manager = new CallbackManager(client);
        var user = client.GetHandler<SteamUser>()!;
        var apps = client.GetHandler<SteamApps>()!;
        var id = uint.Parse(appId, System.Globalization.CultureInfo.InvariantCulture);

        SteamAppInfo? result = null;
        string? error = null;
        var reconnects = 0;

        using var connected = manager.Subscribe<SteamClient.ConnectedCallback>(_ => user.LogOnAnonymous());
        using var disconnected = manager.Subscribe<SteamClient.DisconnectedCallback>(_ =>
        {
            if (!deadline.IsCancellationRequested && reconnects++ < 1) client.Connect();
            else error ??= "Steam 连接已断开。";
        });
        using var loggedOn = manager.Subscribe<SteamUser.LoggedOnCallback>(callback =>
        {
            if (callback.Result != EResult.OK)
            {
                error ??= $"Steam 匿名连接失败：{callback.Result}";
                return;
            }
            apps.PICSGetAccessTokens(id, null);
        });
        using var tokens = manager.Subscribe<SteamApps.PICSTokensCallback>(callback =>
        {
            callback.AppTokens.TryGetValue(id, out var accessToken);
            apps.PICSGetProductInfo(new SteamApps.PICSRequest(id, accessToken), null);
        });
        using var products = manager.Subscribe<SteamApps.PICSProductInfoCallback>(callback =>
        {
            if (callback.Apps.TryGetValue(id, out var info))
            {
                try
                {
                    result = ConvertProduct(info.KeyValues, appId) with
                    {
                        Source = "Steam 直连查询（公开产品信息）",
                        RetrievedAt = DateTimeOffset.Now,
                    };
                }
                catch (SteamAppInfoException ex)
                {
                    error ??= ex.Message;
                }
            }
            else if (!callback.ResponsePending)
            {
                error ??= "Steam 未提供该游戏的公开产品信息。";
            }
        });

        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            client.Connect();
            while (result == null && error == null)
            {
                deadline.Token.ThrowIfCancellationRequested();
                manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
            }

            deadline.Token.ThrowIfCancellationRequested();
            return result ?? throw new SteamAppInfoException(error ?? "Steam 直连查询没有返回数据。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SteamAppInfoException($"Steam 直连查询超时（{_timeout.TotalSeconds:0} 秒）。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SteamAppInfoException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SteamAppInfoException("Steam 直连查询失败：" + ex.Message, ex);
        }
        finally
        {
            try { client.Disconnect(); } catch { }
        }
    }

    /// <summary>Flatten SteamKit's KeyValue tree into the JSON shape the shared parser expects.</summary>
    public static SteamAppInfo ConvertProduct(KeyValue root, string appId)
    {
        object Convert(KeyValue node, int depth)
        {
            if (depth > 64) throw new SteamAppInfoException("Steam 产品信息嵌套过深。");
            if (node.Children.Count == 0) return node.Value ?? "";
            var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in node.Children)
            {
                if (child.Name == null || !fields.TryAdd(child.Name, Convert(child, depth + 1)))
                    throw new SteamAppInfoException("Steam 产品信息包含重复或无效字段。");
            }
            return fields;
        }

        if (root["appid"].Value != appId)
            throw new SteamAppInfoException("Steam 返回的 AppID 不匹配或数据不完整。");

        var json = JsonSerializer.Serialize(new
        {
            status = "success",
            data = new Dictionary<string, object> { [appId] = Convert(root, 0) },
        });
        return SteamAppInfoParser.Parse(json, appId);
    }

    /// <summary>
    /// Public metadata does not need the user's hardware identifiers, so a random per-process
    /// value is used instead of the machine GUID, MAC address or disk serial.
    /// </summary>
    private sealed class AnonymousQueryIdentity : IMachineInfoProvider
    {
        public static AnonymousQueryIdentity Instance { get; } = new();

        private readonly byte[] _id = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

        public byte[] GetMachineGuid() => (byte[])_id.Clone();

        public byte[] GetMacAddress() => (byte[])_id.Clone();

        public byte[] GetDiskId() => (byte[])_id.Clone();
    }
}

/// <summary>
/// Preferred online source: try the direct Steam query first, then fall back to the third-party
/// mirror. The fallback is always labelled so the UI never implies real-time freshness.
/// </summary>
public sealed class FreshSteamAppInfoService : ISteamAppInfoService
{
    private readonly ISteamAppInfoService _live;
    private readonly ISteamAppInfoService _fallback;

    public FreshSteamAppInfoService(ISteamAppInfoService? live = null, ISteamAppInfoService? fallback = null)
    {
        _live = live ?? new SteamLiveAppInfoService();
        _fallback = fallback ?? new SteamAppInfoService();
    }

    public async Task<SteamAppInfo> GetAppInfoAsync(string appId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _live.GetAppInfoAsync(appId, cancellationToken).ConfigureAwait(false);
        }
        catch (SteamAppInfoException liveError)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var app = await _fallback.GetAppInfoAsync(appId, cancellationToken).ConfigureAwait(false);
                return app with
                {
                    Source = "第三方备用接口（不保证实时；" + liveError.Message + "）",
                    RetrievedAt = DateTimeOffset.Now,
                };
            }
            catch (SteamAppInfoException fallbackError)
            {
                throw new SteamAppInfoException(liveError.Message + " " + fallbackError.Message, fallbackError);
            }
        }
    }
}
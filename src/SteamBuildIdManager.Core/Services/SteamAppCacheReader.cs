using System.Text;
using System.Text.Json;
using SteamBuildIdManager.Core.Models;

namespace SteamBuildIdManager.Core.Services;

/// <summary>
/// Read-only reader for the Steam client's own <c>appcache\appinfo.vdf</c> (formats 39-41).
/// This is the data Steam itself believes, so it stays correct when third-party mirrors lag.
/// Unknown formats fail closed instead of guessing.
/// </summary>
public static class SteamAppCacheReader
{
    public const uint MagicV39 = 0x07564427;
    public const uint MagicV40 = 0x07564428;
    public const uint MagicV41 = 0x07564429;

    /// <summary>Default location for a detected Steam install; returns null when the file is absent.</summary>
    public static string? FindCachePath(string? steamInstallPath)
    {
        if (string.IsNullOrWhiteSpace(steamInstallPath)) return null;
        var path = Path.Combine(steamInstallPath.Trim(), "appcache", "appinfo.vdf");
        return File.Exists(path) ? path : null;
    }

    public static Task<SteamAppInfo> ReadAsync(string path, string appId, CancellationToken token = default)
        => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Read(stream, appId, token);
        }, token);

    public static SteamAppInfo Read(Stream stream, string appId, CancellationToken token = default)
    {
        if (!SteamAppInfoService.TryNormalizeAppId(appId, out var normalized))
            throw new InvalidDataException("AppID 无效。");
        if (!uint.TryParse(normalized, out var wanted) || wanted == 0)
            throw new InvalidDataException("AppID 无效。");

        using var reader = new BinaryReader(stream, new UTF8Encoding(false, true), leaveOpen: true);
        var magic = reader.ReadUInt32();
        if (magic is not (MagicV39 or MagicV40 or MagicV41))
            throw new InvalidDataException("不支持的 Steam appinfo 缓存版本。");
        if (reader.ReadUInt32() != 1) throw new InvalidDataException("非公开 Steam universe。");

        string[]? strings = null;
        long recordsEnd = stream.Length;
        if (magic == MagicV41)
        {
            recordsEnd = reader.ReadInt64();
            if (recordsEnd < 16 || recordsEnd > stream.Length - 4) throw new InvalidDataException("appinfo 字符串表位置无效。");
            stream.Position = recordsEnd;
            var count = reader.ReadUInt32();
            if (count > 1_000_000) throw new InvalidDataException("appinfo 字符串表过大。");
            strings = new string[count];
            for (var i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                strings[i] = ReadCString(reader);
            }
            stream.Position = 16;
        }

        var headerSize = magic == MagicV39 ? 40 : 60;
        while (stream.Position + 8 <= recordsEnd)
        {
            token.ThrowIfCancellationRequested();
            var id = reader.ReadUInt32();
            if (id == 0) break;
            var size = reader.ReadUInt32();
            var end = stream.Position + size;
            if (size < headerSize || end > recordsEnd) throw new InvalidDataException("appinfo 记录长度无效。");
            if (id != wanted)
            {
                stream.Position = end;
                continue;
            }

            stream.Position += headerSize;
            var payloadLength = end - stream.Position;
            if (payloadLength <= 0 || payloadLength > 16 * 1024 * 1024)
                throw new InvalidDataException("appinfo 记录过大或为空。");

            using var payload = new MemoryStream(reader.ReadBytes(checked((int)payloadLength)));
            using var data = new BinaryReader(payload, new UTF8Encoding(false, true));
            var root = ReadObject(data, strings, token, 0);
            if (payload.Position != payload.Length
                || !root.TryGetValue("appinfo", out var app)
                || app is not Dictionary<string, object> fields
                || !fields.TryGetValue("appid", out var embedded)
                || embedded.ToString() != normalized)
                throw new InvalidDataException("appinfo 内容无效。");

            // Reuse the single normalized parser so no metadata is mixed across sources.
            var json = JsonSerializer.Serialize(new
            {
                status = "success",
                data = new Dictionary<string, object> { [normalized] = app },
            });
            return SteamAppInfoParser.Parse(json, normalized) with
            {
                Source = "本机 Steam 缓存（appinfo.vdf）",
                RetrievedAt = DateTimeOffset.Now,
            };
        }

        throw new InvalidDataException("本机 Steam 缓存中没有该游戏信息。");
    }

    private static Dictionary<string, object> ReadObject(BinaryReader reader, string[]? strings,
        CancellationToken token, int depth)
    {
        if (depth > 64) throw new InvalidDataException("appinfo 嵌套过深。");
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var type = reader.ReadByte();
            if (type == 8) return result;

            string key;
            if (strings == null) key = ReadCString(reader);
            else
            {
                var index = reader.ReadUInt32();
                if (index >= strings.Length) throw new InvalidDataException("appinfo 字符串索引无效。");
                key = strings[index];
            }

            object value = type switch
            {
                0 => ReadObject(reader, strings, token, depth + 1),
                1 => ReadCString(reader),
                2 => reader.ReadInt32(),
                3 => reader.ReadSingle(),
                7 => reader.ReadUInt64(),
                10 => reader.ReadInt64(),
                _ => throw new InvalidDataException($"不支持的 appinfo 字段类型 {type}。"),
            };
            if (!result.TryAdd(key, value)) throw new InvalidDataException("appinfo 存在重复字段。");
        }
    }

    private static string ReadCString(BinaryReader reader)
    {
        using var bytes = new MemoryStream();
        for (var i = 0; i < 1024 * 1024; i++)
        {
            var value = reader.ReadByte();
            if (value == 0) return new UTF8Encoding(false, true).GetString(bytes.ToArray());
            bytes.WriteByte(value);
        }
        throw new InvalidDataException("appinfo 字符串过长。");
    }
}
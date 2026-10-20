using System.Collections.Concurrent;
using System.Text;

namespace SteamBuildIdManager.Core.Services;

/// <summary>
/// Small in-process log buffer plus an optional rolling file. The UI subscribes to <see cref="EntryAdded"/>
/// and the file copy keeps a record after the window is closed.
/// </summary>
public static class AppLog
{
    private const int MaxEntries = 500;
    private static readonly object SyncRoot = new();
    private static readonly ConcurrentQueue<LogEntry> Entries = new();
    private static readonly string LogFilePath = Path.Combine(AppPaths.LogDirectory, "app.log");

    public static event EventHandler<LogEntry>? EntryAdded;

    public static IReadOnlyList<LogEntry> Snapshot()
    {
        lock (SyncRoot) return Entries.ToArray();
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            while (Entries.TryDequeue(out _)) { }
        }
    }

    public static void Info(string message) => Write("信息", message);

    public static void Success(string message) => Write("成功", message);

    public static void Warning(string message) => Write("警告", message);

    public static void Error(string message) => Write("错误", message);

    public static void Error(string message, Exception exception) =>
        Write("错误", $"{message}：{exception.GetType().Name} {exception.Message}");

    private static void Write(string level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message ?? "");
        lock (SyncRoot)
        {
            Entries.Enqueue(entry);
            while (Entries.Count > MaxEntries) Entries.TryDequeue(out _);
        }

        AppendToFile(entry);

        try
        {
            EntryAdded?.Invoke(null, entry);
        }
        catch
        {
            // A faulty subscriber must not break the caller.
        }
    }

    private static void AppendToFile(LogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Level}] {entry.Message}{Environment.NewLine}";
            File.AppendAllText(LogFilePath, line, Encoding.UTF8);
        }
        catch
        {
            // Logging to disk is best-effort only.
        }
    }
}

public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Message)
{
    public string DisplayText => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";
}

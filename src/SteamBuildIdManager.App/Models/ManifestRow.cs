using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using SteamBuildIdManager.Core.Models;
using Windows.UI;

namespace SteamBuildIdManager.Models;

/// <summary>Display state for one appmanifest row. All mutations happen on the UI thread.</summary>
public sealed class ManifestRow : INotifyPropertyChanged
{
    private static readonly SolidColorBrush UpToDateBrush = new(Color.FromArgb(255, 16, 124, 65));
    private static readonly SolidColorBrush UpdateBrush = new(Color.FromArgb(255, 202, 80, 16));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromArgb(255, 196, 43, 28));
    private static readonly SolidColorBrush UnknownBrush = new(Color.FromArgb(255, 128, 128, 128));

    public ManifestRow(AcfManifest manifest)
    {
        Manifest = manifest;
    }

    public AcfManifest Manifest { get; private set; }
    public string? LatestBuildId { get; private set; }
    public BuildIdState State { get; private set; } = BuildIdState.Unknown;
    public string Message { get; private set; } = "尚未检测";

    public string Name => string.IsNullOrWhiteSpace(Manifest.Name) ? "未命名配置" : Manifest.Name;
    public string AppId => Manifest.AppId;
    public string BuildId => Manifest.BuildIdDisplay;
    public string LatestBuildIdDisplay => string.IsNullOrWhiteSpace(LatestBuildId) ? "—" : LatestBuildId;
    public string InstallDirectory => Manifest.InstallDirectory;
    public string Subtitle => string.IsNullOrWhiteSpace(Manifest.InstallDirectory)
        ? Manifest.LibraryPath
        : $"{Manifest.InstallDirectory}  ·  {Path.GetFileName(Manifest.LibraryPath)}";

    public string StatusText => State switch
    {
        BuildIdState.UpToDate => "已是最新",
        BuildIdState.UpdateAvailable => "发现更新",
        BuildIdState.Error => "检测失败",
        _ => "未检测",
    };

    public string StatusGlyph => State switch
    {
        BuildIdState.UpToDate => "\uE73E",
        BuildIdState.UpdateAvailable => "\uE895",
        BuildIdState.Error => "\uE7BA",
        _ => "\uE946",
    };

    public SolidColorBrush StatusBrush => State switch
    {
        BuildIdState.UpToDate => UpToDateBrush,
        BuildIdState.UpdateAvailable => UpdateBrush,
        BuildIdState.Error => ErrorBrush,
        _ => UnknownBrush,
    };

    public bool CanUpdate => State == BuildIdState.UpdateAvailable
        && !string.IsNullOrWhiteSpace(LatestBuildId);

    public string ToolTip
    {
        get
        {
            var text = $"{Name}（{AppId}）\nACF：{Manifest.FilePath}\n状态：{Message}";
            if (!string.IsNullOrWhiteSpace(Message) && Message != StatusText) text += $"\n{Message}";
            return text;
        }
    }

    public void Apply(BuildIdCheckResult result)
    {
        Manifest = result.Manifest;
        LatestBuildId = result.LatestBuildId;
        State = result.State;
        Message = result.Message;
        RaiseAll();
    }

    public void MarkUpdated(AcfManifest manifest, string latestBuildId)
    {
        Manifest = manifest;
        LatestBuildId = latestBuildId;
        State = BuildIdState.UpToDate;
        Message = $"已更新到 {latestBuildId}";
        RaiseAll();
    }

    public void MarkUnknown()
    {
        State = BuildIdState.Unknown;
        LatestBuildId = null;
        Message = "尚未检测";
        RaiseAll();
    }

    /// <summary>Keep a completed check when a refresh reads the same ACF BuildID.</summary>
    public void CopyStateFrom(ManifestRow other)
    {
        LatestBuildId = other.LatestBuildId;
        State = other.State;
        Message = other.Message;
        RaiseAll();
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
                 {
                     nameof(Manifest), nameof(LatestBuildId), nameof(State), nameof(Message),
                     nameof(BuildId), nameof(LatestBuildIdDisplay), nameof(StatusText),
                     nameof(StatusGlyph), nameof(StatusBrush), nameof(CanUpdate), nameof(ToolTip),
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
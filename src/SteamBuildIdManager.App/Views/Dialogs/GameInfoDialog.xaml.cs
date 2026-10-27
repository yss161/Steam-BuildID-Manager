using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamBuildIdManager.Core.Models;
using SteamBuildIdManager.Core.Services;
using Windows.ApplicationModel.DataTransfer;

namespace SteamBuildIdManager.Views.Dialogs;

/// <summary>Read-only Steam metadata lookup: name, install dir, BuildID, primary Depot and Manifest.</summary>
public sealed partial class GameInfoDialog : ContentDialog
{
    private readonly SteamGameLookupService _lookup = new();
    private SteamGameSummary? _summary;
    private bool _busy;

    public GameInfoDialog(string? initialAppId = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(initialAppId)) AppIdBox.Text = initialAppId;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;
        PrimaryButtonClick += OnPrimaryButtonClick;
        SecondaryButtonClick += OnSecondaryButtonClick;
    }

    /// <summary>Launch command built from the primary executable, offered as a copyable shortcut.</summary>
    public string LaunchCommand { get; private set; } = "";

    /// <summary>Set when the user asks to turn the fetched metadata into a new Steam config.</summary>
    public SteamGameSummary? CreatePreset { get; private set; }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_summary == null || !CanCreateConfig(_summary))
        {
            args.Cancel = true;
            return;
        }

        CreatePreset = _summary;
    }

    private static bool CanCreateConfig(SteamGameSummary summary) =>
        summary.Depots.Count > 0
        && !string.IsNullOrWhiteSpace(summary.BuildId)
        && !string.IsNullOrWhiteSpace(summary.DisplayName)
        && !string.IsNullOrWhiteSpace(summary.InstallDirectory)
        && !string.IsNullOrWhiteSpace(summary.PlaceholderExeName);

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(LaunchCommand)) return;
        try
        {
            var package = new DataPackage();
            package.SetText(LaunchCommand);
            Clipboard.SetContent(package);
        }
        catch
        {
            // Clipboard failures must not block closing the dialog.
        }
    }

    private async void Lookup_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!SteamAppInfoService.TryNormalizeAppId(AppIdBox.Text, out var appId))
        {
            ShowStatus("AppID 无效", "AppID 必须是 1 到 4294967295 之间的整数。", InfoBarSeverity.Error);
            return;
        }

        _busy = true;
        LookupButton.IsEnabled = false;
        LookupButton.Content = "获取中…";
        IsPrimaryButtonEnabled = false;
        ShowStatus("正在获取", $"正在读取 AppID {appId} 的 public 分支信息…", InfoBarSeverity.Informational);
        try
        {
            _summary = await _lookup.LookupAsync(appId);
            Render(_summary);
            IsPrimaryButtonEnabled = _summary.Executables.Count > 0;
            IsSecondaryButtonEnabled = CanCreateConfig(_summary);

            ShowStatus(_summary.Warnings.Count == 0 ? "获取成功" : "部分信息缺失",
                _summary.Warnings.Count == 0
                    ? $"BuildID {_summary.BuildId} · {_summary.Depots.Count} 个 Depot。"
                    : string.Join("\n", _summary.Warnings),
                _summary.Warnings.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            PrimaryButtonText = string.IsNullOrWhiteSpace(LaunchCommand) ? "关闭" : "复制启动命令并关闭";
        }
        catch (Exception ex)
        {
            _summary = null;
            Clear();
            IsSecondaryButtonEnabled = false;
            ShowStatus("获取失败", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _busy = false;
            LookupButton.IsEnabled = true;
            LookupButton.Content = "获取信息";
        }
    }

    private void Render(SteamGameSummary summary)
    {
        NameText.Text = string.IsNullOrWhiteSpace(summary.DisplayName) ? "—" : summary.DisplayName;
        InstallDirText.Text = string.IsNullOrWhiteSpace(summary.InstallDirectory) ? "—" : summary.InstallDirectory;
        BuildIdText.Text = string.IsNullOrWhiteSpace(summary.BuildId) ? "—" : summary.BuildId;
        DepotIdText.Text = summary.PrimaryDepotDisplay;
        ManifestText.Text = summary.PrimaryManifestDisplay;
        ExecutableText.Text = summary.Executables.Count == 0 ? "—" : summary.Executables[0].Path;
        PlaceholderText.Text = summary.PlaceholderExeDisplay;
        DepotsText.Text = summary.DepotSummary;
        LaunchCommand = summary.Executables.Count == 0
            ? ""
            : $"\"{summary.Executables[0].Path}\" %command%";
    }

    private void Clear()
    {
        NameText.Text = "—";
        InstallDirText.Text = "—";
        BuildIdText.Text = "—";
        DepotIdText.Text = "—";
        ManifestText.Text = "—";
        ExecutableText.Text = "—";
        PlaceholderText.Text = "—";
        DepotsText.Text = "尚未获取";
        LaunchCommand = "";
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}

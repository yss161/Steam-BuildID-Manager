using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamBuildIdManager.Core.Models;
using SteamBuildIdManager.Core.Services;
using SteamBuildIdManager.Services;

namespace SteamBuildIdManager.Views.Dialogs;

public sealed partial class GameConfigDialog : ContentDialog
{
    private readonly SteamAppInfoService _appInfoService = new();
    private readonly GameConfigService _configService = new();
    private readonly AppSettings _settings;

    private IReadOnlyList<SteamDepotCandidate> _depots = Array.Empty<SteamDepotCandidate>();
    private string? _fetchedAppId;
    private bool _busy;

    public GameConfigDialog(AppSettings settings, IReadOnlyList<string> libraryRoots, AcfManifest? existing = null)
    {
        InitializeComponent();
        _settings = settings;

        LibraryCombo.ItemsSource = libraryRoots;
        if (libraryRoots.Count > 0) LibraryCombo.SelectedIndex = 0;

        if (existing == null) return;

        Title = "重建 Steam 配置";
        AppIdBox.Text = existing.AppId;
        AppIdBox.IsReadOnly = true;
        AppIdBox.IsEnabled = false;
        NameBox.Text = existing.Name;
        InstallDirBox.Text = existing.InstallDirectory;
        BuildIdBox.Text = existing.BuildId;
        var selectedLibrary = libraryRoots.FirstOrDefault(path =>
            string.Equals(SteamLocator.NormalizeLibraryRoot(path), SteamLocator.NormalizeLibraryRoot(existing.LibraryPath),
                StringComparison.OrdinalIgnoreCase));
        if (selectedLibrary != null) LibraryCombo.SelectedItem = selectedLibrary;
    }

    public GameConfigWriteResult? SavedResult { get; private set; }

    private async void Fetch_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!SteamAppInfoService.TryNormalizeAppId(AppIdBox.Text, out var appId))
        {
            ShowStatus("AppID 无效", "AppID 必须是 1 到 4294967295 之间的整数。", InfoBarSeverity.Error);
            return;
        }

        SetBusy(true, "正在获取 Steam 信息…");
        AppLog.Info($"开始获取 AppID {appId} 的游戏信息（api.steamcmd.net）。");
        _depots = Array.Empty<SteamDepotCandidate>();
        _fetchedAppId = null;
        DepotsText.Text = "正在读取 Depot…";
        try
        {
            var app = await _appInfoService.GetAppInfoAsync(appId);
            var options = new SteamAppSelectionOptions(
                Architecture: Environment.Is64BitOperatingSystem ? "64" : "32");
            var selection = SteamAppSelectionService.Select(app, options);
            var depotSet = SteamAppSelectionService.ChooseDepotsForAutomaticUpdate(selection);
            _depots = depotSet.Candidates;
            _fetchedAppId = appId;

            if (!string.IsNullOrWhiteSpace(selection.DisplayName)) NameBox.Text = selection.DisplayName;
            if (!string.IsNullOrWhiteSpace(selection.InstallDirectory)) InstallDirBox.Text = selection.InstallDirectory;
            if (!string.IsNullOrWhiteSpace(selection.BuildId)) BuildIdBox.Text = selection.BuildId;

            ExecutableCombo.ItemsSource = selection.Executables;
            ExecutableCombo.Visibility = selection.Executables.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            var executable = SteamAppSelectionService.ChooseExecutableForAutomaticUpdate(selection, ExeBox.Text);
            if (executable.Candidate != null)
            {
                ExeBox.Text = executable.Candidate.Path;
                ExecutableCombo.SelectedItem = executable.Candidate;
            }

            // The placeholder EXE must match the filename Steam recorded in the appinfo,
            // otherwise Steam cannot launch the configuration.
            if (string.IsNullOrWhiteSpace(ExeBox.Text))
            {
                var inferred = SteamPlaceholderName.FromExecutables(selection.Executables);
                if (string.IsNullOrWhiteSpace(inferred))
                    inferred = SteamPlaceholderName.FromInstallDirectory(selection.InstallDirectory);
                if (!string.IsNullOrWhiteSpace(inferred))
                {
                    ExeBox.Text = inferred;
                    AppLog.Info($"已根据 AppID {appId} 自动填入 Steam 占位文件名：{inferred}");
                }
            }

            DepotsText.Text = _depots.Count == 0
                ? "没有找到带有 public Manifest 的兼容 Depot。"
                : string.Join("\n", _depots.Select(candidate =>
                    $"{candidate.Depot.Id} · Manifest {candidate.Manifest}"));

            AppLog.Success($"已获取 AppID {appId}：{selection.DisplayName} · BuildID {selection.BuildId} · " +
                           $"{_depots.Count} 个 Depot · 占位文件 {ExeBox.Text}");

            var errors = new[]
                {
                    depotSet.IsSuccess ? "" : depotSet.Error,
                    executable.Candidate == null ? executable.Error : "",
                    string.IsNullOrWhiteSpace(selection.DisplayName) ? "没有获取到显示名称。" : "",
                    string.IsNullOrWhiteSpace(selection.InstallDirectory) ? "没有获取到安装目录名。" : "",
                    string.IsNullOrWhiteSpace(selection.BuildId) ? "public 分支没有可用 BuildID。" : "",
                    string.IsNullOrWhiteSpace(ExeBox.Text) ? "没有获取到 Steam 占位文件名。" : "",
                }
                .Where(message => !string.IsNullOrWhiteSpace(message)).ToList();

            if (errors.Count > 0)
                ShowStatus("信息不完整", string.Join("\n", errors) + "\n请核对后手动填写缺失字段。", InfoBarSeverity.Warning);
            else
                ShowStatus("已获取 Steam 信息",
                    $"BuildID {selection.BuildId} · {_depots.Count} 个 Depot · 占位文件 {ExeBox.Text}。" +
                    (selection.Warnings.Count > 0 ? "\n" + string.Join("\n", selection.Warnings) : ""),
                    InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            AppLog.Error($"获取 AppID {appId} 失败", ex);
            ShowStatus("获取失败", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ExecutableCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExecutableCombo.SelectedItem is SteamExecutableCandidate candidate)
            ExeBox.Text = candidate.Path;
    }

    private async void BrowseRealExe_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickExecutableAsync();
        if (!string.IsNullOrWhiteSpace(path)) RealExeBox.Text = path;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = BuildRequest(out var error);
            if (request == null)
            {
                args.Cancel = true;
                ShowStatus("无法保存", error ?? "请检查输入。", InfoBarSeverity.Error);
                return;
            }

            SetBusy(true, "正在写入 ACF…");
            try
            {
                SavedResult = await _configService.WriteAsync(request);
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                ShowStatus("保存失败", ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private GameConfigRequest? BuildRequest(out string? error)
    {
        error = null;
        if (!SteamAppInfoService.TryNormalizeAppId(AppIdBox.Text, out var appId))
        {
            error = "AppID 必须是 1 到 4294967295 之间的整数。";
            return null;
        }
        if (!string.Equals(_fetchedAppId, appId, StringComparison.Ordinal))
        {
            error = "请先点击“获取 Steam 信息”，确认 Depot 与当前 AppID 匹配。";
            return null;
        }

        var library = LibraryCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(library))
        {
            error = "请选择写入的 Steam 库。";
            return null;
        }

        var request = new GameConfigRequest(
            AppId: appId,            DisplayName: NameBox.Text.Trim(),
            InstallDirectory: InstallDirBox.Text.Trim(),
            PlaceholderExeName: ExeBox.Text.Trim(),
            BuildId: BuildIdBox.Text.Trim(),
            SteamId: _settings.SteamId,
            Depots: _depots.Select(candidate => new SteamDepotConfig
            {
                DepotId = candidate.Depot.Id,
                ManifestId = candidate.Manifest,
                Size = "0",
            }).ToList(),
            LibraryPath: library,
            SteamInstallPath: _settings.SteamInstallPath,
            RealGameExePath: RealExeBox.Text.Trim(),
            LaunchArguments: ArgumentsBox.Text.Trim());

        error = GameConfigService.Validate(request, out _, out _, out _);
        return error == null ? request : null;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        FetchButton.IsEnabled = !busy;
        FetchButton.Content = busy ? "处理中…" : "获取 Steam 信息";
        IsPrimaryButtonEnabled = !busy;
        if (busy && message != null) ShowStatus("请稍候", message, InfoBarSeverity.Informational);
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
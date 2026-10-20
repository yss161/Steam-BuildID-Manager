using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SteamBuildIdManager.Core.Models;
using SteamBuildIdManager.Core.Services;
using SteamBuildIdManager.Models;
using SteamBuildIdManager.Views.Dialogs;
using Windows.ApplicationModel.DataTransfer;

namespace SteamBuildIdManager.Views.Pages;

public sealed partial class LibraryPage : Page
{
    private readonly SettingsService _settingsService = new();
    private readonly BuildIdUpdateService _updateService = new(new SteamBuildIdProvider());
    private readonly ObservableCollection<ManifestRow> _rows = new();

    private AppSettings _settings = new();
    private IReadOnlyList<AcfManifest> _manifests = Array.Empty<AcfManifest>();
    private bool _busy;
    private bool _suppressToggle;

    public LibraryPage()
    {
        InitializeComponent();
        ManifestList.ItemsSource = _rows;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await RefreshAsync(autoCheck: true);
    }

    private async Task RefreshAsync(bool autoCheck)
    {
        if (_busy) return;

        _settings = _settingsService.Load();
        _suppressToggle = true;
        ShowAllToggle.IsOn = _settings.ShowAllSteamGames;
        _suppressToggle = false;

        var roots = ResolveLibraryRoots(_settings);
        if (roots.Count == 0)
        {
            _manifests = Array.Empty<AcfManifest>();
            RebuildRows();
            ShowInfo("未找到 Steam 库",
                "没有检测到包含 steamapps 的 Steam 库文件夹。请在设置中填写 Steam 安装路径，或手动添加库文件夹。",
                InfoBarSeverity.Warning);
            return;
        }

        SetBusy(true, "正在扫描 ACF…");
        try
        {
            var scan = await Task.Run(() => _updateService.Scan(roots));
            _manifests = scan.Manifests;
            RebuildRows();

            if (scan.Errors.Count > 0)
            {
                ShowInfo("部分 ACF 无法读取",
                    string.Join("\n", scan.Errors.Take(3)) + (scan.Errors.Count > 3 ? $"\n…另有 {scan.Errors.Count - 3} 个错误" : ""),
                    InfoBarSeverity.Warning);
            }
            else if (_rows.Count > 0)
            {
                PageInfoBar.IsOpen = false;
            }
        }
        catch (Exception ex)
        {
            ShowInfo("扫描失败", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }

        UpdateSummary();
        if (autoCheck && _rows.Count > 0 && _rows.Any(row => row.State == BuildIdState.Unknown))
            await RunCheckAsync(forceRefresh: false, onlyUnknown: true);
    }

    private IReadOnlyList<string> ResolveLibraryRoots(AppSettings settings)
    {
        var configured = (settings.SteamLibraryPaths ?? new List<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(SteamLocator.IsLibraryRoot)
            .Select(SteamLocator.NormalizeLibraryRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (configured.Count > 0) return configured;

        var installPath = string.IsNullOrWhiteSpace(settings.SteamInstallPath)
            ? SteamLocator.DetectSteamInstallPath() ?? ""
            : settings.SteamInstallPath;
        return SteamLocator.DetectLibraryPaths(installPath);
    }

    private void RebuildRows()
    {
        var previous = _rows.ToDictionary(row => row.AppId, StringComparer.Ordinal);
        _rows.Clear();

        foreach (var manifest in _manifests.Where(m => _settings.ShowAllSteamGames || m.LooksLikePlaceholder))
        {
            var row = new ManifestRow(manifest);
            if (previous.TryGetValue(manifest.AppId, out var old) && old.Manifest.BuildId == manifest.BuildId)
                row.CopyStateFrom(old);
            _rows.Add(row);
        }

        EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateTitle.Text = _settings.ShowAllSteamGames ? "Steam 库中没有 appmanifest" : "没有找到占位配置";
        EmptyStateMessage.Text = _settings.ShowAllSteamGames
            ? "请确认 Steam 库路径；也可以点击“新建配置”创建占位配置。"
            : "仅显示 SizeOnDisk 为 0 的占位配置。可开启“显示全部 Steam 游戏”，或点击“新建配置”。";
        UpdateSummary();
    }

    private async Task RunCheckAsync(bool forceRefresh, bool onlyUnknown)
    {
        if (_busy) return;
        var targets = _rows
            .Where(row => !onlyUnknown || row.State == BuildIdState.Unknown)
            .Select(row => row.Manifest)
            .ToList();
        if (targets.Count == 0)
        {
            if (!onlyUnknown) ShowInfo("无需检测", "当前没有可检测的配置。", InfoBarSeverity.Informational);
            return;
        }

        SetBusy(true, $"正在检测 {targets.Count} 个配置…");
        try
        {
            var progress = new Progress<BuildIdCheckResult>(result =>
            {
                var row = _rows.FirstOrDefault(item => item.AppId == result.Manifest.AppId);
                row?.Apply(result);
                UpdateSummary();
            });

            var results = await _updateService.CheckAsync(targets, forceRefresh, progress);
            var errors = results.Count(result => result.State == BuildIdState.Error);
            var updates = results.Count(result => result.State == BuildIdState.UpdateAvailable);
            ShowInfo("检测完成",
                errors == 0
                    ? $"{results.Count} 个配置完成检测，{updates} 个发现 BuildID 更新。"
                    : $"{results.Count} 个配置完成检测，{updates} 个可更新，{errors} 个获取失败。请检查网络或 AppID。",
                errors == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowInfo("检测失败", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateSummary();
        }
    }

    private async Task UpdateRowsAsync(IReadOnlyList<ManifestRow> rows)
    {
        if (_busy) return;
        var targets = rows.Where(row => row.CanUpdate && !string.IsNullOrWhiteSpace(row.LatestBuildId)).ToList();
        if (targets.Count == 0)
        {
            ShowInfo("没有可更新的项", "请先检测 BuildID 更新。", InfoBarSeverity.Informational);
            return;
        }

        if (!await ConfirmUpdateAsync(targets)) return;

        SetBusy(true, $"正在更新 {targets.Count} 个配置…");
        try
        {
            var checks = targets.Select(row => new BuildIdCheckResult(
                row.Manifest, row.LatestBuildId, BuildIdState.UpdateAvailable, "待更新")).ToList();
            var progress = new Progress<string>(message => BusyText.Text = message);
            var result = await _updateService.UpdateAllAsync(checks, progress);

            var manifests = _manifests.ToList();
            foreach (var updated in result.Updated)
            {
                var row = _rows.FirstOrDefault(item => item.AppId == updated.UpdatedManifest.AppId);
                row?.MarkUpdated(updated.UpdatedManifest, updated.Write.NewBuildId);
                var index = manifests.FindIndex(item => item.AppId == updated.UpdatedManifest.AppId);
                if (index >= 0) manifests[index] = updated.UpdatedManifest;
            }
            _manifests = manifests;

            if (result.HasFailures)
            {
                ShowInfo("部分配置更新失败",
                    $"成功 {result.UpdatedCount} 个，失败 {result.Failures.Count} 个。\n" +
                    string.Join("\n", result.Failures.Take(3)),
                    InfoBarSeverity.Warning);
            }
            else
            {
                var backups = result.Updated.Count(item => item.Write.BackupPath != null);
                ShowInfo("BuildID 更新完成",
                    $"已更新 {result.UpdatedCount} 个配置；{backups} 份原 ACF 已备份到：\n{AppPaths.DefaultBackupRoot}",
                    InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            ShowInfo("更新失败", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateSummary();
        }
    }

    private async Task<bool> ConfirmUpdateAsync(IReadOnlyList<ManifestRow> targets)
    {
        var running = SteamProcess.IsRunning();
        var message = $"将把 {targets.Count} 个配置的 BuildID 更新为公开分支的最新值：\n" +
                      string.Join("\n", targets.Take(6).Select(row => $"· {row.Name}（{row.AppId}）→ {row.LatestBuildId}")) +
                      (targets.Count > 6 ? $"\n…另有 {targets.Count - 6} 个" : "") +
                      "\n\n更新前会自动备份原 ACF。";
        if (running)
            message += "\n\n警告：Steam 正在运行。建议先完全退出 Steam，否则客户端可能覆盖本次修改。";

        var dialog = new ContentDialog
        {
            Title = running ? "Steam 正在运行，仍要更新吗？" : "确认更新 BuildID",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "开始更新",
            CloseButtonText = "取消",
            DefaultButton = running ? ContentDialogButton.Close : ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void SetBusy(bool busy, string? text = null)
    {
        _busy = busy;
        BusyRing.IsActive = busy;
        BusyText.Text = busy ? text ?? "" : "";
        CheckButton.IsEnabled = !busy;
        AddButton.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;
        ShowAllToggle.IsEnabled = !busy;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var updates = _rows.Count(row => row.CanUpdate);
        var errors = _rows.Count(row => row.State == BuildIdState.Error);
        SummaryText.Text = $"共 {_rows.Count} 个配置 · {updates} 个待更新" + (errors > 0 ? $" · {errors} 个失败" : "");
        if (!_busy) UpdateAllButton.IsEnabled = updates > 0;
    }

    private void ShowInfo(string title, string message, InfoBarSeverity severity)
    {
        PageInfoBar.Title = title;
        PageInfoBar.Message = message;
        PageInfoBar.Severity = severity;
        PageInfoBar.IsOpen = true;
    }

    private void OpenSettings()
    {
        if (App.MainWindow is MainWindow window) window.NavigateToSettings();
    }

    private async Task ShowSetupRequiredAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "需要先配置 Steam",
            Content = new TextBlock
            {
                Text = "请先在设置中填写 Steam 安装路径、Steam 库文件夹和 SteamID64，再新建配置。",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "打开设置",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) OpenSettings();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(autoCheck: false);

    private async void Check_Click(object sender, RoutedEventArgs e) => await RunCheckAsync(forceRefresh: true, onlyUnknown: false);

    private async void UpdateAll_Click(object sender, RoutedEventArgs e) =>
        await UpdateRowsAsync(_rows.Where(row => row.CanUpdate).ToList());

    private async void UpdateRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ManifestRow row }) return;
        await UpdateRowsAsync(new[] { row });
    }

    private async void RebuildRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ManifestRow row }) return;
        _settings = _settingsService.Load();
        var roots = ResolveLibraryRoots(_settings);
        if (roots.Count == 0)
        {
            await ShowSetupRequiredAsync();
            return;
        }

        var dialog = new GameConfigDialog(_settings, roots, row.Manifest) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.SavedResult is { } saved)
            await OnConfigSavedAsync(saved);
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        _settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(_settings.SteamInstallPath))
            _settings.SteamInstallPath = SteamLocator.DetectSteamInstallPath() ?? "";
        var roots = ResolveLibraryRoots(_settings);
        if (roots.Count == 0 || string.IsNullOrWhiteSpace(_settings.SteamId)
            || !File.Exists(Path.Combine(_settings.SteamInstallPath, "steam.exe")))
        {
            await ShowSetupRequiredAsync();
            return;
        }

        var dialog = new GameConfigDialog(_settings, roots) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.SavedResult is { } saved)
            await OnConfigSavedAsync(saved);
    }

    private async Task OnConfigSavedAsync(GameConfigWriteResult saved)
    {
        var copied = TryCopyToClipboard(saved.LaunchCommand);
        await RefreshAsync(autoCheck: false);
        ShowInfo("Steam 配置已写入",
            $"ACF：{saved.AcfPath}\n占位文件：{saved.PlaceholderExePath}\n" +
            (string.IsNullOrWhiteSpace(saved.LaunchCommand)
                ? "如需让 Steam 启动真实游戏，请把真实游戏 EXE 的启动命令粘贴到 Steam 启动选项。"
                : copied
                    ? "启动命令已复制到剪贴板，请粘贴到 Steam 库 → 右键游戏 → 属性 → 启动选项。"
                    : $"启动命令：{saved.LaunchCommand}"),
            InfoBarSeverity.Success);
    }

    private static bool TryCopyToClipboard(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private async void GameInfo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new GameInfoDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async void ShowAll_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        var showAll = ShowAllToggle.IsOn;
        _settingsService.Update(settings => settings.ShowAllSteamGames = showAll);
        _settings.ShowAllSteamGames = showAll;
        RebuildRows();
        if (showAll && _rows.Any(row => row.State == BuildIdState.Unknown))
            await RunCheckAsync(forceRefresh: false, onlyUnknown: true);
    }
}
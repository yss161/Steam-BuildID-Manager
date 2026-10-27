using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SteamBuildIdManager.Core.Models;
using SteamBuildIdManager.Core.Services;

namespace SteamBuildIdManager.Views.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsService _settingsService = new();
    private readonly SteamLoginUsersService _loginUsersService = new();
    private readonly ObservableCollection<string> _libraries = new();
    private AppSettings _settings = new();
    private bool _suppressAppearanceEvents;

    public SettingsPage()
    {
        InitializeComponent();
        LibraryList.ItemsSource = _libraries;
        Loaded += (_, _) => LoadSettings();
    }

    private void LoadSettings()
    {
        _settings = _settingsService.Load();
        InstallPathBox.Text = _settings.SteamInstallPath;
        SteamIdBox.Text = _settings.SteamId;
        AboutVersionText.Text = $"{AppInfo.AppName}  v{AppInfo.Version}";

        _libraries.Clear();
        foreach (var path in _settings.SteamLibraryPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            AddLibraryCore(path);

        if (string.IsNullOrWhiteSpace(InstallPathBox.Text))
            InstallPathBox.Text = SteamLocator.DetectSteamInstallPath() ?? "";

        if (_libraries.Count == 0)
        {
            foreach (var path in SteamLocator.DetectLibraryPaths(InstallPathBox.Text))
                AddLibraryCore(path);
        }

        LoadAppearance();

        UpdateSaveStatus(_libraries.Count == 0
            ? "尚未检测到 Steam 库，请手动添加。"
            : $"已载入 {_libraries.Count} 个库文件夹。");

        if (string.IsNullOrWhiteSpace(SteamIdBox.Text))
            TryFillSteamIdFromLocal(notify: false);
    }

    private void LoadAppearance()
    {
        _suppressAppearanceEvents = true;
        try
        {
            var mode = ThemeModes.Normalize(_settings.ThemeMode);
            ThemeCombo.SelectedIndex = mode switch
            {
                ThemeModes.Light => 1,
                ThemeModes.Dark => 2,
                _ => 0,
            };

            BackgroundPathText.Text = string.IsNullOrWhiteSpace(_settings.BackgroundImagePath)
                ? "未设置"
                : _settings.BackgroundImagePath;
            ClearBackgroundButton.IsEnabled = !string.IsNullOrWhiteSpace(_settings.BackgroundImagePath);

            var opacity = (int)Math.Round(_settings.NormalizedBackgroundOpacity * 100);
            BackgroundOpacitySlider.Value = opacity;
            UpdateOpacityText(opacity);

            RunAsAdminToggle.IsOn = _settings.RunAsAdministrator;
            RefreshElevationStatus();
            LoadDataSource();
        }
        finally
        {
            _suppressAppearanceEvents = false;
        }
    }

    private void LoadDataSource()
    {
        var mode = DataSourceModes.Normalize(_settings.DataSource);
        DataSourceCombo.SelectedIndex = mode switch
        {
            DataSourceModes.ApiFirst => 1,
            DataSourceModes.ApiOnly => 2,
            DataSourceModes.SteamCmdOnly => 3,
            _ => 0,
        };

        SteamCmdPathBox.Text = _settings.SteamCmdPath;
        var resolved = SteamCmdLocator.Resolve(_settings.SteamCmdPath, _settings.SteamInstallPath);
        SteamCmdStatusText.Text = resolved == null
            ? "未检测到 steamcmd.exe；选择“仅 SteamCMD”时会自动回退到在线接口。"
            : $"已找到：{resolved}";
    }

    private string SelectedDataSource() =>
        (DataSourceCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? DataSourceModes.SteamCmdFirst;

    private void DataSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAppearanceEvents) return;
        var mode = SelectedDataSource();
        _settingsService.Update(settings => settings.DataSource = mode);
        _settings.DataSource = mode;
        UpdateSaveStatus($"数据来源已切换为“{DataSourceModes.DisplayName(mode)}”。");
    }

    private async void BrowseSteamCmd_Click(object sender, RoutedEventArgs e)
    {
        var path = await Services.PickerHelper.PickExecutableAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        SaveSteamCmdPath(path);
    }

    private void DetectSteamCmd_Click(object sender, RoutedEventArgs e)
    {
        var resolved = SteamCmdLocator.Resolve(_settings.SteamCmdPath, _settings.SteamInstallPath);
        if (resolved == null)
        {
            SteamCmdStatusText.Text = "未检测到 steamcmd.exe，请手动选择或改用“仅在线接口”。";
            ShowInfo("未检测到 SteamCMD",
                "可从 Valve 下载 steamcmd.zip 解压后，点击“浏览”指定 steamcmd.exe。",
                InfoBarSeverity.Warning);
            return;
        }

        SaveSteamCmdPath(resolved);
    }

    private void SaveSteamCmdPath(string path)
    {
        _settingsService.Update(settings => settings.SteamCmdPath = path);
        _settings.SteamCmdPath = path;
        SteamCmdPathBox.Text = path;
        SteamCmdStatusText.Text = $"已找到：{path}";
        UpdateSaveStatus("已保存 steamcmd.exe 路径。");
    }

    private async void TestSteamCmd_Click(object sender, RoutedEventArgs e)
    {
        var path = SteamCmdLocator.Resolve(
            string.IsNullOrWhiteSpace(SteamCmdPathBox.Text) ? _settings.SteamCmdPath : SteamCmdPathBox.Text,
            _settings.SteamInstallPath);
        if (path == null)
        {
            SteamCmdTestText.Text = "未找到 steamcmd.exe。";
            return;
        }

        TestSteamCmdButton.IsEnabled = false;
        SteamCmdTestText.Text = "正在测试，首次运行需要联网自更新，可能需要几十秒…";
        try
        {
            var message = await new SteamCmdService(path).TestAsync();
            SteamCmdTestText.Text = message;
            AppLog.Info($"SteamCMD 测试结果：{message}");
        }
        catch (Exception ex)
        {
            SteamCmdTestText.Text = "测试失败：" + ex.Message;
            AppLog.Warning($"SteamCMD 测试失败：{ex.Message}");
        }
        finally
        {
            TestSteamCmdButton.IsEnabled = true;
        }
    }

    private void RefreshElevationStatus()
    {
        var elevated = ElevationService.IsElevated();
        ElevationStatusText.Text = elevated ? "当前进程已提权。" : "当前进程为标准权限。";
        RestartElevatedButton.IsEnabled = !elevated;
        RestartElevatedButton.Visibility = elevated ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAppearanceEvents) return;
        var mode = SelectedThemeMode();
        _settingsService.Update(settings => settings.ThemeMode = mode);
        _settings.ThemeMode = mode;
        ApplyAppearanceToWindow(notify: true);
        UpdateSaveStatus($"主题已切换为“{ThemeModes.DisplayName(mode)}”。");
    }

    private string SelectedThemeMode() =>
        (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? ThemeModes.System;

    private async void PickBackground_Click(object sender, RoutedEventArgs e)
    {
        var path = await Services.PickerHelper.PickImageAsync();
        if (string.IsNullOrWhiteSpace(path)) return;

        _settingsService.Update(settings => settings.BackgroundImagePath = path);
        _settings.BackgroundImagePath = path;
        BackgroundPathText.Text = path;
        ClearBackgroundButton.IsEnabled = true;
        ApplyAppearanceToWindow(notify: true);
        UpdateSaveStatus("背景图片已更新。");
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Update(settings => settings.BackgroundImagePath = "");
        _settings.BackgroundImagePath = "";
        BackgroundPathText.Text = "未设置";
        ClearBackgroundButton.IsEnabled = false;
        ApplyAppearanceToWindow(notify: false);
        UpdateSaveStatus("已清除背景图片。");
    }

    private void BackgroundOpacity_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var percent = (int)Math.Round(e.NewValue);
        UpdateOpacityText(percent);
        if (_suppressAppearanceEvents) return;

        var opacity = Math.Clamp(percent / 100d, 0.1, 1);
        _settingsService.Update(settings => settings.BackgroundImageOpacity = opacity);
        _settings.BackgroundImageOpacity = opacity;
        ApplyAppearanceToWindow(notify: false);
    }

    private void UpdateOpacityText(int percent) => OpacityHeaderText.Text = $"背景不透明度：{percent}%";

    private void ApplyAppearanceToWindow(bool notify)
    {
        if (App.MainWindow is MainWindow window) window.ApplyAppearance(_settings, notify);
    }

    private async void RunAsAdmin_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressAppearanceEvents) return;
        var enabled = RunAsAdminToggle.IsOn;
        _settingsService.Update(settings => settings.RunAsAdministrator = enabled);
        _settings.RunAsAdministrator = enabled;

        if (!enabled)
        {
            UpdateSaveStatus("已关闭自动提权启动，下次启动不再请求管理员权限。");
            return;
        }

        if (ElevationService.IsElevated())
        {
            UpdateSaveStatus("已开启管理员启动（当前进程已经是管理员）。");
            return;
        }

        UpdateSaveStatus("已开启管理员启动，下次启动会请求管理员权限。");
        var dialog = new ContentDialog
        {
            Title = "立即以管理员重启？",
            Content = new TextBlock
            {
                Text = "已保存“以管理员身份启动”。要现在重启应用并请求管理员权限吗？",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "立即重启",
            CloseButtonText = "稍后",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) RestartElevated();
    }

    private void RestartElevated_Click(object sender, RoutedEventArgs e) => RestartElevated();

    private void RestartElevated()
    {
        if (!ElevationService.TryRestartElevated())
        {
            ShowInfo("提权失败", "未能以管理员身份重启，可能取消了 UAC 提示。当前仍以标准权限运行。",
                InfoBarSeverity.Warning);
            return;
        }

        Application.Current.Exit();
    }

    private void AddLibraryCore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var normalized = SteamLocator.NormalizeLibraryRoot(path);
        if (_libraries.Contains(normalized, StringComparer.OrdinalIgnoreCase)) return;
        _libraries.Add(normalized);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var installPath = InstallPathBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            try
            {
                installPath = Path.GetFullPath(installPath);
            }
            catch
            {
                ShowInfo("路径无效", "Steam 安装路径格式不正确。", InfoBarSeverity.Error);
                return;
            }

            if (!File.Exists(Path.Combine(installPath, "steam.exe")))
            {
                ShowInfo("找不到 steam.exe",
                    "Steam 安装路径下没有 steam.exe，请确认路径或点击“自动检测”。",
                    InfoBarSeverity.Error);
                return;
            }
        }

        var invalidLibraries = _libraries.Where(path => !SteamLocator.IsLibraryRoot(path)).ToList();
        if (invalidLibraries.Count > 0)
        {
            ShowInfo("库文件夹无效",
                "以下路径下没有 steamapps 文件夹：\n" + string.Join("\n", invalidLibraries),
                InfoBarSeverity.Error);
            return;
        }

        var steamId = SteamIdBox.Text.Trim();
        if (steamId.Length > 0 && (steamId.Length != 17 || !steamId.All(char.IsAsciiDigit)))
        {
            ShowInfo("SteamID64 无效", "SteamID64 应为 17 位数字，例如 76561198000000000。", InfoBarSeverity.Error);
            return;
        }

        _settings.SteamInstallPath = installPath;
        _settings.SteamLibraryPaths = _libraries.Select(SteamLocator.NormalizeLibraryRoot).ToList();
        _settings.SteamId = steamId;

        if (!_settingsService.Save(_settings))
        {
            ShowInfo("保存失败", "无法写入设置文件，请检查目录权限。", InfoBarSeverity.Error);
            return;
        }

        ShowInfo("设置已保存", "返回 BuildID 管理页面即可扫描并检测更新。", InfoBarSeverity.Success);
        UpdateSaveStatus("已保存。");
    }

    private void AutoDetectInstall_Click(object sender, RoutedEventArgs e)
    {
        var found = SteamLocator.DetectSteamInstallPath();
        if (string.IsNullOrWhiteSpace(found))
        {
            ShowInfo("未检测到 Steam", "请手动选择 Steam 安装目录。", InfoBarSeverity.Warning);
            return;
        }

        InstallPathBox.Text = found;
        if (_libraries.Count == 0)
            foreach (var path in SteamLocator.DetectLibraryPaths(found))
                AddLibraryCore(path);
        TryFillSteamIdFromLocal(notify: false);
        ShowInfo("已检测到 Steam", found, InfoBarSeverity.Success);
    }

    private async void BrowseInstall_Click(object sender, RoutedEventArgs e)
    {
        var folder = await Services.PickerHelper.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder)) return;
        InstallPathBox.Text = folder;
        TryFillSteamIdFromLocal(notify: false);
    }

    private async void ReadSteamId_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<SteamLoginUser> users;
        try
        {
            users = _loginUsersService.Read(InstallPathBox.Text.Trim());
        }
        catch (Exception ex)
        {
            ShowInfo("读取失败", $"无法读取本地 Steam 账号：{ex.Message}", InfoBarSeverity.Error);
            return;
        }

        if (users.Count == 0)
        {
            ShowInfo("未找到本地账号",
                "请确认 Steam 安装路径正确，并确保本机至少登录过一次 Steam。",
                InfoBarSeverity.Warning);
            return;
        }

        var selected = users.Count == 1 ? users[0] : await PickSteamAccountAsync(users);
        if (selected == null) return;

        SteamIdBox.Text = selected.SteamId64;
        UpdateSaveStatus($"已选择账号 {selected.DisplayName}，记得点击“保存设置”。");
        ShowInfo("已读取 SteamID64",
            $"{selected.DisplayText}\n点击“保存设置”后生效。",
            InfoBarSeverity.Success);
    }

    private bool TryFillSteamIdFromLocal(bool notify)
    {
        if (!string.IsNullOrWhiteSpace(SteamIdBox.Text)) return false;

        IReadOnlyList<SteamLoginUser> users;
        try
        {
            users = _loginUsersService.Read(InstallPathBox.Text.Trim());
        }
        catch
        {
            return false;
        }

        if (users.Count == 0) return false;

        var selected = users[0];
        SteamIdBox.Text = selected.SteamId64;
        UpdateSaveStatus($"已从本地账号填充 SteamID64（{selected.DisplayName}），记得点击“保存设置”。");
        if (notify)
            ShowInfo("已读取 SteamID64", selected.DisplayText, InfoBarSeverity.Success);
        return true;
    }

    private async Task<SteamLoginUser?> PickSteamAccountAsync(IReadOnlyList<SteamLoginUser> users)
    {
        var list = new ListView
        {
            ItemsSource = users,
            SelectedIndex = 0,
            SelectionMode = ListViewSelectionMode.Single,
            DisplayMemberPath = nameof(SteamLoginUser.DisplayText),
            MaxHeight = 360,
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "检测到多个本地 Steam 账号，请选择要使用的账号：",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(list);

        var dialog = new ContentDialog
        {
            Title = "选择 Steam 账号",
            Content = panel,
            PrimaryButtonText = "使用此账号",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? list.SelectedItem as SteamLoginUser
            : null;
    }

    private void AutoDetectLibraries_Click(object sender, RoutedEventArgs e)
    {
        var installPath = InstallPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(installPath))
            installPath = SteamLocator.DetectSteamInstallPath() ?? "";

        var found = SteamLocator.DetectLibraryPaths(installPath);
        if (found.Count == 0)
        {
            ShowInfo("未检测到库文件夹", "请确认 Steam 安装路径，或手动添加包含 steamapps 的库根目录。",
                InfoBarSeverity.Warning);
            return;
        }

        _libraries.Clear();
        foreach (var path in found) AddLibraryCore(path);
        ShowInfo("已检测到库文件夹", $"共 {found.Count} 个：\n" + string.Join("\n", found),
            InfoBarSeverity.Success);
    }

    private async void AddLibrary_Click(object sender, RoutedEventArgs e)
    {
        var input = NewLibraryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            await BrowseLibraryAsync();
            return;
        }

        if (!SteamLocator.IsLibraryRoot(input))
        {
            ShowInfo("库文件夹无效", "该路径下没有 steamapps 文件夹。", InfoBarSeverity.Error);
            return;
        }

        AddLibraryCore(input);
        NewLibraryBox.Text = "";
        UpdateSaveStatus($"已添加 {SteamLocator.NormalizeLibraryRoot(input)}，记得点击“保存设置”。");
    }

    private async void BrowseLibrary_Click(object sender, RoutedEventArgs e) => await BrowseLibraryAsync();

    private async Task BrowseLibraryAsync()
    {
        var folder = await Services.PickerHelper.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder)) return;
        if (!SteamLocator.IsLibraryRoot(folder))
        {
            ShowInfo("库文件夹无效", "该路径下没有 steamapps 文件夹。", InfoBarSeverity.Error);
            return;
        }

        AddLibraryCore(folder);
        UpdateSaveStatus($"已添加 {SteamLocator.NormalizeLibraryRoot(folder)}，记得点击“保存设置”。");
    }

    private void RemoveLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: string path }) return;
        var existing = _libraries.FirstOrDefault(item =>
            string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) _libraries.Remove(existing);
        UpdateSaveStatus("已移除库文件夹，记得点击“保存设置”。");
    }

    private void ShowInfo(string title, string message, InfoBarSeverity severity)
    {
        SettingsInfoBar.Title = title;
        SettingsInfoBar.Message = message;
        SettingsInfoBar.Severity = severity;
        SettingsInfoBar.IsOpen = true;
    }

    private void UpdateSaveStatus(string message) => SaveStatusText.Text = message;
}
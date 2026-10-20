using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamBuildIdManager.Core.Models;
using SteamBuildIdManager.Core.Services;

namespace SteamBuildIdManager.Views.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsService _settingsService = new();
    private readonly SteamLoginUsersService _loginUsersService = new();
    private readonly ObservableCollection<string> _libraries = new();
    private AppSettings _settings = new();

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

        UpdateSaveStatus(_libraries.Count == 0
            ? "尚未检测到 Steam 库，请手动添加。"
            : $"已载入 {_libraries.Count} 个库文件夹。");

        if (string.IsNullOrWhiteSpace(SteamIdBox.Text))
            TryFillSteamIdFromLocal(notify: false);
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
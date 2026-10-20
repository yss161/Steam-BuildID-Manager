using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamBuildIdManager.Core.Models;
using SteamBuildIdManager.Core.Services;
using SteamBuildIdManager.Services;
using SteamBuildIdManager.Views.Pages;
using Windows.Graphics;

namespace SteamBuildIdManager;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly SettingsService _settingsService = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = AppInfo.WindowTitle;
        VersionText.Text = $"v{AppInfo.Version}";

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        // AppWindow.Resize works in physical pixels; scale the intended DIP size for high-DPI displays.
        var scale = GetDpiForWindow(handle) / 96d;
        if (scale <= 0) scale = 1;
        appWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(800 * scale)));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath)) appWindow.SetIcon(iconPath);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        TryEnableBackdrop();
        ApplyAppearance(_settingsService.Load(), notify: false);

        ContentFrame.Navigate(typeof(LibraryPage));
        NavView.SelectedItem = LibraryNavItem;
        Closed += (_, _) => Application.Current.Exit();
    }

    /// <summary>Mica where available; the window stays usable when composition backdrops are unsupported.</summary>
    private void TryEnableBackdrop()
    {
        try
        {
            if (!MicaController.IsSupported()) return;
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
        }
        catch
        {
            // Older Windows builds simply keep the default solid background.
        }
    }

    /// <summary>Re-apply theme, title bar colours and the optional background image.</summary>
    public void ApplyAppearance(AppSettings settings, bool notify)
    {
        var mode = ThemeModes.Normalize(settings.ThemeMode);
        var theme = ThemeService.ToElementTheme(mode);
        RootGrid.RequestedTheme = theme;
        NavView.RequestedTheme = theme;
        ContentFrame.RequestedTheme = theme;
        ThemeService.ApplyImmersiveDarkMode(this, IsDarkTheme(theme));
        ThemeService.ApplyTitleBarTheme(this, theme);

        var hasImage = ThemeService.TryApplyBackground(
            BackgroundImage, settings.BackgroundImagePath, settings.NormalizedBackgroundOpacity, out var error);
        BackgroundScrim.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        BackgroundScrim.Background = ThemeService.CreateOverlayBrush(theme, settings.NormalizedBackgroundOpacity);
        BackgroundScrim.Opacity = hasImage ? 1 : 0;

        if (notify && !string.IsNullOrEmpty(error))
            ThemeChangeFailed?.Invoke(this, error);
    }

    public event EventHandler<string>? ThemeChangeFailed;

    /// <summary>"Follow system" needs the live system value, not the ElementTheme.Default marker.</summary>
    private static bool IsDarkTheme(ElementTheme theme) => theme switch
    {
        ElementTheme.Dark => true,
        ElementTheme.Light => false,
        _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
    };

    public void NavigateToSettings()
    {
        NavView.SelectedItem = SettingsNavItem;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        var tag = item.Tag?.ToString();
        if (string.Equals(tag, "Settings", StringComparison.Ordinal))
        {
            if (ContentFrame.Content is not SettingsPage) ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (ContentFrame.Content is not LibraryPage) ContentFrame.Navigate(typeof(LibraryPage));
    }
}

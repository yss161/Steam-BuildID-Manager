using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamBuildIdManager.Views.Pages;
using Windows.Graphics;

namespace SteamBuildIdManager;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
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

        ContentFrame.Navigate(typeof(LibraryPage));
        NavView.SelectedItem = LibraryNavItem;
        Closed += (_, _) => Application.Current.Exit();
    }

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
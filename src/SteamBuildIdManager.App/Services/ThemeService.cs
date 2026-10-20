using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SteamBuildIdManager.Core.Models;
using Windows.UI;

namespace SteamBuildIdManager.Services;

/// <summary>Applies theme, window backdrop and the optional background image to the main window.</summary>
public static class ThemeService
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Without this the system keeps painting a light caption strip above a dark Mica surface,
    /// which shows up as a white block behind the minimise/maximise/close buttons.
    /// </summary>
    public static void ApplyImmersiveDarkMode(Window window, bool dark)
    {
        try
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (handle == IntPtr.Zero) return;
            var value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
        catch
        {
            // Cosmetic only.
        }
    }
    /// <summary>Push the requested theme onto the shared application resources.</summary>
    public static void Apply(ElementTheme theme, FrameworkElement? root)
    {
        if (root != null) root.RequestedTheme = theme;
        if (App.MainWindow?.Content is FrameworkElement content && !ReferenceEquals(content, root))
            content.RequestedTheme = theme;
    }

    public static ElementTheme ToElementTheme(string? mode) => ThemeModes.Normalize(mode) switch
    {
        ThemeModes.Light => ElementTheme.Light,
        ThemeModes.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>
    /// Layer the chosen image above the Mica/Acrylic backdrop. A missing or unreadable file silently
    /// clears the layer so a deleted picture never breaks startup.
    /// </summary>
    public static bool TryApplyBackground(Image? target, string? path, double opacity, out string error)
    {
        error = "";
        if (target == null) return false;

        if (string.IsNullOrWhiteSpace(path))
        {
            target.Source = null;
            target.Opacity = 0;
            target.Visibility = Visibility.Collapsed;
            return true;
        }

        try
        {
            if (!File.Exists(path))
            {
                error = "背景图片不存在，请重新选择。";
                target.Source = null;
                target.Opacity = 0;
                target.Visibility = Visibility.Collapsed;
                return false;
            }

            var bitmap = new BitmapImage(new Uri(Path.GetFullPath(path)));
            target.Source = bitmap;
            target.Opacity = Math.Clamp(opacity, 0.1, 1);
            target.Visibility = Visibility.Visible;
            return true;
        }
        catch (Exception ex)
        {
            error = "无法加载背景图片：" + ex.Message;
            target.Source = null;
            target.Opacity = 0;
            target.Visibility = Visibility.Collapsed;
            return false;
        }
    }

    /// <summary>
    /// Overlay tint keeps text readable regardless of the image brightness. The stronger the image,
    /// the stronger the scrim, so a 100% wallpaper still leaves labels legible.
    /// </summary>
    public static SolidColorBrush CreateOverlayBrush(ElementTheme theme, double imageOpacity)
    {
        var strength = Math.Clamp(imageOpacity, 0.1, 1);
        var alpha = (byte)Math.Clamp(110 + (int)Math.Round(strength * 120), 110, 220);
        var color = theme == ElementTheme.Light
            ? Color.FromArgb(alpha, 250, 250, 252)
            : Color.FromArgb(alpha, 18, 20, 24);
        return new SolidColorBrush(color);
    }

    public static void ApplyTitleBarTheme(Window window, ElementTheme theme)
    {
        try
        {
            var titleBar = window.AppWindow?.TitleBar;
            if (titleBar == null) return;
            if (theme == ElementTheme.Dark)
            {
                // Explicit colours stop the system from painting a white caption over the Mica surface.
                titleBar.BackgroundColor = Color.FromArgb(255, 31, 31, 31);
                titleBar.InactiveBackgroundColor = Color.FromArgb(255, 31, 31, 31);
                titleBar.ForegroundColor = Colors.White;
                titleBar.InactiveForegroundColor = Colors.White;
                titleBar.ButtonBackgroundColor = Color.FromArgb(255, 31, 31, 31);
                titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(255, 31, 31, 31);
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(180, 255, 255, 255);
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(60, 255, 255, 255);
            }
            else if (theme == ElementTheme.Light)
            {
                titleBar.BackgroundColor = Color.FromArgb(255, 243, 243, 243);
                titleBar.InactiveBackgroundColor = Color.FromArgb(255, 243, 243, 243);
                titleBar.ForegroundColor = Color.FromArgb(255, 26, 26, 26);
                titleBar.InactiveForegroundColor = Color.FromArgb(255, 26, 26, 26);
                titleBar.ButtonBackgroundColor = Color.FromArgb(255, 243, 243, 243);
                titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(255, 243, 243, 243);
                titleBar.ButtonForegroundColor = Color.FromArgb(255, 32, 32, 32);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 96, 96, 96);
                titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 32, 32, 32);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 0, 0, 0);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(50, 0, 0, 0);
            }
            else
            {
                // Let Windows follow the system light/dark setting.
                titleBar.BackgroundColor = null;
                titleBar.InactiveBackgroundColor = null;
                titleBar.ForegroundColor = null;
                titleBar.InactiveForegroundColor = null;
                titleBar.ButtonBackgroundColor = null;
                titleBar.ButtonInactiveBackgroundColor = null;
                titleBar.ButtonForegroundColor = null;
                titleBar.ButtonInactiveForegroundColor = null;
                titleBar.ButtonHoverForegroundColor = null;
                titleBar.ButtonHoverBackgroundColor = null;
                titleBar.ButtonPressedBackgroundColor = null;
            }
        }
        catch
        {
            // Title bar theming is cosmetic; never fail the window because of it.
        }
    }
}

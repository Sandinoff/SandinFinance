using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SandinFinance.Desktop;

public static class WindowAppearance
{
    public static readonly DependencyProperty DarkTitleBarProperty = DependencyProperty.RegisterAttached(
        "DarkTitleBar", typeof(bool), typeof(WindowAppearance), new PropertyMetadata(false, Changed));
    public static bool GetDarkTitleBar(DependencyObject obj) => (bool)obj.GetValue(DarkTitleBarProperty);
    public static void SetDarkTitleBar(DependencyObject obj, bool value) => obj.SetValue(DarkTitleBarProperty, value);

    private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        if (obj is not Window window) return;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Apply(window);
        else window.SourceInitialized += SourceInitialized;
    }

    private static void SourceInitialized(object? sender, EventArgs args)
    {
        var window = (Window)sender!;
        window.SourceInitialized -= SourceInitialized;
        Apply(window);
    }

    private static void Apply(Window window)
    {
        var dark = GetDarkTitleBar(window) ? 1 : 0;
        // Unsupported Windows versions keep their standard title bar.
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

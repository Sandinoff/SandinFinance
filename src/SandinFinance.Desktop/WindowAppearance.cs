using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Shell;

namespace SandinFinance.Desktop;

public static class WindowAppearance
{
    public static readonly DependencyProperty FramelessProperty = DependencyProperty.RegisterAttached(
        "Frameless", typeof(bool), typeof(WindowAppearance), new PropertyMetadata(false, Changed));
    public static bool GetFrameless(DependencyObject obj) => (bool)obj.GetValue(FramelessProperty);
    public static void SetFrameless(DependencyObject obj, bool value) => obj.SetValue(FramelessProperty, value);
    public static readonly DependencyProperty ShowMinimizeButtonProperty = DependencyProperty.RegisterAttached(
        "ShowMinimizeButton", typeof(bool), typeof(WindowAppearance), new PropertyMetadata(true));
    public static bool GetShowMinimizeButton(DependencyObject obj) => (bool)obj.GetValue(ShowMinimizeButtonProperty);
    public static void SetShowMinimizeButton(DependencyObject obj, bool value) => obj.SetValue(ShowMinimizeButtonProperty, value);

    private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        if (obj is not Window window || !(bool)args.NewValue) return;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 36, CornerRadius = new CornerRadius(14), GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize ? new Thickness(0) : new Thickness(6),
            UseAeroCaptionButtons = false
        });
        window.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => window.Close()));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(window)));
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
        var chrome = WindowChrome.GetWindowChrome(window);
        if (chrome is not null)
        {
            chrome = (WindowChrome)chrome.Clone();
            chrome.ResizeBorderThickness = window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize ? new Thickness(0) : new Thickness(6);
            WindowChrome.SetWindowChrome(window, chrome);
        }
        var handle = new WindowInteropHelper(window).Handle;
        var dark = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        // Windows 11: rounded silhouette without the native one-pixel frame.
        // Earlier Windows versions ignore these attributes.
        var noBorder = -2;
        _ = DwmSetWindowAttribute(handle, 34, ref noBorder, sizeof(int));
        var rounded = 2;
        _ = DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

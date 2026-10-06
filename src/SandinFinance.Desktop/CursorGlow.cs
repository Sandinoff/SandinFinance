using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SandinFinance.Desktop;

/// <summary>A diffuse, non-interactive light shared by every application window.</summary>
public static class CursorGlow
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(CursorGlow), new PropertyMetadata(false, EnabledChanged));

    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

    private static void EnabledChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        if (obj is not Window window) return;
        if ((bool)args.NewValue) window.Loaded += Attach;
        else window.Loaded -= Attach;
    }

    private static void Attach(object sender, RoutedEventArgs args)
    {
        var window = (Window)sender;
        window.Loaded -= Attach;
        if (window.Content is not UIElement content) return;
        var surface = new Grid { ClipToBounds = true };
        var glow = new GlowSurface { IsHitTestVisible = false, Focusable = false, Opacity = 0 };
        window.Content = null;
        surface.Children.Add(content);
        surface.Children.Add(glow);
        window.Content = surface;
        window.PreviewMouseMove += Move;
        window.MouseLeave += Leave;
        window.Deactivated += Leave;
        window.Closed += Closed;

        void Move(object sender, MouseEventArgs e)
        {
            glow.Position = e.GetPosition(glow);
            glow.InvalidateVisual();
            if (!glow.IsLit)
            {
                glow.IsLit = true;
                glow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            }
        }
        void Leave(object? sender, EventArgs e)
        {
            glow.IsLit = false;
            glow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)));
        }
        void Closed(object? sender, EventArgs e)
        {
            window.PreviewMouseMove -= Move;
            window.MouseLeave -= Leave;
            window.Deactivated -= Leave;
            window.Closed -= Closed;
        }
    }

    private sealed class GlowSurface : FrameworkElement
    {
        private static readonly Brush Light = CreateLight();
        public Point Position { get; set; }
        public bool IsLit { get; set; }

        private static Brush CreateLight()
        {
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(32, 122, 156, 215), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(15, 171, 161, 222), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 171, 161, 222), 1));
            brush.Freeze();
            return brush;
        }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            context.DrawEllipse(Light, null, Position, 240, 240);
        }
    }
}

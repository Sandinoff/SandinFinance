using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SandinFinance.Desktop;

/// <summary>Quiet polygon parallax beneath the main page content; sleeps when idle.</summary>
public sealed class PolygonBackdrop : FrameworkElement
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private Window? owner;
    private Point target;
    private Point position;
    private double strength;
    private long lastMove;
    private long lastFrame;
    private bool inside;
    private static readonly (double X, double Y, double Radius, double Angle)[] Shapes =
    [
        (0.12, 0.14, 95, 0.2), (0.56, 0.09, 125, 1.1),
        (0.90, 0.29, 105, 0.6), (0.18, 0.63, 140, 0.9),
        (0.64, 0.74, 120, 0.3), (0.90, 0.93, 90, 1.5)
    ];

    public PolygonBackdrop()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        Loaded += Attach;
        Unloaded += Detach;
        timer.Tick += Tick;
    }

    private void Attach(object sender, RoutedEventArgs e)
    {
        if (owner is not null) return;
        owner = Window.GetWindow(this);
        if (owner is null) return;
        owner.PreviewMouseMove += Move;
        owner.MouseLeave += Leave;
        owner.Deactivated += Leave;
    }

    private void Detach(object sender, RoutedEventArgs e)
    {
        timer.Stop();
        if (owner is null) return;
        owner.PreviewMouseMove -= Move;
        owner.MouseLeave -= Leave;
        owner.Deactivated -= Leave;
        owner = null;
        strength = 0;
        inside = false;
    }

    private void Move(object sender, MouseEventArgs e) => MovePointer(e.GetPosition(this));

    private void MovePointer(Point pointer)
    {
        inside = pointer.X >= 0 && pointer.Y >= 0 && pointer.X <= ActualWidth && pointer.Y <= ActualHeight;
        if (!inside) return;
        target = pointer;
        lastMove = Stopwatch.GetTimestamp();
        if (timer.IsEnabled) return;
        if (strength < 0.003) position = target;
        lastFrame = lastMove;
        timer.Start();
    }

    private void Leave(object? sender, EventArgs e) => inside = false;

    private void Tick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Math.Min(0.1, Stopwatch.GetElapsedTime(lastFrame, now).TotalSeconds);
        lastFrame = now;
        var follow = 1 - Math.Exp(-elapsed / 0.35);
        position += (target - position) * follow;
        var active = inside && Stopwatch.GetElapsedTime(lastMove, now).TotalSeconds < 1.2;
        strength += ((active ? 1 : 0) - strength) * (1 - Math.Exp(-elapsed / (active ? 0.45 : 0.8)));
        InvalidateVisual();
        if (!active && strength < 0.003) { strength = 0; timer.Stop(); }
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (strength <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var shiftX = (position.X / ActualWidth - 0.5) * 30;
        var shiftY = (position.Y / ActualHeight - 0.5) * 24;
        drawing.PushOpacity(strength);
        for (var index = 0; index < Shapes.Length; index++)
        {
            var shape = Shapes[index];
            var depth = 0.6 + index * 0.12;
            var center = new Point(shape.X * ActualWidth + shiftX * depth, shape.Y * ActualHeight + shiftY * depth);
            var distance = (center - position).Length;
            var proximity = Math.Max(0, 1 - distance / 450);
            var fill = new SolidColorBrush(Color.FromArgb((byte)(7 + proximity * 12), 120, 155, 212));
            var edge = new Pen(new SolidColorBrush(Color.FromArgb((byte)(18 + proximity * 24), 130, 168, 224)), 1);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                Point Vertex(int vertex)
                {
                    var angle = shape.Angle + vertex * Math.PI * 2 / 3 + shiftX * 0.002;
                    return new Point(center.X + Math.Cos(angle) * shape.Radius, center.Y + Math.Sin(angle) * shape.Radius * 0.8);
                }
                context.BeginFigure(Vertex(0), true, true);
                context.LineTo(Vertex(1), true, false);
                context.LineTo(Vertex(2), true, false);
            }
            geometry.Freeze();
            drawing.DrawGeometry(fill, edge, geometry);
        }
        drawing.Pop();
    }
}

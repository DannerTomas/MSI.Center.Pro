using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MSICenterPro.Controls;

/// <summary>
/// MD2 ring progress with animated sweep (QuinticEase deceleration).
/// Center content is provided via the Content property.
/// </summary>
public class RingProgress : ContentControl
{
    static RingProgress()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RingProgress),
            new FrameworkPropertyMetadata(typeof(RingProgress)));
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RingProgress),
        new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue), typeof(double), typeof(RingProgress),
        new PropertyMetadata(100.0, OnVisualChanged));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RingProgress),
        new PropertyMetadata(11.0, OnVisualChanged));

    private static readonly DependencyProperty AnimatedValueProperty = DependencyProperty.Register(
        nameof(AnimatedValue), typeof(double), typeof(RingProgress),
        new PropertyMetadata(0.0, OnVisualChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    internal double AnimatedValue
    {
        get => (double)GetValue(AnimatedValueProperty);
        set => SetValue(AnimatedValueProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ring = (RingProgress)d;
        double from = ring.AnimatedValue;
        double to = (double)e.NewValue;
        double duration = Math.Min(0.95, 0.35 + Math.Abs(to - from) / 140.0);
        var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(duration))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        ring.BeginAnimation(AnimatedValueProperty, anim);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((RingProgress)d).UpdateArc();
    }

    private Ellipse? _track;
    private Path? _arc;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _track = GetTemplateChild("PART_Track") as Ellipse;
        _arc = GetTemplateChild("PART_Arc") as Path;
        UpdateArc();
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        var result = base.ArrangeOverride(arrangeBounds);
        UpdateArc();
        return result;
    }

    private void UpdateArc()
    {
        if (_arc is null || _track is null) return;
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 10) return;

        double thickness = Math.Max(2, Thickness);
        double r = (size - thickness) / 2 - 1;
        // WPF draws an Ellipse stroke fully inside its layout bounds (Stretch.Fill
        // insets by half a stroke), while the Path arc below is drawn with the
        // stroke centered on radius r. Inflate the track bounds by half a stroke so
        // both strokes share the same centerline radius.
        double trackR = r + thickness / 2;
        _track.Width = trackR * 2;
        _track.Height = trackR * 2;
        _track.StrokeThickness = thickness;
        _arc.StrokeThickness = thickness;

        double max = MaxValue <= 0 ? 100 : MaxValue;
        double angle = Math.Clamp(AnimatedValue / max, 0, 1) * 360;
        if (angle <= 0.4)
        {
            _arc.Data = null;
            return;
        }
        if (angle >= 359.97) angle = 359.97;

        double cx = ActualWidth / 2;
        double cy = ActualHeight / 2;
        double rad = (angle - 90) * Math.PI / 180.0;
        var start = new Point(cx, cy - r);
        var end = new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, angle > 180,
            SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _arc.Data = geometry;
    }
}

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OrbitConvert.App.Controls;

/// <summary>Circular progress indicator. A negative <see cref="Value"/> shows a spinning indeterminate arc.</summary>
public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((ProgressRing)d).UpdateSpin()));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(ProgressRing),
        new FrameworkPropertyMetadata(Brushes.SlateBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(ProgressRing),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)), FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly RotateTransform _spin = new();
    private bool _spinning;

    public ProgressRing()
    {
        RenderTransform = _spin;
        RenderTransformOrigin = new Point(0.5, 0.5);
        Loaded += (_, _) => UpdateSpin();
        Unloaded += (_, _) => StopSpin();
    }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2)
        {
            return;
        }
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - Thickness) / 2;
        dc.DrawEllipse(null, new Pen(Track, Thickness), center, radius, radius);

        var fraction = Value < 0 ? 0.28 : Math.Clamp(Value, 0, 1);
        if (fraction <= 0)
        {
            return;
        }
        var pen = new Pen(Foreground, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (fraction >= 0.999)
        {
            dc.DrawEllipse(null, pen, center, radius, radius);
            return;
        }
        var start = new Point(center.X, center.Y - radius);
        var angle = fraction * 2 * Math.PI;
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void UpdateSpin()
    {
        if (Value < 0 && IsLoaded)
        {
            if (!_spinning)
            {
                _spinning = true;
                _spin.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
            }
        }
        else
        {
            StopSpin();
        }
    }

    private void StopSpin()
    {
        if (_spinning)
        {
            _spinning = false;
            _spin.BeginAnimation(RotateTransform.AngleProperty, null);
            _spin.Angle = 0;
        }
    }
}

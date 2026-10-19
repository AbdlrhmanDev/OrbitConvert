using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OrbitConvert.App.ViewModels;

namespace OrbitConvert.App.Controls;

/// <summary>
/// The Orbit Menu wheel, drawn exactly as the Identity v1.0 spec on a 400-unit grid: a translucent
/// halo (r186), annular segments from r84 to r176 with 5° gaps, mono labels at r132 with a hint line,
/// and a core (r70) whose rim becomes the lime progress ring. The hovered segment fills with Ion Lime.
/// Vector-rendered, so it stays crisp on every monitor DPI.
/// </summary>
public sealed class RadialWheel : FrameworkElement
{
    private const double Grid = 400, Halo = 186, Outer = 176, Inner = 84, LabelRadius = 132, Core = 70, GapDegrees = 5;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(RadialWheel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(RadialItemViewModel), typeof(RadialWheel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsHoveringProperty = DependencyProperty.Register(
        nameof(IsHovering), typeof(bool), typeof(RadialWheel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(RadialWheel), new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ItemCommandProperty = DependencyProperty.Register(
        nameof(ItemCommand), typeof(ICommand), typeof(RadialWheel));

    private INotifyCollectionChanged? _observable;

    public RadialWheel()
    {
        Focusable = false;
        MouseMove += (_, e) => Cursor = SelectAt(e.GetPosition(this)) is null ? Cursors.Arrow : Cursors.Hand;
        MouseLeave += (_, _) => SetCurrentValue(IsHoveringProperty, false);
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public RadialItemViewModel? SelectedItem { get => (RadialItemViewModel?)GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public bool IsHovering { get => (bool)GetValue(IsHoveringProperty); set => SetValue(IsHoveringProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public ICommand? ItemCommand { get => (ICommand?)GetValue(ItemCommandProperty); set => SetValue(ItemCommandProperty, value); }

    private double Scale => Math.Min(ActualWidth, ActualHeight) / Grid;
    private Point Center => new(ActualWidth / 2, ActualHeight / 2);

    protected override void OnRender(DrawingContext dc)
    {
        var s = Scale;
        if (s <= 0)
        {
            return;
        }
        var c = Center;
        dc.DrawEllipse(Brush("Brush.RadialDisc", Brushes.Black), new Pen(Brush("Brush.RadialBorder", Brushes.Gray), 1), c, Halo * s, Halo * s);

        var items = Items();
        var accent = Brush("Brush.Accent", Brushes.GreenYellow);
        var segment = Brush("Brush.RadialItem", Brushes.DimGray);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var mono = Application.Current.TryFindResource("MonoFont") as FontFamily ?? new FontFamily("Consolas");
        var ui = Application.Current.TryFindResource("UiFont") as FontFamily ?? new FontFamily("Segoe UI");
        var labelFace = new Typeface(mono, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var hintFace = new Typeface(ui, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var n = items.Count;
        for (var i = 0; i < n; i++)
        {
            var item = items[i];
            var a0 = -90 + i * 360.0 / n - 180.0 / n + GapDegrees / 2;
            var a1 = a0 + 360.0 / n - GapDegrees;
            var active = IsEnabled && IsHovering && ReferenceEquals(item, SelectedItem);
            var fill = active ? accent : segment;
            // A same-colored 8-unit stroke with round joins softens the corners, as in the design.
            dc.DrawGeometry(fill, new Pen(fill, 8 * s) { LineJoin = PenLineJoin.Round }, Wedge(c, Inner * s, Outer * s, a0, a1));

            var mid = Polar(c, LabelRadius * s, (a0 + a1) / 2);
            var label = Text(item.Label, labelFace, 14 * s, active ? Brush("Brush.OnAccent", Brushes.Black) : Brush("Brush.RadialItemText", Brushes.White), dpi);
            var hint = Text(item.Hint, hintFace, 10.5 * s, active ? Brush("Brush.RadialActiveHint", Brushes.DarkOliveGreen) : Brush("Brush.RadialItemHint", Brushes.Gray), dpi);
            var maxWidth = Math.Max(40 * s, 2 * LabelRadius * s * Math.Sin(Math.PI / Math.Max(n, 2)) - 10 * s);
            label.MaxTextWidth = maxWidth;
            hint.MaxTextWidth = maxWidth;
            label.Trimming = hint.Trimming = TextTrimming.CharacterEllipsis;
            label.MaxLineCount = hint.MaxLineCount = 1;
            var total = label.Height + 2 * s + (item.Hint.Length > 0 ? hint.Height : 0);
            var top = mid.Y - total / 2;
            dc.DrawText(label, new Point(mid.X - label.Width / 2, top));
            if (item.Hint.Length > 0)
            {
                dc.DrawText(hint, new Point(mid.X - hint.Width / 2, top + label.Height + 2 * s));
            }
            if (item.Experimental)
            {
                dc.DrawEllipse(Brush("Brush.Warning", Brushes.Gold), null, Polar(c, (Outer - 14) * s, a1 - 6), 3.5 * s, 3.5 * s);
            }
        }

        dc.DrawEllipse(Brush("Brush.RadialCenter", Brushes.Black), new Pen(Brush("Brush.RadialBorder", Brushes.Gray), 1), c, Core * s, Core * s);
        if (Progress > 0)
        {
            var pen = new Pen(accent, 4 * s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (Progress >= 0.999)
            {
                dc.DrawEllipse(null, pen, c, Core * s, Core * s);
            }
            else
            {
                dc.DrawGeometry(null, pen, Arc(c, Core * s, -90, -90 + 360 * Progress));
            }
        }
    }

    private static FormattedText Text(string text, Typeface face, double size, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, Math.Max(1, size), brush, dpi);

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = SelectAt(e.GetPosition(this));
        if (item is not null && ItemCommand?.CanExecute(item) == true)
        {
            ItemCommand.Execute(item);
            e.Handled = true;
        }
    }

    /// <summary>Selects the segment under a point (mouse or OLE drag); null outside the ring.</summary>
    public RadialItemViewModel? SelectAt(Point point)
    {
        var item = HitItem(point);
        SetCurrentValue(IsHoveringProperty, item is not null);
        if (item is not null && !ReferenceEquals(item, SelectedItem))
        {
            if (SelectedItem is not null) SelectedItem.IsSelected = false;
            item.IsSelected = true;
            SetCurrentValue(SelectedItemProperty, item);
        }
        return item;
    }

    public bool ActivateSelected()
    {
        var item = SelectedItem;
        if (item is null || ItemCommand?.CanExecute(item) != true)
        {
            return false;
        }
        ItemCommand.Execute(item);
        return true;
    }

    private RadialItemViewModel? HitItem(Point point)
    {
        var items = Items();
        if (items.Count == 0)
        {
            return null;
        }
        var c = Center;
        var dx = point.X - c.X;
        var dy = point.Y - c.Y;
        var r = Math.Sqrt(dx * dx + dy * dy) / Scale;
        if (r < Inner - 4 || r > Outer + 4)
        {
            return null;
        }
        // Degrees clockwise from 12 o'clock, shifted so segment 0 is centered on the top.
        var step = 360.0 / items.Count;
        var angle = Math.Atan2(dy, dx) * 180 / Math.PI + 90 + step / 2;
        angle = (angle % 360 + 360) % 360;
        return items[Math.Min(items.Count - 1, (int)(angle / step))];
    }

    private List<RadialItemViewModel> Items() => ItemsSource?.Cast<object>().OfType<RadialItemViewModel>().ToList() ?? [];

    private static Geometry Wedge(Point c, double inner, double outer, double a0, double a1)
    {
        var large = a1 - a0 > 180;
        var figure = new PathFigure { StartPoint = Polar(c, outer, a0), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(Polar(c, outer, a1), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(Polar(c, inner, a1), true));
        figure.Segments.Add(new ArcSegment(Polar(c, inner, a0), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static Geometry Arc(Point c, double r, double a0, double a1)
    {
        var figure = new PathFigure { StartPoint = Polar(c, r, a0) };
        figure.Segments.Add(new ArcSegment(Polar(c, r, a1), new Size(r, r), 0, a1 - a0 > 180, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static Point Polar(Point c, double r, double degrees)
    {
        var q = degrees * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(q), c.Y + r * Math.Sin(q));
    }

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var wheel = (RadialWheel)d;
        if (wheel._observable is not null) wheel._observable.CollectionChanged -= wheel.OnCollectionChanged;
        wheel._observable = e.NewValue as INotifyCollectionChanged;
        if (wheel._observable is not null) wheel._observable.CollectionChanged += wheel.OnCollectionChanged;
        wheel.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}

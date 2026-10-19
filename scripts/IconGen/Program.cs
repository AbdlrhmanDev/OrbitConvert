using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

// OrbitConvert "Orbit" mark (Identity v1.0), on a 100-unit grid:
// core r9 (the file), orbit r30 / stroke 13 with a 23.5-unit gap (the formats),
// and a satellite r8 leaving the ring at 45° (the moment of conversion).
// Below 32 px the core drops out and the stroke thickens so the gap stays readable.

var Lime = Color.FromRgb(0xC6, 0xF4, 0x32);
var Paper = Color.FromRgb(0xF2, 0xF3, 0xEE);
var Void = Color.FromRgb(0x0B, 0x0C, 0x10);

/// <summary>An SVG-style dashed circle as an explicit arc: starts at -22.5° and runs clockwise.</summary>
static Geometry Orbit(double r, double dash)
{
    var a0 = -22.5 * Math.PI / 180;
    var a1 = a0 + dash / r;
    var start = new Point(50 + r * Math.Cos(a0), 50 + r * Math.Sin(a0));
    var end = new Point(50 + r * Math.Cos(a1), 50 + r * Math.Sin(a1));
    var g = new StreamGeometry();
    using (var ctx = g.Open())
    {
        ctx.BeginFigure(start, false, false);
        ctx.ArcTo(end, new Size(r, r), 0, isLargeArc: true, SweepDirection.Clockwise, true, false);
    }
    g.Freeze();
    return g;
}

void Mark(DrawingContext dc, int size)
{
    var lime = new SolidColorBrush(Lime);
    if (size <= 16)
    {
        dc.DrawGeometry(null, new Pen(lime, 20), Orbit(32, 165));
        return;
    }
    if (size <= 32)
    {
        dc.DrawGeometry(null, new Pen(lime, 16), Orbit(30, 165));
        dc.DrawEllipse(lime, null, new Point(72, 28), 10, 10);
        return;
    }
    dc.DrawGeometry(null, new Pen(lime, 13), Orbit(30, 165));
    dc.DrawEllipse(lime, null, new Point(71.2, 28.8), 8, 8);
    dc.DrawEllipse(new SolidColorBrush(Paper), null, new Point(50, 50), 9, 9);
}

/// <summary>The Windows app icon: a dark rounded tile from 24 px up, the bare ring at 16 px.</summary>
DrawingVisual Icon(int size)
{
    var visual = new DrawingVisual();
    using var dc = visual.RenderOpen();
    dc.PushTransform(new ScaleTransform(size / 100.0, size / 100.0));
    if (size > 16)
    {
        var radius = size >= 48 ? 22 : 25;
        Brush tile = size >= 48
            ? new LinearGradientBrush(Color.FromRgb(0x1E, 0x21, 0x29), Void, new Point(0.33, 0), new Point(0.67, 1))
            : new SolidColorBrush(Void);
        dc.DrawRoundedRectangle(tile, new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x30, 0x3B)), 100.0 / size), new Rect(0.5, 0.5, 99, 99), radius, radius);
        // The mark fills about two thirds of the tile, as in the identity sheet.
        var scale = size >= 48 ? 0.66 : 0.69;
        dc.PushTransform(new TranslateTransform(50 * (1 - scale), 50 * (1 - scale)));
        dc.PushTransform(new ScaleTransform(scale, scale));
        Mark(dc, size);
        dc.Pop();
        dc.Pop();
    }
    else
    {
        Mark(dc, size);
    }
    dc.Pop();
    return visual;
}

/// <summary>The bare mark on a transparent background (used in the app and README).</summary>
DrawingVisual BareMark(int size)
{
    var visual = new DrawingVisual();
    using var dc = visual.RenderOpen();
    dc.PushTransform(new ScaleTransform(size / 100.0, size / 100.0));
    Mark(dc, size);
    dc.Pop();
    return visual;
}

static byte[] Png(DrawingVisual visual, int size)
{
    var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bmp.Render(visual);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bmp));
    using var ms = new MemoryStream();
    encoder.Save(ms);
    return ms.ToArray();
}

var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("..", "..", "assets", "icons"));
var appAssets = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine("..", "..", "src", "OrbitConvert.App", "Assets"));
Directory.CreateDirectory(outDir);
var thread = new Thread(() =>
{
    using var icon = new MagickImageCollection();
    foreach (var size in new[] { 256, 128, 64, 48, 32, 24, 16 })
    {
        icon.Add(new MagickImage(Png(Icon(size), size)));
    }
    icon.Write(Path.Combine(outDir, "orbitconvert.ico"), MagickFormat.Ico);
    foreach (var size in new[] { 512, 256, 64 })
    {
        File.WriteAllBytes(Path.Combine(outDir, $"orbitconvert-{size}.png"), Png(Icon(size), size));
    }
    File.WriteAllBytes(Path.Combine(appAssets, "logo.png"), Png(Icon(64), 64));
    File.WriteAllBytes(Path.Combine(appAssets, "logo-256.png"), Png(Icon(256), 256));
    File.WriteAllBytes(Path.Combine(appAssets, "mark-128.png"), Png(BareMark(128), 128));
    Console.WriteLine("Wrote icons to " + outDir + " and " + appAssets);
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

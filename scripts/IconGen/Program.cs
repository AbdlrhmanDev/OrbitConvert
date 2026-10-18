using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

// Logo: a gradient disc, a tilted orbit ring, a satellite, and two curved arrows suggesting conversion.
// Coordinates are on a 256×256 canvas, matching assets/icons/orbitconvert.svg.
static DrawingVisual Draw(int size)
{
    var visual = new DrawingVisual();
    using var dc = visual.RenderOpen();
    dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));

    var disc = new LinearGradientBrush(Color.FromRgb(0x5B, 0x4B, 0xE0), Color.FromRgb(0x14, 0xB8, 0xD6), new Point(0, 0), new Point(1, 1));
    dc.DrawEllipse(disc, null, new Point(128, 128), 120, 120);

    // Soft highlight on the upper left.
    var glow = new RadialGradientBrush(Color.FromArgb(0x55, 255, 255, 255), Color.FromArgb(0, 255, 255, 255))
    { Center = new Point(0.3, 0.25), GradientOrigin = new Point(0.3, 0.25), RadiusX = 0.6, RadiusY = 0.6 };
    dc.DrawEllipse(glow, null, new Point(128, 128), 120, 120);

    var white = Brushes.White;
    var ring = new Pen(new SolidColorBrush(Color.FromArgb(0xE6, 255, 255, 255)), 12) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
    dc.PushTransform(new RotateTransform(-28, 128, 128));
    // Orbit drawn as two arcs so the "planet" appears to pass in front of it.
    var orbit = Geometry.Parse("M 30,128 A 98,42 0 0 1 226,128");
    var orbitBack = Geometry.Parse("M 226,128 A 98,42 0 0 1 30,128");
    dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)), 10), orbit);
    dc.Pop();

    // Planet: a white core carrying two conversion arrows.
    dc.DrawEllipse(white, null, new Point(128, 128), 46, 46);
    var arrows = new Pen(disc, 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
    dc.DrawGeometry(null, arrows, Geometry.Parse("M 106,120 A 24,24 0 0 1 148,112 M 141,103 L 149,113 L 137,117"));
    dc.DrawGeometry(null, arrows, Geometry.Parse("M 150,136 A 24,24 0 0 1 108,144 M 115,153 L 107,143 L 119,139"));

    dc.PushTransform(new RotateTransform(-28, 128, 128));
    dc.DrawGeometry(null, ring, orbitBack);
    dc.Pop();
    // Satellite on the front arc (ellipse parameter t = 20°, rotated with the orbit).
    dc.DrawEllipse(white, new Pen(disc, 4), new Point(216, 98), 15, 15);
    dc.Pop();
    return visual;
}

static byte[] RenderPng(int size)
{
    var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bmp.Render(Draw(size));
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bmp));
    using var ms = new MemoryStream();
    encoder.Save(ms);
    return ms.ToArray();
}

var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("..", "..", "assets", "icons"));
Directory.CreateDirectory(outDir);
var thread = new Thread(() =>
{
    using var icon = new MagickImageCollection();
    foreach (var size in new[] { 256, 128, 64, 48, 32, 24, 16 })
    {
        var png = RenderPng(size);
        icon.Add(new MagickImage(png));
        if (size is 256 or 64 or 32)
        {
            File.WriteAllBytes(Path.Combine(outDir, $"orbitconvert-{size}.png"), png);
        }
    }
    File.WriteAllBytes(Path.Combine(outDir, "orbitconvert-512.png"), RenderPng(512));
    icon.Write(Path.Combine(outDir, "orbitconvert.ico"), MagickFormat.Ico);
    Console.WriteLine("Wrote icons to " + outDir);
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

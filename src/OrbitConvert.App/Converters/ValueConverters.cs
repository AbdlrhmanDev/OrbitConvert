using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OrbitConvert.Core.Queue;

namespace OrbitConvert.App.Converters;

/// <summary>true → Visible. Pass ConverterParameter="Invert" to reverse.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter as string == "Invert")
        {
            flag = !flag;
        }
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ (parameter as string == "Invert");
}

/// <summary>Non-null and non-empty → Visible. ConverterParameter="Invert" reverses.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var present = value switch
        {
            null => false,
            string s => s.Length > 0,
            int n => n > 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        if (parameter as string == "Invert")
        {
            present = !present;
        }
        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Binds a RadioButton to an enum value: IsChecked="{Binding Mode, Converter=..., ConverterParameter=Value}".</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}

/// <summary>Fixed palette that reads well on both light and dark surfaces.</summary>
public sealed class CategoryToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Identity v1.0 category hues, used only on format glyphs.
        ["Image"] = Freeze(0x4F, 0xD1, 0xE8),
        ["Video"] = Freeze(0xFF, 0x7A, 0xC6),
        ["Audio"] = Freeze(0xB6, 0x8C, 0xFF),
        ["Document"] = Freeze(0x6E, 0xA8, 0xFF),
        ["Pdf"] = Freeze(0xFF, 0x6B, 0x5E),
        ["Archive"] = Freeze(0xE8, 0xD3, 0x6A),
        ["Other"] = Freeze(0x8A, 0x8D, 0x98),
    };

    public static SolidColorBrush For(string? category) =>
        category is not null && Brushes.TryGetValue(category, out var b) ? b : Brushes["Other"];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => For(value?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed class FileSizeConverter : IValueConverter
{
    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long l ? Format(l) : value is int i ? Format(i) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class JobStatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        JobStatus.Queued => "Queued",
        JobStatus.Paused => "Paused",
        JobStatus.Running => "Converting",
        JobStatus.Succeeded => "Done",
        JobStatus.Failed => "Failed",
        JobStatus.Canceled => "Canceled",
        JobStatus.Skipped => "Skipped",
        _ => "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Maps a job status to a semantic brush resource (Brush.Success, Brush.Danger, ...).</summary>
public sealed class JobStatusBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            JobStatus.Succeeded => "Brush.Success",
            JobStatus.Failed => "Brush.Danger",
            JobStatus.Canceled or JobStatus.Skipped or JobStatus.Paused => "Brush.Warning",
            JobStatus.Running => "Brush.Accent",
            _ => "Brush.TextTertiary",
        };
        return Application.Current.TryFindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class JobStatusGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        JobStatus.Succeeded => "",
        JobStatus.Failed => "",
        JobStatus.Canceled => "",
        JobStatus.Skipped => "",
        JobStatus.Paused => "",
        JobStatus.Running => "",
        _ => "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>0..1 → 0..100 for ProgressBar; negative (indeterminate) → 0.</summary>
public sealed class ProgressPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d && d > 0 ? d * 100 : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class IsNegativeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is double d && d < 0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTimeOffset time ? Format(time, culture) : "";

    public static string Format(DateTimeOffset time, CultureInfo? culture = null)
    {
        var age = DateTimeOffset.Now - time;
        return age.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)age.TotalMinutes} min ago",
            < 60 * 24 => $"{(int)age.TotalHours} h ago",
            < 60 * 48 => "yesterday",
            _ => time.LocalDateTime.ToString("g", culture ?? CultureInfo.CurrentCulture),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// (Status, Progress, Error, Note) → the status line of a queue row: "62%" while running,
/// the error message when failed, otherwise the plain status.
/// </summary>
public sealed class JobStatusLineConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = values.Length > 0 && values[0] is JobStatus s ? s : JobStatus.Queued;
        var progress = values.Length > 1 && values[1] is double p ? p : -1;
        var error = values.Length > 2 ? values[2] as string : null;
        var note = values.Length > 3 ? values[3] as string : null;
        return status switch
        {
            JobStatus.Running => progress >= 0 ? $"{progress * 100:0}%" : "Converting…",
            JobStatus.Failed => error ?? "Failed",
            JobStatus.Succeeded => string.IsNullOrEmpty(note) ? "Done" : "Done · " + note,
            JobStatus.Skipped => string.IsNullOrEmpty(note) ? "Skipped" : "Skipped · " + note,
            JobStatus.Paused => "Paused",
            JobStatus.Canceled => "Canceled",
            _ => "Queued",
        };
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => [];
}

/// <summary>Progress-bar fill per status: lime while working, muted when done, red when failed.</summary>
public sealed class JobBarBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.TryFindResource(value switch
        {
            JobStatus.Failed => "Brush.Danger",
            JobStatus.Succeeded or JobStatus.Skipped or JobStatus.Canceled => "Brush.BorderStrong",
            _ => "Brush.Accent",
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Finished jobs show a full bar; queued ones an empty one.</summary>
public sealed class JobBarValueConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = values.Length > 0 && values[0] is JobStatus s ? s : JobStatus.Queued;
        var progress = values.Length > 1 && values[1] is double p ? p : -1;
        return status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Skipped ? 100.0 : progress > 0 ? progress * 100 : 0.0;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => [];
}

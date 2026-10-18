using System.Windows;
using System.Windows.Controls;

namespace OrbitConvert.App.Controls;

/// <summary>
/// Vector file-format badge: a page with a folded corner and a colored band carrying the
/// extension ("PNG"), or a round glyph for operations such as "Merge". Fully vector, so it stays
/// crisp at any DPI. The template lives in Themes/Styles.xaml.
/// </summary>
public sealed class FormatBadge : Control
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(FormatBadge), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CategoryProperty =
        DependencyProperty.Register(nameof(Category), typeof(string), typeof(FormatBadge), new PropertyMetadata("Other"));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(FormatBadge), new PropertyMetadata(null));

    static FormatBadge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FormatBadge), new FrameworkPropertyMetadata(typeof(FormatBadge)));
        FocusableProperty.OverrideMetadata(typeof(FormatBadge), new FrameworkPropertyMetadata(false));
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Image, Video, Audio, Document, Pdf, Archive or Other; selects the accent color.</summary>
    public string Category
    {
        get => (string)GetValue(CategoryProperty);
        set => SetValue(CategoryProperty, value);
    }

    /// <summary>When set, a round glyph badge is drawn instead of a page.</summary>
    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }
}

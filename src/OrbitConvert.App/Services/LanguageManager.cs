using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace OrbitConvert.App.Services;

/// <summary>Applies the selected UI culture and translates static WPF labels as elements load.</summary>
public sealed class LanguageManager
{
    private readonly ResourceDictionary _english = Load("en");
    private readonly ResourceDictionary _arabic = Load("ar");
    private readonly Dictionary<string, string> _toArabic;
    private readonly Dictionary<string, string> _toEnglish;
    private string _language = "en";

    public LanguageManager()
    {
        _toArabic = BuildMap(_english, _arabic);
        _toEnglish = BuildMap(_arabic, _english);
    }

    public event EventHandler? Changed;
    public bool IsRtl => _language == "ar";

    public void Apply(string? language)
    {
        _language = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
        var culture = CultureInfo.GetCultureInfo(_language);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        foreach (Window window in Application.Current.Windows)
        {
            Enable(window);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Enable(Window window)
    {
        window.FlowDirection = IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.Language = XmlLanguage.GetLanguage(_language);
        window.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnElementLoaded), true);
        Translate(window);
    }

    private void OnElementLoaded(object sender, RoutedEventArgs args)
    {
        if (args.OriginalSource is DependencyObject element)
        {
            Translate(element);
        }
    }

    private void Translate(DependencyObject element)
    {
        var map = IsRtl ? _toArabic : _toEnglish;
        switch (element)
        {
            case TextBox textBox:
                textBox.FlowDirection = FlowDirection.LeftToRight;
                break;
            case PasswordBox passwordBox:
                passwordBox.FlowDirection = FlowDirection.LeftToRight;
                break;
            case TextBlock text when map.TryGetValue(text.Text, out var translated):
                text.Text = translated;
                break;
            case ContentControl content when content.Content is string value && map.TryGetValue(value, out var translated):
                content.Content = translated;
                break;
            case HeaderedContentControl header when header.Header is string value && map.TryGetValue(value, out var translated):
                header.Header = translated;
                break;
        }

        if (element is FrameworkElement framework && framework.ToolTip is string tip && map.TryGetValue(tip, out var translatedTip))
        {
            framework.ToolTip = translatedTip;
        }
    }

    private static ResourceDictionary Load(string language) => new()
    {
        Source = new Uri($"pack://application:,,,/OrbitConvert;component/Localization/Strings.{language}.xaml", UriKind.Absolute),
    };

    private static Dictionary<string, string> BuildMap(ResourceDictionary source, ResourceDictionary destination)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in source.Keys)
        {
            if (source[key] is string from && destination[key] is string to)
            {
                result[from] = to;
            }
        }
        return result;
    }
}

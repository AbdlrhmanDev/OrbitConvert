using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OrbitConvert.App.Services;

/// <summary>Applies the selected UI culture and translates static WPF labels as elements load.</summary>
public sealed class LanguageManager
{
    private readonly ResourceDictionary _english = Load("en");
    private readonly ResourceDictionary _arabic = Load("ar");
    private readonly Dictionary<string, string> _toArabic;
    private readonly Dictionary<string, string> _toEnglish;
    private readonly HashSet<Window> _enabledWindows = [];
    private string _language = "en";

    public LanguageManager()
    {
        _toArabic = BuildMap(_english, _arabic);
        _toEnglish = BuildMap(_arabic, _english);
    }

    public event EventHandler? Changed;
    public bool IsRtl => _language == "ar";

    /// <summary>Translates a literal UI value without changing bindings or persisted user data.</summary>
    public string TranslateLiteral(string value)
    {
        var map = IsRtl ? _toArabic : _toEnglish;
        return map.TryGetValue(value, out var translated) ? translated : value;
    }

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
        if (_enabledWindows.Add(window))
        {
            window.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnElementLoaded), true);
            window.Closed += (_, _) => _enabledWindows.Remove(window);
        }
        TranslateTree(window);
    }

    private void OnElementLoaded(object sender, RoutedEventArgs args)
    {
        if (args.OriginalSource is DependencyObject element)
        {
            TranslateTree(element);
        }
    }

    /// <summary>
    /// Loaded is raised only once. When the user changes language while a window is already open,
    /// walk the existing visual and logical trees so every label is refreshed immediately.
    /// </summary>
    private void TranslateTree(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            Translate(current);

            if (current is FrameworkElement or FrameworkContentElement)
            {
                foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                {
                    pending.Push(child);
                }
            }

            if (current is Visual or Visual3D)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                {
                    pending.Push(VisualTreeHelper.GetChild(current, i));
                }
            }
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
            case TextBlock text when !BindingOperations.IsDataBound(text, TextBlock.TextProperty)
                                     && map.TryGetValue(text.Text, out var translated):
                text.Text = translated;
                break;
            case ContentControl content when !BindingOperations.IsDataBound(content, ContentControl.ContentProperty)
                                            && content.Content is string value && map.TryGetValue(value, out var translated):
                content.Content = translated;
                break;
            case HeaderedContentControl header when !BindingOperations.IsDataBound(header, HeaderedContentControl.HeaderProperty)
                                                  && header.Header is string value && map.TryGetValue(value, out var translated):
                header.Header = translated;
                break;
            case Run run when !BindingOperations.IsDataBound(run, Run.TextProperty)
                              && map.TryGetValue(run.Text, out var translated):
                run.Text = translated;
                break;
        }

        if (element is FrameworkElement framework
            && !BindingOperations.IsDataBound(framework, FrameworkElement.ToolTipProperty)
            && framework.ToolTip is string tip && map.TryGetValue(tip, out var translatedTip))
        {
            framework.ToolTip = translatedTip;
        }

        var automationName = System.Windows.Automation.AutomationProperties.GetName(element);
        if (!BindingOperations.IsDataBound(element, System.Windows.Automation.AutomationProperties.NameProperty)
            && !string.IsNullOrEmpty(automationName) && map.TryGetValue(automationName, out var translatedAutomationName))
        {
            System.Windows.Automation.AutomationProperties.SetName(element, translatedAutomationName);
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

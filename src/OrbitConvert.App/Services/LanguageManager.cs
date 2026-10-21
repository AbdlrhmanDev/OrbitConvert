using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
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
    public static LanguageManager? Current { get; private set; }

    private readonly ResourceDictionary _english = Load("en");
    private readonly ResourceDictionary _arabic = Load("ar");
    private readonly Dictionary<string, string> _toArabic;
    private readonly Dictionary<string, string> _toEnglish;
    private readonly HashSet<Window> _enabledWindows = [];
    private readonly ConditionalWeakTable<DependencyObject, object> _hookedElements = new();
    private readonly HashSet<DependencyObject> _updatingElements = new(ReferenceEqualityComparer.Instance);
    private string _language = "en";

    public LanguageManager()
    {
        Current = this;
        _toArabic = BuildMap(_english, _arabic);
        _toEnglish = BuildMap(_arabic, _english);
        // Loaded is a direct routed event, so a handler attached only to the Window misses pages
        // and data templates created later. A class handler sees every FrameworkElement instance.
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAnyElementLoaded),
            true);
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

        // XAML uses DynamicResource for every static label. Replacing this one dictionary makes
        // current and future pages resolve the selected language as soon as they are created.
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var localized = Load(_language);
        var existingIndex = dictionaries
            .Select((dictionary, index) => (dictionary, index))
            .Where(item => item.dictionary.Source?.OriginalString.Contains("/Localization/Strings.", StringComparison.OrdinalIgnoreCase) == true)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
        if (existingIndex >= 0)
        {
            dictionaries[existingIndex] = localized;
        }
        else
        {
            dictionaries.Add(localized);
        }

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

    private void OnAnyElementLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is DependencyObject element)
        {
            Translate(element);
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
        HookLiveValueChanges(element);
        var map = IsRtl ? _toArabic : _toEnglish;
        switch (element)
        {
            case TextBox textBox:
                textBox.FlowDirection = FlowDirection.LeftToRight;
                break;
            case PasswordBox passwordBox:
                passwordBox.FlowDirection = FlowDirection.LeftToRight;
                break;
            case TextBlock text when BindingOperations.GetBindingExpression(text, TextBlock.TextProperty) is { } textBinding:
                textBinding.UpdateTarget();
                break;
            case TextBlock text when map.TryGetValue(text.Text, out var translated):
                SetTranslatedValue(text, TextBlock.TextProperty, translated);
                break;
            case HeaderedContentControl header when header.Header is string value && map.TryGetValue(value, out var translated):
                SetTranslatedValue(header, HeaderedContentControl.HeaderProperty, translated);
                break;
            case ContentControl content when content.Content is string value && map.TryGetValue(value, out var translated):
                SetTranslatedValue(content, ContentControl.ContentProperty, translated);
                break;
            case Run run when BindingOperations.GetBindingExpression(run, Run.TextProperty) is { } runBinding:
                runBinding.UpdateTarget();
                break;
            case Run run when map.TryGetValue(run.Text, out var translated):
                SetTranslatedValue(run, Run.TextProperty, translated);
                break;
        }

        if (element is FrameworkElement framework
            && framework.ToolTip is string tip && map.TryGetValue(tip, out var translatedTip))
        {
            SetTranslatedValue(framework, FrameworkElement.ToolTipProperty, translatedTip);
        }

        var automationName = System.Windows.Automation.AutomationProperties.GetName(element);
        if (!string.IsNullOrEmpty(automationName) && map.TryGetValue(automationName, out var translatedAutomationName))
        {
            SetTranslatedValue(element, System.Windows.Automation.AutomationProperties.NameProperty, translatedAutomationName);
        }
    }

    /// <summary>
    /// Bound text can change after Loaded (status, format choices, search results). Observe those
    /// target values and translate with SetCurrentValue, which keeps the original binding intact.
    /// </summary>
    private void HookLiveValueChanges(DependencyObject element)
    {
        if (_hookedElements.TryGetValue(element, out _))
        {
            return;
        }
        _hookedElements.Add(element, new object());

        switch (element)
        {
            case TextBlock:
                AddValueHook(element, TextBlock.TextProperty, typeof(TextBlock));
                break;
            case HeaderedContentControl:
                AddValueHook(element, HeaderedContentControl.HeaderProperty, typeof(HeaderedContentControl));
                break;
            case ContentControl:
                AddValueHook(element, ContentControl.ContentProperty, typeof(ContentControl));
                break;
            case Run:
                AddValueHook(element, Run.TextProperty, typeof(Run));
                break;
        }

        if (element is FrameworkElement)
        {
            AddValueHook(element, FrameworkElement.ToolTipProperty, typeof(FrameworkElement));
        }
    }

    private void AddValueHook(DependencyObject element, DependencyProperty property, Type ownerType) =>
        DependencyPropertyDescriptor.FromProperty(property, ownerType)?.AddValueChanged(element, OnTranslatableValueChanged);

    private void OnTranslatableValueChanged(object? sender, EventArgs args)
    {
        if (sender is DependencyObject element && !_updatingElements.Contains(element))
        {
            Translate(element);
        }
    }

    private void SetTranslatedValue(DependencyObject element, DependencyProperty property, string value)
    {
        if (Equals(element.GetValue(property), value))
        {
            return;
        }

        _updatingElements.Add(element);
        try
        {
            element.SetCurrentValue(property, value);
        }
        finally
        {
            _updatingElements.Remove(element);
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

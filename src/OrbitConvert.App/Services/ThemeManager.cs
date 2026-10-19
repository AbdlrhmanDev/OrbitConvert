using System.Windows;
using Microsoft.Win32;
using OrbitConvert.Core.Settings;

namespace OrbitConvert.App.Services;

/// <summary>
/// Applies light/dark/system theming: WPF's built-in Fluent theme styles the standard controls,
/// and OrbitConvert's semantic brushes (Themes/Light.xaml, Themes/Dark.xaml) style custom surfaces.
/// </summary>
public sealed class ThemeManager
{
    private AppTheme _theme = AppTheme.System;

    public ThemeManager()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _theme == AppTheme.System)
            {
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(_theme));
            }
        };
    }

    public bool IsDark { get; private set; }

    public event EventHandler? Changed;

    public void Apply(AppTheme theme)
    {
        _theme = theme;
        var app = Application.Current;
        IsDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemUsesDarkTheme(),
        };

        app.ThemeMode = IsDark ? ThemeMode.Dark : ThemeMode.Light;

        var selectedTheme = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/OrbitConvert;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };

        // Put semantic theme values directly in the application's top-level dictionary.
        // WPF's ThemeMode can rebuild framework theme dictionaries at runtime; replacing a
        // merged dictionary alone may therefore leave custom controls on their previous palette.
        // Top-level values have stable precedence and updating them invalidates every
        // DynamicResource consumer immediately.
        foreach (var key in selectedTheme.Keys)
        {
            app.Resources[key] = selectedTheme[key];
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}

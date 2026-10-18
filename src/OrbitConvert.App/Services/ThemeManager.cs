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

        app.ThemeMode = theme switch
        {
            AppTheme.Dark => ThemeMode.Dark,
            AppTheme.Light => ThemeMode.Light,
            _ => ThemeMode.System,
        };

        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) == true
                                                     || d.Source?.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/OrbitConvert;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };
        if (existing is null)
        {
            dictionaries.Add(replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
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

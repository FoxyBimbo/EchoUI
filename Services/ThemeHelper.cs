using System.Windows;
using System.Windows.Media;
using EchoUI.Models;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Application = System.Windows.Application;

namespace EchoUI.Services;

public static class ThemeHelper
{
    /// <summary>
    /// Returns true if Windows is set to use light mode for apps.
    /// </summary>
    public static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves the effective <see cref="ThemeColors"/> for the given settings.
    /// </summary>
    public static ThemeColors ResolveColors(AppSettings settings)
    {
        var colors = settings.ThemeMode switch
        {
            "Light" => ThemeColors.Light,
            "Dark" => ThemeColors.Dark,
            "Custom" => settings.CustomTheme ?? ThemeColors.Dark,
            _ => IsLightTheme() ? ThemeColors.Light : ThemeColors.Dark // Auto
        };

        if (!string.IsNullOrWhiteSpace(settings.AccentColor))
        {
            try
            {
                ParseColor(settings.AccentColor.Trim());
                colors.Accent = settings.AccentColor.Trim();
            }
            catch
            {
            }
        }

        return colors;
    }

    /// <summary>
    /// Builds a <see cref="ResourceDictionary"/> from the given color scheme.
    /// </summary>
    public static ResourceDictionary BuildDictionary(ThemeColors colors)
    {
        var dict = new ResourceDictionary();
        dict["WindowBackgroundBrush"] = Brush(colors.WindowBackground);
        dict["ControlBackgroundBrush"] = Brush(colors.ControlBackground);
        dict["ForegroundBrush"] = Brush(colors.Foreground);
        dict["MutedForegroundBrush"] = Brush(colors.MutedForeground);
        dict["BorderBrush"] = Brush(colors.Border);
        dict["AccentBrush"] = Brush(colors.Accent);
        dict["SecondaryButtonBrush"] = Brush(colors.SecondaryButton);
        dict["DropdownBackgroundBrush"] = Brush(colors.DropdownBackground);
        dict["DropdownItemHoverBrush"] = Brush(colors.DropdownItemHover);

        // Widget-specific: semi-transparent variant used for floating root border
        var wbColor = ParseColor(colors.WindowBackground);
        wbColor.A = 0xDD;
        var semiTransparent = new SolidColorBrush(wbColor);
        semiTransparent.Freeze();
        dict["WindowBackgroundSemiBrush"] = semiTransparent;

        // Merge control style templates that reference the brush keys above
        dict.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new System.Uri("pack://application:,,,/Themes/ControlStyles.xaml")
        });

        return dict;
    }

    /// <summary>
    /// Applies a theme to the application-level resources.
    /// </summary>
    public static void ApplyToApp(ThemeColors colors)
    {
        var app = Application.Current;
        // Remove any previously added theme dictionaries
        var toRemove = app.Resources.MergedDictionaries
            .Where(d => d.Source is null) // code-built dictionaries have no Source
            .ToList();
        foreach (var d in toRemove)
            app.Resources.MergedDictionaries.Remove(d);

        // Also remove file-based theme dictionaries
        var fileThemes = app.Resources.MergedDictionaries
            .Where(d => d.Source?.OriginalString.Contains("Theme") == true)
            .ToList();
        foreach (var d in fileThemes)
            app.Resources.MergedDictionaries.Remove(d);

        app.Resources.MergedDictionaries.Add(BuildDictionary(colors));
    }

    /// <summary>
    /// Applies per-widget color overrides to a <see cref="FrameworkElement"/>.
    /// Falls back to the global theme colors when <paramref name="widgetColors"/> is null.
    /// </summary>
    public static void ApplyToElement(FrameworkElement element, ThemeColors? widgetColors)
    {
        if (widgetColors is null)
        {
            element.Resources.MergedDictionaries.Clear();
            return;
        }
        element.Resources.MergedDictionaries.Clear();
        element.Resources.MergedDictionaries.Add(BuildDictionary(widgetColors));
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush(ParseColor(hex));
        brush.Freeze();
        return brush;
    }

    public static Color ParseColor(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex);
    }
}

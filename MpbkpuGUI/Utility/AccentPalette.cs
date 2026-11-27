using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using MpbkpuGUI.Models;

namespace MpbkpuGUI.Utility;

/// <summary>
/// The accent colors of the theme dictionaries of App.axaml: the Mpbkpu blue, or colors derived from the accent color of
/// the operating system (followed when the user changes it). Systems that do not report one keep the Mpbkpu blue.
/// </summary>
public static class AccentPalette
{
    private static readonly string[] Keys =
        ["AccentColor", "AccentTextColor", "AccentSoftColor", "AccentHoverColor", "AccentPressedColor", "AccentForegroundColor"];

    private static readonly ThemeVariant[] Variants = [ThemeVariant.Dark, ThemeVariant.Light];

    /// <summary>
    /// The Mpbkpu colors of App.axaml, read before the first change
    /// </summary>
    private static Dictionary<ThemeVariant, Dictionary<string, Color>>? _defaults;

    private static bool _listening;

    /// <summary>
    /// Apply an accent mode; with <see cref="AppSettings.AccentModes.System"/> the colors follow the system from now on
    /// </summary>
    public static void Apply(AppSettings.AccentModes mode)
    {
        if (Application.Current is not { } app)
            return;
        var platform = app.PlatformSettings;
        if (platform != null && !_listening) {
            _listening = true;
            platform.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(() =>
                Apply(App.Settings?.Accent ?? AppSettings.AccentModes.Mpbkpu));
        }

        var dictionaries = app.Resources.ThemeDictionaries;
        _defaults ??= Variants.ToDictionary(v => v,
            v => Keys.ToDictionary(k => k, k => (Color)((ResourceDictionary)dictionaries[v])[k]!));

        var system = mode == AppSettings.AccentModes.System ? GetSystemAccent(platform) : null;
        var fluent = app.Styles.OfType<FluentTheme>().FirstOrDefault();
        foreach (var variant in Variants) {
            var dictionary = (ResourceDictionary)dictionaries[variant];
            var colors = system == null ? _defaults[variant] : Derive(system.Value, variant == ThemeVariant.Dark, (Color)dictionary["BgColor"]!);
            foreach (var (key, color) in colors)
                dictionary[key] = color;
            // Fluent controls (switches, progress bars, selections)
            if (fluent?.Palettes.TryGetValue(variant, out var palette) == true)
                palette.Accent = colors["AccentColor"];
        }
    }

    /// <summary>
    /// The accent color of the system, null when it does not report one
    /// </summary>
    private static Color? GetSystemAccent(IPlatformSettings? platform)
    {
        var color = platform?.GetColorValues().AccentColor1;
        return color is { A: > 0 } c ? Color.FromRgb(c.R, c.G, c.B) : null;
    }

    /// <summary>
    /// The colors of App.axaml derived from an accent: text readable on the background, a soft background, hover and
    /// pressed shades, and the text of accent buttons (white, or black on light accents)
    /// </summary>
    private static Dictionary<string, Color> Derive(Color accent, bool dark, Color background)
    {
        var white = Colors.White;
        var black = Colors.Black;
        // White like the system buttons, black only when white is not readable (3:1, WCAG for large text and controls)
        var foreground = Contrast(accent, white) >= 3 ? white : black;

        // Accent text: the accent moved towards the text color until it reads on the background (WCAG AA, 4.5:1)
        var towards = dark ? white : black;
        var text = accent;
        for (var amount = dark ? 0.3 : 0.1; Contrast(text, background) < 4.5 && amount <= 1; amount += 0.05)
            text = Mix(accent, towards, amount);

        return new Dictionary<string, Color>
        {
            ["AccentColor"] = accent,
            ["AccentTextColor"] = text,
            ["AccentSoftColor"] = Mix(background, accent, dark ? 0.16 : 0.13),
            ["AccentHoverColor"] = Mix(accent, foreground == white ? white : black, 0.1),
            ["AccentPressedColor"] = Mix(accent, foreground == white ? black : white, 0.15),
            ["AccentForegroundColor"] = foreground
        };
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * Math.Clamp(amount, 0, 1));
        return Color.FromRgb(Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }

    /// <summary>
    /// WCAG contrast ratio of two colors (1 to 21)
    /// </summary>
    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        double Linear(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }
}

// SPDX-License-Identifier: GPL-3.0-only
// Palettes, and the mechanism that swaps between them.
//
// Only colour lives here. Spacing, type, corner radii and every ControlTemplate
// stay in App.xaml and are shared by all themes.
//
// Switching works by mutating each palette brush's Color in place rather than
// replacing the resource entry. XAML and code-behind both hold references to
// those brush objects, so replacing an entry would leave existing references on
// the old colour, while mutating the instance repaints every consumer.
//
// Brushes that arrive from compiled XAML are frozen and cannot be mutated, so
// the first Apply replaces each entry with a mutable clone. That is safe only
// before any window exists, because a StaticResource resolves when a style is
// applied to an element rather than when the dictionary is parsed. Applying a
// theme before the first window is therefore required, not an optimisation.
// App.xaml.cs asserts that each brush took its value.
//
// Palette references in XAML must use DynamicResource, and code-behind must use
// SetResourceReference; a cached brush does not follow a theme change.
// manager/tools/validate_theme_refs.py enforces the first of those.
//
// Contrast is measured against each palette's own Surface: primary text 7:1,
// secondary 4.5:1, disabled 3:1, and every accent and state colour 4.5:1.
// Re-measure StateBad after any change to a Surface value.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CraneLoader
{
    internal sealed class ThemeDefinition
    {
        public string Name;
        public string Summary;
        public bool Dark;
        public Dictionary<string, string> Colors;
    }

    internal static class Theme
    {
        public const string Default = "Graphite";

        private static string _current = Default;
        public static string Current { get { return _current; } }

        private static ThemeDefinition Make(string name, string summary, bool dark,
                                            string[] pairs)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            for (int i = 0; i < pairs.Length; i += 2) map[pairs[i]] = pairs[i + 1];
            ThemeDefinition theme = new ThemeDefinition();
            theme.Name = name;
            theme.Summary = summary;
            theme.Dark = dark;
            theme.Colors = map;
            return theme;
        }

        // Ordered light to dark, so choosing by brightness moves one way.
        public static readonly ThemeDefinition[] All = new ThemeDefinition[]
        {
            Make("Light", "Plain and bright.", false, new string[] {
                "Surface","#FFFFFF", "SurfaceAlt","#F4F6F7", "SurfaceRaised","#FFFFFF",
                "SurfaceHover","#ECEFF1", "SurfacePress","#E1E5E8", "SurfaceInput","#FFFFFF",
                "SurfaceAlert","#FDF3F1", "Rule","#DCE0E3", "RuleStrong","#B4BABF",
                "Ink","#1B1D1F", "InkMuted","#5A6166", "InkFaint","#868D92",
                "Accent","#1F6C7D", "AccentDim","#BFDCE3", "AccentWarm","#7A5E14",
                "AccentWarmDim","#E6D9AE", "OnAccent","#FFFFFF",
                "StateGood","#2E7D32", "StateWarn","#8A5A00", "StateBad","#B3261E",
                "StateIdle","#9AA1A6" }),

            Make("Paper", "Warm and bright, easier at night than plain white.", false, new string[] {
                "Surface","#FAF7F2", "SurfaceAlt","#F1ECE3", "SurfaceRaised","#FFFDF9",
                "SurfaceHover","#E9E2D6", "SurfacePress","#DED6C7", "SurfaceInput","#FFFDF9",
                "SurfaceAlert","#F8EDE8", "Rule","#DDD5C6", "RuleStrong","#B3AA98",
                "Ink","#211E19", "InkMuted","#5C554A", "InkFaint","#8A8375",
                "Accent","#1F5F6C", "AccentDim","#C7DCE0", "AccentWarm","#7A5A16",
                "AccentWarmDim","#E3D5AC", "OnAccent","#FAF7F2",
                "StateGood","#2E6B33", "StateWarn","#8A5A00", "StateBad","#A83A2C",
                "StateIdle","#9C9486" }),

            Make("Graphite", "Dark without going near black.", true, new string[] {
                "Surface","#2B2F33", "SurfaceAlt","#33383D", "SurfaceRaised","#3D4348",
                "SurfaceHover","#474E54", "SurfacePress","#31363A", "SurfaceInput","#24282B",
                "SurfaceAlert","#3B2A26", "Rule","#4A5157", "RuleStrong","#656D74",
                "Ink","#F1F2F2", "InkMuted","#B6BBBE", "InkFaint","#888E92",
                "Accent","#7CC0D2", "AccentDim","#3F5D66", "AccentWarm","#DCBE74",
                "AccentWarmDim","#6E5E38", "OnAccent","#2B2F33",
                "StateGood","#7CC0D2", "StateWarn","#DCBE74", "StateBad","#DC8471",
                "StateIdle","#888E92" }),

            Make("Slate", "Cool and dark, teal and oxidised gold.", true, new string[] {
                "Surface","#1E2225", "SurfaceAlt","#252A2E", "SurfaceRaised","#2E3438",
                "SurfaceHover","#373E42", "SurfacePress","#23282B", "SurfaceInput","#191D20",
                "SurfaceAlert","#2E211D", "Rule","#3D444A", "RuleStrong","#565E64",
                "Ink","#ECEBE7", "InkMuted","#A7ABAC", "InkFaint","#767B7E",
                "Accent","#7FBAC8", "AccentDim","#3E5A63", "AccentWarm","#D2B36C",
                "AccentWarmDim","#6A5A36", "OnAccent","#1E2225",
                "StateGood","#7FBAC8", "StateWarn","#D2B36C", "StateBad","#CB7360",
                "StateIdle","#767B7E" }),

            // Matched to the game's own menus: warm near-black rather than the
            // cool grey of the other dark themes, and gold in place of teal.
            Make("Beast", "Matched to Dying Light: The Beast's own menus.", true, new string[] {
                "Surface","#1C1A18", "SurfaceAlt","#232120", "SurfaceRaised","#2C2926",
                "SurfaceHover","#37332E", "SurfacePress","#211F1C", "SurfaceInput","#151412",
                "SurfaceAlert","#2E1F1A", "Rule","#3A3631", "RuleStrong","#565049",
                "Ink","#F2EEE6", "InkMuted","#ADA79D", "InkFaint","#7C766C",
                "Accent","#E0B558", "AccentDim","#6B5528", "AccentWarm","#E0B558",
                "AccentWarmDim","#6B5528", "OnAccent","#1C1A18",
                "StateGood","#E0B558", "StateWarn","#D08A3E", "StateBad","#D2705A",
                "StateIdle","#7C766C" }),
        };

        public static ThemeDefinition Find(string name)
        {
            foreach (ThemeDefinition theme in All)
                if (string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase))
                    return theme;
            foreach (ThemeDefinition theme in All)
                if (theme.Name == Default) return theme;
            return All[0];
        }

        /* Repaint everything. Safe before any window exists, and safe to call
           again at any time. The first call behaves differently; see above. */
        public static void Apply(string name)
        {
            Application app = Application.Current;
            if (app == null) return;
            ThemeDefinition theme = Find(name);
            _current = theme.Name;

            foreach (KeyValuePair<string, string> entry in theme.Colors)
            {
                Color color = (Color)ColorConverter.ConvertFromString(entry.Value);
                SolidColorBrush brush = app.Resources[entry.Key] as SolidColorBrush;
                if (brush != null && !brush.IsFrozen)
                {
                    brush.Color = color;
                    continue;
                }
                // Frozen or absent: install a mutable brush to repaint.
                app.Resources[entry.Key] = new SolidColorBrush(color);
            }

            foreach (Window window in app.Windows)
                WindowChrome.Apply(window, theme.Dark);
        }
    }

    /*
     * A title bar that matches the theme, drawn by the shell.
     *
     * Asking Windows to render its own chrome dark or light avoids
     * WindowStyle=None, which would mean reimplementing drag, resize, snap,
     * minimise, maximise and the accessibility behaviours that come with them.
     *
     * The attribute id is 19 before Windows 10 20H1 and 20 after, so both are
     * tried. Failure is silent: a mismatched title bar is cosmetic and not a
     * reason to fail a window.
     */
    internal static class WindowChrome
    {
        private const int BeforeTwentyH1 = 19;
        private const int UseImmersiveDarkMode = 20;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr window, int attribute, ref int value, int size);

        public static void Apply(Window window, bool dark)
        {
            if (window == null) return;
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                // Not yet sourced. Ask again when it is, reading the theme then.
                window.SourceInitialized += delegate
                {
                    Apply(window, Theme.Find(Theme.Current).Dark);
                };
                return;
            }
            try
            {
                int on = dark ? 1 : 0;
                if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode,
                                          ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, BeforeTwentyH1, ref on, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}

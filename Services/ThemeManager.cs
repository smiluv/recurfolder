using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace RecurFolder.Services;

public enum AppTheme
{
    Default,
    Light,
    Dark,
    Ocean,
    Forest,
    Dusk,
    Glacier,
    Copper
}

public static class ThemeManager
{
    private static readonly string ThemePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RecurFolder",
        "theme.txt");

    public static AppTheme Selected { get; private set; } = AppTheme.Default;

    public static bool UseDarkTitleBar { get; private set; }

    public static bool UseSystemCaption { get; private set; }

    public static Color CaptionColor { get; private set; }

    public static Color CaptionTextColor { get; private set; }

    public static event EventHandler? ThemeChanged;

    public static void ApplyTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        var enabled = UseDarkTitleBar ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));

        var caption = UseSystemCaption ? unchecked((int)0xFFFFFFFE) : ColorRef(CaptionColor);
        var text = UseSystemCaption ? unchecked((int)0xFFFFFFFE) : ColorRef(CaptionTextColor);
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));
    }

    private static int ColorRef(Color color)
    {
        return color.R | (color.G << 8) | (color.B << 16);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void LoadAndApply()
    {
        Apply(Load(), save: false);
    }

    public static void Apply(AppTheme theme, bool save = true)
    {
        Selected = theme;
        if (theme == AppTheme.Default && SystemParameters.HighContrast)
            ApplyHighContrast();
        else
            ApplyPalette(PaletteFor(theme));

        if (save)
            Save(theme);

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static Palette PaletteFor(AppTheme theme)
    {
        var windowsBlue = Color.FromRgb(0x00, 0x78, 0xD4);
        return theme switch
        {
            AppTheme.Dark => Standard(dark: true, windowsBlue),
            AppTheme.Light => Standard(dark: false, windowsBlue),
            AppTheme.Ocean => DarkTheme(C(0x0B, 0x1F, 0x33), C(0x14, 0x32, 0x4C), C(0x3E, 0xC6, 0xE0), C(0x2A, 0x52, 0x74)),
            AppTheme.Forest => DarkTheme(C(0x12, 0x1A, 0x14), C(0x1E, 0x33, 0x24), C(0x8F, 0xCB, 0x6B), C(0x34, 0x54, 0x3A)),
            AppTheme.Dusk => DarkTheme(C(0x18, 0x13, 0x22), C(0x2A, 0x21, 0x40), C(0xE7, 0xB1, 0x5A), C(0x4A, 0x3C, 0x60)),
            AppTheme.Copper => DarkTheme(C(0x24, 0x16, 0x10), C(0x3A, 0x24, 0x1C), C(0xF0, 0xA3, 0x6A), C(0x5C, 0x40, 0x34)),
            AppTheme.Glacier => LightTheme(C(0xE4, 0xF0, 0xF4), C(0xF7, 0xFB, 0xFC), C(0x0E, 0x7C, 0x8A), C(0xB7, 0xD0, 0xD6)),
            _ => Standard(dark: !WindowsUsesLightTheme(), ReadWindowsAccent())
        };
    }

    private static Palette Standard(bool dark, Color accent)
    {
        if (dark)
        {
            return new Palette
            {
                DarkTitle = true,
                Window = C(0x1F, 0x1F, 0x1F),
                Surface = C(0x2B, 0x2B, 0x2B),
                Ink = Colors.White,
                Muted = C(0xC6, 0xC6, 0xC6),
                Line = C(0x45, 0x45, 0x45),
                Accent = accent,
                Field = C(0x1F, 0x1F, 0x1F),
                FieldBorder = C(0x76, 0x76, 0x76),
                Hint = C(0x9A, 0x9A, 0x9A),
                DisabledField = C(0x2A, 0x2A, 0x2A),
                Header = C(0x2B, 0x2B, 0x2B),
                HeaderText = Colors.White,
                HeaderLine = C(0x45, 0x45, 0x45),
                Row = C(0x1F, 0x1F, 0x1F),
                AltRow = C(0x25, 0x25, 0x25),
                GridLine = C(0x3A, 0x3A, 0x3A),
                Glyph = Luminance(accent) < 140 ? Blend(accent, Colors.White, 0.55) : accent,
                ButtonFace = C(0x2B, 0x2B, 0x2B),
                ButtonBorder = C(0x6B, 0x6B, 0x6B),
                ButtonHover = C(0x38, 0x38, 0x38),
                ButtonPressed = C(0x45, 0x45, 0x45),
                MenuHover = C(0x3A, 0x3A, 0x3A)
            };
        }

        return new Palette
        {
            DarkTitle = false,
            Window = C(0xF3, 0xF3, 0xF3),
            Surface = Colors.White,
            Ink = C(0x1A, 0x1A, 0x1A),
            Muted = C(0x61, 0x61, 0x61),
            Line = C(0xD0, 0xD0, 0xD0),
            Accent = accent,
            Field = Colors.White,
            FieldBorder = C(0x8A, 0x8A, 0x8A),
            Hint = C(0x6E, 0x6E, 0x6E),
            DisabledField = C(0xF3, 0xF3, 0xF3),
            Header = C(0xF3, 0xF3, 0xF3),
            HeaderText = C(0x1A, 0x1A, 0x1A),
            HeaderLine = C(0xD6, 0xD6, 0xD6),
            Row = Colors.White,
            AltRow = C(0xF7, 0xF7, 0xF7),
            GridLine = C(0xE6, 0xE6, 0xE6),
            Glyph = accent,
            ButtonFace = Colors.White,
            ButtonBorder = C(0xAC, 0xAC, 0xAC),
            ButtonHover = C(0xF0, 0xF0, 0xF0),
            ButtonPressed = C(0xE5, 0xE5, 0xE5),
            MenuHover = C(0xE8, 0xE8, 0xE8)
        };
    }

    private static Palette DarkTheme(Color window, Color surface, Color accent, Color line)
    {
        var ink = C(0xF4, 0xF6, 0xF8);
        return new Palette
        {
            DarkTitle = true,
            Window = window,
            Surface = surface,
            Ink = ink,
            Muted = Blend(ink, surface, 0.42),
            Line = line,
            Accent = accent,
            Field = window,
            FieldBorder = Blend(line, ink, 0.38),
            Hint = Blend(ink, surface, 0.58),
            DisabledField = surface,
            Header = surface,
            HeaderText = ink,
            HeaderLine = line,
            Row = Blend(window, surface, 0.35),
            AltRow = Blend(window, surface, 0.72),
            GridLine = line,
            Glyph = Luminance(accent) < 140 ? Blend(accent, Colors.White, 0.45) : accent,
            ButtonFace = surface,
            ButtonBorder = Blend(line, ink, 0.38),
            ButtonHover = Blend(surface, ink, 0.10),
            ButtonPressed = Blend(surface, ink, 0.16),
            MenuHover = Blend(surface, accent, 0.30)
        };
    }

    private static Palette LightTheme(Color window, Color surface, Color accent, Color line)
    {
        var ink = C(0x1A, 0x24, 0x28);
        return new Palette
        {
            DarkTitle = false,
            Window = window,
            Surface = surface,
            Ink = ink,
            Muted = Blend(ink, window, 0.42),
            Line = line,
            Accent = accent,
            Field = Colors.White,
            FieldBorder = Blend(line, ink, 0.28),
            Hint = Blend(ink, window, 0.55),
            DisabledField = window,
            Header = window,
            HeaderText = ink,
            HeaderLine = line,
            Row = Colors.White,
            AltRow = Blend(Colors.White, window, 0.65),
            GridLine = Blend(Colors.White, line, 0.7),
            Glyph = accent,
            ButtonFace = Colors.White,
            ButtonBorder = Blend(line, ink, 0.18),
            ButtonHover = Blend(surface, accent, 0.10),
            ButtonPressed = Blend(surface, accent, 0.18),
            MenuHover = Blend(surface, accent, 0.16)
        };
    }

    private static void ApplyPalette(Palette palette)
    {
        var accent = palette.Accent;
        var ink = palette.Ink;
        var surface = palette.Surface;
        UseDarkTitleBar = palette.DarkTitle;
        UseSystemCaption = false;
        CaptionColor = palette.Window;
        CaptionTextColor = palette.Ink;
        var accentText = ContrastText(accent);
        var selection = Blend(palette.DarkTitle ? surface : Colors.White, accent, palette.DarkTitle ? 0.45 : 0.22);
        var selectionText = ContrastText(selection);

        Set("WindowBackgroundBrush", palette.Window);
        Set("SurfaceBrush", surface);
        Set("InkBrush", ink);
        Set("MutedBrush", palette.Muted);
        Set("LineBrush", palette.Line);
        Set("AccentBrush", accent);
        Set("AccentHoverBrush", Blend(accent, Colors.Black, 0.12));
        Set("AccentPressedBrush", Blend(accent, Colors.Black, 0.22));
        Set("AccentTextBrush", accentText);
        Set("FieldBrush", palette.Field);
        Set("FieldBorderBrush", palette.FieldBorder);
        Set("HintBrush", palette.Hint);
        Set("DisabledFieldBrush", palette.DisabledField);
        Set("HeaderBrush", palette.Header);
        Set("HeaderTextBrush", palette.HeaderText);
        Set("HeaderLineBrush", palette.HeaderLine);
        Set("RowBrush", palette.Row);
        Set("AltRowBrush", palette.AltRow);
        Set("SelectionBrush", selection);
        Set("SelectionTextBrush", selectionText);
        Set("GridLineBrush", palette.GridLine);
        Set("GlyphBrush", palette.Glyph);
        Set("ButtonFaceBrush", palette.ButtonFace);
        Set("ButtonBorderBrush", palette.ButtonBorder);
        Set("ButtonHoverBrush", palette.ButtonHover);
        Set("ButtonPressedBrush", palette.ButtonPressed);
        Set("StatusBrush", palette.Window);
        Set("MenuHoverBrush", palette.MenuHover);
        Set("MenuHoverTextBrush", ink);
        Set("ScrollThumbBrush", Blend(ink, surface, palette.DarkTitle ? 0.55 : 0.74));
        Set("ScrollThumbHoverBrush", Blend(ink, surface, palette.DarkTitle ? 0.32 : 0.52));

        SetSystem(SystemColors.WindowBrushKey, surface);
        SetSystem(SystemColors.WindowTextBrushKey, ink);
        SetSystem(SystemColors.ControlBrushKey, palette.Window);
        SetSystem(SystemColors.ControlTextBrushKey, ink);
        SetSystem(SystemColors.MenuBrushKey, surface);
        SetSystem(SystemColors.MenuTextBrushKey, ink);
        SetSystem(SystemColors.MenuBarBrushKey, palette.Window);
        SetSystem(SystemColors.HighlightBrushKey, selection);
        SetSystem(SystemColors.HighlightTextBrushKey, selectionText);
        SetSystem(SystemColors.InactiveSelectionHighlightBrushKey, selection);
        SetSystem(SystemColors.InactiveSelectionHighlightTextBrushKey, selectionText);
        SetSystem(SystemColors.GrayTextBrushKey, palette.Muted);
    }

    private static void ApplyHighContrast()
    {
        UseDarkTitleBar = Luminance(SystemColors.ControlColor) < 128;
        UseSystemCaption = true;
        Set("WindowBackgroundBrush", SystemColors.ControlBrush);
        Set("SurfaceBrush", SystemColors.WindowBrush);
        Set("InkBrush", SystemColors.ControlTextBrush);
        Set("MutedBrush", SystemColors.GrayTextBrush);
        Set("LineBrush", SystemColors.ControlTextBrush);
        Set("AccentBrush", SystemColors.HighlightBrush);
        Set("AccentHoverBrush", SystemColors.HighlightBrush);
        Set("AccentPressedBrush", SystemColors.HighlightBrush);
        Set("AccentTextBrush", SystemColors.HighlightTextBrush);
        Set("FieldBrush", SystemColors.WindowBrush);
        Set("FieldBorderBrush", SystemColors.ControlTextBrush);
        Set("HintBrush", SystemColors.GrayTextBrush);
        Set("DisabledFieldBrush", SystemColors.ControlBrush);
        Set("HeaderBrush", SystemColors.ControlBrush);
        Set("HeaderTextBrush", SystemColors.ControlTextBrush);
        Set("HeaderLineBrush", SystemColors.ControlTextBrush);
        Set("RowBrush", SystemColors.WindowBrush);
        Set("AltRowBrush", SystemColors.WindowBrush);
        Set("SelectionBrush", SystemColors.HighlightBrush);
        Set("SelectionTextBrush", SystemColors.HighlightTextBrush);
        Set("GridLineBrush", SystemColors.ControlTextBrush);
        Set("GlyphBrush", SystemColors.ControlTextBrush);
        Set("ButtonFaceBrush", SystemColors.ControlBrush);
        Set("ButtonBorderBrush", SystemColors.ControlTextBrush);
        Set("ButtonHoverBrush", SystemColors.ControlLightBrush);
        Set("ButtonPressedBrush", SystemColors.ControlDarkBrush);
        Set("StatusBrush", SystemColors.ControlBrush);
        Set("MenuHoverBrush", SystemColors.HighlightBrush);
        Set("MenuHoverTextBrush", SystemColors.HighlightTextBrush);
        Set("ScrollThumbBrush", SystemColors.ControlTextBrush);
        Set("ScrollThumbHoverBrush", SystemColors.ControlTextBrush);

        SetSystem(SystemColors.WindowBrushKey, SystemColors.WindowBrush);
        SetSystem(SystemColors.WindowTextBrushKey, SystemColors.WindowTextBrush);
        SetSystem(SystemColors.ControlBrushKey, SystemColors.ControlBrush);
        SetSystem(SystemColors.ControlTextBrushKey, SystemColors.ControlTextBrush);
        SetSystem(SystemColors.MenuBrushKey, SystemColors.MenuBrush);
        SetSystem(SystemColors.MenuTextBrushKey, SystemColors.MenuTextBrush);
        SetSystem(SystemColors.MenuBarBrushKey, SystemColors.MenuBarBrush);
        SetSystem(SystemColors.HighlightBrushKey, SystemColors.HighlightBrush);
        SetSystem(SystemColors.HighlightTextBrushKey, SystemColors.HighlightTextBrush);
        SetSystem(SystemColors.InactiveSelectionHighlightBrushKey, SystemColors.InactiveSelectionHighlightBrush);
        SetSystem(SystemColors.InactiveSelectionHighlightTextBrushKey, SystemColors.InactiveSelectionHighlightTextBrush);
        SetSystem(SystemColors.GrayTextBrushKey, SystemColors.GrayTextBrush);
    }

    private static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }

    private static Color ReadWindowsAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int packed)
            {
                var color = Color.FromRgb((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16));
                var luminance = Luminance(color);
                if (luminance > 20 && luminance < 245)
                    return color;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        return Color.FromRgb(0x00, 0x78, 0xD4);
    }

    private static AppTheme Load()
    {
        try
        {
            var text = File.ReadAllText(ThemePath).Trim();
            return Enum.TryParse(text, ignoreCase: true, out AppTheme theme) ? theme : AppTheme.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return AppTheme.Default;
        }
    }

    private static void Save(AppTheme theme)
    {
        try
        {
            var directory = Path.GetDirectoryName(ThemePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(ThemePath, theme.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Set(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }

    private static void Set(string key, Brush brush)
    {
        Application.Current.Resources[key] = brush;
    }

    private static void SetSystem(ResourceKey key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }

    private static void SetSystem(ResourceKey key, Brush brush)
    {
        Application.Current.Resources[key] = brush;
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static double Luminance(Color color)
    {
        return (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
    }

    private static Color ContrastText(Color background)
    {
        return Luminance(background) > 150 ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White;
    }

    private static Color C(byte red, byte green, byte blue) => Color.FromRgb(red, green, blue);

    private sealed class Palette
    {
        public bool DarkTitle { get; init; }
        public Color Window { get; init; }
        public Color Surface { get; init; }
        public Color Ink { get; init; }
        public Color Muted { get; init; }
        public Color Line { get; init; }
        public Color Accent { get; init; }
        public Color Field { get; init; }
        public Color FieldBorder { get; init; }
        public Color Hint { get; init; }
        public Color DisabledField { get; init; }
        public Color Header { get; init; }
        public Color HeaderText { get; init; }
        public Color HeaderLine { get; init; }
        public Color Row { get; init; }
        public Color AltRow { get; init; }
        public Color GridLine { get; init; }
        public Color Glyph { get; init; }
        public Color ButtonFace { get; init; }
        public Color ButtonBorder { get; init; }
        public Color ButtonHover { get; init; }
        public Color ButtonPressed { get; init; }
        public Color MenuHover { get; init; }
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Finderly.UI
{
    /// <summary>macOS-like light / dark palette. Brushes are frozen and also published as
    /// DynamicResources so XAML styles update live when the system theme flips.</summary>
    public static class Theme
    {
        public static bool Dark { get; private set; }
        public static bool Backdrop { get; set; }
        public static event Action Changed;

        public static readonly FontFamily UIFont = new FontFamily("SF Pro Text, SF Pro, Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily DisplayFont = new FontFamily("SF Pro Display, SF Pro, Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily MonoFont = new FontFamily("SF Mono, Menlo, Cascadia Mono, Consolas");
        public static Typeface Regular = new Typeface(UIFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        public static Typeface Medium = new Typeface(UIFont, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        public static Typeface Bold = new Typeface(UIFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public static Brush ContentBg, SidebarBg, Text, TextSecondary, TextTertiary, Separator, Accent, SelBg, SelBgInactive,
            SelText, SidebarSel, HoverBg, PressBg, Zebra, MenuBg, MenuBorder, MenuHighlight, ControlBg, ControlBorder, ControlBorderStrong,
            FieldBg, TabBarBg, TabActiveBg, FocusRing, IconSelBg, ToolGlyph, SidebarGlyph, ScrollThumb, KnobBg, TrackBg, ThumbBorder,
            DropHighlight, InfoBlue, HeaderText, PanelBg, QuickLookBg, LightsInactive;

        public static readonly Color[] TagColors =
        {
            Colors.Transparent,
            Color.FromRgb(0xFF, 0x45, 0x3A), Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xFF, 0xD6, 0x0A),
            Color.FromRgb(0x32, 0xD7, 0x4B), Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xBF, 0x5A, 0xF2),
            Color.FromRgb(0x98, 0x98, 0x9D),
        };
        static readonly Dictionary<int, Brush> tagBrushes = new Dictionary<int, Brush>();
        public static Brush TagBrush(int i)
        {
            if (i <= 0 || i >= TagColors.Length) i = 7;
            if (!tagBrushes.TryGetValue(i, out var b)) { b = new SolidColorBrush(TagColors[i]); b.Freeze(); tagBrushes[i] = b; }
            return b;
        }

        static Brush B(uint argb)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            b.Freeze();
            return b;
        }

        public static bool SystemIsDark()
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        public static void Apply(bool dark)
        {
            Dark = dark;
            if (!dark)
            {
                ContentBg = B(0xFFFFFFFF);
                SidebarBg = Backdrop ? B(0x8CF0F0F2) : B(0xFFE9E9EB);
                Text = B(0xE6000000);
                TextSecondary = B(0x85000000);
                TextTertiary = B(0x40000000);
                HeaderText = B(0x7A000000);
                Separator = B(0x1A000000);
                Accent = B(0xFF007AFF);
                SelBg = B(0xFF0064E1);
                SelBgInactive = B(0xFFDCDCDC);
                SelText = B(0xFFFFFFFF);
                SidebarSel = B(0x17000000);
                HoverBg = B(0x0F000000);
                PressBg = B(0x1C000000);
                Zebra = B(0xFFF4F5F5);
                MenuBg = B(0xFAF4F4F4);
                MenuBorder = B(0x2E000000);
                MenuHighlight = B(0xFF2C6FE6);
                ControlBg = B(0xFFFFFFFF);
                ControlBorder = B(0x29000000);
                ControlBorderStrong = B(0x47000000);
                FieldBg = B(0xFFFFFFFF);
                TabBarBg = B(0xFFE3E3E3);
                TabActiveBg = B(0xFFF6F6F6);
                FocusRing = B(0x80007AFF);
                IconSelBg = B(0x1F000000);
                ToolGlyph = B(0xB3000000);
                SidebarGlyph = B(0xFF007AFF);
                ScrollThumb = B(0x73000000);
                KnobBg = B(0xFFFFFFFF);
                TrackBg = B(0x1F000000);
                ThumbBorder = B(0x26000000);
                DropHighlight = B(0x33007AFF);
                InfoBlue = B(0xFF1F6FDB);
                PanelBg = B(0xFFECECEC);
                QuickLookBg = B(0xF2F5F5F5);
                LightsInactive = B(0xFFD0D0D2);
            }
            else
            {
                ContentBg = B(0xFF1E1E1E);
                SidebarBg = Backdrop ? B(0x73262628) : B(0xFF2A2A2C);
                Text = B(0xE6FFFFFF);
                TextSecondary = B(0x8CFFFFFF);
                TextTertiary = B(0x40FFFFFF);
                HeaderText = B(0x80FFFFFF);
                Separator = B(0x1FFFFFFF);
                Accent = B(0xFF0A84FF);
                SelBg = B(0xFF0058D0);
                SelBgInactive = B(0xFF464646);
                SelText = B(0xFFFFFFFF);
                SidebarSel = B(0x1FFFFFFF);
                HoverBg = B(0x14FFFFFF);
                PressBg = B(0x26FFFFFF);
                Zebra = B(0xFF242424);
                MenuBg = B(0xFA2B2B2D);
                MenuBorder = B(0x33FFFFFF);
                MenuHighlight = B(0xFF2C6FE6);
                ControlBg = B(0xFF4A4A4C);
                ControlBorder = B(0x1FFFFFFF);
                ControlBorderStrong = B(0x33FFFFFF);
                FieldBg = B(0xFF2A2A2A);
                TabBarBg = B(0xFF161616);
                TabActiveBg = B(0xFF2C2C2C);
                FocusRing = B(0x800A84FF);
                IconSelBg = B(0x26FFFFFF);
                ToolGlyph = B(0xB3FFFFFF);
                SidebarGlyph = B(0xFF0A84FF);
                ScrollThumb = B(0x80FFFFFF);
                KnobBg = B(0xFFCCCCCC);
                TrackBg = B(0x26FFFFFF);
                ThumbBorder = B(0x26FFFFFF);
                DropHighlight = B(0x400A84FF);
                InfoBlue = B(0xFF3F8EF7);
                PanelBg = B(0xFF323232);
                QuickLookBg = B(0xF2262626);
                LightsInactive = B(0xFF4A4A4C);
            }

            var r = Application.Current.Resources;
            r["UIFont"] = UIFont;
            void S(string k, Brush v) => r[k] = v;
            S("ContentBg", ContentBg); S("SidebarBg", SidebarBg); S("Text", Text); S("TextSecondary", TextSecondary);
            S("TextTertiary", TextTertiary); S("Separator", Separator); S("Accent", Accent); S("SelBg", SelBg);
            S("SelBgInactive", SelBgInactive); S("SidebarSel", SidebarSel); S("HoverBg", HoverBg); S("PressBg", PressBg);
            S("Zebra", Zebra); S("MenuBg", MenuBg); S("MenuBorder", MenuBorder); S("MenuHighlight", MenuHighlight);
            S("ControlBg", ControlBg); S("ControlBorder", ControlBorder); S("ControlBorderStrong", ControlBorderStrong);
            S("FieldBg", FieldBg); S("TabBarBg", TabBarBg); S("TabActiveBg", TabActiveBg); S("FocusRing", FocusRing);
            S("ToolGlyph", ToolGlyph); S("SidebarGlyph", SidebarGlyph); S("ScrollThumb", ScrollThumb); S("KnobBg", KnobBg);
            S("TrackBg", TrackBg); S("HeaderText", HeaderText); S("PanelBg", PanelBg); S("InfoBlue", InfoBlue);
            Changed?.Invoke();
        }

        /// <summary>Cached FormattedText factory.</summary>
        public static FormattedText Fmt(string s, double size, Brush b, Typeface tf, double dpi, double maxWidth = 0, TextAlignment align = TextAlignment.Left, int maxLines = 0)
        {
            var ft = new FormattedText(s ?? "", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, size, b, dpi);
            if (maxWidth > 0) ft.MaxTextWidth = Math.Max(1, maxWidth);
            ft.TextAlignment = align;
            if (maxLines > 0) { ft.MaxLineCount = maxLines; ft.Trimming = TextTrimming.CharacterEllipsis; }
            return ft;
        }

        static readonly Dictionary<(string, int, double, bool), string> fitCache = new Dictionary<(string, int, double, bool), string>();

        /// <summary>Truncates text in the middle (macOS style) so it fits in maxWidth on one line.</summary>
        public static string FitMiddle(string s, double size, double maxWidth, bool bold, double dpi)
        {
            if (string.IsNullOrEmpty(s) || maxWidth <= 8) return "";
            var key = (s, (int)maxWidth, size, bold);
            if (fitCache.TryGetValue(key, out var r)) return r;
            var tf = bold ? Bold : Regular;
            double W(string t) => new FormattedText(t, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, size, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
            if (W(s) <= maxWidth) r = s;
            else
            {
                int lo = 0, hi = s.Length - 1;
                string best = "…";
                while (lo <= hi)
                {
                    int keep = (lo + hi) / 2;
                    int head = (keep + 1) / 2, tail = keep - head;
                    var t = s.Substring(0, head) + "…" + s.Substring(s.Length - tail);
                    if (W(t) <= maxWidth) { best = t; lo = keep + 1; } else hi = keep - 1;
                }
                r = best;
            }
            if (fitCache.Count > 30000) fitCache.Clear();
            fitCache[key] = r;
            return r;
        }

        static readonly Dictionary<(string, int, double, int), string> wrapCache = new Dictionary<(string, int, double, int), string>();

        /// <summary>Middle-truncates text so that it wraps into at most maxLines lines.</summary>
        public static string FitLines(string s, double size, double maxWidth, int maxLines, double dpi)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var key = (s, (int)maxWidth, size, maxLines);
            if (wrapCache.TryGetValue(key, out var r)) return r;
            double lineH = new FormattedText("Ag", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Regular, size, Brushes.Black, dpi).Height;
            if (lineH <= 0) lineH = size * 1.3;
            double H(string t)
            {
                var ft = new FormattedText(t, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Regular, size, Brushes.Black, dpi) { MaxTextWidth = Math.Max(1, maxWidth), TextAlignment = TextAlignment.Center };
                return ft.Height / lineH;
            }
            if (H(s) <= maxLines + 0.01) r = s;
            else
            {
                int lo = 0, hi = s.Length - 1; string best = "…";
                while (lo <= hi)
                {
                    int keep = (lo + hi) / 2;
                    int head = (int)Math.Ceiling(keep * 0.6), tail = keep - head;
                    var t = s.Substring(0, head) + "…" + s.Substring(s.Length - tail);
                    if (H(t) <= maxLines + 0.01) { best = t; lo = keep + 1; } else hi = keep - 1;
                }
                r = best;
            }
            if (wrapCache.Count > 20000) wrapCache.Clear();
            wrapCache[key] = r;
            return r;
        }
    }
}

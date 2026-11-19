using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Finderly.Core;

namespace Finderly.UI
{
    /// <summary>Hand-drawn line glyphs on a 24x24 grid (SF-Symbols-like weight).</summary>
    public static class Glyphs
    {
        static readonly Dictionary<string, Geometry> cache = new Dictionary<string, Geometry>();

        static string C(double cx, double cy, double r) =>
            FormattableString.Invariant($"M{cx + r},{cy} A{r},{r} 0 1 1 {cx - r},{cy} A{r},{r} 0 1 1 {cx + r},{cy} Z ");

        static string Data(string name)
        {
            switch (name)
            {
                case "recents": return C(12, 12, 8.5) + "M12,7 L12,12 L15.5,14.2";
                case "apps": return "M7.5,20 L12,4.5 L16.5,20 M9.2,14.5 H14.8 M5,17.5 H19";
                case "desktop": return "M3.5,5 H20.5 V15.5 H3.5 Z M9,19.5 H15 M12,15.5 V19.5";
                case "documents": return "M6,3.5 H13.5 L18.5,8.5 V20.5 H6 Z M13.5,3.5 V8.5 H18.5 M8.8,12.5 H15.5 M8.8,15.5 H15.5";
                case "downloads": return C(12, 12, 8.5) + "M12,7.3 V16.2 M8.6,12.9 L12,16.3 L15.4,12.9";
                case "home": return "M3.8,11.2 L12,4.2 L20.2,11.2 M6.2,9.4 V19.8 H10.2 V14.6 H13.8 V19.8 H17.8 V9.4";
                case "pictures": return "M3.5,5.5 H20.5 V18.5 H3.5 Z M3.5,15.5 L8.5,11 L12.5,14.5 L15.5,12 L20.5,16.5 " + C(15.5, 9, 1.4);
                case "music": return "M9.5,17.5 V6 L19,4 V15.5 " + C(7.5, 17.5, 2.1) + C(17, 15.5, 2.1);
                case "movies": return "M4,4.5 H20 V19.5 H4 Z M8,4.5 V19.5 M16,4.5 V19.5 M4,8.5 H8 M4,12 H8 M4,15.5 H8 M16,8.5 H20 M16,12 H20 M16,15.5 H20";
                case "drive": return "M3.5,13 H20.5 V19 H3.5 Z M3.5,13 L6.5,5.5 H17.5 L20.5,13 M16.5,16 H17.6";
                case "usb": return "M3.5,13 H20.5 V19 H3.5 Z M3.5,13 L6.5,5.5 H17.5 L20.5,13 M16.5,16 H17.6";
                case "network": return C(12, 12, 8.5) + "M3.5,12 H20.5 M12,3.5 C8,8 8,16 12,20.5 M12,3.5 C16,8 16,16 12,20.5";
                case "cloud": return "M7.2,18.5 H17.2 A3.6,3.6 0 0 0 17.4,11.3 A5.6,5.6 0 0 0 6.6,10.8 A3.9,3.9 0 0 0 7.2,18.5 Z";
                case "trash": return "M4.5,7 H19.5 M9.2,7 V4.5 H14.8 V7 M6.3,7 L7.3,20 H16.7 L17.7,7 M10,10.5 V16.5 M14,10.5 V16.5";
                case "computer": return "M5,5.5 H19 V15.5 H5 Z M2.5,18.5 H21.5";
                case "folder": return "M3.5,6.5 H9.5 L11.5,8.5 H20.5 V18.5 H3.5 Z";
                case "tag": return "M3.5,11.8 V4.5 H10.8 L19.8,13.5 L12.8,20.5 Z " + C(7.6, 8.6, 1.3);
                case "back": return "M14.8,5.5 L8.3,12 L14.8,18.5";
                case "forward": return "M9.2,5.5 L15.7,12 L9.2,18.5";
                case "up": return "M5.5,14.8 L12,8.3 L18.5,14.8";
                case "down": return "M5.5,9.2 L12,15.7 L18.5,9.2";
                case "chevdown": return "M8,10 L12,14 L16,10";
                case "chevright": return "M10,8 L14,12 L10,16";
                case "view-icon": return "M4.5,4.5 H10.5 V10.5 H4.5 Z M13.5,4.5 H19.5 V10.5 H13.5 Z M4.5,13.5 H10.5 V19.5 H4.5 Z M13.5,13.5 H19.5 V19.5 H13.5 Z";
                case "view-list": return "M8.5,6.5 H20 M8.5,12 H20 M8.5,17.5 H20 M4.2,6.5 H4.8 M4.2,12 H4.8 M4.2,17.5 H4.8";
                case "view-column": return "M3.5,5 H20.5 V19 H3.5 Z M9.2,5 V19 M14.8,5 V19";
                case "view-gallery": return "M3.5,4 H20.5 V14.5 H3.5 Z M3.5,18.5 H7 M10.25,18.5 H13.75 M17,18.5 H20.5";
                case "group": return "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z";
                case "share": return "M12,3.5 V14.5 M8.3,7.2 L12,3.5 L15.7,7.2 M8.8,10 H6.5 V20.5 H17.5 V10 H15.2";
                case "more": return C(12, 12, 8.5) + C(8, 12, 0.6) + C(12, 12, 0.6) + C(16, 12, 0.6);
                case "search": return C(10.5, 10.5, 6) + "M15,15 L20,20";
                case "sidebar": return "M3.5,5 H20.5 V19 H3.5 Z M9.5,5 V19 M5.5,8 H7.5 M5.5,10.5 H7.5 M5.5,13 H7.5";
                case "zoomin": return C(10.5, 10.5, 6.5) + "M15.2,15.2 L20.5,20.5 M10.5,7.5 V13.5 M7.5,10.5 H13.5";
                case "zoomout": return C(10.5, 10.5, 6.5) + "M15.2,15.2 L20.5,20.5 M7.5,10.5 H13.5";
                case "info": return C(12, 12, 8.5) + "M12,11 V16.5 M11.4,7.9 H12.6";
                case "markup": return C(12, 12, 8.5) + "M8.8,15.2 L9.3,12.6 L14.6,7.3 L16.7,9.4 L11.4,14.7 Z";
                case "rotate": return "M7.5,10.5 H17 V20 H7.5 Z M4.5,12 A7,7 0 0 1 11.5,4.5 H13 M11,2.3 L13.3,4.5 L11,6.7";
                case "rotateright": return "M7,10.5 H16.5 V20 H7 Z M19.5,12 A7,7 0 0 0 12.5,4.5 H11 M13,2.3 L10.7,4.5 L13,6.7";
                case "close": return "M7,7 L17,17 M17,7 L7,17";
                case "plus": return "M12,6 V18 M6,12 H18";
                case "minus": return "M6,12 H18";
                case "pen": return "M5,19 L6,15 L15.5,5.5 L18.5,8.5 L9,18 Z M13.5,7.5 L16.5,10.5";
                case "sketch": return "M4,16 C7,8 9,8 10,13 C11,18 14,18 16,10 C17,7 19,6 20,8";
                case "shapes": return "M3.5,10 H11.5 V18 H3.5 Z " + C(16, 8, 4.5);
                case "text": return "M5,6.5 V4.5 H19 V6.5 M12,4.5 V19.5 M9,19.5 H15";
                case "crop": return "M7,3 V17 H21 M3,7 H17 V21";
                case "select": return "M4.5,4.5 H7 M9.5,4.5 H12 M14.5,4.5 H17 M19.5,4.5 V7 M19.5,9.5 V12 M19.5,14.5 V17 M19.5,19.5 H17 M14.5,19.5 H12 M9.5,19.5 H7 M4.5,19.5 V17 M4.5,14.5 V12 M4.5,9.5 V7";
                case "linewidth": return "M4,6 H20 M4,11 H20 M4,17 H20";
                case "adjust": return "M12,3.5 V20.5 M3.5,12 H20.5 " + C(12, 12, 8.5);
                case "resize": return "M4,9 V4 H9 M20,15 V20 H15 M4,4 L10,10 M20,20 L14,14";
                case "flip": return "M12,3 V21 M9,6 L3,18 H9 Z M15,6 L21,18 H15 Z";
                case "eject": return "M12,5.5 L18.5,13 H5.5 Z M5.5,17 H18.5";
                case "terminal": return "M3.5,5 H20.5 V19 H3.5 Z M7,9.5 L10,12 L7,14.5 M12,15 H16";
                case "link": return "M10,14 L14,10 M8.5,11.5 L6.5,13.5 A3,3 0 0 0 10.5,17.5 L12.5,15.5 M15.5,12.5 L17.5,10.5 A3,3 0 0 0 13.5,6.5 L11.5,8.5";
                case "star": return "M12,3.8 L14.4,9 L20,9.5 L15.7,13.2 L17,18.7 L12,15.8 L7,18.7 L8.3,13.2 L4,9.5 L9.6,9 Z";
                case "airdrop": return C(12, 12, 1.5) + "M8.5,15.5 A5,5 0 1 1 15.5,15.5 M6,18 A8.5,8.5 0 1 1 18,18";
                case "slideshow": return "M3.5,5 H20.5 V17 H3.5 Z M10,8.5 L15,11 L10,13.5 Z M8,20.5 H16";
                case "fullscreen": return "M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15";
                case "play": return "M8,5 L19,12 L8,19 Z";
                case "pause": return "M8,5 V19 M16,5 V19";
                case "check": return "M5,12.5 L10,17.5 L19,6.5";
                case "highlight": return "M5,20 H19 M8,16 L7,13 L14,6 L17,9 L10,16 Z";
                case "arrow": return "M5,19 L19,5 M11,5 H19 V13";
                case "line": return "M5,19 L19,5";
                case "rect": return "M4.5,6.5 H19.5 V17.5 H4.5 Z";
                case "oval": return "M19.5,12 A7.5,5.5 0 1 1 4.5,12 A7.5,5.5 0 1 1 19.5,12 Z";
                case "color": return C(12, 12, 8);
                case "undo": return "M9,14 L4,9 L9,4 M4,9 H14 A6,6 0 0 1 14,21 H9";
                case "print": return "M6.5,9 V3.5 H17.5 V9 M6.5,17 H3.5 V9 H20.5 V17 H17.5 M6.5,13.5 H17.5 V20.5 H6.5 Z";
            }
            return "";
        }

        public static Geometry Get(string name)
        {
            if (cache.TryGetValue(name, out var g)) return g;
            g = Geometry.Parse(Data(name));
            g.Freeze();
            cache[name] = g;
            return g;
        }

        /// <summary>Creates a stroked glyph element; brush resource key picks the colour.</summary>
        public static Path Make(string name, double size = 18, string brushKey = "ToolGlyph", double stroke = 1.15)
        {
            var p = new Path
            {
                Data = Get(name),
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                StrokeThickness = stroke,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                SnapsToDevicePixels = false,
                IsHitTestVisible = false,
            };
            // keep 24-unit design box so glyphs align consistently
            p.Stretch = Stretch.Fill;
            p.Data = WithBox(name);
            p.SetResourceReference(Shape.StrokeProperty, brushKey);
            return p;
        }

        static readonly Dictionary<string, Geometry> boxed = new Dictionary<string, Geometry>();
        /// <summary>Geometry padded to the full 0..24 design box so Stretch=Fill keeps proportions.</summary>
        public static Geometry WithBox(string name)
        {
            if (boxed.TryGetValue(name, out var g)) return g;
            var grp = new GeometryGroup();
            grp.Children.Add(Get(name));
            grp.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, 0)));
            grp.Children.Add(new LineGeometry(new Point(24, 24), new Point(24, 24)));
            grp.Freeze();
            boxed[name] = grp;
            return grp;
        }

        public static void Draw(DrawingContext dc, string name, Rect r, Brush b, double stroke = 1.15)
        {
            var pen = new Pen(b, stroke * 24 / Math.Max(1, r.Width)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.PushTransform(new MatrixTransform(r.Width / 24, 0, 0, r.Height / 24, r.X, r.Y));
            dc.DrawGeometry(null, pen, Get(name));
            dc.Pop();
        }
    }

    /// <summary>Renders the blue macOS-style folder (and variants with embossed glyphs).</summary>
    public static class FolderArt
    {
        static readonly Dictionary<(string, int), BitmapSource> cache = new Dictionary<(string, int), BitmapSource>();

        public static Geometry BackShape, FrontShape;
        static FolderArt()
        {
            BackShape = Geometry.Parse("M10,20 C10,15.6 13.6,12 18,12 L44,12 C47,12 48.6,13 50.4,15.4 L53.8,19.8 C55.2,21.6 56.4,22 58.6,22 L110,22 C114.4,22 118,25.6 118,30 L118,100 C118,104.4 114.4,108 110,108 L18,108 C13.6,108 10,104.4 10,100 Z");
            FrontShape = Geometry.Parse("M10,38 C10,33.6 13.6,30 18,30 L110,30 C114.4,30 118,33.6 118,38 L118,100 C118,104.4 114.4,108 110,108 L18,108 C13.6,108 10,104.4 10,100 Z");
            BackShape.Freeze(); FrontShape.Freeze();
        }

        public static void DrawVector(DrawingContext dc, double size, string variant, bool dim = false)
        {
            double s = size / 128.0;
            dc.PushTransform(new ScaleTransform(s, s));
            var back = new LinearGradientBrush(Color.FromRgb(0x46, 0x9E, 0xE6), Color.FromRgb(0x2F, 0x8B, 0xDC), 90);
            var front = new LinearGradientBrush(Color.FromRgb(0x7E, 0xCB, 0xF7), Color.FromRgb(0x58, 0xB4, 0xF0), 90);
            if (dim) { back.Opacity = 0.55; front.Opacity = 0.55; }
            dc.DrawGeometry(back, null, BackShape);
            // inner paper sheet peeking out
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(dim ? (byte)120 : (byte)235, 0xF4, 0xFA, 0xFF)), null, new Rect(16, 25, 96, 30), 4, 4);
            dc.DrawGeometry(front, null, FrontShape);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 0xC8, 0xEA, 0xFF)), 1.6), new Point(14, 31.2), new Point(114, 31.2));
            if (variant != null)
            {
                var g = Glyphs.Get(variant);
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0x2C, 0x86, 0xD3)), 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                dc.PushTransform(new MatrixTransform(1.75, 0, 0, 1.75, 64 - 21, 69 - 21));
                dc.DrawGeometry(null, pen, g);
                dc.Pop();
            }
            dc.Pop();
        }

        public static BitmapSource Get(string variant, int px)
        {
            var key = (variant ?? "", px);
            if (cache.TryGetValue(key, out var b)) return b;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) DrawVector(dc, px, variant);
            var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            cache[key] = rtb;
            return rtb;
        }
    }

    /// <summary>Draws file icons the way Finder does: folders, real thumbnails, or a paper document with the type badge.</summary>
    public static class IconPainter
    {
        static readonly Geometry Page = Geometry.Parse("M20,6 L76,6 L108,38 L108,118 C108,120.2 106.2,122 104,122 L24,122 C21.8,122 20,120.2 20,118 L20,10 C20,7.8 21.8,6 24,6 Z");
        static readonly Geometry Fold = Geometry.Parse("M76,6 L76,34 C76,36.2 77.8,38 80,38 L108,38 Z");
        static readonly Dictionary<int, BitmapSource> pageCache = new Dictionary<int, BitmapSource>();
        static IconPainter() { Page.Freeze(); Fold.Freeze(); }

        static BitmapSource PageBitmap(int px)
        {
            if (pageCache.TryGetValue(px, out var b)) return b;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double s = px / 128.0;
                dc.PushTransform(new ScaleTransform(s, s));
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, Page.Clone().WithOffset(0, 1.5));
                dc.DrawGeometry(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), 1.2), Page);
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE6)), new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), 1.0), Fold);
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv); rtb.Freeze();
            pageCache[px] = rtb;
            return rtb;
        }

        static Geometry WithOffset(this Geometry g, double x, double y)
        {
            var c = g.Clone(); c.Transform = new TranslateTransform(x, y); return c;
        }

        public static string VariantFor(FileItem it) => it.IsDir ? KnownFolders.VariantOf(it.Path) : null;

        /// <summary>Draws the icon for an item into rect (DIPs). Returns the actual drawn rect.</summary>
        public static Rect Draw(DrawingContext dc, FileItem it, Rect r, double dpi, Action onReady, bool preview = true, bool selectedDim = false)
        {
            int px = Thumbs.Bucket(r.Width * dpi);
            if (it.IsDir && it.Special != SpecialKind.Drive)
            {
                dc.DrawImage(FolderArt.Get(VariantFor(it), px), r);
                return r;
            }
            bool iconOnly = !preview || it.Special == SpecialKind.Drive || it.Special == SpecialKind.AppShortcut;
            var t = Thumbs.Get(it.Path, px, onReady, iconOnly);
            if (t == null || t.Image == null)
            {
                if (r.Width >= 28 && !it.IsDir) { dc.DrawImage(PageBitmap(px), r); }
                return r;
            }
            var img = t.Image;
            if (t.IsThumbnail)
            {
                var fit = Fit(img, r);
                dc.DrawImage(img, fit);
                if (r.Width >= 30)
                    dc.DrawRectangle(null, new Pen(Theme.ThumbBorder, 1), new Rect(fit.X + 0.5, fit.Y + 0.5, Math.Max(0, fit.Width - 1), Math.Max(0, fit.Height - 1)));
                return fit;
            }
            bool appLike = Format.AppLikeExts.Contains(it.Ext) || it.Special != SpecialKind.None;
            if (r.Width >= 30 && !appLike && !it.IsDir)
            {
                dc.DrawImage(PageBitmap(px), r);
                double isz = r.Width * 0.42;
                var ir = new Rect(r.X + (r.Width - isz) / 2, r.Y + r.Height * 0.36, isz, isz);
                dc.DrawImage(img, Fit(img, ir));
                if (r.Width >= 48 && it.Ext.Length > 0 && it.Ext.Length <= 6)
                {
                    var ft = Theme.Fmt(it.Ext.ToUpperInvariant(), Math.Max(7, r.Width * 0.105), new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8E)), Theme.Bold, dpi);
                    dc.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2 + r.Width * 0.03, r.Y + r.Height * 0.8));
                }
                return r;
            }
            var f = Fit(img, r);
            dc.DrawImage(img, f);
            return f;
        }

        public static Rect Fit(ImageSource img, Rect r)
        {
            double w = img.Width, h = img.Height;
            if (img is BitmapSource bs) { w = bs.PixelWidth; h = bs.PixelHeight; }
            if (w <= 0 || h <= 0) return r;
            double sc = Math.Min(r.Width / w, r.Height / h);
            double dw = w * sc, dh = h * sc;
            return new Rect(r.X + (r.Width - dw) / 2, r.Y + (r.Height - dh) / 2, dw, dh);
        }

        /// <summary>Static image of an item's icon, for WPF Image elements (info windows etc.).</summary>
        public static ImageSource Snapshot(FileItem it, int size, Action onReady = null)
        {
            var dv = new DrawingVisual();
            double dpi = 1.0;
            try { dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip; } catch { }
            using (var dc = dv.RenderOpen()) Draw(dc, it, new Rect(0, 0, size, size), dpi, onReady);
            var rtb = new RenderTargetBitmap((int)(size * dpi), (int)(size * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            rtb.Render(dv); rtb.Freeze();
            return rtb;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Finderly.Core;
using Finderly.UI;

namespace Finderly.Views
{
    /// <summary>One column of the Column view: icon + name + disclosure chevron for folders.</summary>
    public sealed class ColumnSurface : ItemsSurface
    {
        public string FolderPath;
        public FileItem Highlight;   // the item that is "open" into the next column
        public event Action<ColumnSurface, FileItem> RightArrow;
        public event Action<ColumnSurface> LeftArrow;
        const double RowH = 24;

        protected override void DoLayout(double width, double height)
        {
            double y = 4;
            foreach (var e in entries) { e.Bounds = new Rect(0, y, width, RowH); y += RowH; }
            extentH = y + 4; extentW = width;
        }

        double TextSz => Settings.Current.ColumnTextSize;

        public override Rect EditRect(FileItem it)
        {
            if (!posOf.TryGetValue(it, out var p)) return Rect.Empty;
            var b = entries[items[p]].Bounds;
            var tl = ToSurface(new Point(b.X + 34, b.Y + 1));
            return new Rect(tl, new Size(Math.Max(80, b.Width - 56), RowH - 2));
        }

        protected override void DrawEntry(DrawingContext dc, int idx, Entry e, bool sel)
        {
            var b = e.Bounds; var it = e.Item;
            bool active = LooksActive && !DimSelection;
            bool open = !sel && Highlight != null && it.Equals(Highlight);
            if (sel || open)
            {
                var r = new Rect(6, b.Y, Math.Max(0, b.Width - 12), b.Height);
                Brush bg = sel && active ? Theme.SelBg : Theme.SelBgInactive;
                dc.DrawRoundedRectangle(bg, null, r, 5, 5);
            }
            bool hl = sel && active;
            Brush fg = hl ? Theme.SelText : Theme.Text;
            bool cut = IsCut(it);
            if (cut) dc.PushOpacity(0.45);
            double ic = RowH - 6;
            if (Settings.Current.ColumnShowIcons)
                IconPainter.Draw(dc, it, new Rect(14, b.Y + 3, ic, ic), dpi, Redraw, Settings.Current.ShowIconPreview);
            double x = Settings.Current.ColumnShowIcons ? 14 + ic + 6 : 14;
            double rightPad = it.IsDir ? 24 : 12;
            var tw = Settings.Current.TagsOf(it.Path) != null ? TagDotsWidth(it, 8) : 0;
            var text = Theme.FitMiddle(it.DisplayName, TextSz, b.Width - x - rightPad - tw, false, dpi);
            var ft = Theme.Fmt(text, TextSz, fg, Theme.Regular, dpi);
            dc.DrawText(ft, new Point(x, b.Y + (RowH - ft.Height) / 2));
            if (tw > 0) DrawTagDots(dc, it, new Point(x + ft.WidthIncludingTrailingWhitespace + 5, b.Y + RowH / 2), 8);
            if (it.IsDir && it.IsReal)
            {
                var pen = new Pen(hl ? Theme.SelText : Theme.TextSecondary, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                double cx = b.Right - 16, cy = b.Y + RowH / 2;
                var g = new StreamGeometry();
                using (var c = g.Open()) { c.BeginFigure(new Point(cx - 2, cy - 4), false, false); c.LineTo(new Point(cx + 2, cy), true, true); c.LineTo(new Point(cx - 2, cy + 4), true, true); }
                dc.DrawGeometry(null, pen, g);
            }
            if (cut) dc.Pop();
        }

        protected override bool OnKey(KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0) return false;
            if (e.Key == Key.Right) { RightArrow?.Invoke(this, FocusedItem); return true; }
            if (e.Key == Key.Left) { LeftArrow?.Invoke(this); return true; }
            return false;
        }
    }

    /// <summary>Gallery strip: row of square thumbnails, selected one shown big above.</summary>
    public sealed class GalleryStrip : ItemsSurface
    {
        protected override bool HorizontalScroll => true;
        double Cell => Settings.Current.GalleryThumbSize + 22;

        protected override void DoLayout(double width, double height)
        {
            double x = 6, c = Cell;
            foreach (var e in entries) { e.Bounds = new Rect(x, 0, c, ActualHeight); x += c; }
            extentW = x + 6; extentH = ActualHeight;
        }

        public override Rect EditRect(FileItem it) => Rect.Empty;

        protected override void DrawEntry(DrawingContext dc, int idx, Entry e, bool sel)
        {
            var b = e.Bounds; var it = e.Item;
            double sz = Settings.Current.GalleryThumbSize;
            var r = new Rect(b.X + (b.Width - sz) / 2, b.Y + 8, sz, sz);
            bool active = LooksActive;
            if (sel)
                dc.DrawRoundedRectangle(null, new Pen(active ? Theme.Accent : Theme.TextTertiary, 3), new Rect(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), 5, 5);
            IconPainter.Draw(dc, it, r, dpi, Redraw, true);
            var text = Theme.FitMiddle(it.DisplayName, 11, b.Width - 4, false, dpi);
            var ft = Theme.Fmt(text, 11, sel && active ? Theme.SelText : Theme.TextSecondary, Theme.Regular, dpi);
            double tw = ft.WidthIncludingTrailingWhitespace;
            if (sel) dc.DrawRoundedRectangle(active ? Theme.SelBg : Theme.SelBgInactive, null, new Rect(b.X + (b.Width - tw) / 2 - 4, r.Bottom + 5, tw + 8, ft.Height + 2), 4, 4);
            dc.DrawText(ft, new Point(b.X + (b.Width - tw) / 2, r.Bottom + 6));
        }

        protected override int Navigate(int from, Key key)
        {
            if (from < 0) return 0;
            return key == Key.Left || key == Key.Up ? from - 1 : from + 1;
        }
    }
}

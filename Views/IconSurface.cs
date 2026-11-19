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
    /// <summary>Icon view: grid of large icons with centered, wrapped names.</summary>
    public sealed class IconSurface : ItemsSurface
    {
        double cellW, cellH, iconSz, textSz;
        int cols = 1;
        const double HeaderH = 30;
        public Func<FileItem, string> InfoLine;

        protected override void DoLayout(double width, double height)
        {
            var s = Settings.Current;
            iconSz = s.IconSize; textSz = s.IconTextSize;
            double pad = 14 + s.GridSpacing * 14;
            cellW = Math.Max(iconSz + 24, 76) + s.GridSpacing * 24;
            double textH = s.LabelBottom ? textSz * 1.3 * 2 + 8 + (s.ShowItemInfo ? textSz * 1.2 : 0) : 0;
            cellH = iconSz + 12 + textH + s.GridSpacing * 10;
            if (!s.LabelBottom) { cellW = iconSz + 150; cellH = iconSz + 14; }
            double usable = Math.Max(cellW, width - 12);
            cols = Math.Max(1, (int)(usable / cellW));
            double x0 = 6 + Math.Max(0, (usable - cols * cellW) / 2) * 0;
            double y = 6;
            int col = 0;
            double rowTop = y;
            foreach (var e in entries)
            {
                if (e.IsHeader)
                {
                    if (col != 0) { y += cellH; col = 0; }
                    e.Bounds = new Rect(0, y, width, HeaderH);
                    y += HeaderH; rowTop = y;
                    continue;
                }
                e.Bounds = new Rect(x0 + col * cellW, y, cellW, cellH);
                if (++col >= cols) { col = 0; y += cellH; }
            }
            if (col != 0) y += cellH;
            extentH = y + 10; extentW = width;
        }

        Rect IconRect(Rect b)
        {
            var s = Settings.Current;
            if (s.LabelBottom) return new Rect(b.X + (b.Width - iconSz) / 2, b.Y + 6, iconSz, iconSz);
            return new Rect(b.X + 8, b.Y + 7, iconSz, iconSz);
        }

        protected override Rect IconBox(Entry e) => IconRect(e.Bounds);

        double NameWidth => Settings.Current.LabelBottom ? cellW - 10 : 140;

        Rect LabelRect(Entry e, out string text, out int lines)
        {
            var b = e.Bounds;
            lines = 2;
            text = Theme.FitLines(e.Item.DisplayName, textSz, NameWidth - 8, 2, dpi);
            var ft = Theme.Fmt(text, textSz, Theme.Text, Theme.Regular, dpi, NameWidth - 8, TextAlignment.Center);
            if (Settings.Current.LabelBottom)
            {
                double w = Math.Min(NameWidth, ft.WidthIncludingTrailingWhitespace + 10);
                return new Rect(b.X + (b.Width - w) / 2, b.Y + iconSz + 11, w, ft.Height + 4);
            }
            double w2 = Math.Min(NameWidth, ft.WidthIncludingTrailingWhitespace + 10);
            return new Rect(b.X + iconSz + 14, b.Y + (b.Height - ft.Height) / 2 - 2, w2, ft.Height + 4);
        }

        protected override bool HitEntry(Entry e, Point c)
        {
            if (!e.Bounds.Contains(c)) return false;
            return true;
        }

        protected override bool IsNameHit(Entry e, Point c)
        {
            LabelRect(e, out _, out _);
            return LabelRect(e, out _, out _).Contains(c);
        }

        protected override bool IntersectsEntry(Entry e, Rect r)
        {
            var ic = IconRect(e.Bounds);
            if (ic.IntersectsWith(r)) return true;
            return LabelRect(e, out _, out _).IntersectsWith(r);
        }

        public override Rect EditRect(FileItem it)
        {
            if (!posOf.TryGetValue(it, out var p)) return Rect.Empty;
            var e = entries[items[p]];
            var lr = LabelRect(e, out _, out _);
            double w = Settings.Current.LabelBottom ? NameWidth : 150;
            double x = Settings.Current.LabelBottom ? e.Bounds.X + (e.Bounds.Width - w) / 2 : lr.X;
            var r = new Rect(x, lr.Y, w, Math.Max(lr.Height, textSz * 1.3 * 2 + 4));
            var tl = ToSurface(r.TopLeft);
            return new Rect(tl, r.Size);
        }

        protected override void DrawEntry(DrawingContext dc, int idx, Entry e, bool sel)
        {
            if (e.IsHeader)
            {
                var ft = Theme.Fmt(e.Header, 14, Theme.Text, Theme.Bold, dpi);
                dc.DrawText(ft, new Point(14, e.Bounds.Y + 6));
                dc.DrawRectangle(Theme.Separator, null, new Rect(14, e.Bounds.Bottom - 3, Math.Max(0, e.Bounds.Width - 28), 1));
                return;
            }
            var it = e.Item;
            var s = Settings.Current;
            var ic = IconRect(e.Bounds);
            bool active = LooksActive;
            bool cut = IsCut(it);
            if (cut) dc.PushOpacity(0.45);
            if (sel && s.LabelBottom)
            {
                dc.DrawRoundedRectangle(active ? Theme.IconSelBg : Theme.IconSelBg, null, new Rect(ic.X - 3, ic.Y - 3, ic.Width + 6, ic.Height + 6), 6, 6);
            }
            IconPainter.Draw(dc, it, ic, dpi, Redraw, s.ShowIconPreview);
            var tags = Settings.Current.TagsOf(it.Path);
            if (tags != null && tags.Count > 0 && s.LabelBottom)
                DrawTagDots(dc, it, new Point(ic.Right - 12 - tags.Count * 2, ic.Bottom - 5), 9);
            var text = Theme.FitLines(it.DisplayName, textSz, NameWidth - 8, 2, dpi);
            var fg = sel && active ? Theme.SelText : Theme.Text;
            var ftn = Theme.Fmt(text, textSz, fg, Theme.Regular, dpi, NameWidth - 8, s.LabelBottom ? TextAlignment.Center : TextAlignment.Left);
            var lr = LabelRect(e, out _, out _);
            if (sel)
                dc.DrawRoundedRectangle(active ? Theme.SelBg : Theme.SelBgInactive, null, lr, 4, 4);
            double tx = s.LabelBottom ? e.Bounds.X + (e.Bounds.Width - (NameWidth - 8)) / 2 : lr.X + 5;
            dc.DrawText(ftn, new Point(tx, lr.Y + 2));
            if (s.ShowItemInfo && s.LabelBottom && InfoLine != null)
            {
                var info = InfoLine(it);
                if (!string.IsNullOrEmpty(info))
                {
                    var fi = Theme.Fmt(info, Math.Max(10, textSz - 1.5), Theme.TextSecondary, Theme.Regular, dpi, NameWidth - 8, TextAlignment.Center);
                    dc.DrawText(fi, new Point(e.Bounds.X + 5, lr.Bottom + 1));
                }
            }
            if (cut) dc.Pop();
        }

        protected override int Navigate(int from, Key key)
        {
            int n = items.Count;
            if (from < 0) return key == Key.Up || key == Key.Left ? n - 1 : 0;
            // use geometric navigation to respect group headers
            var cur = entries[items[from]].Bounds;
            switch (key)
            {
                case Key.Left: return from - 1;
                case Key.Right: return from + 1;
                case Key.Down:
                    return NearestVertical(from, cur, 1);
                case Key.Up:
                    return NearestVertical(from, cur, -1);
            }
            return from;
        }

        int NearestVertical(int from, Rect cur, int dir)
        {
            double cx = cur.X + cur.Width / 2;
            int best = -1; double bestScore = double.MaxValue;
            for (int i = 0; i < items.Count; i++)
            {
                var b = entries[items[i]].Bounds;
                double dy = (b.Y - cur.Y) * dir;
                if (dy < cur.Height * 0.5) continue;
                double score = dy * 1000 + Math.Abs(b.X + b.Width / 2 - cx);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best >= 0 ? best : from;
        }

        public int Columns => cols;
    }
}

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
    public sealed class ColumnDef
    {
        public string Id, Title; public double Width; public double Min = 50; public SortKey? Sort; public bool Right;
    }

    /// <summary>List view: sortable resizable columns, zebra rows, expandable folders, group headers.</summary>
    public sealed class ListSurface : ItemsSurface
    {
        public static readonly Dictionary<string, ColumnDef> AllColumns = new Dictionary<string, ColumnDef>
        {
            ["name"] = new ColumnDef { Id = "name", Title = "Name", Width = 300, Min = 120, Sort = SortKey.Name },
            ["created"] = new ColumnDef { Id = "created", Title = "Date Created", Width = 170, Sort = SortKey.DateCreated },
            ["modified"] = new ColumnDef { Id = "modified", Title = "Date Modified", Width = 170, Sort = SortKey.DateModified },
            ["opened"] = new ColumnDef { Id = "opened", Title = "Date Last Opened", Width = 170, Sort = SortKey.DateLastOpened },
            ["size"] = new ColumnDef { Id = "size", Title = "Size", Width = 90, Sort = SortKey.Size, Right = true },
            ["kind"] = new ColumnDef { Id = "kind", Title = "Kind", Width = 150, Sort = SortKey.Kind },
            ["tags"] = new ColumnDef { Id = "tags", Title = "Tags", Width = 110, Sort = SortKey.Tags },
            ["comments"] = new ColumnDef { Id = "comments", Title = "Comments", Width = 160 },
            ["where"] = new ColumnDef { Id = "where", Title = "Where", Width = 220 },
        };

        public SortKey SortKey = SortKey.Name; public bool SortAsc = true;
        public event Action<SortKey, bool> SortChanged;
        public event Action<FileItem> ExpandToggled;
        public event Action ColumnsChanged;
        public bool ShowWhere;
        public bool AllowExpand = true;

        double rowH, textSz;
        const double HdrH = 22, GroupH = 26, Indent = 18;
        protected override double TopInset => HdrH;

        List<ColumnDef> Cols()
        {
            var s = Settings.Current;
            var l = new List<ColumnDef> { Def("name") };
            foreach (var id in s.ListColumns) if (AllColumns.ContainsKey(id) && id != "name") l.Add(Def(id));
            if (ShowWhere && !l.Any(c => c.Id == "where")) l.Add(Def("where"));
            return l;
        }

        ColumnDef Def(string id)
        {
            var b = AllColumns[id];
            double w = Settings.Current.ColumnWidths.TryGetValue(id, out var sw) ? sw : b.Width;
            return new ColumnDef { Id = b.Id, Title = b.Title, Width = w, Min = b.Min, Sort = b.Sort, Right = b.Right };
        }

        List<ColumnDef> cols = new List<ColumnDef>();
        List<double> colX = new List<double>();

        protected override void DoLayout(double width, double height)
        {
            var s = Settings.Current;
            textSz = s.ListTextSize;
            rowH = s.ListLargeIcons ? 34 : 22;
            cols = Cols();
            colX.Clear();
            double x = 0;
            foreach (var c in cols) { colX.Add(x); x += c.Width; }
            double total = Math.Max(width, x);
            // last column stretches
            if (x < width && cols.Count > 0) cols[cols.Count - 1].Width += width - x;
            double y = 0;
            foreach (var e in entries)
            {
                if (e.IsHeader) { e.Bounds = new Rect(0, y, total, GroupH); y += GroupH; }
                else { e.Bounds = new Rect(0, y, total, rowH); y += rowH; }
            }
            extentH = y + 4; extentW = Math.Max(total, x);
        }

        protected override bool HorizontalScroll => false;

        double IconSize => Math.Max(1, rowH - 6);

        Rect NameTextRect(Entry e)
        {
            double x = 8 + e.Depth * Indent + (AllowExpand ? 16 : 2) + IconSize + 6;
            return new Rect(x, e.Bounds.Y, Math.Max(0, cols[0].Width - x - 6), rowH);
        }

        protected override bool IsNameHit(Entry e, Point c)
        {
            var r = NameTextRect(e);
            var ft = Theme.Fmt(e.Item.DisplayName, textSz, Theme.Text, Theme.Regular, dpi);
            return c.X >= r.X && c.X <= r.X + Math.Min(r.Width, ft.Width);
        }

        public override Rect EditRect(FileItem it)
        {
            if (!posOf.TryGetValue(it, out var p)) return Rect.Empty;
            var e = entries[items[p]];
            var r = NameTextRect(e);
            var tl = ToSurface(new Point(r.X - 4, r.Y + 1));
            return new Rect(tl, new Size(Math.Max(120, r.Width + 4), rowH - 2));
        }

        // ---- header interactions ----
        int hdrResize = -1; double hdrResizeStart, hdrResizeW; int hdrPress = -1;

        int HdrHit(Point p, out bool edge)
        {
            edge = false;
            if (p.Y > HdrH) return -1;
            double px = p.X + offX;
            for (int i = 0; i < cols.Count; i++)
            {
                double r = colX[i] + cols[i].Width;
                if (Math.Abs(px - r) <= 4 && i < cols.Count) { edge = true; return i; }
                if (px >= colX[i] && px < r) return i;
            }
            return -1;
        }

        protected override bool HandleClick(Point p, MouseButtonEventArgs e)
        {
            if (p.Y > HdrH) return false;
            int i = HdrHit(p, out bool edge);
            if (i < 0) return true;
            if (edge) { hdrResize = i; hdrResizeStart = p.X; hdrResizeW = cols[i].Width; CaptureMouse(); return true; }
            hdrPress = i; CaptureMouse();
            return true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (hdrResize >= 0)
            {
                var id = cols[hdrResize].Id;
                double w = Math.Max(cols[hdrResize].Min, hdrResizeW + p.X - hdrResizeStart);
                Settings.Current.ColumnWidths[id] = w;
                Relayout();
                return;
            }
            if (p.Y <= HdrH) { HdrHit(p, out bool edge); Cursor = edge ? Cursors.SizeWE : Cursors.Arrow; } else if (Cursor != Cursors.Arrow) Cursor = Cursors.Arrow;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (hdrResize >= 0) { hdrResize = -1; ReleaseMouseCapture(); Settings.Save(false); ColumnsChanged?.Invoke(); return; }
            if (hdrPress >= 0)
            {
                var p = e.GetPosition(this);
                int i = HdrHit(p, out bool edge);
                int pr = hdrPress; hdrPress = -1; ReleaseMouseCapture();
                if (i == pr && !edge && cols[i].Sort.HasValue)
                {
                    var k = cols[i].Sort.Value;
                    if (k == SortKey) SortAsc = !SortAsc; else { SortKey = k; SortAsc = true; }
                    InvalidateVisual();
                    SortChanged?.Invoke(SortKey, SortAsc);
                }
                return;
            }
            base.OnMouseLeftButtonUp(e);
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            var p = e.GetPosition(this);
            if (p.Y <= HdrH) { HeaderContext?.Invoke(); e.Handled = true; return; }
            base.OnMouseRightButtonDown(e);
        }
        public event Action HeaderContext;

        protected override bool HitEntry(Entry e, Point c) => e.Bounds.Contains(c);

        protected override bool IntersectsEntry(Entry e, Rect r) => e.Bounds.IntersectsWith(new Rect(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height))) && r.X < colX[0] + cols[0].Width;

        // ---- expanding folders ----
        protected override bool OnKey(KeyEventArgs e)
        {
            var key = e.Key;
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return false;
            var f = FocusedItem;
            if (f == null || !AllowExpand) return false;
            int ei = items[focusPos];
            if (key == Key.Right && f.IsDir && !entries[ei].Expanded) { ExpandToggled?.Invoke(f); return true; }
            if (key == Key.Left)
            {
                if (f.IsDir && entries[ei].Expanded) { ExpandToggled?.Invoke(f); return true; }
                // go to parent
                int d = entries[ei].Depth;
                if (d > 0) for (int i = focusPos - 1; i >= 0; i--) if (entries[items[i]].Depth < d) { SetFocusPos(i, false); return true; }
                return true;
            }
            return false;
        }

        protected override int Navigate(int from, Key key) => base.Navigate(from, key);

        // ---- drawing ----
        protected override void DrawUnderlay(DrawingContext dc, Rect vis)
        {
            // zebra
            int n = 0;
            foreach (var e in entries)
            {
                if (e.Bounds.Bottom < vis.Top) { if (!e.IsHeader) n++; continue; }
                if (e.Bounds.Top > vis.Bottom) break;
                if (!e.IsHeader) { if (n % 2 == 1) dc.DrawRectangle(Theme.Zebra, null, new Rect(0, e.Bounds.Y, Math.Max(extentW, VpW), e.Bounds.Height)); n++; }
            }
        }

        protected override void DrawEntry(DrawingContext dc, int idx, Entry e, bool sel)
        {
            var b = e.Bounds;
            if (e.IsHeader)
            {
                var ft = Theme.Fmt(e.Header, 12.5, Theme.Text, Theme.Bold, dpi);
                dc.DrawText(ft, new Point(10, b.Y + 5));
                dc.DrawRectangle(Theme.Separator, null, new Rect(0, b.Bottom - 1, b.Width, 1));
                return;
            }
            var it = e.Item;
            bool active = LooksActive;
            if (sel)
            {
                // Finder merges adjacent selected rows into one rounded block
                bool prevSel = idx > 0 && !entries[idx - 1].IsHeader && Selection.Contains(entries[idx - 1].Item);
                bool nextSel = idx + 1 < entries.Count && !entries[idx + 1].IsHeader && Selection.Contains(entries[idx + 1].Item);
                var r = new Rect(6, b.Y, Math.Max(0, Math.Max(VpW, extentW) - 12), b.Height);
                dc.DrawGeometry(active ? Theme.SelBg : Theme.SelBgInactive, null, RoundedRect(r, prevSel ? 0 : 5, prevSel ? 0 : 5, nextSel ? 0 : 5, nextSel ? 0 : 5));
            }
            Brush fg = sel && active ? Theme.SelText : Theme.Text;
            Brush fg2 = sel && active ? Theme.SelText : Theme.TextSecondary;
            bool cut = IsCut(it);
            if (cut) dc.PushOpacity(0.45);
            double x = 8 + e.Depth * Indent;
            if (AllowExpand)
            {
                if (it.IsDir && it.Special != SpecialKind.TrashItem)
                {
                    var pen = new Pen(sel && active ? Theme.SelText : Theme.TextSecondary, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    double cy = b.Y + rowH / 2;
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        if (e.Expanded) { c.BeginFigure(new Point(x + 2.5, cy - 2), false, false); c.LineTo(new Point(x + 6.5, cy + 2), true, true); c.LineTo(new Point(x + 10.5, cy - 2), true, true); }
                        else { c.BeginFigure(new Point(x + 4.5, cy - 4), false, false); c.LineTo(new Point(x + 8.5, cy), true, true); c.LineTo(new Point(x + 4.5, cy + 4), true, true); }
                    }
                    dc.DrawGeometry(null, pen, g);
                }
                x += 16;
            }
            else x += 2;
            var ic = new Rect(x, b.Y + (rowH - IconSize) / 2, IconSize, IconSize);
            IconPainter.Draw(dc, it, ic, dpi, Redraw, Settings.Current.ShowIconPreview);
            x += IconSize + 6;
            for (int i = 0; i < cols.Count; i++)
            {
                var c = cols[i];
                double cx = colX[i] + (i == 0 ? 0 : 10);
                double cw = c.Width - (i == 0 ? 0 : 14);
                if (i == 0)
                {
                    double nameW = c.Width - x - 8;
                    var tw = Settings.Current.TagsOf(it.Path) != null ? TagDotsWidth(it, 9) : 0;
                    var text = Theme.FitMiddle(it.DisplayName, textSz, nameW - tw, false, dpi);
                    var ft = Theme.Fmt(text, textSz, fg, Theme.Regular, dpi);
                    dc.DrawText(ft, new Point(x, b.Y + (rowH - ft.Height) / 2));
                    if (tw > 0) DrawTagDots(dc, it, new Point(x + ft.WidthIncludingTrailingWhitespace + 6, b.Y + rowH / 2), 9);
                    continue;
                }
                string s = CellText(it, c.Id);
                if (string.IsNullOrEmpty(s)) continue;
                var cf = Theme.Fmt(Theme.FitMiddle(s, textSz, cw, false, dpi), textSz, fg2, Theme.Regular, dpi);
                double tx = c.Right ? colX[i] + c.Width - 10 - cf.WidthIncludingTrailingWhitespace : cx;
                dc.DrawText(cf, new Point(tx, b.Y + (rowH - cf.Height) / 2));
            }
            if (cut) dc.Pop();
        }

        public Func<FileItem, string> WhereOf;

        string CellText(FileItem it, string id)
        {
            switch (id)
            {
                case "modified": return it.Special == SpecialKind.Drive ? "--" : Format.Date(it.Modified, Settings.Current.RelativeDates);
                case "created": return it.Special == SpecialKind.Drive ? "--" : Format.Date(it.Created, Settings.Current.RelativeDates);
                case "opened": return Format.Date(FileSystem.LastOpenedOf(it), Settings.Current.RelativeDates);
                case "size":
                    if (it.IsDir) return it.DirSize >= 0 ? Format.Size(it.DirSize) : "--";
                    return Format.Size(it.Size);
                case "kind": return it.Kind;
                case "tags": { var t = Settings.Current.TagsOf(it.Path); return t == null ? "" : string.Join(", ", t); }
                case "comments": return Settings.Current.Comments.TryGetValue(it.Path, out var c) ? c : "";
                case "where": return WhereOf != null ? WhereOf(it) : System.IO.Path.GetDirectoryName(it.Path);
            }
            return "";
        }

        protected override void DrawOverlay(DrawingContext dc)
        {
            // header bar (fixed)
            var hb = new Rect(0, 0, ActualWidth, HdrH);
            dc.DrawRectangle(Theme.ContentBg, null, hb);
            dc.DrawRectangle(Theme.Separator, null, new Rect(0, HdrH - 1, ActualWidth, 1));
            dc.PushClip(new RectangleGeometry(hb));
            for (int i = 0; i < cols.Count; i++)
            {
                var c = cols[i];
                double x = colX[i] - offX;
                bool cur = c.Sort.HasValue && c.Sort.Value == SortKey;
                var ft = Theme.Fmt(c.Title, 11.5, cur ? Theme.Text : Theme.TextSecondary, cur ? Theme.Bold : Theme.Medium, dpi);
                double tx = i == 0 ? x + 10 : x + 10;
                if (c.Right) tx = x + c.Width - 10 - ft.Width - (cur ? 14 : 0);
                dc.DrawText(ft, new Point(tx, (HdrH - ft.Height) / 2));
                if (cur)
                {
                    var pen = new Pen(Theme.TextSecondary, 1.3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    double ax = tx + ft.Width + 8, ay = HdrH / 2;
                    var g = new StreamGeometry();
                    using (var cx = g.Open())
                    {
                        if (SortAsc) { cx.BeginFigure(new Point(ax - 3, ay + 1.5), false, false); cx.LineTo(new Point(ax, ay - 1.5), true, true); cx.LineTo(new Point(ax + 3, ay + 1.5), true, true); }
                        else { cx.BeginFigure(new Point(ax - 3, ay - 1.5), false, false); cx.LineTo(new Point(ax, ay + 1.5), true, true); cx.LineTo(new Point(ax + 3, ay - 1.5), true, true); }
                    }
                    dc.DrawGeometry(null, pen, g);
                }
                if (i > 0) dc.DrawRectangle(Theme.Separator, null, new Rect(x, 4, 1, HdrH - 8));
            }
            dc.Pop();
        }

        public double NameColumnWidth => cols.Count > 0 ? cols[0].Width : 300;
    }
}

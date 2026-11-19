using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Finderly.Core;
using Finderly.UI;

namespace Finderly.Views
{
    public sealed class SidebarRow
    {
        public string Id, Title, Glyph, Path;
        public bool IsHeader, Eject, Tag; public int TagColor;
        public Location Loc;
        public Rect Bounds;
        public bool Collapsed;
    }

    /// <summary>Finder-style sidebar: Favorites, OneDrive, Locations, Tags — fully custom drawn.</summary>
    public sealed class Sidebar : FrameworkElement
    {
        readonly List<SidebarRow> rows = new List<SidebarRow>();
        SidebarRow hot, dropRow; Location current;
        public event Action<Location, bool> Navigate;   // bool: open in new tab
        public event Action<SidebarRow> ContextRequested;
        public event Action<string, IDataObject, DragDropEffects> DropRequested;
        double dpi = 1, offY, extent;
        public event Action<string> EjectRequested;

        public Sidebar()
        {
            AllowDrop = true; ClipToBounds = true; Focusable = false; SnapsToDevicePixels = true;
            Theme.Changed += () => InvalidateVisual();
            Loaded += (s, e) => { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; Rebuild(); };
            Settings.Changed += Rebuild;
            FileOps.Changed += _ => { };
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (s, e) => { if (IsVisible) RefreshDrives(); };
            t.Start();
        }

        string driveSig = "";
        void RefreshDrives()
        {
            var sig = string.Join("|", DriveInfo.GetDrives().Select(d => d.Name + d.IsReady));
            if (sig != driveSig) Rebuild();
        }

        public void SetCurrent(Location l) { current = l; InvalidateVisual(); }

        static string TitleFor(string id)
        {
            switch (id)
            {
                case "::recents": return "Recents";
                case "::applications": return "Applications";
                case "::airdrop": return "AirDrop";
            }
            return Format.FolderTitle(id);
        }

        public void Rebuild()
        {
            var s = Settings.Current;
            rows.Clear();
            driveSig = string.Join("|", DriveInfo.GetDrives().Select(d => d.Name + d.IsReady));
            void Header(string id, string title) { rows.Add(new SidebarRow { IsHeader = true, Id = id, Title = title, Collapsed = s.CollapsedSections.Contains(id) }); }
            Header("favorites", "Favorites");
            if (!s.CollapsedSections.Contains("favorites"))
                foreach (var f in s.Favorites)
                {
                    if (s.HiddenSidebarItems.Contains(f)) continue;
                    if (f == "::recents") rows.Add(new SidebarRow { Id = f, Title = "Recents", Glyph = "recents", Loc = Location.Recents });
                    else if (f == "::applications") rows.Add(new SidebarRow { Id = f, Title = "Applications", Glyph = "apps", Loc = Location.Applications });
                    else if (Directory.Exists(f))
                        rows.Add(new SidebarRow { Id = f, Title = TitleFor(f), Glyph = KnownFolders.VariantOf(f) ?? "folder", Path = f, Loc = Location.Folder(f) });
                }
            var od = KnownFolders.OneDrive;
            if (od != null && !s.HiddenSidebarItems.Contains("::cloud"))
            {
                Header("cloud", "OneDrive");
                if (!s.CollapsedSections.Contains("cloud"))
                    rows.Add(new SidebarRow { Id = od, Title = "OneDrive", Glyph = "cloud", Path = od, Loc = Location.Folder(od) });
            }
            Header("locations", "Locations");
            if (!s.CollapsedSections.Contains("locations"))
            {
                rows.Add(new SidebarRow { Id = "::computer", Title = Native.PcName, Glyph = "computer", Loc = Location.Computer });
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady && d.DriveType != DriveType.CDRom) continue;
                        var it = FileItem.Drive(d);
                        string g = d.DriveType == DriveType.Removable ? "usb" : d.DriveType == DriveType.Network ? "network" : "drive";
                        rows.Add(new SidebarRow { Id = d.Name, Title = it.Name, Glyph = g, Path = d.Name, Loc = Location.Folder(d.Name), Eject = d.DriveType == DriveType.Removable });
                    }
                    catch { }
                }
                rows.Add(new SidebarRow { Id = "::trash", Title = "Trash", Glyph = "trash", Loc = Location.Trash });
            }
            var tags = s.Tags.Where(t => t.InSidebar).ToList();
            if (tags.Count > 0)
            {
                Header("tags", "Tags");
                if (!s.CollapsedSections.Contains("tags"))
                    foreach (var t in tags) rows.Add(new SidebarRow { Id = "tag:" + t.Name, Title = t.Name, Tag = true, TagColor = t.Color, Loc = Location.ForTag(t.Name) });
            }
            Layout();
            InvalidateVisual();
        }

        const double RowH = 28, HdrH = 28;
        void Layout()
        {
            double y = 8;
            foreach (var r in rows)
            {
                double h = r.IsHeader ? HdrH : RowH;
                if (r.IsHeader && y > 8) y += 6;
                r.Bounds = new Rect(8, y, Math.Max(0, ActualWidth - 16), h);
                y += h;
            }
            extent = y + 8;
            offY = Math.Max(0, Math.Min(Math.Max(0, extent - ActualHeight), offY));
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo i) { base.OnRenderSizeChanged(i); Layout(); InvalidateVisual(); }

        bool IsCurrent(SidebarRow r)
        {
            if (current == null || r.Loc == null) return false;
            if (r.Loc.Kind != current.Kind) return false;
            if (r.Loc.Kind == LocKind.Folder) return string.Equals(r.Loc.Path.TrimEnd('\\'), current.Path?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            if (r.Loc.Kind == LocKind.Tag) return r.Loc.Tag == current.Tag;
            return true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            dc.PushTransform(new TranslateTransform(0, -offY));
            foreach (var r in rows)
            {
                var b = r.Bounds;
                if (b.Bottom < offY || b.Top > offY + ActualHeight) continue;
                if (r.IsHeader)
                {
                    var ft = Theme.Fmt(r.Title, 11.5, Theme.HeaderText, Theme.Bold, dpi);
                    dc.DrawText(ft, new Point(b.X + 8, b.Y + 8));
                    if (hot == r)
                    {
                        var pen = new Pen(Theme.HeaderText, 1.3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                        double cx = b.Right - 14, cy = b.Y + 14;
                        var g = new StreamGeometry();
                        using (var c = g.Open())
                        {
                            if (r.Collapsed) { c.BeginFigure(new Point(cx - 2, cy - 3.5), false, false); c.LineTo(new Point(cx + 1.5, cy), true, true); c.LineTo(new Point(cx - 2, cy + 3.5), true, true); }
                            else { c.BeginFigure(new Point(cx - 3.5, cy - 1.5), false, false); c.LineTo(new Point(cx, cy + 2), true, true); c.LineTo(new Point(cx + 3.5, cy - 1.5), true, true); }
                        }
                        dc.DrawGeometry(null, pen, g);
                    }
                    continue;
                }
                bool sel = IsCurrent(r);
                if (sel) dc.DrawRoundedRectangle(Theme.SidebarSel, null, new Rect(b.X, b.Y + 1, b.Width, b.Height - 2), 6, 6);
                else if (dropRow == r) dc.DrawRoundedRectangle(Theme.DropHighlight, new Pen(Theme.Accent, 1.5), new Rect(b.X, b.Y + 1, b.Width, b.Height - 2), 6, 6);
                else if (hot == r && false) dc.DrawRoundedRectangle(Theme.HoverBg, null, b, 6, 6);
                double x = b.X + 9;
                if (r.Tag)
                {
                    dc.DrawEllipse(Theme.TagBrush(r.TagColor), null, new Point(x + 8, b.Y + b.Height / 2), 5.5, 5.5);
                }
                else
                {
                    Glyphs.Draw(dc, r.Glyph ?? "folder", new Rect(x, b.Y + (b.Height - 17) / 2, 17, 17), Theme.SidebarGlyph, 1.1);
                }
                var tt = Theme.Fmt(Theme.FitMiddle(r.Title, 13, b.Width - 62 - (r.Eject ? 20 : 0), false, dpi), 13, Theme.Text, Theme.Regular, dpi);
                dc.DrawText(tt, new Point(x + 26, b.Y + (b.Height - tt.Height) / 2));
                if (r.Eject)
                {
                    Glyphs.Draw(dc, "eject", new Rect(b.Right - 22, b.Y + (b.Height - 13) / 2, 13, 13), Theme.TextSecondary, 1.2);
                }
            }
            dc.Pop();
        }

        SidebarRow RowAt(Point p) { double y = p.Y + offY; return rows.FirstOrDefault(r => y >= r.Bounds.Top && y < r.Bounds.Bottom); }

        protected override void OnMouseMove(MouseEventArgs e) { var r = RowAt(e.GetPosition(this)); if (r != hot) { hot = r; InvalidateVisual(); } }
        protected override void OnMouseLeave(MouseEventArgs e) { hot = null; InvalidateVisual(); }
        protected override void OnMouseWheel(MouseWheelEventArgs e) { offY = Math.Max(0, Math.Min(Math.Max(0, extent - ActualHeight), offY - e.Delta * 0.6)); InvalidateVisual(); }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            var p = e.GetPosition(this); var r = RowAt(p);
            if (r == null) return;
            if (r.IsHeader)
            {
                var s = Settings.Current;
                if (!s.CollapsedSections.Remove(r.Id)) s.CollapsedSections.Add(r.Id);
                Settings.Save(false); Rebuild(); return;
            }
            if (r.Eject && p.X > ActualWidth - 34) { EjectRequested?.Invoke(r.Path); return; }
            if (r.Loc != null) Navigate?.Invoke(r.Loc, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            e.Handled = true;
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            var r = RowAt(e.GetPosition(this));
            if (r != null && !r.IsHeader) ContextRequested?.Invoke(r);
        }

        // ---- drops: onto folders, or onto Favorites to add ----
        protected override void OnDragOver(DragEventArgs e)
        {
            var r = RowAt(e.GetPosition(this));
            var old = dropRow;
            dropRow = null; e.Effects = DragDropEffects.None;
            if (r != null && !r.IsHeader)
            {
                if (r.Path != null) { dropRow = r; e.Effects = ItemsSurface.EffectFor(e, r.Path); }
                else if (r.Loc?.Kind == LocKind.Trash && e.Data.GetDataPresent(DataFormats.FileDrop)) { dropRow = r; e.Effects = DragDropEffects.Move; }
                else if (r.Tag && e.Data.GetDataPresent(DataFormats.FileDrop)) { dropRow = r; e.Effects = DragDropEffects.Link; }
            }
            else if (r != null && r.IsHeader && r.Id == "favorites" && e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Link; }
            if (old != dropRow) InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnDragLeave(DragEventArgs e) { dropRow = null; InvalidateVisual(); }

        protected override void OnDrop(DragEventArgs e)
        {
            var r = RowAt(e.GetPosition(this));
            dropRow = null; InvalidateVisual();
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || r == null) return;
            if (r.IsHeader && r.Id == "favorites")
            {
                foreach (var p in paths.Where(Directory.Exists))
                    if (!Settings.Current.Favorites.Contains(p)) Settings.Current.Favorites.Add(p);
                Settings.Save(); return;
            }
            if (r.Tag) { Settings.Current.ToggleTag(paths, r.Title); return; }
            if (r.Loc?.Kind == LocKind.Trash) { _ = FileOps.MoveToTrash(paths); return; }
            if (r.Path != null) DropRequested?.Invoke(r.Path, e.Data, ItemsSurface.EffectFor(e, r.Path));
        }
    }

}

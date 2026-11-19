using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Finderly.Core;
using Finderly.Views;

namespace Finderly.UI
{
    public sealed partial class MainWindow
    {
        // ---------- menu construction helpers ----------
        MenuItem Mi(string header, Action act, string gesture = null, bool enabled = true, bool? check = null)
        {
            var m = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            if (check.HasValue) { m.IsCheckable = false; m.IsChecked = check.Value; }
            if (act != null) m.Click += (s, e) => act();
            return m;
        }

        MenuItem Sub(string header, params object[] items)
        {
            var m = new MenuItem { Header = header };
            foreach (var i in items) m.Items.Add(i);
            return m;
        }

        static readonly Separator Sep_ = null;
        Separator Sep() => new Separator();

        bool HasSel => Selected.Any();
        bool OneSel => Selected.Count() == 1;
        string CurDir => tab?.View.CurrentFolderPath;
        IEnumerable<FileItem> RealSel => Selected.Where(i => i.IsReal);

        // ---------- menu bar ----------
        Menu menuBar;

        void BuildMenus()
        {
            // insert a menu-bar row above the toolbar
            root.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });
            foreach (UIElement c in root.Children) Grid.SetRow(c, Grid.GetRow(c) + 1);
            menuBar = new Menu { Margin = new Thickness(6, 0, 6, 0), Height = 22 };
            var mbHost = new Border { Child = menuBar, Height = 24, Background = Brushes.Transparent };
            mbHost.SetResourceReference(Border.BackgroundProperty, "PanelBg");
            WindowFrame.MakeDragArea(mbHost, this);
            Grid.SetRow(mbHost, 0); root.Children.Add(mbHost);
            // The sidebar spans rows; menu bar lives over the main column only
            RebuildMenuBar();
            foreach (var h in new[] { "File", "Edit", "View", "Go", "Window", "Help" }) { }
            menuBar.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler((s, e) => { if (e.OriginalSource is MenuItem mi && mi.Parent == menuBar) PopulateTop(mi); }));
        }

        void RebuildMenuBar()
        {
            menuBar.Items.Clear();
            var app = new MenuItem { Header = "Finderly", FontWeight = FontWeights.Bold };
            var file = new MenuItem { Header = "File" }; var edit = new MenuItem { Header = "Edit" }; var view = new MenuItem { Header = "View" };
            var go = new MenuItem { Header = "Go" }; var win = new MenuItem { Header = "Window" }; var help = new MenuItem { Header = "Help" };
            foreach (var m in new[] { app, file, edit, view, go, win, help }) { menuBar.Items.Add(m); m.Items.Add(new MenuItem { Header = "…" }); }
            foreach (MenuItem m in menuBar.Items) PopulateTop(m);
        }

        void PopulateTop(MenuItem mi)
        {
            mi.Items.Clear();
            if (tab == null) { mi.Items.Add(new MenuItem { Header = "…" }); return; }
            switch (mi.Header as string)
            {
                case "Finderly":
                    mi.Items.Add(Mi("About Finderly", ShowAbout));
                    mi.Items.Add(Sep());
                    mi.Items.Add(Mi("Settings…", () => SettingsWindow.ShowIt(this), "Ctrl+,"));
                    mi.Items.Add(Sep());
                    mi.Items.Add(Mi("Empty Trash…", () => EmptyTrash(), "Ctrl+Shift+Del"));
                    mi.Items.Add(Sep());
                    mi.Items.Add(Mi("Hide Finderly", () => WindowState = WindowState.Minimized, "Ctrl+H"));
                    mi.Items.Add(Mi("Quit Finderly", () => Application.Current.Shutdown(), "Ctrl+Q"));
                    break;
                case "File": FillFileMenu(mi.Items); break;
                case "Edit": FillEditMenu(mi.Items); break;
                case "View": FillViewMenu(mi.Items); break;
                case "Go": FillGoMenu(mi.Items); break;
                case "Window": FillWindowMenu(mi.Items); break;
                case "Help":
                    mi.Items.Add(Mi("Finderly Keyboard Shortcuts", ShowShortcuts));
                    mi.Items.Add(Mi("About Finderly", ShowAbout));
                    break;
            }
        }

        void FillFileMenu(ItemCollection c)
        {
            bool sel = HasSel; bool one = OneSel; var s0 = Selected.FirstOrDefault();
            c.Add(Mi("New Finderly Window", () => NewWindow(), "Ctrl+N"));
            c.Add(Mi("New Folder", NewFolder, "Ctrl+Shift+N", CurDir != null));
            c.Add(Mi("New Folder with Selection (" + Selected.Count() + " Items)", () => NewFolderWithSelection(), "Ctrl+Ctrl+N", sel && CurDir != null));
            c.Add(Mi("New Tab", () => NewTab(CurrentLocation ?? StartLocation(), true), "Ctrl+T"));
            c.Add(Mi("Open", () => OpenSelection(), "Ctrl+O", sel));
            c.Add(Sub("Open With", BuildOpenWith()));
            c.Items_Add_Enabled(sel);
            c.Add(Mi("Close Window", () => Close(), "Ctrl+Shift+W"));
            c.Add(Mi("Close Tab", () => CloseTab(tab), "Ctrl+W"));
            c.Add(Sep());
            c.Add(Mi("Get Info", () => GetInfo(), "Ctrl+I", sel));
            c.Add(Sep());
            c.Add(Mi("Rename", () => RenameSelection(), "Enter", one));
            c.Add(Mi("Compress", async () => await CompressSelection(), null, sel && CurDir != null));
            c.Add(Mi("Duplicate", async () => await DuplicateSelection(), "Ctrl+D", sel && CurDir != null));
            c.Add(Mi("Make Alias", () => MakeAlias(), "Ctrl+Alt+A", one));
            c.Add(Mi("Quick Look", () => ToggleQuickLook(), "Space", sel));
            c.Add(Mi("Show Original", () => ShowOriginal(), "Ctrl+R", one && s0?.IsLink == true));
            c.Add(Sep());
            c.Add(Mi("Move to Trash", async () => await TrashSelection(), "Delete", sel));
            c.Add(Mi("Delete Immediately…", async () => await DeleteSelection(), "Shift+Delete", sel));
            c.Add(Sep());
            c.Add(Sub("Tags…", BuildTagMenuItems()));
            c.Add(Sep());
            c.Add(Mi("Find", () => FocusSearch(), "Ctrl+F"));
        }

        void FillEditMenu(ItemCollection c)
        {
            bool sel = HasSel;
            var undo = FileOps.UndoStack.Count > 0 ? FileOps.UndoStack.Peek().Label : null;
            var redo = FileOps.RedoStack.Count > 0 ? FileOps.RedoStack.Peek().Label : null;
            c.Add(Mi(undo != null ? "Undo " + undo : "Undo", async () => { await FileOps.DoUndo(); }, "Ctrl+Z", undo != null));
            c.Add(Mi(redo != null ? "Redo " + redo : "Redo", async () => { await FileOps.DoRedo(); }, "Ctrl+Shift+Z", redo != null));
            c.Add(Sep());
            c.Add(Mi("Cut", () => CutSelection(), "Ctrl+X", sel));
            c.Add(Mi("Copy", () => CopySelection(), "Ctrl+C", sel));
            c.Add(Mi("Paste Item", async () => await PasteHere(), "Ctrl+V", CurDir != null && FileOps.GetClipboard().paths.Count > 0));
            c.Add(Mi("Move Item Here", async () => await PasteHere(true), "Ctrl+Alt+V", CurDir != null && FileOps.GetClipboard().paths.Count > 0));
            c.Add(Mi("Select All", () => tab.View.SelectAll(), "Ctrl+A"));
            c.Add(Mi("Invert Selection", () => tab.View.InvertSelection(), "Ctrl+Alt+I"));
            c.Add(Sep());
            c.Add(Mi("Copy as Pathname", () => CopyPath(), "Ctrl+Alt+C", sel));
        }

        void FillViewMenu(ItemCollection c)
        {
            var v = tab.View; var s = Settings.Current;
            c.Add(Mi("as Icons", () => SetMode(ViewMode.Icon), "Ctrl+1", true, v.Mode == ViewMode.Icon));
            c.Add(Mi("as List", () => SetMode(ViewMode.List), "Ctrl+2", true, v.Mode == ViewMode.List));
            c.Add(Mi("as Columns", () => SetMode(ViewMode.Column), "Ctrl+3", true, v.Mode == ViewMode.Column));
            c.Add(Mi("as Gallery", () => SetMode(ViewMode.Gallery), "Ctrl+4", true, v.Mode == ViewMode.Gallery));
            c.Add(Sep());
            c.Add(Sub("Use Groups", BuildGroupItems()));
            c.Add(Sub("Sort By", BuildSortItems()));
            c.Add(Mi("Clean Up", () => { }, null, false));
            c.Add(Sep());
            c.Add(Mi(s.ShowToolbar ? "Hide Toolbar" : "Show Toolbar", () => { s.ShowToolbar = !s.ShowToolbar; Settings.Save(); ApplyChromeVisibility(); }, "Ctrl+Alt+T"));
            c.Add(Mi(s.AlwaysShowTabBar || tabs.Count > 1 ? "Hide Tab Bar" : "Show Tab Bar", () => { s.AlwaysShowTabBar = !(s.AlwaysShowTabBar || tabs.Count > 1); Settings.Save(); ApplyChromeVisibility(); }, "Ctrl+Shift+T"));
            c.Add(Mi(s.ShowPathBar ? "Hide Path Bar" : "Show Path Bar", () => { s.ShowPathBar = !s.ShowPathBar; Settings.Save(); ApplyChromeVisibility(); }, "Ctrl+Alt+P"));
            c.Add(Mi(s.ShowStatusBar ? "Hide Status Bar" : "Show Status Bar", () => { s.ShowStatusBar = !s.ShowStatusBar; Settings.Save(); ApplyChromeVisibility(); }, "Ctrl+/"));
            c.Add(Mi(s.ShowSidebar ? "Hide Sidebar" : "Show Sidebar", () => ToggleSidebar(), "Ctrl+Alt+S"));
            c.Add(Mi(s.ShowPreview ? "Hide Preview" : "Show Preview", () => { s.ShowPreview = !s.ShowPreview; Settings.Save(); ApplyChromeVisibility(); }, "Ctrl+Shift+P"));
            c.Add(Sep());
            c.Add(Mi("Show View Options", () => ViewOptionsWindow.ShowIt(this, tab.View), "Ctrl+J"));
            c.Add(Sep());
            c.Add(Mi(s.ShowHidden ? "Hide Hidden Files" : "Show Hidden Files", () => { s.ShowHidden = !s.ShowHidden; Settings.Save(); tab.View.Reload(); }, "Ctrl+Shift+."));
            c.Add(Mi("Enter Full Screen", () => ToggleFullScreen(), "F11"));
            c.Add(Sep());
            c.Add(Mi("Zoom In", () => Zoom(1), "Ctrl++")); c.Add(Mi("Zoom Out", () => Zoom(-1), "Ctrl+-"));
        }

        void FillGoMenu(ItemCollection c)
        {
            c.Add(Mi("Back", () => GoBack(), "Alt+Left", tab.Back.Count > 0));
            c.Add(Mi("Forward", () => GoForward(), "Alt+Right", tab.Fwd.Count > 0));
            c.Add(Mi("Enclosing Folder", () => GoUp(), "Ctrl+Up", tab.Loc?.Kind == LocKind.Folder));
            c.Add(Sep());
            c.Add(Mi("Recents", () => NavigateTo(Location.Recents), "Ctrl+Shift+F"));
            c.Add(Mi("Documents", () => NavigateTo(Location.Folder(KnownFolders.Documents)), "Ctrl+Shift+O"));
            c.Add(Mi("Desktop", () => NavigateTo(Location.Folder(KnownFolders.Desktop)), "Ctrl+Shift+D"));
            c.Add(Mi("Downloads", () => NavigateTo(Location.Folder(KnownFolders.Downloads)), "Ctrl+Alt+L"));
            c.Add(Mi("Home", () => NavigateTo(Location.Folder(KnownFolders.Home)), "Ctrl+Shift+H"));
            c.Add(Mi("Computer", () => NavigateTo(Location.Computer), "Ctrl+Shift+C"));
            c.Add(Mi("Applications", () => NavigateTo(Location.Applications), "Ctrl+Shift+A"));
            c.Add(Mi("Pictures", () => NavigateTo(Location.Folder(KnownFolders.Pictures))));
            c.Add(Mi("Trash", () => NavigateTo(Location.Trash)));
            c.Add(Sep());
            var rec = Settings.Current.RecentFolders.Where(Directory.Exists).Take(10).ToList();
            var rm = new MenuItem { Header = "Recent Folders", IsEnabled = rec.Count > 0 };
            foreach (var r in rec) { var p = r; rm.Items.Add(Mi(Format.FolderTitle(p), () => NavigateTo(Location.Folder(p)))); }
            c.Add(rm);
            c.Add(Mi("Go to Folder…", () => GoToFolder(), "Ctrl+Shift+G"));
        }

        void FillWindowMenu(ItemCollection c)
        {
            c.Add(Mi("Minimize", () => WindowState = WindowState.Minimized, "Ctrl+M"));
            c.Add(Mi("Zoom", () => WindowFrame.ToggleMax(this)));
            c.Add(Mi("Show Previous Tab", () => NextTab(-1), "Ctrl+Shift+Tab", tabs.Count > 1));
            c.Add(Mi("Show Next Tab", () => NextTab(1), "Ctrl+Tab", tabs.Count > 1));
            c.Add(Mi("Move Tab to New Window", () => MoveTabToNewWindow(), null, tabs.Count > 1));
            c.Add(Mi("Merge All Windows", () => MergeWindows(), null, Windows.Count > 1));
            c.Add(Sep());
            foreach (var w in Windows) { var ww = w; c.Add(Mi(w.Title.Replace(" — Finderly", ""), () => { ww.WindowState = WindowState.Normal; ww.Activate(); }, null, true, w == this)); }
            foreach (var p in PreviewWindow.Open) { var pp = p; c.Add(Mi(p.Title + " (Preview)", () => pp.Activate())); }
        }

        // ---------- popup menus used by toolbar ----------
        ContextMenu BuildGroupMenu() { var m = new ContextMenu(); foreach (var i in BuildGroupItems()) m.Items.Add(i); m.Items.Add(Sep()); foreach (var i in BuildSortItems()) m.Items.Add(i); return m; }

        List<object> BuildGroupItems()
        {
            var v = tab.View; var l = new List<object>();
            foreach (var (g, n) in new[] { (GroupKey.None, "None"), (GroupKey.Name, "Name"), (GroupKey.Kind, "Kind"), (GroupKey.DateLastOpened, "Date Last Opened"), (GroupKey.DateModified, "Date Modified"), (GroupKey.DateCreated, "Date Created"), (GroupKey.Size, "Size"), (GroupKey.Tags, "Tags") })
            { var gg = g; l.Add(Mi(n, () => v.SetGroup(gg), null, v.Mode != ViewMode.Column, v.Group == g)); }
            return l;
        }

        List<object> BuildSortItems()
        {
            var v = tab.View; var l = new List<object>();
            foreach (var (k, n) in new[] { (SortKey.Name, "Name"), (SortKey.Kind, "Kind"), (SortKey.DateLastOpened, "Date Last Opened"), (SortKey.DateModified, "Date Modified"), (SortKey.DateCreated, "Date Created"), (SortKey.Size, "Size"), (SortKey.Tags, "Tags") })
            { var kk = k; l.Add(Mi(n, () => v.SetSort(kk), null, true, v.Sort == k)); }
            return l;
        }

        ContextMenu BuildListColumnsMenu()
        {
            var m = new ContextMenu(); var s = Settings.Current;
            foreach (var id in new[] { "created", "modified", "opened", "size", "kind", "tags", "comments" })
            {
                var d = ListSurface.AllColumns[id]; var i = id;
                m.Items.Add(Mi(d.Title, () => { if (!s.ListColumns.Remove(i)) { s.ListColumns.Add(i); } Settings.Save(); tab.View.Rebuild(false); }, null, true, s.ListColumns.Contains(id)));
            }
            return m;
        }

        ContextMenu BuildTagMenu() { var m = new ContextMenu(); foreach (var i in BuildTagMenuItems()) m.Items.Add(i); return m; }

        List<object> BuildTagMenuItems()
        {
            var l = new List<object>(); var sel = RealSel.Select(i => i.Path).ToList();
            foreach (var t in Settings.Current.Tags)
            {
                var tt = t;
                bool all = sel.Count > 0 && sel.All(p => Settings.Current.TagsOf(p)?.Contains(tt.Name) == true);
                var mi = Mi(t.Name, () => Settings.Current.ToggleTag(sel, tt.Name), null, sel.Count > 0, all);
                l.Add(mi);
            }
            l.Add(Sep());
            l.Add(Mi("Clear Tags", () => { foreach (var p in sel) Settings.Current.FileTags.Remove(p); Settings.Save(); tab.View.Rebuild(false); }, null, sel.Count > 0));
            return l;
        }

        ContextMenu BuildActionMenu()
        {
            var m = new ContextMenu();
            bool sel = HasSel;
            m.Items.Add(Mi("Get Info", () => GetInfo(), "Ctrl+I", sel));
            m.Items.Add(Mi("Rename", () => RenameSelection(), null, OneSel));
            m.Items.Add(Mi("Compress", async () => await CompressSelection(), null, sel && CurDir != null));
            m.Items.Add(Mi("Duplicate", async () => await DuplicateSelection(), null, sel && CurDir != null));
            m.Items.Add(Mi("Make Alias", MakeAlias, null, OneSel));
            m.Items.Add(Mi("Quick Look", ToggleQuickLook, null, sel));
            m.Items.Add(Sep());
            m.Items.Add(Mi("Copy", CopySelection, null, sel));
            m.Items.Add(Mi("Share", ShareSelected, null, sel));
            m.Items.Add(Sep());
            foreach (var i in BuildSortItems().Take(0)) m.Items.Add(i);
            m.Items.Add(Mi("Show View Options", () => ViewOptionsWindow.ShowIt(this, tab.View), "Ctrl+J"));
            return m;
        }

        List<object> BuildOpenWith()
        {
            var l = new List<object>();
            var it = Selected.FirstOrDefault();
            if (it == null) return l;
            var def = it.IsDir ? "File Explorer" : Native.DefaultAppName(it.Ext);
            l.Add(Mi((def ?? "Default Application") + " (default)", () => OpenSelection(), null, true));
            if (Format.IsPreviewable(it)) l.Add(Mi("Preview", () => OpenInPreview(Selected.ToList())));
            l.Add(Sep());
            l.Add(Mi("Choose Application…", () => { foreach (var s in RealSel) Native.ShellVerb(s.Path, "openas"); }));
            return l;
        }

        // ---------- context menus ----------
        void ShowContext(FileItem hit)
        {
            var m = new ContextMenu();
            var sel = Selected.ToList();
            bool trash = tab.Loc?.Kind == LocKind.Trash;
            if (hit == null)
            {
                if (trash)
                {
                    m.Items.Add(Mi("Empty Trash…", () => EmptyTrash(), null, true));
                }
                else
                {
                    m.Items.Add(Mi("New Folder", NewFolder, null, CurDir != null));
                    m.Items.Add(Sub("New File", BuildNewFileItems()));
                    ((MenuItem)m.Items[m.Items.Count - 1]).IsEnabled = CurDir != null;
                    m.Items.Add(Sep());
                    m.Items.Add(Mi("Get Info", () => GetFolderInfo(), null, tab.Loc?.Kind == LocKind.Folder));
                    m.Items.Add(Sep());
                    m.Items.Add(Mi("Paste Item", async () => await PasteHere(), "Ctrl+V", CurDir != null && FileOps.GetClipboard().paths.Count > 0));
                    m.Items.Add(Sep());
                    m.Items.Add(Sub("Sort By", BuildSortItems().ToArray()));
                    m.Items.Add(Sub("Use Groups", BuildGroupItems().ToArray()));
                    m.Items.Add(Mi("Show View Options", () => ViewOptionsWindow.ShowIt(this, tab.View), "Ctrl+J"));
                    m.Items.Add(Sep());
                    m.Items.Add(Mi("Open in Terminal", () => OpenTerminal(), null, CurDir != null));
                }
            }
            else if (trash)
            {
                m.Items.Add(Mi("Put Back", async () => { await FileSystem.PutBack(sel.Select(i => i.Path)); tab.View.Reload(); }));
                m.Items.Add(Mi("Delete Immediately…", async () => await DeleteSelection()));
                m.Items.Add(Sep());
                m.Items.Add(Mi("Get Info", () => GetInfo()));
                m.Items.Add(Mi("Quick Look", ToggleQuickLook, "Space"));
            }
            else
            {
                string openLabel = sel.Count == 1 && sel[0].IsDir ? "Open" : "Open";
                m.Items.Add(Mi(openLabel, () => OpenSelection()));
                m.Items.Add(Sub("Open With", BuildOpenWith().ToArray()));
                if (sel.Count == 1 && sel[0].IsDir) m.Items.Add(Mi("Open in New Tab", () => NewTab(Location.Folder(sel[0].Path), true)));
                m.Items.Add(Mi("Show in Windows Explorer", () => ShowInExplorer()));
                m.Items.Add(Sep());
                m.Items.Add(Mi("Move to Trash", async () => await TrashSelection(), "Delete"));
                m.Items.Add(Sep());
                m.Items.Add(Mi("Get Info", () => GetInfo(), "Ctrl+I"));
                if (sel.Count == 1) m.Items.Add(Mi("Rename", () => RenameSelection()));
                if (sel.Count == 1 && sel[0].IsDir) m.Items.Add(Mi("Add to Sidebar", () => { if (!Settings.Current.Favorites.Contains(sel[0].Path)) Settings.Current.Favorites.Add(sel[0].Path); Settings.Save(); }));
                m.Items.Add(Mi("Compress " + (sel.Count == 1 ? "“" + sel[0].DisplayName + "”" : sel.Count + " Items"), async () => await CompressSelection(), null, CurDir != null));
                if (sel.Count == 1 && FileOps.IsArchive(sel[0])) m.Items.Add(Mi("Extract Here", async () => { await FileOps.Extract(sel[0].Path); }));
                m.Items.Add(Mi("Duplicate", async () => await DuplicateSelection(), "Ctrl+D", CurDir != null));
                m.Items.Add(Mi("Make Alias", MakeAlias, null, sel.Count == 1));
                m.Items.Add(Mi("Quick Look", ToggleQuickLook, "Space"));
                m.Items.Add(Sep());
                m.Items.Add(Mi("Copy", CopySelection, "Ctrl+C"));
                m.Items.Add(Mi("Cut", CutSelection, "Ctrl+X"));
                m.Items.Add(Mi("Copy as Pathname", CopyPath));
                m.Items.Add(Sep());
                // tag row
                m.Items.Add(TagRow(sel));
                m.Items.Add(Sub("Tags…", BuildTagMenuItems().ToArray()));
                m.Items.Add(Sep());
                m.Items.Add(Mi("Share…", ShareSelected));
            }
            m.Placement = PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        UIElement TagRow(List<FileItem> sel)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 4) };
            var paths = sel.Select(i => i.Path).ToList();
            foreach (var t in Settings.Current.Tags.Take(7))
            {
                var tt = t;
                bool all = paths.All(p => Settings.Current.TagsOf(p)?.Contains(tt.Name) == true);
                var dot = new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(8), Background = Theme.TagBrush(t.Color), Margin = new Thickness(0, 0, 6, 0), BorderThickness = new Thickness(all ? 2 : 0), BorderBrush = Theme.Text, ToolTip = t.Name, Cursor = Cursors.Hand };
                dot.MouseLeftButtonUp += (s, e) => { Settings.Current.ToggleTag(paths, tt.Name); tab.View.Rebuild(false); if (dot.Parent != null) { var cm = FindContextMenu(dot); if (cm != null) cm.IsOpen = false; } };
                sp.Children.Add(dot);
            }
            return new MenuItem { Header = sp, Template = null, StaysOpenOnClick = true, Focusable = false, IsHitTestVisible = true, Style = TagRowStyle() };
        }

        static ContextMenu FindContextMenu(DependencyObject d)
        {
            while (d != null) { if (d is ContextMenu c) return c; d = LogicalTreeHelper.GetParent(d) ?? VisualTreeHelper.GetParent(d); }
            return null;
        }

        static Style TagRowStyle()
        {
            var st = new Style(typeof(MenuItem));
            var tpl = new ControlTemplate(typeof(MenuItem));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter)); cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            tpl.VisualTree = cp;
            st.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return st;
        }

        List<object> BuildNewFileItems()
        {
            var l = new List<object>();
            foreach (var (n, ext) in new[] { ("Text Document", ".txt"), ("Markdown Document", ".md"), ("HTML File", ".html"), ("JSON File", ".json") })
            {
                var e = ext; var name = n;
                l.Add(Mi(n, () => { var p = FileOps.NewFile(CurDir, "untitled", e); tab.View.SelectAfterReload(p); AfterCreate(p); }));
            }
            return l;
        }

        void SidebarContext(SidebarRow r)
        {
            var m = new ContextMenu();
            if (r.Loc != null) m.Items.Add(Mi("Open in New Tab", () => NewTab(r.Loc, true)));
            if (r.Loc?.Kind == LocKind.Trash) m.Items.Add(Mi("Empty Trash…", () => EmptyTrash()));
            if (r.Path != null) m.Items.Add(Mi("Show in Windows Explorer", () => Process.Start("explorer.exe", "\"" + r.Path + "\"")));
            if (r.Eject) m.Items.Add(Mi("Eject", () => Eject(r.Path)));
            if (Settings.Current.Favorites.Contains(r.Id))
                m.Items.Add(Mi("Remove from Sidebar", () => { Settings.Current.Favorites.Remove(r.Id); Settings.Save(); }));
            if (r.Tag) m.Items.Add(Mi("Remove from Sidebar", () => { var t = Settings.Current.TagDefOf(r.Title); if (t != null) t.InSidebar = false; Settings.Save(); }));
            if (m.Items.Count > 0) { m.Placement = PlacementMode.MousePoint; m.IsOpen = true; }
        }

        // ---------- open ----------
        void OpenItem(FileItem it, bool ctrl = false)
        {
            if (it == null) return;
            if (it.Special == SpecialKind.TrashItem) { GetInfo(); return; }
            if (it.IsLink && !it.IsDir)
            {
                var t = Native.ResolveLink(it.Path);
                if (t != null && Directory.Exists(t)) { NavigateTo(Location.Folder(t)); return; }
            }
            if (it.IsDir)
            {
                if (ctrl || (Settings.Current.OpenFoldersInTabs && false)) NewTab(Location.Folder(it.Path), true);
                else NavigateTo(Location.Folder(it.Path));
                return;
            }
            if (Settings.Current.OpenMediaInPreview && Format.IsPreviewable(it))
            {
                OpenInPreview(new List<FileItem> { it });
                return;
            }
            try { Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(it.Path) }); Native.SHAddToRecentDocs(3, it.Path); }
            catch (Exception ex) { MacAlert.Show(this, "The file “" + it.Name + "” couldn’t be opened.", ex.Message, new[] { "OK" }); }
        }

        void OpenSelection()
        {
            var sel = Selected.ToList();
            if (sel.Count == 0) return;
            if (sel.Count > 1 && sel.All(Format.IsPreviewable) && Settings.Current.OpenMediaInPreview) { OpenInPreview(sel); return; }
            if (sel.Count == 1 && sel[0].IsDir) { OpenItem(sel[0]); return; }
            foreach (var s in sel.Where(i => !i.IsDir)) OpenItem(s);
        }

        public void OpenInPreview(List<FileItem> items)
        {
            var files = items.Where(i => Format.IsPreviewable(i)).Select(i => i.Path).ToList();
            if (files.Count == 0) return;
            // when opening one image, include sibling images so arrow keys browse the folder
            if (files.Count == 1 && !files[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(files[0]);
                var sib = tab.View.Items.Where(i => Format.IsImage(i) && string.Equals(Path.GetDirectoryName(i.Path), dir, StringComparison.OrdinalIgnoreCase)).Select(i => i.Path).ToList();
                if (sib.Count > 1) { int ix = sib.FindIndex(p => p.Equals(files[0], StringComparison.OrdinalIgnoreCase)); new PreviewWindow(sib, Math.Max(0, ix)).Show(); return; }
            }
            new PreviewWindow(files).Show();
            foreach (var f in files) Native.SHAddToRecentDocs(3, f);
        }

        void ShowInExplorer()
        {
            foreach (var s in RealSel) try { Process.Start("explorer.exe", "/select,\"" + s.Path + "\""); } catch { }
        }

        void OpenTerminal()
        {
            try { Process.Start(new ProcessStartInfo("wt.exe", "-d \"" + CurDir + "\"") { UseShellExecute = true }); }
            catch { try { Process.Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = CurDir, UseShellExecute = true }); } catch { } }
        }

        void ShowOriginal()
        {
            var s = Selected.FirstOrDefault(); if (s == null) return;
            var t = Native.ResolveLink(s.Path); if (t == null) return;
            var dir = Directory.Exists(t) ? t : Path.GetDirectoryName(t);
            NavigateTo(Location.Folder(dir));
            void Sel() { tab.View.Loaded2 -= Sel; tab.View.SelectPaths(new[] { t }); }
            tab.View.Loaded2 += Sel;
        }

        void ShareSelected() { var s = RealSel.FirstOrDefault(); if (s != null) Native.ShellVerb(s.Path, "properties"); }

        void Eject(string drive)
        {
            try
            {
                var ps = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"$d=(New-Object -comObject Shell.Application).Namespace(17).ParseName('" + drive.TrimEnd('\\') + "'); $d.InvokeVerb('Eject')\"") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(ps);
            }
            catch { }
        }

        // ---------- selection operations ----------
        void GetInfo()
        {
            foreach (var it in Selected.Take(8)) InfoWindow.Show_(it, this);
        }

        void GetFolderInfo()
        {
            var p = CurDir; if (p == null) return;
            var it = FileItem.FromPath(p); if (it != null) InfoWindow.Show_(it, this);
        }

        void RenameSelection() { var s = Selected.FirstOrDefault(); if (s != null && OneSel) tab.View.BeginRename(s); }

        void CopySelection() { var p = RealSel.Select(i => i.Path).ToList(); if (p.Count == 0) return; FileOps.SetClipboard(p, false); SetCut(null); }
        void CutSelection() { var p = RealSel.Select(i => i.Path).ToList(); if (p.Count == 0) return; FileOps.SetClipboard(p, true); SetCut(new HashSet<string>(p, StringComparer.OrdinalIgnoreCase)); }
        void SetCut(HashSet<string> s) { cutPaths = s; foreach (var t in tabs) t.View.SetCut(s); }

        async Task PasteHere(bool forceMove = false)
        {
            var dir = CurDir; if (dir == null) return;
            var (paths, cut) = FileOps.GetClipboard();
            if (paths.Count == 0) return;
            var before = tab.View.Items.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            await FileOps.Paste(dir, paths, cut || forceMove);
            if (cut) { FileOps.SetClipboard(new List<string>(), false); SetCut(null); }
        }

        void CopyPath()
        {
            var p = string.Join("\n", Selected.Select(i => i.Path));
            if (p.Length > 0) try { Clipboard.SetText(p); } catch { }
        }

        void NewFolder()
        {
            var dir = CurDir; if (dir == null) return;
            var p = FileOps.NewFolder(dir);
            AfterCreate(p);
        }

        void NewFolderWithSelection()
        {
            var dir = CurDir; if (dir == null) return;
            var p = FileOps.NewFolder(dir, RealSel.Select(i => i.Path).ToList());
            AfterCreate(p);
        }

        void AfterCreate(string path)
        {
            tab.View.Reload();
            void Sel()
            {
                tab.View.Loaded2 -= Sel;
                tab.View.SelectPaths(new[] { path });
                var it = tab.View.Selected.FirstOrDefault();
                if (it != null) Dispatcher.BeginInvoke(new Action(() => tab.View.BeginRename(it)), System.Windows.Threading.DispatcherPriority.Background);
            }
            tab.View.Loaded2 += Sel;
        }

        async Task DuplicateSelection()
        {
            var paths = RealSel.Select(i => i.Path).ToList();
            await FileOps.Duplicate(paths);
            tab.View.Reload();
            var created = FileOps.LastCreated;
            void Sel() { tab.View.Loaded2 -= Sel; if (created != null) tab.View.SelectPaths(created); }
            tab.View.Loaded2 += Sel;
        }

        void MakeAlias() { var s = RealSel.FirstOrDefault(); if (s == null) return; var p = FileOps.MakeAlias(s.Path); tab.View.Reload(); void Sel() { tab.View.Loaded2 -= Sel; tab.View.SelectPaths(new[] { p }); } tab.View.Loaded2 += Sel; }

        async Task CompressSelection()
        {
            var list = RealSel.Select(i => i.Path).ToList();
            var zip = await FileOps.Compress(list);
            tab.View.Reload();
            if (zip != null) { void Sel() { tab.View.Loaded2 -= Sel; tab.View.SelectPaths(new[] { zip }); } tab.View.Loaded2 += Sel; }
        }

        async Task TrashSelection()
        {
            var sel = Selected.ToList();
            if (tab.Loc?.Kind == LocKind.Trash) { await DeleteSelection(); return; }
            var paths = sel.Where(i => i.IsReal && i.Special != SpecialKind.Drive).Select(i => i.Path).ToList();
            if (paths.Count == 0) return;
            await FileOps.MoveToTrash(paths);
            tab.View.Reload();
            Dispatcher.BeginInvoke(new Action(() => tab.View.FocusSurface()), System.Windows.Threading.DispatcherPriority.Background);
        }

        async Task DeleteSelection()
        {
            var sel = Selected.ToList(); if (sel.Count == 0) return;
            int r = 0;
            if (Settings.Current.WarnBeforeDelete)
                r = MacAlert.Show(this, sel.Count == 1 ? "Are you sure you want to permanently erase the item “" + sel[0].DisplayName + "”?" : "Are you sure you want to permanently erase the " + sel.Count + " items?", "You can’t undo this action.", new[] { "Delete", "Cancel" }, 1, cancelIndex: 1);
            if (r != 0) return;
            if (tab.Loc?.Kind == LocKind.Trash) await Task.Run(() => { foreach (var s in sel) FileSystem.DeleteTrashItemPermanently(s.Path); });
            else await FileOps.DeleteImmediately(sel.Select(i => i.Path));
            tab.View.Reload();
        }

        void EmptyTrash()
        {
            if (Native.RecycleBinCount() == 0) return;
            int r = 0;
            if (Settings.Current.WarnEmptyTrash)
                r = MacAlert.Show(this, "Are you sure you want to permanently erase the items in the Trash?", "You can’t undo this action.", new[] { "Empty Trash", "Cancel" }, 1, cancelIndex: 1);
            if (r != 0) return;
            Native.SHEmptyRecycleBin(IntPtr.Zero, null, 0x7);
            FileOps.UndoStack.Clear();
            if (tab.Loc?.Kind == LocKind.Trash) tab.View.Reload();
        }

        async void HandleDrop(string target, IDataObject data, DragDropEffects eff)
        {
            var paths = data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null) return;
            await FileOps.Transfer(target, paths, eff);
            foreach (var t in tabs) t.View.Reload();
        }

        // ---------- view ops ----------
        void SetMode(ViewMode m) { tab.View.SetMode(m); UpdateToolbarState(); UpdateStatus(); }

        void Zoom(int dir)
        {
            var s = Settings.Current;
            switch (tab.View.Mode)
            {
                case ViewMode.Icon: s.IconSize = Math.Max(16, Math.Min(128, s.IconSize + dir * 8)); zoomSlider.Value = s.IconSize; break;
                case ViewMode.List: s.ListLargeIcons = dir > 0; s.ListTextSize = dir > 0 ? 14 : 13; break;
                case ViewMode.Column: s.ColumnTextSize = Math.Max(11, Math.Min(18, s.ColumnTextSize + dir)); break;
                case ViewMode.Gallery: s.GalleryThumbSize = Math.Max(32, Math.Min(120, s.GalleryThumbSize + dir * 8)); break;
            }
            Settings.Save();
        }

        bool zoomGuard;
        void OnZoomSlider()
        {
            if (tab == null || tab.View.Mode != ViewMode.Icon) return;
            if (Math.Abs(Settings.Current.IconSize - zoomSlider.Value) < 0.5) return;
            Settings.Current.IconSize = Math.Round(zoomSlider.Value / 2) * 2;
            Settings.Save();
        }

        void ToggleFullScreen()
        {
            if (!fullScreen) { preFull = WindowState; WindowState = WindowState.Normal; WindowState = WindowState.Maximized; fullScreen = true; Topmost = false; }
            else { WindowState = preFull; fullScreen = false; }
        }

        void GoToFolder()
        {
            var s = MacAlert.Prompt(this, "Go to the folder:", "Enter a path, e.g. C:\\Users or ~/Documents", CurDir ?? KnownFolders.Home, "Go");
            if (string.IsNullOrWhiteSpace(s)) return;
            s = Environment.ExpandEnvironmentVariables(s.Trim().Trim('"').Replace("~", KnownFolders.Home).Replace('/', '\\'));
            if (Directory.Exists(s)) NavigateTo(Location.Folder(s));
            else MacAlert.Show(this, "The folder can’t be found.", "Check the path and try again.", new[] { "OK" });
        }

        void NewWindow(Location loc = null) { var w = new MainWindow(loc ?? StartLocation()); w.Show(); }

        void MoveTabToNewWindow()
        {
            if (tabs.Count < 2) return;
            var loc = tab.Loc; CloseTab(tab); NewWindow(loc);
        }

        void MergeWindows()
        {
            foreach (var w in Windows.Where(x => x != this).ToList()) { foreach (var t in w.tabs.ToList()) NewTab(t.Loc, false); w.Close(); }
        }

        // ---------- Quick Look ----------
        void ToggleQuickLook()
        {
            if (QuickLookWindow.Instance != null) { QuickLookWindow.Instance.Close(); return; }
            var sel = Selected.Where(i => i.Special != SpecialKind.Drive || true).ToList();
            if (sel.Count == 0) return;
            IList<FileItem> list = sel.Count > 1 ? sel : tab.View.Items.Where(i => !i.IsHidden || Settings.Current.ShowHidden).ToList();
            int idx = sel.Count > 1 ? 0 : Math.Max(0, list.ToList().FindIndex(i => i.Equals(sel[0])));
            QuickLookWindow.Toggle(list, idx, this, it => tab.View.Surface?.ItemScreenRect(it) ?? Rect.Empty);
            if (QuickLookWindow.Instance != null)
            {
                QuickLookWindow.Instance.OpenWithPreview = it => OpenInPreview(new List<FileItem> { it });
                QuickLookWindow.Instance.Navigated += it => { if (sel.Count <= 1) tab.View.SelectItem(it); };
                QuickLookWindow.Instance.Closed2 += () => FocusContent();
            }
        }

        void QuickLookSync()
        {
            var q = QuickLookWindow.Instance; if (q == null) return;
            var sel = Selected.ToList();
            if (sel.Count == 1 && q.Current != null && !sel[0].Equals(q.Current))
            {
                var all = tab.View.Items.Where(i => !i.IsHidden || Settings.Current.ShowHidden).ToList();
                q.SetItems(all, all.FindIndex(i => i.Equals(sel[0])));
            }
        }

        // ---------- preview pane ----------
        void UpdatePreviewPane()
        {
            if (!Settings.Current.ShowPreview || tab == null) return;
            var sel = Selected.ToList();
            var sp = new StackPanel { Margin = new Thickness(14) };
            if (sel.Count == 0)
            {
                var t = MacChrome.Label(tab.Loc?.Title ?? "", 14, true); t.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(t);
                var n = MacChrome.Label(tab.View.ItemTotal + " items", 12, false, "TextSecondary"); n.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(n);
                previewPane.Child = sp; return;
            }
            if (sel.Count > 1)
            {
                var t = MacChrome.Label(sel.Count + " items selected", 14, true); t.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(t);
                long total = sel.Where(i => !i.IsDir).Sum(i => i.Size);
                var n = MacChrome.Label(Format.Size(total), 12, false, "TextSecondary"); n.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(n);
                previewPane.Child = sp; return;
            }
            var it = sel[0]; previewItem = it;
            var img = new Image { Source = IconPainter.Snapshot(it, 160, () => { if (previewItem == it) UpdatePreviewPane(); }), Width = 160, Height = 160, Margin = new Thickness(0, 6, 0, 8) };
            var t1 = MacChrome.Label(it.DisplayName, 14, true); t1.TextWrapping = TextWrapping.Wrap; t1.TextAlignment = TextAlignment.Center; t1.TextTrimming = TextTrimming.None;
            var t2 = MacChrome.Label(it.Kind + (it.IsDir ? "" : " — " + Format.Size(it.Size)), 12, false, "TextSecondary"); t2.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(img); sp.Children.Add(t1); sp.Children.Add(t2);
            var hair = MacChrome.Hairline(); hair.Margin = new Thickness(0, 12, 0, 8); sp.Children.Add(hair);
            void Row(string k, string v)
            {
                var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
                var a = MacChrome.Label(k, 11.5, false, "TextSecondary"); a.TextAlignment = TextAlignment.Right; a.Margin = new Thickness(0, 0, 8, 0);
                var b = MacChrome.Label(v, 11.5); b.TextWrapping = TextWrapping.Wrap; b.TextTrimming = TextTrimming.None;
                Grid.SetColumn(b, 1); g.Children.Add(a); g.Children.Add(b); sp.Children.Add(g);
            }
            Row("Created", Format.FullDate(it.Created)); Row("Modified", Format.FullDate(it.Modified));
            if (Format.IsImage(it)) { var (w, h) = Thumbs.ImageSize(it.Path); if (w > 0) Row("Dimensions", w + " × " + h); }
            previewPane.Child = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ---------- about / help ----------
        void ShowAbout()
        {
            MacAlert.Show(this, "Finderly", "Version 1.0\nA Finder-style file manager and Preview-style viewer for Windows.\n\nNot affiliated with Apple Inc.", new[] { "OK" });
        }

        void ShowShortcuts()
        {
            MacAlert.Show(this, "Keyboard Shortcuts", "Space  Quick Look\nEnter  Rename\nCtrl+O  Open\nCtrl+Up  Enclosing folder\nCtrl+1…4  Icons, List, Columns, Gallery\nCtrl+Shift+N  New folder\nCtrl+I  Get Info\nCtrl+D  Duplicate\nDelete  Move to Trash\nCtrl+Shift+G  Go to folder\nCtrl+T  New tab\nCtrl+F  Search\nCtrl+J  View options\nCtrl+Shift+.  Show hidden files", new[] { "OK" });
        }
    }

    static class MenuExt
    {
        public static void Items_Add_Enabled(this ItemCollection c, bool enabled)
        {
            if (c.Count > 0 && c[c.Count - 1] is MenuItem m) m.IsEnabled = enabled;
        }
    }
}

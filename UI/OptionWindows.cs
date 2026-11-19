using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Finderly.Core;
using Finderly.Views;

namespace Finderly.UI
{
    /// <summary>Finder Settings: General, Sidebar, Advanced tabs.</summary>
    public sealed class SettingsWindow : Window
    {
        static SettingsWindow inst;
        public static void ShowIt(Window owner)
        {
            if (inst != null) { inst.Activate(); return; }
            inst = new SettingsWindow(); inst.Left = owner.Left + 80; inst.Top = owner.Top + 70; inst.Show();
        }

        readonly ContentControl page = new ContentControl();
        readonly TextBlock title;
        readonly List<ToggleButton_> tabsBtn = new List<ToggleButton_>();

        sealed class ToggleButton_ { public Border B; public string Name; }

        public SettingsWindow()
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Width = 460; SizeToContent = SizeToContent.Height;
            MacChrome.Setup(this); WindowFrame.Attach(this, false, false);
            Title = "General";
            var root = new StackPanel();
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 10) };
            foreach (var (n, g) in new[] { ("General", "adjust"), ("Sidebar", "sidebar"), ("Advanced", "more") })
            {
                var name = n;
                var sp = new StackPanel { Margin = new Thickness(14, 4, 14, 4) };
                var ic = Glyphs.Make(g, 22, "ToolGlyph", 1.1); ic.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(ic);
                var l = MacChrome.Label(n, 11.5); l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 2, 0, 0); sp.Children.Add(l);
                var b = new Border { Child = sp, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Arrow };
                b.MouseLeftButtonDown += (s, e) => Select(name);
                tabsBtn.Add(new ToggleButton_ { B = b, Name = n });
                tabs.Children.Add(b);
            }
            root.Children.Add(tabs);
            root.Children.Add(page);
            Content = MacChrome.Build(this, "General", root, out title);
            Select("General");
            Closed += (s, e) => { inst = null; Settings.Save(); };
            KeyDown += (s, e) => { if (e.Key == Key.Escape || (e.Key == Key.W && (Keyboard.Modifiers & ModifierKeys.Control) != 0)) Close(); };
        }

        void Select(string name)
        {
            foreach (var t in tabsBtn) t.B.Background = t.Name == name ? Theme.PressBg : Brushes.Transparent;
            title.Text = name; Title = name;
            var s = Settings.Current;
            var sp = new StackPanel { Margin = new Thickness(26, 4, 26, 24) };
            CheckBox Chk(string text, bool v, Action<bool> set)
            {
                var c = new CheckBox { Content = text, IsChecked = v, Margin = new Thickness(0, 4, 0, 4) };
                c.Click += (a, b) => { set(c.IsChecked == true); Settings.Save(); };
                return c;
            }
            Grid Row(string label, UIElement el)
            {
                var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); g.ColumnDefinitions.Add(new ColumnDefinition());
                var l = MacChrome.Label(label, 13); l.TextAlignment = TextAlignment.Right; l.Margin = new Thickness(0, 3, 10, 0); l.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(el, 1); g.Children.Add(l); g.Children.Add(el); return g;
            }
            if (name == "General")
            {
                var opts = new[] { "Home", "Recents", "Desktop", "Documents", "Downloads", "Computer" };
                sp.Children.Add(Row("New Finderly windows show:", MacChrome.PopupButton(opts, Math.Max(0, Array.IndexOf(opts, s.NewWindowTarget)), i => { s.NewWindowTarget = opts[i]; Settings.Save(); }, 150)));
                sp.Children.Add(Row("Open folders in tabs instead of new windows", Chk("", s.OpenFoldersInTabs, v => s.OpenFoldersInTabs = v)));
                sp.Children.Add(Row("Open images and PDFs in:", Chk("Finderly Preview", s.OpenMediaInPreview, v => s.OpenMediaInPreview = v)));
                var tagBox = new StackPanel();
                foreach (var t in s.Tags)
                {
                    var tt = t;
                    var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                    r.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Theme.TagBrush(t.Color), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                    var c = new CheckBox { Content = t.Name, IsChecked = t.InSidebar, Margin = new Thickness(0) };
                    c.Click += (a, b) => { tt.InSidebar = c.IsChecked == true; Settings.Save(); };
                    r.Children.Add(c); tagBox.Children.Add(r);
                }
                sp.Children.Add(Row("Tags in sidebar:", tagBox));
            }
            else if (name == "Sidebar")
            {
                var l = MacChrome.Label("Show these items in the sidebar:", 12.5); l.Margin = new Thickness(0, 0, 0, 6); sp.Children.Add(l);
                foreach (var f in new[] { "::recents", "::applications", KnownFolders.Desktop, KnownFolders.Documents, KnownFolders.Downloads, KnownFolders.Pictures, KnownFolders.Music, KnownFolders.Videos, KnownFolders.Home })
                {
                    var id = f;
                    string t = id == "::recents" ? "Recents" : id == "::applications" ? "Applications" : Format.FolderTitle(id);
                    if (id == KnownFolders.Home) t = System.IO.Path.GetFileName(KnownFolders.Home) + " (Home)";
                    sp.Children.Add(Chk(t, s.Favorites.Contains(id), v => { if (v) { if (!s.Favorites.Contains(id)) s.Favorites.Add(id); } else s.Favorites.Remove(id); }));
                }
                var hdr = MacChrome.Label("Locations", 12.5, true); hdr.Margin = new Thickness(0, 10, 0, 4); sp.Children.Add(hdr);
                sp.Children.Add(Chk("OneDrive", !s.HiddenSidebarItems.Contains("::cloud"), v => { if (v) s.HiddenSidebarItems.Remove("::cloud"); else s.HiddenSidebarItems.Add("::cloud"); }));
            }
            else
            {
                sp.Children.Add(Chk("Show all filename extensions", s.ShowAllExtensions, v => s.ShowAllExtensions = v));
                sp.Children.Add(Chk("Show warning before changing an extension", s.WarnExtensionChange, v => s.WarnExtensionChange = v));
                sp.Children.Add(Chk("Show warning before deleting immediately", s.WarnBeforeDelete, v => s.WarnBeforeDelete = v));
                sp.Children.Add(Chk("Show warning before emptying the Trash", s.WarnEmptyTrash, v => s.WarnEmptyTrash = v));
                sp.Children.Add(Chk("Show hidden files", s.ShowHidden, v => s.ShowHidden = v));
                var hdr = MacChrome.Label("Keep folders on top:", 12.5, true); hdr.Margin = new Thickness(0, 10, 0, 4); sp.Children.Add(hdr);
                sp.Children.Add(Chk("In windows when sorting by name", s.FoldersOnTop, v => s.FoldersOnTop = v));
                var h2 = MacChrome.Label("When performing a search:", 12.5, true); h2.Margin = new Thickness(0, 10, 0, 4); sp.Children.Add(h2);
                var opts = new[] { "Search the Current Folder", "Search This PC" };
                sp.Children.Add(MacChrome.PopupButton(opts, s.SearchCurrentFolder ? 0 : 1, i => { s.SearchCurrentFolder = i == 0; Settings.Save(); }, 200));
                var rst = new Button { Content = "Reset Folder View Settings", Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                rst.Click += (a, b) => { s.Folders.Clear(); Settings.Save(); };
                sp.Children.Add(rst);
            }
            page.Content = sp;
        }
    }

    /// <summary>"Show View Options" panel (⌘J): per-view display settings.</summary>
    public sealed class ViewOptionsWindow : Window
    {
        static ViewOptionsWindow inst;
        FolderView view;

        public static void ShowIt(Window owner, FolderView v)
        {
            if (inst != null) { inst.view = v; inst.Build(); inst.Activate(); return; }
            inst = new ViewOptionsWindow { view = v };
            inst.Left = owner.Left + owner.ActualWidth - 340; inst.Top = owner.Top + 90;
            inst.Build(); inst.Show();
        }

        readonly ContentControl host = new ContentControl();
        TextBlock titleBlock;

        ViewOptionsWindow()
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Width = 270; SizeToContent = SizeToContent.Height; Topmost = true; ShowInTaskbar = false;
            MacChrome.Setup(this); WindowFrame.Attach(this, false, false);
            Content = MacChrome.Build(this, "", host, out titleBlock);
            Closed += (s, e) => { inst = null; Settings.Save(); };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        void Build()
        {
            var s = Settings.Current;
            var mode = view.Mode;
            titleBlock.Text = (view.Loc?.Title ?? "") + " — " + (mode == ViewMode.Icon ? "Icon View" : mode == ViewMode.List ? "List View" : mode == ViewMode.Column ? "Column View" : "Gallery View");
            var sp = new StackPanel { Margin = new Thickness(16, 6, 16, 16) };
            CheckBox Chk(string t, bool v, Action<bool> set) { var c = new CheckBox { Content = t, IsChecked = v }; c.Click += (a, b) => { set(c.IsChecked == true); Settings.Save(); }; return c; }
            Slider Sld(double min, double max, double v, Action<double> set, double? step = null)
            {
                var sl = new Slider { Minimum = min, Maximum = max, Value = v, Margin = new Thickness(0, 2, 0, 6) };
                if (step.HasValue) { sl.IsSnapToTickEnabled = true; sl.TickFrequency = step.Value; }
                sl.ValueChanged += (a, b) => { set(sl.Value); Settings.Save(); };
                return sl;
            }
            TextBlock L(string t) { var l = MacChrome.Label(t, 12.5); l.Margin = new Thickness(0, 6, 0, 0); return l; }
            string[] sizes = { "10 pt", "11 pt", "12 pt", "13 pt", "14 pt", "15 pt", "16 pt" };
            Panel PopupRow(string label, IList<string> opts, int sel, Action<int> pick)
            {
                var g = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
                var l = MacChrome.Label(label, 12.5); l.Width = 90; l.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(l, Dock.Left);
                g.Children.Add(l); g.Children.Add(MacChrome.PopupButton(opts, sel, pick, 120)); return g;
            }
            // Always open in / browse
            var def = new Button { Content = "Use as Defaults", Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            def.Click += (a, b) => { s.DefaultMode = view.Mode; s.DefaultSort = view.Sort; s.DefaultGroup = view.Group; s.Folders.Clear(); Settings.Save(); };

            if (mode == ViewMode.Icon)
            {
                sp.Children.Add(L("Icon size: " + (int)s.IconSize + "×" + (int)s.IconSize));
                sp.Children.Add(Sld(16, 128, s.IconSize, v => s.IconSize = Math.Round(v / 2) * 2));
                sp.Children.Add(L("Grid spacing"));
                sp.Children.Add(Sld(0, 1, s.GridSpacing, v => s.GridSpacing = v));
                sp.Children.Add(PopupRow("Text size:", sizes, (int)s.IconTextSize - 10, i => { s.IconTextSize = 10 + i; Settings.Save(); }));
                sp.Children.Add(PopupRow("Label position:", new[] { "Bottom", "Right" }, s.LabelBottom ? 0 : 1, i => { s.LabelBottom = i == 0; Settings.Save(); }));
                sp.Children.Add(Chk("Show item info", s.ShowItemInfo, v => s.ShowItemInfo = v));
                sp.Children.Add(Chk("Show icon preview", s.ShowIconPreview, v => s.ShowIconPreview = v));
            }
            else if (mode == ViewMode.List)
            {
                sp.Children.Add(Chk("Use relative dates", s.RelativeDates, v => s.RelativeDates = v));
                sp.Children.Add(Chk("Calculate all sizes", s.CalculateAllSizes, v => { s.CalculateAllSizes = v; view.Reload(); }));
                sp.Children.Add(Chk("Show icon preview", s.ShowIconPreview, v => s.ShowIconPreview = v));
                sp.Children.Add(PopupRow("Icon size:", new[] { "Small", "Large" }, s.ListLargeIcons ? 1 : 0, i => { s.ListLargeIcons = i == 1; Settings.Save(); }));
                sp.Children.Add(PopupRow("Text size:", sizes, (int)s.ListTextSize - 10, i => { s.ListTextSize = 10 + i; Settings.Save(); }));
                var h = MacChrome.Label("Show Columns", 12.5, true); h.Margin = new Thickness(0, 8, 0, 4); sp.Children.Add(h);
                foreach (var id in new[] { "created", "modified", "opened", "size", "kind", "tags", "comments" })
                {
                    var i2 = id;
                    sp.Children.Add(Chk(ListSurface.AllColumns[id].Title, s.ListColumns.Contains(id), v => { if (v && !s.ListColumns.Contains(i2)) s.ListColumns.Add(i2); if (!v) s.ListColumns.Remove(i2); }));
                }
            }
            else if (mode == ViewMode.Column)
            {
                sp.Children.Add(PopupRow("Text size:", sizes, (int)s.ColumnTextSize - 10, i => { s.ColumnTextSize = 10 + i; Settings.Save(); }));
                sp.Children.Add(Chk("Show icons", s.ColumnShowIcons, v => s.ColumnShowIcons = v));
                sp.Children.Add(Chk("Show icon preview", s.ShowIconPreview, v => s.ShowIconPreview = v));
                sp.Children.Add(Chk("Show preview column", s.ColumnShowPreview, v => s.ColumnShowPreview = v));
            }
            else
            {
                sp.Children.Add(L("Thumbnail size"));
                sp.Children.Add(Sld(32, 120, s.GalleryThumbSize, v => s.GalleryThumbSize = v));
            }
            // sort & group
            var hdr = MacChrome.Label("Sort By", 12.5, true); hdr.Margin = new Thickness(0, 10, 0, 2); sp.Children.Add(hdr);
            var sortNames = new[] { "Name", "Kind", "Date Last Opened", "Date Modified", "Date Created", "Size", "Tags" };
            var sortKeys = new[] { SortKey.Name, SortKey.Kind, SortKey.DateLastOpened, SortKey.DateModified, SortKey.DateCreated, SortKey.Size, SortKey.Tags };
            sp.Children.Add(MacChrome.PopupButton(sortNames, Array.IndexOf(sortKeys, view.Sort), i => view.SetSort(sortKeys[i]), 180));
            if (mode != ViewMode.Column)
            {
                var h2 = MacChrome.Label("Group By", 12.5, true); h2.Margin = new Thickness(0, 8, 0, 2); sp.Children.Add(h2);
                var gn = new[] { "None", "Name", "Kind", "Date Last Opened", "Date Modified", "Date Created", "Size", "Tags" };
                var gk = new[] { GroupKey.None, GroupKey.Name, GroupKey.Kind, GroupKey.DateLastOpened, GroupKey.DateModified, GroupKey.DateCreated, GroupKey.Size, GroupKey.Tags };
                sp.Children.Add(MacChrome.PopupButton(gn, Array.IndexOf(gk, view.Group), i => view.SetGroup(gk[i]), 180));
            }
            sp.Children.Add(def);
            host.Content = sp;
        }
    }
}

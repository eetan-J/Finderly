using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;
using Finderly.Core;
using Finderly.Views;

namespace Finderly.UI
{
    public sealed class TabState
    {
        public FolderView View = new FolderView();
        public List<Location> Back = new List<Location>(), Fwd = new List<Location>();
        public Location Loc;
        public string Title => Loc?.Title ?? "";
        public Border Header;
        public TextBlock HeaderText;
        public Image HeaderIcon;
    }

    public sealed partial class MainWindow : Window
    {
        public static readonly List<MainWindow> Windows = new List<MainWindow>();

        readonly List<TabState> tabs = new List<TabState>();
        TabState tab;
        readonly Grid contentHost = new Grid();
        readonly Grid tabBar = new Grid();
        readonly StackPanel tabStack = new StackPanel { Orientation = Orientation.Horizontal };
        readonly Sidebar sidebar = new Sidebar();
        readonly Grid sidebarHost = new Grid();
        readonly Grid root = new Grid();
        readonly TextBlock titleText = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        readonly Image titleIcon = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly Button backBtn, fwdBtn;
        readonly ToggleButton[] viewBtns = new ToggleButton[4];
        readonly TextBox searchBox = new TextBox { Height = 24, Padding = new Thickness(24, 2, 20, 2), VerticalContentAlignment = VerticalAlignment.Center };
        readonly TextBlock searchHint = new TextBlock { Text = "Search", IsHitTestVisible = false, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 0, 0) };
        readonly StackPanel pathBar = new StackPanel { Orientation = Orientation.Horizontal };
        readonly TextBlock status = new TextBlock { FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly Slider zoomSlider = new Slider { Minimum = 16, Maximum = 128, Width = 90, Value = 64, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        readonly Border searchScopeBar = new Border { Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0, 0, 0, 1), Height = 32 };
        readonly Border previewPane = new Border { Width = 260, BorderThickness = new Thickness(1, 0, 0, 0), Visibility = Visibility.Collapsed };
        readonly ColumnDefinition sidebarCol = new ColumnDefinition { Width = new GridLength(200) };
        readonly ColumnDefinition previewCol = new ColumnDefinition { Width = new GridLength(0) };
        readonly DispatcherTimer searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        readonly Border toolbarBg = new Border();
        readonly Border pathBarHost = new Border { Height = 24, BorderThickness = new Thickness(0, 1, 0, 0) };
        readonly Border statusHost = new Border { Height = 24 };
        TrafficLights lights;
        Location beforeSearch;
        bool fullScreen; WindowState preFull;
        HashSet<string> cutPaths;
        FileItem previewItem;

        public MainWindow(Location start)
        {
            Windows.Add(this);
            Title = "Finderly";
            Width = 960; Height = 620; MinWidth = 520; MinHeight = 300;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Left + 120 + (Windows.Count % 8) * 28; Top = SystemParameters.WorkArea.Top + 80 + (Windows.Count % 8) * 28;
            Icon = MacChrome.AppIcon();
            MacChrome.Setup(this);
            WindowFrame.Attach(this, Settings.Current.ShowSidebar && WindowFrame.BackdropSupported);
            Theme.Backdrop = WindowFrame.BackdropSupported;
            var wb = Settings.Current.Window;
            if (wb != null && Windows.Count == 1)
            {
                Left = wb.X; Top = wb.Y; Width = Math.Max(MinWidth, wb.W); Height = Math.Max(MinHeight, wb.H);
                var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                if (!vs.Contains(new Point(Left + 40, Top + 20))) { Left = 140; Top = 100; }
                if (wb.Max) WindowState = WindowState.Maximized;
            }

            backBtn = ToolBtn("back", "Back (Alt+Left)", () => GoBack());
            fwdBtn = ToolBtn("forward", "Forward (Alt+Right)", () => GoForward());
            BuildLayout();
            BuildMenus();
            WireCommands();

            Closing += (s, e) => { if (Windows.Count == 1) SaveBounds(); };
            Closed += (s, e) => { Windows.Remove(this); foreach (var t in tabs) t.View.Navigate(Location.Computer); if (Windows.Count == 0) Application.Current.Shutdown(); };
            StateChanged += (s, e) => { Theme.Backdrop = WindowFrame.BackdropSupported; };
            Settings.Changed += OnSettings;
            Theme.Changed += () => { toolbarBg.Background = Theme.PanelBg; };
            Loaded += (s, e) => { };
            NewTab(start ?? StartLocation(), true);
            ApplyChromeVisibility();
        }

        public static Location StartLocation()
        {
            switch (Settings.Current.NewWindowTarget)
            {
                case "Recents": return Location.Recents;
                case "Desktop": return Location.Folder(KnownFolders.Desktop);
                case "Documents": return Location.Folder(KnownFolders.Documents);
                case "Downloads": return Location.Folder(KnownFolders.Downloads);
                case "Computer": return Location.Computer;
                default: return Location.Folder(KnownFolders.Home);
            }
        }

        void SaveBounds()
        {
            var r = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            Settings.Current.Window = new WinBounds { X = r.X, Y = r.Y, W = r.Width, H = r.Height, Max = WindowState == WindowState.Maximized };
            Settings.SaveNow();
        }

        Button ToolBtn(string glyph, string tip, Action act, double size = 18)
        {
            var b = new Button { Style = (Style)Application.Current.Resources["ToolButton"], Content = Glyphs.Make(glyph, size), ToolTip = tip, Padding = new Thickness(8, 5, 8, 5) };
            b.Click += (s, e) => act();
            return b;
        }

        // ---------- layout ----------
        void BuildLayout()
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // toolbar
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // tab bar
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // search scope
            root.RowDefinitions.Add(new RowDefinition());                                // content
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // path
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // status

            // Sidebar column spans the full window height (like Finder) with traffic lights on top of it
            var outer = new Grid();
            outer.ColumnDefinitions.Add(sidebarCol);
            outer.ColumnDefinitions.Add(new ColumnDefinition());
            outer.ColumnDefinitions.Add(previewCol);

            // sidebar host
            sidebarHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            sidebarHost.RowDefinitions.Add(new RowDefinition());
            sidebarHost.SetResourceReference(Panel.BackgroundProperty, "SidebarBg");
            lights = new TrafficLights { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), CanZoom = true };
            var sbTop = new Grid { Background = Brushes.Transparent };
            sbTop.Children.Add(lights);
            var toggle = ToolBtn("sidebar", "Hide Sidebar", () => ToggleSidebar(), 17);
            toggle.HorizontalAlignment = HorizontalAlignment.Right; toggle.VerticalAlignment = VerticalAlignment.Center; toggle.Margin = new Thickness(0, 0, 6, 0);
            sbTop.Children.Add(toggle);
            WindowFrame.MakeDragArea(sbTop, this);
            sidebarHost.Children.Add(sbTop);
            Grid.SetRow(sidebar, 1); sidebarHost.Children.Add(sidebar);
            var sbSep = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Right }; sbSep.SetResourceReference(Border.BackgroundProperty, "Separator");
            Grid.SetRowSpan(sbSep, 2); sidebarHost.Children.Add(sbSep);
            outer.Children.Add(sidebarHost);

            // sidebar resize grip
            var grip = new Thumb2Grip(w => { Settings.Current.SidebarWidth = Math.Max(140, Math.Min(380, w)); sidebarCol.Width = new GridLength(Settings.Current.SidebarWidth); Settings.Save(false); }, () => sidebarCol.Width.Value) { HorizontalAlignment = HorizontalAlignment.Left, Width = 6, Margin = new Thickness(0, 0, 0, 0) };
            Grid.SetColumn(grip, 0); grip.HorizontalAlignment = HorizontalAlignment.Right; grip.Margin = new Thickness(0, 0, -3, 0);
            Panel.SetZIndex(grip, 10);

            // main column
            var main = root;
            Grid.SetColumn(main, 1); outer.Children.Add(main);
            outer.Children.Add(grip);

            // preview pane
            previewPane.SetResourceReference(Border.BorderBrushProperty, "Separator");
            previewPane.SetResourceReference(Border.BackgroundProperty, "ContentBg");
            Grid.SetColumn(previewPane, 2); outer.Children.Add(previewPane);

            // toolbar
            toolbarBg.SetResourceReference(Border.BackgroundProperty, "PanelBg");
            var tb = new Grid { Height = 52, Background = Brushes.Transparent };
            tb.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tb.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tb.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var navPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            navPanel.Children.Add(backBtn); navPanel.Children.Add(fwdBtn);
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            titlePanel.Children.Add(titleIcon); titlePanel.Children.Add(titleText);
            var left = new StackPanel { Orientation = Orientation.Horizontal }; left.Children.Add(navPanel); left.Children.Add(titlePanel);
            tb.Children.Add(left);

            var rightTools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(rightTools, 2);
            // view mode segmented control
            var seg = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(0.5), Padding = new Thickness(1), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            seg.SetResourceReference(Border.BackgroundProperty, "HoverBg"); seg.SetResourceReference(Border.BorderBrushProperty, "Separator");
            var segSp = new StackPanel { Orientation = Orientation.Horizontal };
            string[] vg = { "view-icon", "view-list", "view-column", "view-gallery" };
            string[] vt = { "as Icons (Ctrl+1)", "as List (Ctrl+2)", "as Columns (Ctrl+3)", "as Gallery (Ctrl+4)" };
            for (int i = 0; i < 4; i++)
            {
                int m = i;
                var b = new ToggleButton { Style = (Style)Application.Current.Resources["ToolButton"], Content = Glyphs.Make(vg[i], 16), ToolTip = "View " + vt[i], Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0) };
                b.Click += (s, e) => SetMode((ViewMode)m);
                viewBtns[i] = b; segSp.Children.Add(b);
            }
            seg.Child = segSp;
            rightTools.Children.Add(seg);
            rightTools.Children.Add(ToolBtn("group", "Group By", () => ShowMenu(BuildGroupMenu())));
            rightTools.Children.Add(ToolBtn("share", "Share", () => ShareSelected()));
            rightTools.Children.Add(ToolBtn("tag", "Edit Tags", () => ShowMenu(BuildTagMenu())));
            rightTools.Children.Add(ToolBtn("more", "Action", () => ShowMenu(BuildActionMenu())));
            // search
            var sg = new Grid { Width = 170, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            searchHint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            var mg = Glyphs.Make("search", 13, "TextSecondary", 1.5); mg.HorizontalAlignment = HorizontalAlignment.Left; mg.Margin = new Thickness(7, 0, 0, 0);
            var clear = new Button { Style = (Style)Application.Current.Resources["PlainButton"], Content = Glyphs.Make("close", 11, "TextSecondary", 1.6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0), Visibility = Visibility.Collapsed, Cursor = Cursors.Arrow };
            clear.Click += (s, e) => { searchBox.Text = ""; ExitSearch(); FocusContent(); };
            sg.Children.Add(searchBox); sg.Children.Add(searchHint); sg.Children.Add(mg); sg.Children.Add(clear);
            rightTools.Children.Add(sg);
            tb.Children.Add(rightTools);
            toolbarBg.Child = tb;
            WindowFrame.MakeDragArea(tb, this);
            Grid.SetRow(toolbarBg, 0); root.Children.Add(toolbarBg);
            var tbHair = MacChrome.Hairline(); tbHair.VerticalAlignment = VerticalAlignment.Bottom; Grid.SetRow(tbHair, 0); root.Children.Add(tbHair);

            searchBox.TextChanged += (s, e) =>
            {
                searchHint.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                clear.Visibility = searchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                searchTimer.Stop(); searchTimer.Start();
            };
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); RunSearch(); };
            searchBox.KeyDown += (s, e) => { if (e.Key == Key.Escape) { searchBox.Text = ""; ExitSearch(); FocusContent(); e.Handled = true; } else if (e.Key == Key.Down || e.Key == Key.Return) { FocusContent(); e.Handled = true; } };

            // tab bar
            tabBar.Height = 28; tabBar.Visibility = Visibility.Collapsed;
            tabBar.SetResourceReference(Panel.BackgroundProperty, "TabBarBg");
            tabBar.ColumnDefinitions.Add(new ColumnDefinition()); tabBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tabBar.Children.Add(tabStack);
            var plus = new Button { Style = (Style)Application.Current.Resources["ToolButton"], Content = Glyphs.Make("plus", 12), Padding = new Thickness(8, 4, 8, 4), ToolTip = "New Tab" };
            plus.Click += (s, e) => NewTab(CurrentLocation ?? StartLocation(), true);
            Grid.SetColumn(plus, 1); tabBar.Children.Add(plus);
            Grid.SetRow(tabBar, 1); root.Children.Add(tabBar);

            // search scope bar
            BuildSearchScope();
            Grid.SetRow(searchScopeBar, 2); root.Children.Add(searchScopeBar);

            // content
            contentHost.SetResourceReference(Panel.BackgroundProperty, "ContentBg");
            Grid.SetRow(contentHost, 3); root.Children.Add(contentHost);

            // path bar
            pathBarHost.Child = new ScrollViewer { Content = pathBar, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            pathBarHost.SetResourceReference(Border.BackgroundProperty, "ContentBg"); pathBarHost.SetResourceReference(Border.BorderBrushProperty, "Separator");
            Grid.SetRow(pathBarHost, 4); root.Children.Add(pathBarHost);

            // status bar
            status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            var sg2 = new Grid();
            sg2.Children.Add(status);
            zoomSlider.HorizontalAlignment = HorizontalAlignment.Right;
            zoomSlider.ValueChanged += (s, e) => OnZoomSlider();
            sg2.Children.Add(zoomSlider);
            statusHost.Child = sg2;
            statusHost.SetResourceReference(Border.BackgroundProperty, "ContentBg");
            Grid.SetRow(statusHost, 5); root.Children.Add(statusHost);

            sidebar.Navigate += (loc, newTab) => { if (newTab) NewTab(loc, true); else NavigateTo(loc); };
            sidebar.ContextRequested += SidebarContext;
            sidebar.DropRequested += (t, d, e) => HandleDrop(t, d, e);
            sidebar.EjectRequested += Eject;
            Content = outer;
            sidebarCol.Width = new GridLength(Settings.Current.SidebarWidth);
        }

        class Thumb2Grip : FrameworkElement
        {
            readonly Action<double> cb; readonly Func<double> get; double sx, sw;
            public Thumb2Grip(Action<double> c, Func<double> g) { cb = c; get = g; Cursor = Cursors.SizeWE; }
            protected override void OnRender(DrawingContext dc) { dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight)); }
            protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { sx = e.GetPosition(Window.GetWindow(this)).X; sw = get(); CaptureMouse(); }
            protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured) cb(sw + e.GetPosition(Window.GetWindow(this)).X - sx); }
            protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { ReleaseMouseCapture(); }
        }

        void ToggleSidebar()
        {
            Settings.Current.ShowSidebar = !Settings.Current.ShowSidebar;
            Settings.Save();
            ApplyChromeVisibility();
        }

        void ApplyChromeVisibility()
        {
            var s = Settings.Current;
            sidebarCol.Width = new GridLength(s.ShowSidebar ? s.SidebarWidth : 0);
            sidebarHost.Visibility = s.ShowSidebar ? Visibility.Visible : Visibility.Collapsed;
            pathBarHost.Visibility = s.ShowPathBar ? Visibility.Visible : Visibility.Collapsed;
            statusHost.Visibility = s.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
            toolbarBg.Visibility = s.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
            previewCol.Width = new GridLength(s.ShowPreview ? 260 : 0);
            previewPane.Visibility = s.ShowPreview ? Visibility.Visible : Visibility.Collapsed;
            tabBar.Visibility = tabs.Count > 1 || s.AlwaysShowTabBar ? Visibility.Visible : Visibility.Collapsed;
            // lights live in sidebar when visible; otherwise float over toolbar
            if (!s.ShowSidebar)
            {
                if (lights.Parent is Panel p) p.Children.Remove(lights);
                if (!((Grid)toolbarBg.Child).Children.Contains(lights)) { ((Grid)toolbarBg.Child).Children.Add(lights); lights.Margin = new Thickness(14, 0, 0, 0); }
                ((StackPanel)((Grid)toolbarBg.Child).Children[0] is StackPanel sp ? sp : null)?.SetValue(MarginProperty, new Thickness(80, 0, 0, 0));
            }
            else
            {
                if (lights.Parent is Panel p) p.Children.Remove(lights);
                var sbTop = (Grid)sidebarHost.Children[0];
                if (!sbTop.Children.Contains(lights)) sbTop.Children.Insert(0, lights);
                (((Grid)toolbarBg.Child).Children[0] as StackPanel)?.SetValue(MarginProperty, new Thickness(0));
            }
            UpdateToolbarState();
            UpdateStatus();
        }

        void OnSettings()
        {
            UpdateToolbarState();
            sidebar.Rebuild();
            UpdateStatus();
        }

        // ---------- tabs ----------
        public Location CurrentLocation => tab?.Loc;

        void NewTab(Location loc, bool select)
        {
            var t = new TabState();
            WireView(t);
            tabs.Add(t);
            BuildTabHeader(t);
            if (select || tab == null) SelectTab(t);
            Go(t, loc, false);
            ApplyChromeVisibility();
        }

        void WireView(TabState t)
        {
            var v = t.View;
            v.Visibility = Visibility.Collapsed;
            contentHost.Children.Add(v);
            v.SelectionChanged += () => { if (tab == t) { UpdateStatus(); UpdatePreviewPane(); if (QuickLookWindow.Instance != null) QuickLookSync(); } };
            v.ItemActivated += (it, ctrl) => OpenItem(it, ctrl);
            v.ContextRequested += it => ShowContext(it);
            v.DropRequested += (target, data, eff) => HandleDrop(target, data, eff);
            v.SpringLoad += it => { if (it != null && it.IsDir && tab == t) NavigateTo(Location.Folder(it.Path)); };
            v.Loaded2 += () => { if (tab == t) { UpdateStatus(); UpdateToolbarState(); } };
            v.ViewChanged += () => { if (tab == t) UpdateToolbarState(); };
            v.HeaderContext += () => ShowMenu(BuildListColumnsMenu());
            v.OpenFolderRequested += p => { if (tab == t) { UpdatePathBar(p); } };
            v.ZoomRequested += d => { if (tab == t) Zoom(d); };
            v.InfoLine = it => it.IsDir ? "" : (Format.IsImage(it) ? Format.Size(it.Size) : Format.Size(it.Size));
        }

        void BuildTabHeader(TabState t)
        {
            var txt = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 170 };
            txt.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            var close = new Button { Style = (Style)Application.Current.Resources["ToolButton"], Content = Glyphs.Make("close", 9, "TextSecondary", 1.7), Padding = new Thickness(4), Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Hidden };
            close.Click += (s, e) => CloseTab(t);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(txt); sp.Children.Add(close);
            var b = new Border { Child = sp, Padding = new Thickness(14, 0, 8, 0), MinWidth = 120, Background = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 1, 0), Cursor = Cursors.Arrow };
            b.SetResourceReference(Border.BorderBrushProperty, "Separator");
            b.MouseEnter += (s, e) => close.Visibility = Visibility.Visible;
            b.MouseLeave += (s, e) => { if (tab != t) close.Visibility = Visibility.Hidden; };
            b.MouseLeftButtonDown += (s, e) => { SelectTab(t); };
            b.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Middle) CloseTab(t); };
            t.Header = b; t.HeaderText = txt;
            tabStack.Children.Add(b);
            b.Tag = close;
        }

        void RefreshTabHeaders()
        {
            foreach (var t in tabs)
            {
                t.HeaderText.Text = t.Title;
                t.Header.Background = t == tab ? Theme.TabActiveBg : Brushes.Transparent;
                if (t.Header.Tag is Button c) c.Visibility = t == tab ? Visibility.Visible : Visibility.Hidden;
            }
            tabBar.Visibility = tabs.Count > 1 || Settings.Current.AlwaysShowTabBar ? Visibility.Visible : Visibility.Collapsed;
        }

        void SelectTab(TabState t)
        {
            foreach (var o in tabs) o.View.Visibility = o == t ? Visibility.Visible : Visibility.Collapsed;
            tab = t;
            RefreshTabHeaders();
            sidebar.SetCurrent(t.Loc);
            UpdateAll();
            t.View.FocusSurface();
        }

        void CloseTab(TabState t)
        {
            if (tabs.Count == 1) { Close(); return; }
            int i = tabs.IndexOf(t);
            tabs.Remove(t);
            tabStack.Children.Remove(t.Header);
            contentHost.Children.Remove(t.View);
            if (tab == t) SelectTab(tabs[Math.Min(i, tabs.Count - 1)]);
            RefreshTabHeaders();
            ApplyChromeVisibility();
        }

        void NextTab(int d) { if (tabs.Count < 2) return; SelectTab(tabs[(tabs.IndexOf(tab) + d + tabs.Count) % tabs.Count]); }

        // ---------- navigation ----------
        public void NavigateTo(Location loc, bool record = true)
        {
            if (loc == null || tab == null) return;
            Go(tab, loc, record);
        }

        void Go(TabState t, Location loc, bool record)
        {
            if (record && t.Loc != null && !t.Loc.Equals(loc) && t.Loc.Kind != LocKind.Search) { t.Back.Add(t.Loc); if (t.Back.Count > 200) t.Back.RemoveAt(0); t.Fwd.Clear(); }
            if (loc.Kind == LocKind.Folder)
            {
                try { if (!Directory.Exists(loc.Path)) { MacAlert.Show(this, "The folder “" + Format.FolderTitle(loc.Path) + "” can’t be found.", null, new[] { "OK" }); return; } }
                catch { }
                var recents = Settings.Current.RecentFolders; recents.Remove(loc.Path); recents.Insert(0, loc.Path); if (recents.Count > 12) recents.RemoveAt(12);
            }
            t.Loc = loc;
            t.View.Navigate(loc);
            if (tab == t) { searchBox.Text = loc.Kind == LocKind.Search ? loc.Query : ""; UpdateAll(); }
            RefreshTabHeaders();
            Settings.Save(false);
        }

        void GoBack() { if (tab == null || tab.Back.Count == 0) return; var l = tab.Back[tab.Back.Count - 1]; tab.Back.RemoveAt(tab.Back.Count - 1); if (tab.Loc != null) tab.Fwd.Add(tab.Loc); Go(tab, l, false); }
        void GoForward() { if (tab == null || tab.Fwd.Count == 0) return; var l = tab.Fwd[tab.Fwd.Count - 1]; tab.Fwd.RemoveAt(tab.Fwd.Count - 1); if (tab.Loc != null) tab.Back.Add(tab.Loc); Go(tab, l, false); }

        void GoUp()
        {
            var l = tab?.Loc; if (l == null) return;
            if (l.Kind == LocKind.Folder)
            {
                var p = Path.GetDirectoryName(l.Path.TrimEnd('\\'));
                if (p == null) NavigateTo(Location.Computer);
                else
                {
                    var from = l.Path;
                    NavigateTo(Location.Folder(p));
                    tab.View.SelectAfterReload(from);
                    void Sel() { tab.View.Loaded2 -= Sel; tab.View.SelectPaths(new[] { from }); }
                    tab.View.Loaded2 += Sel;
                }
            }
            else if (l.Kind == LocKind.Search && beforeSearch != null) ExitSearch();
        }

        // ---------- updates ----------
        void UpdateAll()
        {
            if (tab == null) return;
            var loc = tab.Loc;
            titleText.Text = loc?.Title ?? "";
            Title = (loc?.Title ?? "") + " — Finderly";
            try
            {
                if (loc?.Kind == LocKind.Folder)
                    titleIcon.Source = IconPainter.Snapshot(FileItem.FromPath(loc.Path) ?? new FileItem { Path = loc.Path, Name = loc.Title, IsDir = true, Ext = "" }, 16);
                else titleIcon.Source = null;
            }
            catch { titleIcon.Source = null; }
            titleIcon.Visibility = titleIcon.Source == null ? Visibility.Collapsed : Visibility.Visible;
            sidebar.SetCurrent(loc);
            UpdatePathBar(loc?.Kind == LocKind.Folder ? loc.Path : null);
            UpdateToolbarState();
            UpdateStatus();
            UpdatePreviewPane();
            RefreshTabHeaders();
            searchScopeBar.Visibility = loc?.Kind == LocKind.Search ? Visibility.Visible : Visibility.Collapsed;
            if (loc?.Kind == LocKind.Search) UpdateScopeButtons();
        }

        void UpdateToolbarState()
        {
            if (tab == null) return;
            backBtn.IsEnabled = tab.Back.Count > 0; fwdBtn.IsEnabled = tab.Fwd.Count > 0;
            for (int i = 0; i < 4; i++) viewBtns[i].IsChecked = (int)tab.View.Mode == i;
            zoomSlider.Visibility = tab.View.Mode == ViewMode.Icon && Settings.Current.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
            zoomSlider.Value = Settings.Current.IconSize;
        }

        public void UpdateStatus()
        {
            if (tab == null) return;
            var v = tab.View;
            var sel = v.Selected.ToList();
            string s;
            int total = v.ItemTotal;
            if (sel.Count > 0)
            {
                s = sel.Count + " of " + total + " selected";
                if (sel.Count == 1 && !sel[0].IsDir) s += ", " + Format.Size(sel[0].Size);
            }
            else s = total + (total == 1 ? " item" : " items");
            if (v.Loc?.Kind == LocKind.Folder)
            {
                try
                {
                    var d = new DriveInfo(Path.GetPathRoot(v.Loc.Path));
                    if (sel.Count == 0) s += ", " + Format.Size(d.AvailableFreeSpace).Replace("Zero bytes", "0 bytes") + " available";
                }
                catch { }
            }
            if (v.IsLoading) s = "Loading…";
            status.Text = s;
        }

        void UpdatePathBar(string folderPath)
        {
            pathBar.Children.Clear();
            if (folderPath == null && tab?.Loc != null && tab.Loc.Kind != LocKind.Folder)
            {
                AddPathPart(tab.Loc.Title, null, tab.Loc.Kind == LocKind.Trash ? "trash" : tab.Loc.Kind == LocKind.Computer ? "computer" : tab.Loc.Kind == LocKind.Tag ? "tag" : tab.Loc.Kind == LocKind.Recents ? "recents" : "apps", true);
                return;
            }
            if (folderPath == null) return;
            var parts = new List<string>();
            var p = folderPath.TrimEnd('\\');
            string cur = p;
            while (cur != null)
            {
                parts.Insert(0, cur);
                var par = Path.GetDirectoryName(cur);
                cur = par;
                if (par != null && par.Length <= 3) { parts.Insert(0, par); break; }
            }
            if (parts.Count > 0 && parts[0].Length <= 3 && !parts[0].EndsWith("\\")) parts[0] += "\\";
            for (int i = 0; i < parts.Count; i++)
            {
                var path = parts[i];
                bool last = i == parts.Count - 1;
                var title = path.Length <= 3 ? FileItem.Drive(new DriveInfo(path)).Name : Path.GetFileName(path.TrimEnd('\\'));
                if (path.TrimEnd('\\').Equals(KnownFolders.Home.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) title = Path.GetFileName(KnownFolders.Home);
                string glyph = i == 0 ? "drive" : (KnownFolders.VariantOf(path) ?? "folder");
                AddPathPart(title, path, glyph, last);
                if (!last)
                {
                    var ch = Glyphs.Make("chevright", 9, "TextTertiary", 1.7); ch.Margin = new Thickness(1, 0, 1, 0); ch.VerticalAlignment = VerticalAlignment.Center; pathBar.Children.Add(ch);
                }
            }
        }

        void AddPathPart(string title, string path, string glyph, bool last)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0) };
            var g = Glyphs.Make(glyph, 13, "SidebarGlyph", 1.2); g.Margin = new Thickness(0, 0, 4, 0); g.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(g);
            var t = new TextBlock { Text = title, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, FontWeight = last ? FontWeights.SemiBold : FontWeights.Normal };
            t.SetResourceReference(TextBlock.ForegroundProperty, last ? "Text" : "TextSecondary");
            sp.Children.Add(t);
            var b = new Border { Child = sp, Padding = new Thickness(6, 1, 6, 1), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, AllowDrop = path != null };
            b.MouseEnter += (s, e) => b.Background = Theme.HoverBg; b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            if (path != null)
            {
                b.MouseLeftButtonDown += (s, e) => { if (e.ClickCount >= 1) { if (tab?.Loc?.Kind == LocKind.Folder && string.Equals(tab.Loc.Path.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return; NavigateTo(Location.Folder(path)); } };
                b.DragOver += (s, e) => { e.Effects = ItemsSurface.EffectFor(e, path); e.Handled = true; };
                b.Drop += (s, e) => { HandleDrop(path, e.Data, ItemsSurface.EffectFor(e, path)); e.Handled = true; };
                b.MouseRightButtonDown += (s, e) => { var m = new ContextMenu(); var o = new MenuItem { Header = "Open Enclosing Folder" }; o.Click += (a, c) => NavigateTo(Location.Folder(path)); m.Items.Add(o); var cp = new MenuItem { Header = "Copy as Pathname" }; cp.Click += (a, c) => { try { Clipboard.SetText(path); } catch { } }; m.Items.Add(cp); m.IsOpen = true; };
            }
            pathBar.Children.Add(b);
        }

        // ---------- misc UI helpers ----------
        void ShowMenu(ContextMenu m) { m.Placement = PlacementMode.MousePoint; m.IsOpen = true; }
        void FocusContent() { tab?.View.FocusSurface(); }

        IEnumerable<FileItem> Selected => tab?.View.Selected ?? Enumerable.Empty<FileItem>();

        // ---------- search ----------
        void BuildSearchScope()
        {
            searchScopeBar.SetResourceReference(Border.BackgroundProperty, "ContentBg"); searchScopeBar.SetResourceReference(Border.BorderBrushProperty, "Separator");
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var lbl = MacChrome.Label("Search:", 12.5, false, "TextSecondary"); lbl.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(lbl);
            foreach (var (name, whole) in new[] { ("This PC", true), ("Current folder", false) })
            {
                var rb = new ToggleButton { Content = name, Style = ScopeStyle(), Margin = new Thickness(0, 0, 6, 0), Tag = whole };
                rb.Click += (s, e) => { scopeWhole = whole; RunSearch(); };
                sp.Children.Add(rb);
            }
            var save = new Button { Content = "Save", Margin = new Thickness(10, 0, 0, 0), MinWidth = 50, MinHeight = 22 };
            save.Click += (s, e) => { };
            searchScopeBar.Child = sp;
        }

        Style ScopeStyle()
        {
            var st = new Style(typeof(ToggleButton));
            var tpl = new ControlTemplate(typeof(ToggleButton));
            var bd = new FrameworkElementFactory(typeof(Border)); bd.Name = "Bd"; bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(5)); bd.SetValue(Border.PaddingProperty, new Thickness(9, 2, 9, 2));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter)); cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); cp.SetValue(TextElement.FontSizeProperty, 12.5); cp.SetValue(TextElement.ForegroundProperty, new DynamicResourceExtension("Text"));
            bd.AppendChild(cp); tpl.VisualTree = bd;
            var chk = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            chk.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("PressBg"), "Bd"));
            tpl.Triggers.Add(chk);
            var hov = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hov.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverBg"), "Bd"));
            tpl.Triggers.Add(hov);
            st.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            st.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            return st;
        }

        bool scopeWhole;

        void UpdateScopeButtons()
        {
            if (searchScopeBar.Child is StackPanel sp)
                foreach (var b in sp.Children.OfType<ToggleButton>()) b.IsChecked = (bool)b.Tag == scopeWhole;
        }

        void RunSearch()
        {
            var q = searchBox.Text.Trim();
            if (q.Length == 0) { ExitSearch(); return; }
            if (tab.Loc.Kind != LocKind.Search) beforeSearch = tab.Loc;
            var root = beforeSearch ?? tab.Loc;
            if (root.Kind != LocKind.Folder) scopeWhole = true; else if (tab.Loc.Kind != LocKind.Search) scopeWhole = !Settings.Current.SearchCurrentFolder;
            var loc = new Location { Kind = LocKind.Search, Path = root.Kind == LocKind.Folder ? root.Path : KnownFolders.Home, Query = q, SearchWholePC = scopeWhole };
            Go(tab, loc, false);
        }

        void ExitSearch()
        {
            if (tab?.Loc?.Kind != LocKind.Search) return;
            var back = beforeSearch ?? Location.Folder(KnownFolders.Home);
            beforeSearch = null;
            Go(tab, back, false);
        }

        public void FocusSearch() { searchBox.Focus(); searchBox.SelectAll(); }
    }
}

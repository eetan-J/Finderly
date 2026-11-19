using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Finderly.Core;
using Finderly.UI;

namespace Finderly.Views
{
    /// <summary>Hosts one location and shows it in Icon / List / Column / Gallery mode.</summary>
    public sealed class FolderView : Grid
    {
        public Location Loc { get; private set; }
        public ViewMode Mode { get; private set; }
        public SortKey Sort { get; private set; }
        public bool Asc { get; private set; } = true;
        public GroupKey Group { get; private set; }

        readonly IconSurface icon = new IconSurface();
        readonly ListSurface list = new ListSurface();
        readonly GalleryStrip strip = new GalleryStrip();
        readonly Grid colHost = new Grid();
        readonly ScrollViewer colScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Visibility_Disabled() };
        readonly StackPanel colStack = new StackPanel { Orientation = Orientation.Horizontal };
        readonly GalleryBig galleryBig = new GalleryBig();
        readonly Grid galleryHost = new Grid();
        readonly Canvas overlay = new Canvas { IsHitTestVisible = false };
        readonly TextBlock empty = new TextBlock { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        readonly Grid editLayer = new Grid();

        static ScrollBarVisibility Visibility_Disabled() => ScrollBarVisibility.Disabled;

        public List<FileItem> Items = new List<FileItem>();           // loaded flat items
        readonly HashSet<string> expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<FileItem>> children = new Dictionary<string, List<FileItem>>(StringComparer.OrdinalIgnoreCase);
        CancellationTokenSource loadCts, searchCts, sizeCts;
        FileSystemWatcher watcher;
        DispatcherTimer reloadTimer;
        ItemsSurface active;
        readonly List<ColumnSurface> columns = new List<ColumnSurface>();
        readonly ColumnPreview colPreview = new ColumnPreview();

        public event Action SelectionChanged;
        public event Action<FileItem, bool> ItemActivated;
        public event Action<FileItem> ContextRequested;
        public event Action<string, IDataObject, DragDropEffects> DropRequested;
        public event Action<FileItem> SpringLoad;
        public event Action Loaded2;
        public event Action<string> OpenFolderRequested;       // column view deep navigation (updates path bar without re-load)
        public event Action ViewChanged;
        public event Action HeaderContext;
        public event Action<int> ZoomRequested;

        public bool IsLoading { get; private set; }
        public string SearchQuery;
        public Func<FileItem, string> InfoLine;

        public FolderView()
        {
            ClipToBounds = true;
            Background = Brushes.Transparent;
            Children.Add(icon); Children.Add(list); Children.Add(colHost); Children.Add(galleryHost);
            Children.Add(editLayer); Children.Add(empty);
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");
            empty.FontFamily = Theme.UIFont;
            empty.FontWeight = FontWeights.SemiBold;

            colScroll.Content = colStack;
            colHost.Children.Add(colScroll);
            colScroll.SetResourceReference(Control.BackgroundProperty, "ContentBg");

            // gallery: big preview on top, strip below
            galleryHost.RowDefinitions.Add(new RowDefinition());
            galleryHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(120) });
            galleryHost.Children.Add(galleryBig);
            Grid.SetRow(strip, 1);
            var stripBorder = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = strip };
            stripBorder.SetResourceReference(Border.BorderBrushProperty, "Separator");
            Grid.SetRow(stripBorder, 1);
            galleryHost.Children.Add(stripBorder);

            icon.InfoLine = i => InfoLine?.Invoke(i);
            foreach (var s in new ItemsSurface[] { icon, list, strip }) Wire(s);
            list.SortChanged += (k, a) => { Sort = k; Asc = a; PersistPrefs(); Rebuild(false); ViewChanged?.Invoke(); };
            list.ExpandToggled += ToggleExpand;
            list.HeaderContext += () => HeaderContext?.Invoke();
            list.ColumnsChanged += () => Rebuild(false);
            strip.SelectionChanged += () => galleryBig.Show(strip.FocusedItem ?? strip.Selection.FirstOrDefault());

            Settings.Changed += OnSettingsChanged;
            FileOps.Changed += OnFsChanged;
            Unloaded += (s, e) => { Settings.Changed -= OnSettingsChanged; FileOps.Changed -= OnFsChanged; Stop(); };
            SizeChanged += (s, e) => { if (Mode == ViewMode.Column) UpdateColumnSizes(); };
        }

        void Wire(ItemsSurface s)
        {
            s.SelectionChanged += () => { if (s == active) SelectionChanged?.Invoke(); };
            s.ItemActivated += (it, ctrl) => ItemActivated?.Invoke(it, ctrl);
            s.ContextRequested += it => ContextRequested?.Invoke(it);
            s.RenameRequested += it => BeginRename(it);
            s.DropRequested += (t, d, e) => DropRequested?.Invoke(t, d, e);
            s.SpringLoad += it => SpringLoad?.Invoke(it);
            s.ZoomRequested += d => ZoomRequested?.Invoke(d);
        }

        // ---------- public state ----------
        public ItemsSurface Surface => active;
        public IEnumerable<FileItem> Selected => Mode == ViewMode.Column ? ColumnSelection() : (active?.SelectedInOrder() ?? new List<FileItem>());
        public FileItem Focused => Mode == ViewMode.Column ? ColumnFocused() : active?.FocusedItem;
        public string CurrentFolderPath => Loc?.Kind == LocKind.Folder ? (Mode == ViewMode.Column && columns.Count > 0 ? ColumnTargetFolder() : Loc.Path) : null;
        public bool CanCreateHere => CurrentFolderPath != null;
        public int ItemTotal => Mode == ViewMode.Column && columns.Count > 0 ? columns.Last().ItemCount : Items.Count;

        public void SetCut(HashSet<string> cut) { foreach (var s in new ItemsSurface[] { icon, list, strip }.Concat(columns)) { s.CutPaths = cut; s.InvalidateVisual(); } }

        public void FocusSurface() { (Mode == ViewMode.Column ? (columns.LastOrDefault(c => c.Selection.Count > 0) ?? columns.LastOrDefault()) : active)?.Focus(); }

        // ---------- loading ----------
        public async void Navigate(Location loc, Dictionary<string, object> restore = null)
        {
            Stop();
            Loc = loc;
            var p = Settings.Current.PrefsFor(loc);
            Mode = p.Mode; Sort = p.Sort; Asc = p.Asc; Group = p.Group;
            if (loc.Kind == LocKind.Search) { Mode = Mode == ViewMode.Column ? ViewMode.List : Mode; }
            expanded.Clear(); children.Clear();
            list.ShowWhere = loc.Kind == LocKind.Search || loc.Kind == LocKind.Recents || loc.Kind == LocKind.Tag;
            list.AllowExpand = loc.Kind == LocKind.Folder;
            list.WhereOf = i => { var d = Path.GetDirectoryName(i.Path); return d == null ? "" : d.Replace(KnownFolders.Home, "~"); };
            icon.BackgroundDropTarget = list.BackgroundDropTarget = strip.BackgroundDropTarget = loc.Kind == LocKind.Folder ? loc.Path : null;
            icon.AllowBackgroundDrop = list.AllowBackgroundDrop = loc.Kind == LocKind.Folder;
            ApplyModeVisibility();
            SetItems(new List<FileItem>(), true);
            IsLoading = true;
            if (loc.Kind == LocKind.Search) { StartSearch(); return; }
            loadCts = new CancellationTokenSource();
            var ct = loadCts.Token;
            if (loc.Kind == LocKind.Folder && !Directory.Exists(loc.Path)) { IsLoading = false; ShowEmpty("Folder not found"); Loaded2?.Invoke(); return; }
            try
            {
                var res = await FileSystem.List(loc, Settings.Current.ShowHidden, ct);
                if (ct.IsCancellationRequested) return;
                IsLoading = false;
                Items = res;
                Rebuild(true);
                StartWatch();
                ComputeFolderSizes();
                Loaded2?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch { IsLoading = false; }
        }

        void StartSearch()
        {
            searchCts = new CancellationTokenSource();
            var ct = searchCts.Token;
            var buf = new List<FileItem>();
            var loc = Loc;
            var t = FileSystem.Search(loc, Settings.Current.ShowHidden, batch =>
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    Items.AddRange(batch);
                    Rebuild(false, true);
                }));
            }, ct);
            t.ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() => { if (!ct.IsCancellationRequested) { IsLoading = false; Rebuild(false, true); Loaded2?.Invoke(); } })));
        }

        void Stop()
        {
            loadCts?.Cancel(); searchCts?.Cancel(); sizeCts?.Cancel();
            if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); watcher = null; }
        }

        void StartWatch()
        {
            if (Loc.Kind != LocKind.Folder) return;
            try
            {
                watcher = new FileSystemWatcher(Loc.Path) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes, IncludeSubdirectories = false, InternalBufferSize = 65536 };
                FileSystemEventHandler h = (s, e) => Dispatcher.BeginInvoke(new Action(ScheduleReload));
                watcher.Created += h; watcher.Deleted += h; watcher.Changed += h; watcher.Renamed += (s, e) => Dispatcher.BeginInvoke(new Action(ScheduleReload));
                watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        void ScheduleReload()
        {
            reloadTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            reloadTimer.Stop(); reloadTimer.Tick -= ReloadTick; reloadTimer.Tick += ReloadTick; reloadTimer.Start();
        }

        void ReloadTick(object s, EventArgs e) { reloadTimer.Stop(); Reload(); }

        void OnFsChanged(IEnumerable<string> dirs)
        {
            if (Loc == null) return;
            if (Loc.Kind == LocKind.Folder || Loc.Kind == LocKind.Trash || Loc.Kind == LocKind.Recents) ScheduleReload();
        }

        void OnSettingsChanged()
        {
            if (Loc == null) return;
            Rebuild(false);
        }

        public async void Reload()
        {
            if (Loc == null || Loc.Kind == LocKind.Search) return;
            var ct = (loadCts = new CancellationTokenSource()).Token;
            var sel = Selected.Select(i => i.Path).ToList();
            try
            {
                var res = await FileSystem.List(Loc, Settings.Current.ShowHidden, ct);
                if (ct.IsCancellationRequested) return;
                Items = res;
                foreach (var k in children.Keys.ToList())
                    children[k] = FileSystem.ListFolder(k, Settings.Current.ShowHidden);
                Rebuild(false);
                var set = new HashSet<string>(sel, StringComparer.OrdinalIgnoreCase);
                if (Mode != ViewMode.Column) active.SelectMany(AllLoaded().Where(i => set.Contains(i.Path)));
                if (Mode == ViewMode.Column) RefreshColumns();
                ComputeFolderSizes();
            }
            catch { }
        }

        IEnumerable<FileItem> AllLoaded() => Items.Concat(children.Values.SelectMany(x => x));

        void ComputeFolderSizes()
        {
            sizeCts?.Cancel();
            if (Mode != ViewMode.List || !Settings.Current.CalculateAllSizes && !(Sort == SortKey.Size)) return;
            sizeCts = new CancellationTokenSource();
            var ct = sizeCts.Token;
            var dirs = Items.Where(i => i.IsDir && i.DirSize < 0 && i.Special != SpecialKind.TrashItem).ToList();
            Task.Run(() =>
            {
                int n = 0;
                foreach (var d in dirs)
                {
                    if (ct.IsCancellationRequested) return;
                    d.DirSize = FileSystem.DirectorySize(d.Path, ct);
                    if (++n % 4 == 0) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => list.InvalidateVisual()));
                }
                Dispatcher.BeginInvoke(new Action(() => { if (!ct.IsCancellationRequested) { Rebuild(false); } }));
            }, ct);
        }

        // ---------- build entries ----------
        public void SetItems(List<FileItem> items, bool reset) { Items = items; Rebuild(reset); }

        Func<FileItem, DateTime> OpenedFn => FileSystem.LastOpenedOf;

        List<FileItem> Sorted(IEnumerable<FileItem> src)
        {
            var l = src.ToList();
            bool fold = Settings.Current.FoldersOnTop && Mode != ViewMode.Icon || Settings.Current.FoldersOnTop && Sort == SortKey.Name;
            var cmp = Format.Comparer(Sort, Asc, Settings.Current.FoldersOnTop, OpenedFn);
            l.Sort(cmp);
            return l;
        }

        public void Rebuild(bool reset, bool keepScroll = false)
        {
            var oldScroll = active?.ScrollOffset ?? (0, 0);
            var shown = Items.Where(i => Settings.Current.ShowHidden || !i.IsHidden || Loc.Kind == LocKind.Trash).ToList();
            var sorted = Sorted(shown);
            var es = new List<Entry>(sorted.Count + 8);
            if (Group != GroupKey.None && Mode != ViewMode.Column)
            {
                var groups = sorted.GroupBy(i => Format.GroupOf(i, Group, OpenedFn)).OrderBy(g => g.Key.order).ThenBy(g => g.Key.key, Comparer<string>.Create(Format.NameCmp)).ToList();
                if (!Asc && (Group == GroupKey.Name)) groups.Reverse();
                foreach (var g in groups)
                {
                    es.Add(new Entry { Header = g.Key.key });
                    foreach (var i in g) es.Add(new Entry { Item = i });
                }
            }
            else
            {
                foreach (var i in sorted)
                {
                    AddTree(es, i, 0);
                }
            }
            if (Mode == ViewMode.Column) { BuildColumnsRoot(sorted); return; }
            active.SetEntries(es, reset);
            if (!reset) active.ScrollOffset = oldScroll;
            if (Mode == ViewMode.Gallery) galleryBig.Show(strip.FocusedItem ?? strip.Selection.FirstOrDefault());
            ShowEmpty(es.Count == 0 && !IsLoading ? EmptyText() : null);
        }

        void AddTree(List<Entry> es, FileItem i, int depth)
        {
            bool ex = Mode == ViewMode.List && Loc.Kind == LocKind.Folder && Group == GroupKey.None && i.IsDir && expanded.Contains(i.Path);
            es.Add(new Entry { Item = i, Depth = depth, Expanded = ex });
            if (ex && children.TryGetValue(i.Path, out var kids))
            {
                var vis = kids.Where(k => Settings.Current.ShowHidden || !k.IsHidden);
                foreach (var k in Sorted(vis)) AddTree(es, k, depth + 1);
            }
        }

        string EmptyText()
        {
            switch (Loc.Kind)
            {
                case LocKind.Trash: return "Trash is empty";
                case LocKind.Search: return "No results";
                case LocKind.Recents: return "No recent items";
                case LocKind.Tag: return "No items with this tag";
                default: return null;
            }
        }

        void ShowEmpty(string text)
        {
            empty.Text = text ?? "";
            empty.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        }

        async void ToggleExpand(FileItem it)
        {
            if (it == null || !it.IsDir) return;
            if (expanded.Contains(it.Path)) expanded.Remove(it.Path);
            else
            {
                expanded.Add(it.Path);
                if (!children.ContainsKey(it.Path))
                    children[it.Path] = await Task.Run(() => FileSystem.ListFolder(it.Path, Settings.Current.ShowHidden));
            }
            Rebuild(false);
        }

        // ---------- view modes ----------
        void ApplyModeVisibility()
        {
            icon.Visibility = Mode == ViewMode.Icon ? Visibility.Visible : Visibility.Collapsed;
            list.Visibility = Mode == ViewMode.List ? Visibility.Visible : Visibility.Collapsed;
            colHost.Visibility = Mode == ViewMode.Column ? Visibility.Visible : Visibility.Collapsed;
            galleryHost.Visibility = Mode == ViewMode.Gallery ? Visibility.Visible : Visibility.Collapsed;
            active = Mode == ViewMode.Icon ? icon : Mode == ViewMode.List ? list : Mode == ViewMode.Gallery ? (ItemsSurface)strip : null;
            if (Mode == ViewMode.Column) active = columns.LastOrDefault();
            list.SortKey = Sort; list.SortAsc = Asc;
            if (Mode == ViewMode.Gallery) strip.Relayout();
        }

        public void SetMode(ViewMode m)
        {
            if (m == Mode || Loc == null) return;
            var sel = Selected.ToList();
            var keepPath = CurrentFolderPath;
            Mode = m;
            if (Loc.Kind == LocKind.Search && m == ViewMode.Column) Mode = ViewMode.List;
            PersistPrefs();
            ApplyModeVisibility();
            Rebuild(true);
            if (Mode != ViewMode.Column && sel.Count > 0) { active.SelectMany(sel.Where(s => active.Entries.Any(e => s.Equals(e.Item)))); }
            ViewChanged?.Invoke();
            SelectionChanged?.Invoke();
            focusAfterBuild = Mode == ViewMode.Column;
            FocusSurface();
        }
        bool focusAfterBuild;

        public void SetSort(SortKey k, bool? asc = null)
        {
            if (Sort == k && asc == null && k != SortKey.Name) { }
            Sort = k; Asc = asc ?? (k == SortKey.Name || k == SortKey.Kind);
            if (k == SortKey.DateModified || k == SortKey.DateCreated || k == SortKey.DateLastOpened || k == SortKey.Size) { if (asc == null) Asc = true; }
            list.SortKey = Sort; list.SortAsc = Asc;
            PersistPrefs(); Rebuild(false); ComputeFolderSizes(); ViewChanged?.Invoke();
            if (Mode == ViewMode.Column) RefreshColumns();
        }

        public void SetGroup(GroupKey g)
        {
            Group = g; PersistPrefs(); Rebuild(false); ViewChanged?.Invoke();
        }

        void PersistPrefs()
        {
            if (Loc == null) return;
            Settings.Current.SetPrefs(Loc, new FolderPrefs { Mode = Mode, Sort = Sort, Asc = Asc, Group = Group });
        }

        public void ExpandAll(bool open)
        {
            if (Mode != ViewMode.List) return;
            var sel = Selected.Where(i => i.IsDir).ToList();
            foreach (var d in sel) { if (open) { expanded.Add(d.Path); if (!children.ContainsKey(d.Path)) children[d.Path] = FileSystem.ListFolder(d.Path, Settings.Current.ShowHidden); } else expanded.Remove(d.Path); }
            Rebuild(false);
        }

        public void SelectAll() { if (Mode == ViewMode.Column) { (columns.LastOrDefault())?.SelectAll(); SelectionChanged?.Invoke(); } else active?.SelectAll(); }
        public void SelectItem(FileItem it) { if (Mode == ViewMode.Column) { var c = columns.LastOrDefault(); c?.SelectOnly(it); } else active?.SelectOnly(it); }
        public void SelectPaths(IEnumerable<string> paths)
        {
            var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            if (Mode == ViewMode.Column) { var c = columns.LastOrDefault(); if (c != null) c.SelectMany(c.Entries.Where(e => e.Item != null && set.Contains(e.Item.Path)).Select(e => e.Item)); }
            else active?.SelectMany(AllLoaded().Where(i => set.Contains(i.Path)));
        }

        public void InvertSelection()
        {
            var s = Mode == ViewMode.Column ? columns.LastOrDefault() : active;
            if (s == null) return;
            var cur = new HashSet<FileItem>(s.Selection);
            s.SelectMany(s.Entries.Where(e => e.Item != null && !cur.Contains(e.Item)).Select(e => e.Item));
        }

        // ---------- inline rename ----------
        public void BeginRename(FileItem it)
        {
            if (it == null || !it.IsReal || it.Special == SpecialKind.Drive || it.Special == SpecialKind.AppShortcut) return;
            ItemsSurface s = Mode == ViewMode.Column ? columns.FirstOrDefault(c => c.Entries.Any(e => it.Equals(e.Item))) : active;
            if (s == null || s == strip) return;
            s.ScrollIntoView(it);
            var r = s.EditRect(it);
            if (r.IsEmpty) return;
            var origin = s.TranslatePoint(new Point(0, 0), editLayer);
            bool multi = s == icon && Settings.Current.LabelBottom;
            var tb = new TextBox
            {
                Text = it.Name,
                Style = (Style)Application.Current.Resources["BareTextBox"],
                FontSize = s == icon ? Settings.Current.IconTextSize : s == list ? Settings.Current.ListTextSize : Settings.Current.ColumnTextSize,
                TextAlignment = multi ? TextAlignment.Center : TextAlignment.Left,
                TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap,
                AcceptsReturn = false,
                Width = r.Width, MinHeight = Math.Min(r.Height, 22),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(origin.X + r.X, origin.Y + r.Y, 0, 0),
                Padding = new Thickness(3, 1, 3, 1),
                BorderThickness = new Thickness(2),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            tb.SetResourceReference(Control.BackgroundProperty, "ContentBg");
            tb.SetResourceReference(Control.BorderBrushProperty, "Accent");
            bool done = false;
            Action<bool> finish = commit =>
            {
                if (done) return; done = true;
                editLayer.Children.Remove(tb);
                if (commit && tb.Text.Trim().Length > 0 && tb.Text != it.Name) DoRename(it, tb.Text.Trim());
                FocusSurface();
            };
            tb.KeyDown += (a, e) => { if (e.Key == Key.Return) { finish(true); e.Handled = true; } else if (e.Key == Key.Escape) { finish(false); e.Handled = true; } };
            tb.LostKeyboardFocus += (a, e) => finish(true);
            editLayer.Children.Add(tb);
            tb.Loaded += (a, e) =>
            {
                tb.Focus();
                var (b, ext) = FileSystem.SplitName(it.Name, it.IsDir);
                tb.Select(0, b.Length);
            };
        }

        void DoRename(FileItem it, string name)
        {
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MacAlert.Show(Window.GetWindow(this), "The name “" + name + "” can’t be used.", "Try using a name with fewer characters or with no punctuation marks.", new[] { "OK" });
                return;
            }
            var oldExt = Path.GetExtension(it.Name); var newExt = Path.GetExtension(name);
            if (!it.IsDir && Settings.Current.WarnExtensionChange && !string.Equals(oldExt, newExt, StringComparison.OrdinalIgnoreCase) && oldExt.Length > 0)
            {
                int r = MacAlert.Show(Window.GetWindow(this), "Are you sure you want to change the extension from “" + oldExt + "” to “" + (newExt.Length == 0 ? "(none)" : newExt) + "”?", "If you make this change, your document may open in a different application.", new[] { "Use " + (newExt.Length == 0 ? "no extension" : newExt), "Keep " + oldExt }, 1, cancelIndex: 1);
                if (r == 1) name = Path.GetFileNameWithoutExtension(name) + oldExt;
            }
            try
            {
                var np = FileOps.Rename(it.Path, name);
                pendingSelect = np;
            }
            catch (IOException)
            {
                MacAlert.Show(Window.GetWindow(this), "The name “" + name + "” is already taken. Please choose a different name.", null, new[] { "OK" });
            }
            catch (Exception ex)
            {
                MacAlert.Show(Window.GetWindow(this), "The operation can’t be completed.", ex.Message, new[] { "OK" });
            }
        }

        string pendingSelect;
        public void SelectAfterReload(string path) { pendingSelect = path; }

        // ---------- column view ----------
        void UpdateColumnSizes() { foreach (var c in columns) c.Width = Settings.Current.ColumnWidth; }

        void BuildColumnsRoot(List<FileItem> sortedRoot)
        {
            // Rebuild from scratch when location changes; otherwise refresh first column only
            if (columns.Count == 0 || columns[0].FolderPath != Loc.Path)
            {
                colStack.Children.Clear(); columns.Clear();
                AddColumn(Loc.Path, sortedRoot);
                focusAfterBuild = true;
            }
            else { RefreshColumns(); }
            active = columns.LastOrDefault();
            if (focusAfterBuild) { focusAfterBuild = false; Dispatcher.BeginInvoke(new Action(() => { if (!(Keyboard.FocusedElement is TextBox)) FocusSurface(); }), System.Windows.Threading.DispatcherPriority.Loaded); }
            ShowEmpty(null);
        }

        ColumnSurface AddColumn(string path, List<FileItem> sorted)
        {
            var c = new ColumnSurface { FolderPath = path, Width = Settings.Current.ColumnWidth, ForceActiveLook = false };
            var es = sorted.Select(i => new Entry { Item = i }).ToList();
            c.SetEntries(es, true);
            Wire(c);
            c.SelectionChanged += () => OnColumnSelection(c);
            c.RightArrow += (cc, it) => { int i = columns.IndexOf(cc); if (i + 1 < columns.Count && columns[i + 1].ItemCount > 0) { columns[i + 1].Focus(); columns[i + 1].SelectOnly(columns[i + 1].ItemAt(0)); } };
            c.LeftArrow += cc => { int i = columns.IndexOf(cc); if (i > 0) { var prev = columns[i - 1]; prev.Focus(); TruncateAfter(i - 1); SelectionChanged?.Invoke(); } };
            c.BackgroundDropTarget = path;
            var border = new Border { Child = c, BorderThickness = new Thickness(0, 0, 1, 0), Width = Settings.Current.ColumnWidth };
            border.SetResourceReference(Border.BorderBrushProperty, "Separator");
            var grip = new Thumb2(w => { Settings.Current.ColumnWidth = Math.Max(140, Math.Min(500, w)); UpdateColumnBorders(); }) { HorizontalAlignment = HorizontalAlignment.Right };
            var host = new Grid { Width = Settings.Current.ColumnWidth };
            host.Children.Add(border); host.Children.Add(grip);
            host.Tag = c;
            colStack.Children.Add(host);
            columns.Add(c);
            return c;
        }

        void UpdateColumnBorders()
        {
            foreach (Grid g in colStack.Children.OfType<Grid>().Where(x => x.Tag is ColumnSurface)) { g.Width = Settings.Current.ColumnWidth; ((Border)g.Children[0]).Width = Settings.Current.ColumnWidth; ((ColumnSurface)g.Tag).Width = Settings.Current.ColumnWidth; ((ColumnSurface)g.Tag).Relayout(); }
            colPreview.Width = Math.Max(Settings.Current.ColumnWidth, 280);
            Settings.Save(false);
        }

        void TruncateAfter(int idx)
        {
            while (columns.Count > idx + 1)
            {
                var last = columns[columns.Count - 1];
                columns.RemoveAt(columns.Count - 1);
                var host = colStack.Children.OfType<Grid>().FirstOrDefault(g => g.Tag == last);
                if (host != null) colStack.Children.Remove(host);
            }
            if (colStack.Children.Contains(colPreview) && columns.Count > 0 && columns[idx].Selection.Count != 1) colStack.Children.Remove(colPreview);
            columns[idx].Highlight = null;
            active = columns[idx];
            foreach (var c in columns) { c.DimSelection = c != columns[idx]; c.InvalidateVisual(); }
        }

        async void OnColumnSelection(ColumnSurface c)
        {
            int idx = columns.IndexOf(c);
            if (idx < 0) return;
            foreach (var cc in columns) cc.DimSelection = cc != c;
            active = c;
            TruncateAfter(idx);
            colStack.Children.Remove(colPreview);
            var sel = c.SelectedInOrder();
            if (sel.Count == 1)
            {
                var it = sel[0];
                c.Highlight = it;
                if (it.IsDir && it.IsReal)
                {
                    var path = it.Path;
                    var kids = await Task.Run(() => FileSystem.ListFolder(path, Settings.Current.ShowHidden));
                    if (c.Selection.Count != 1 || !c.Selection.Contains(it) || columns.IndexOf(c) != columns.Count - 1) return;
                    AddColumn(path, Sorted(kids.Where(k => Settings.Current.ShowHidden || !k.IsHidden)));
                    OpenFolderRequested?.Invoke(path);
                }
                else if (Settings.Current.ColumnShowPreview)
                {
                    colPreview.Show(it);
                    colPreview.Width = Math.Max(Settings.Current.ColumnWidth, 280);
                    colStack.Children.Add(colPreview);
                }
                ScrollColumnsToEnd();
            }
            SelectionChanged?.Invoke();
        }

        void ScrollColumnsToEnd()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => colScroll.ScrollToRightEnd()));
        }

        void RefreshColumns()
        {
            for (int i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                var sel = c.SelectedInOrder().Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                List<FileItem> raw = i == 0 ? Items : FileSystem.ListFolder(c.FolderPath, Settings.Current.ShowHidden);
                var vis = Sorted(raw.Where(k => Settings.Current.ShowHidden || !k.IsHidden));
                c.SetEntries(vis.Select(v => new Entry { Item = v }).ToList(), false);
                if (sel.Count > 0) c.SelectMany(vis.Where(v => sel.Contains(v.Path)));
            }
        }

        public void EnterColumnPath(string fullPath)
        {
            // expand columns to reveal a path under the current root
            if (Mode != ViewMode.Column || Loc.Kind != LocKind.Folder) return;
            var root = Loc.Path.TrimEnd('\\');
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
            var rel = fullPath.Substring(root.Length).Trim('\\');
            if (rel.Length == 0) return;
            ColumnSurface cur = columns[0];
            var cp = root;
            foreach (var part in rel.Split('\\'))
            {
                cp = Path.Combine(cp, part);
                var it = cur.Entries.FirstOrDefault(e => e.Item != null && string.Equals(e.Item.Path, cp, StringComparison.OrdinalIgnoreCase))?.Item;
                if (it == null) break;
                cur.SelectOnly(it);
                if (!it.IsDir) break;
                var next = columns.ElementAtOrDefault(columns.IndexOf(cur) + 1);
                if (next == null) break;
                cur = next;
            }
        }

        IEnumerable<FileItem> ColumnSelection() { var c = columns.LastOrDefault(x => x.Selection.Count > 0 && (x == active || true)); return active is ColumnSurface a && a.Selection.Count > 0 ? a.SelectedInOrder() : (c?.SelectedInOrder() ?? new List<FileItem>()); }
        FileItem ColumnFocused() => ColumnSelection().FirstOrDefault();
        string ColumnTargetFolder()
        {
            // The folder whose contents are displayed in the rightmost folder column
            var a = active as ColumnSurface ?? columns.Last();
            var sel = a.SelectedInOrder();
            if (sel.Count == 1 && sel[0].IsDir && columns.IndexOf(a) == columns.Count - 2) return sel[0].Path;
            return a.FolderPath;
        }
    }

    /// <summary>Drag handle on a column's right edge.</summary>
    public sealed class Thumb2 : FrameworkElement
    {
        readonly Action<double> onWidth; double startX, startW;
        public Thumb2(Action<double> cb) { onWidth = cb; Width = 6; Cursor = Cursors.SizeWE; }
        protected override void OnRender(DrawingContext dc) { dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight)); }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { startX = e.GetPosition(Window.GetWindow(this)).X; startW = Settings.Current.ColumnWidth; CaptureMouse(); e.Handled = true; }
        protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured) onWidth(startW + e.GetPosition(Window.GetWindow(this)).X - startX); }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { ReleaseMouseCapture(); }
    }

    /// <summary>Rightmost "preview" column: large icon/thumbnail plus key facts.</summary>
    public sealed class ColumnPreview : Border
    {
        readonly Image img = new Image { Stretch = Stretch.Uniform, MaxHeight = 220, Margin = new Thickness(16, 20, 16, 12) };
        readonly TextBlock name = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(14, 0, 14, 2) };
        readonly TextBlock sub = new TextBlock { FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(14, 0, 14, 14) };
        readonly StackPanel info = new StackPanel { Margin = new Thickness(14, 0, 14, 14) };
        FileItem cur;

        public ColumnPreview()
        {
            BorderThickness = new Thickness(0, 0, 1, 0);
            this.SetResourceReference(BorderBrushProperty, "Separator");
            var sp = new StackPanel();
            sp.Children.Add(img); sp.Children.Add(name); sp.Children.Add(sub);
            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            sub.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            var hl = new Border { Height = 1, Margin = new Thickness(14, 0, 14, 10) }; hl.SetResourceReference(Border.BackgroundProperty, "Separator");
            sp.Children.Add(hl);
            sp.Children.Add(info);
            Child = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        void Row(string k, string v)
        {
            if (string.IsNullOrEmpty(v)) return;
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            var a = new TextBlock { Text = k, FontSize = 11.5, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 8, 0), TextWrapping = TextWrapping.Wrap }; a.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            var b = new TextBlock { Text = v, FontSize = 11.5, TextWrapping = TextWrapping.Wrap }; b.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            Grid.SetColumn(b, 1); g.Children.Add(a); g.Children.Add(b);
            info.Children.Add(g);
        }

        public void Show(FileItem it)
        {
            cur = it;
            name.Text = it.DisplayName;
            sub.Text = it.Kind + (it.IsDir ? "" : " - " + Format.Size(it.Size));
            info.Children.Clear();
            Row("Created", Format.FullDate(it.Created));
            Row("Modified", Format.FullDate(it.Modified));
            if (Format.IsImage(it)) { var (w, h) = Thumbs.ImageSize(it.Path); if (w > 0) Row("Dimensions", w + " × " + h); }
            SetImage(it);
        }

        void SetImage(FileItem it)
        {
            var t = Thumbs.Get(it.Path, 256, () => { if (cur == it) SetImage(it); });
            if (t?.Image != null) img.Source = t.Image;
            else img.Source = FolderArt.Get(null, 128);
        }
    }

    /// <summary>Large preview of the selected item in Gallery view.</summary>
    public sealed class GalleryBig : Grid
    {
        readonly Image img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(24) };
        readonly TextBlock title = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8), FontSize = 12 };
        FileItem cur; int gen;

        public GalleryBig()
        {
            Children.Add(img);
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            Children.Add(title);
            SizeChanged += (s, e) => { if (cur != null) Show(cur); };
        }

        public async void Show(FileItem it)
        {
            cur = it;
            int g = ++gen;
            if (it == null) { img.Source = null; title.Text = ""; return; }
            title.Text = it.DisplayName + " - " + it.Kind + (it.IsDir ? "" : " - " + Format.Size(it.Size));
            int px = (int)Math.Max(256, Math.Min(2000, Math.Max(ActualWidth, ActualHeight) * 1.5));
            var quick = Thumbs.Peek(it.Path, 256)?.Image;
            if (quick != null) img.Source = quick;
            System.Windows.Media.Imaging.BitmapSource full = null;
            if (it.IsDir) { img.Source = FolderArt.Get(KnownFolders.VariantOf(it.Path), 256); return; }
            if (Format.IsImage(it) && it.Ext != "heic") full = await Task.Run(() => Thumbs.LoadImage(it.Path, px));
            else full = await Task.Run(() => Native.ShellImage(it.Path, 512, Native.SIIGBF_BIGGERSIZEOK));
            if (g == gen && full != null) img.Source = full;
        }
    }
}

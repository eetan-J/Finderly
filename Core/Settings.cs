using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;

namespace Finderly.Core
{
    public sealed class TagDef
    {
        public string Name { get; set; }
        public int Color { get; set; }          // index into Theme.TagColors (0 = none)
        public bool InSidebar { get; set; } = true;
    }

    public sealed class FolderPrefs
    {
        public ViewMode Mode { get; set; }
        public SortKey Sort { get; set; }
        public bool Asc { get; set; } = true;
        public GroupKey Group { get; set; }
    }

    public sealed class WinBounds { public double X { get; set; } public double Y { get; set; } public double W { get; set; } public double H { get; set; } public bool Max { get; set; } }

    public sealed class Settings
    {
        public static Settings Current { get; private set; } = new Settings();
        public static event Action Changed;

        // General
        public string NewWindowTarget { get; set; } = "Home";
        public bool OpenFoldersInTabs { get; set; } = true;
        public bool OpenMediaInPreview { get; set; } = true;
        // Advanced
        public bool ShowAllExtensions { get; set; } = true;
        public bool WarnExtensionChange { get; set; } = true;
        public bool WarnEmptyTrash { get; set; } = true;
        public bool WarnBeforeDelete { get; set; } = true;
        public bool FoldersOnTop { get; set; } = true;
        public bool SearchCurrentFolder { get; set; } = true;
        public bool ShowHidden { get; set; }
        // Window chrome
        public bool ShowSidebar { get; set; } = true;
        public bool ShowPathBar { get; set; } = true;
        public bool ShowStatusBar { get; set; } = true;
        public bool ShowPreview { get; set; }
        public bool ShowToolbar { get; set; } = true;
        public bool AlwaysShowTabBar { get; set; }
        public double SidebarWidth { get; set; } = 200;
        public WinBounds Window { get; set; }
        // Icon view
        public double IconSize { get; set; } = 64;
        public double GridSpacing { get; set; } = 0.5;
        public double IconTextSize { get; set; } = 12;
        public bool LabelBottom { get; set; } = true;
        public bool ShowItemInfo { get; set; }
        public bool ShowIconPreview { get; set; } = true;
        // List view
        public double ListTextSize { get; set; } = 13;
        public bool ListLargeIcons { get; set; }
        public bool RelativeDates { get; set; } = true;
        public bool CalculateAllSizes { get; set; }
        public List<string> ListColumns { get; set; } = new List<string> { "modified", "size", "kind" };
        public Dictionary<string, double> ColumnWidths { get; set; } = new Dictionary<string, double>();
        // Column view
        public double ColumnTextSize { get; set; } = 13;
        public bool ColumnShowIcons { get; set; } = true;
        public bool ColumnShowPreview { get; set; } = true;
        public double ColumnWidth { get; set; } = 245;
        // Gallery
        public double GalleryThumbSize { get; set; } = 56;
        // Defaults & per folder
        public ViewMode DefaultMode { get; set; } = ViewMode.Icon;
        public SortKey DefaultSort { get; set; } = SortKey.Name;
        public GroupKey DefaultGroup { get; set; } = GroupKey.None;
        public Dictionary<string, FolderPrefs> Folders { get; set; } = new Dictionary<string, FolderPrefs>();
        // Sidebar
        public List<string> Favorites { get; set; }
        public HashSet<string> HiddenSidebarItems { get; set; } = new HashSet<string>();
        public HashSet<string> CollapsedSections { get; set; } = new HashSet<string>();
        // Tags
        public List<TagDef> Tags { get; set; }
        public Dictionary<string, List<string>> FileTags { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Comments { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> RecentFolders { get; set; } = new List<string>();

        static readonly string Dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Finderly");
        static readonly string FilePath = System.IO.Path.Combine(Dir, "settings.json");
        static DispatcherTimer saveTimer;

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                    if (s != null) Current = s;
                }
            }
            catch { }
            var c = Current;
            c.FileTags = new Dictionary<string, List<string>>(c.FileTags ?? new Dictionary<string, List<string>>(), StringComparer.OrdinalIgnoreCase);
            c.Comments = new Dictionary<string, string>(c.Comments ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            c.Folders ??= new Dictionary<string, FolderPrefs>();
            c.ColumnWidths ??= new Dictionary<string, double>();
            c.HiddenSidebarItems ??= new HashSet<string>();
            c.CollapsedSections ??= new HashSet<string>();
            c.RecentFolders ??= new List<string>();
            c.ListColumns ??= new List<string> { "modified", "size", "kind" };
            if (c.Tags == null || c.Tags.Count == 0)
                c.Tags = new List<TagDef>
                {
                    new TagDef { Name = "Red", Color = 1 }, new TagDef { Name = "Orange", Color = 2 },
                    new TagDef { Name = "Yellow", Color = 3 }, new TagDef { Name = "Green", Color = 4 },
                    new TagDef { Name = "Blue", Color = 5 }, new TagDef { Name = "Purple", Color = 6 },
                    new TagDef { Name = "Gray", Color = 7 },
                };
            if (c.Favorites == null)
                c.Favorites = new List<string> { "::recents", "::applications", KnownFolders.Desktop, KnownFolders.Documents, KnownFolders.Downloads };
        }

        /// <summary>Persist (debounced) and notify listeners.</summary>
        public static void Save(bool notify = true)
        {
            if (notify) Changed?.Invoke();
            if (saveTimer == null)
            {
                saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveNow(); };
            }
            saveTimer.Stop(); saveTimer.Start();
        }

        public static void SaveNow()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = false }));
                File.Move(tmp, FilePath, true);
            }
            catch { }
        }

        // ---- tag helpers ----
        public List<string> TagsOf(string path) => path != null && FileTags.TryGetValue(path, out var l) ? l : null;
        public TagDef TagDefOf(string name) => Tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        public void ToggleTag(IEnumerable<string> paths, string tag)
        {
            var list = paths.ToList();
            bool allHave = list.All(p => TagsOf(p)?.Contains(tag) == true);
            foreach (var p in list)
            {
                if (!FileTags.TryGetValue(p, out var l)) FileTags[p] = l = new List<string>();
                if (allHave) l.Remove(tag); else if (!l.Contains(tag)) l.Add(tag);
                if (l.Count == 0) FileTags.Remove(p);
            }
            Save();
        }

        public void MovePathMetadata(string from, string to)
        {
            if (FileTags.TryGetValue(from, out var t)) { FileTags.Remove(from); FileTags[to] = t; }
            if (Comments.TryGetValue(from, out var c)) { Comments.Remove(from); Comments[to] = c; }
            // descendants
            var pre = from.TrimEnd('\\') + "\\";
            foreach (var k in FileTags.Keys.Where(k => k.StartsWith(pre, StringComparison.OrdinalIgnoreCase)).ToList())
            { var v = FileTags[k]; FileTags.Remove(k); FileTags[to.TrimEnd('\\') + "\\" + k.Substring(pre.Length)] = v; }
        }

        public FolderPrefs PrefsFor(Location loc)
        {
            var key = loc.Kind == LocKind.Folder ? loc.Path.ToLowerInvariant() : "::" + loc.Kind;
            if (Folders.TryGetValue(key, out var p)) return p;
            var mode = DefaultMode;
            if (loc.Kind == LocKind.Recents || loc.Kind == LocKind.Search || loc.Kind == LocKind.Tag || loc.Kind == LocKind.Trash) mode = mode == ViewMode.Column ? ViewMode.List : mode;
            var sort = loc.Kind == LocKind.Recents ? SortKey.DateLastOpened : DefaultSort;
            var asc = loc.Kind != LocKind.Recents;
            var grp = loc.Kind == LocKind.Recents ? GroupKey.DateLastOpened : DefaultGroup;
            return new FolderPrefs { Mode = mode, Sort = sort, Asc = asc, Group = grp };
        }

        public void SetPrefs(Location loc, FolderPrefs p)
        {
            var key = loc.Kind == LocKind.Folder ? loc.Path.ToLowerInvariant() : "::" + loc.Kind;
            if (loc.Kind == LocKind.Search) return;
            Folders[key] = p;
            if (Folders.Count > 3000) Folders.Remove(Folders.Keys.First());
            Save(false);
        }
    }

    public static class KnownFolders
    {
        public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        public static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public static string Pictures => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        public static string Music => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        public static string Videos => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        public static string Downloads
        {
            get
            {
                try
                {
                    using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
                    var v = k?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
                    if (!string.IsNullOrEmpty(v)) return Environment.ExpandEnvironmentVariables(v);
                }
                catch { }
                return System.IO.Path.Combine(Home, "Downloads");
            }
        }
        public static string OneDrive
        {
            get
            {
                var p = Environment.GetEnvironmentVariable("OneDrive");
                return !string.IsNullOrEmpty(p) && Directory.Exists(p) ? p : null;
            }
        }

        static Dictionary<string, string> _special;
        /// <summary>Maps special folder paths to a glyph variant name used for folder art.</summary>
        public static string VariantOf(string path)
        {
            if (_special == null)
            {
                _special = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                void Add(string p, string v) { if (!string.IsNullOrEmpty(p)) _special[p.TrimEnd('\\')] = v; }
                Add(Desktop, "desktop"); Add(Documents, "documents"); Add(Downloads, "downloads");
                Add(Pictures, "pictures"); Add(Music, "music"); Add(Videos, "movies"); Add(Home, "home");
                Add(OneDrive, "cloud");
                Add(System.IO.Path.Combine(Home, "Applications"), "apps");
                Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "apps");
            }
            return path != null && _special.TryGetValue(path.TrimEnd('\\'), out var v) ? v : null;
        }
    }
}

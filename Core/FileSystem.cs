using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Finderly.Core
{
    public static class FileSystem
    {
        /// <summary>Last-opened timestamps gathered from the Windows Recent folder.</summary>
        public static readonly ConcurrentDictionary<string, DateTime> LastOpened = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public static DateTime LastOpenedOf(FileItem i) => LastOpened.TryGetValue(i.Path, out var d) ? d : i.Accessed;

        static EnumerationOptions Opts(bool hidden, bool recurse = false) => new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = recurse,
            AttributesToSkip = hidden ? 0 : (FileAttributes.Hidden | FileAttributes.System),
            ReturnSpecialDirectories = false,
            MatchType = MatchType.Win32,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        public static List<FileItem> ListFolder(string path, bool hidden)
        {
            var list = new List<FileItem>(256);
            try
            {
                foreach (var fi in new DirectoryInfo(path).EnumerateFileSystemInfos("*", Opts(hidden)))
                {
                    if (!hidden && fi.Name.Length > 0 && fi.Name[0] == '.') continue;
                    list.Add(FileItem.From(fi));
                }
            }
            catch { }
            return list;
        }

        public static int CountChildren(string path, bool hidden)
        {
            try { return new DirectoryInfo(path).EnumerateFileSystemInfos("*", Opts(hidden)).Count(f => hidden || f.Name[0] != '.'); }
            catch { return 0; }
        }

        public static long DirectorySize(string path, CancellationToken ct)
        {
            long total = 0;
            try
            {
                foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", Opts(true, true)))
                {
                    if (ct.IsCancellationRequested) return -1;
                    total += f.Length;
                }
            }
            catch { }
            return total;
        }

        public static Task<List<FileItem>> List(Location loc, bool hidden, CancellationToken ct)
        {
            switch (loc.Kind)
            {
                case LocKind.Folder: return Task.Run(() => ListFolder(loc.Path, hidden), ct);
                case LocKind.Recents: return Task.Run(Recents, ct);
                case LocKind.Applications: return Task.Run(Applications, ct);
                case LocKind.Computer: return Task.Run(Drives, ct);
                case LocKind.Tag: return Task.Run(() => Tagged(loc.Tag), ct);
                case LocKind.Trash: return Native.Sta(TrashItems);
            }
            return Task.FromResult(new List<FileItem>());
        }

        public static List<FileItem> Drives()
        {
            var l = new List<FileItem>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try { if (d.IsReady) l.Add(FileItem.Drive(d)); } catch { }
            }
            return l;
        }

        public static List<FileItem> Recents()
        {
            var res = new List<FileItem>();
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                var links = new DirectoryInfo(dir).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(400);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in links)
                {
                    var t = Native.ResolveLink(l.FullName);
                    if (t == null || !seen.Add(t)) continue;
                    if (!File.Exists(t)) continue;
                    var it = FileItem.FromPath(t);
                    if (it == null) continue;
                    LastOpened[t] = l.LastWriteTime;
                    res.Add(it);
                    if (res.Count >= 250) break;
                }
            }
            catch { }
            return res;
        }

        public static List<FileItem> Applications()
        {
            var res = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            };
            string[] junk = { "uninstall", "readme", "help", "documentation", "license", "release notes", "website", "manual", "changelog" };
            foreach (var r in roots)
            {
                try
                {
                    foreach (var f in new DirectoryInfo(r).EnumerateFiles("*.lnk", Opts(false, true)))
                    {
                        var n = Path.GetFileNameWithoutExtension(f.Name);
                        var lower = n.ToLowerInvariant();
                        if (junk.Any(lower.Contains)) continue;
                        if (res.ContainsKey(n)) continue;
                        var it = FileItem.From(f);
                        it.Special = SpecialKind.AppShortcut;
                        res[n] = it;
                    }
                }
                catch { }
            }
            return res.Values.ToList();
        }

        public static List<FileItem> Tagged(string tag)
        {
            var res = new List<FileItem>();
            foreach (var kv in Settings.Current.FileTags.ToList())
            {
                if (tag != null && !kv.Value.Contains(tag)) continue;
                var it = FileItem.FromPath(kv.Key);
                if (it != null) res.Add(it);
            }
            return res;
        }

        // ---------- Trash (Recycle Bin) via Shell.Application ----------
        public static List<FileItem> TrashItems()
        {
            var res = new List<FileItem>();
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic bin = shell.NameSpace(10);
                foreach (dynamic item in bin.Items())
                {
                    try
                    {
                        string path = item.Path;
                        string name = item.Name;
                        string from = "";
                        try { from = (string)item.ExtendedProperty("System.Recycle.DeletedFrom"); } catch { }
                        DateTime deleted = DateTime.MinValue;
                        try { deleted = (DateTime)item.ExtendedProperty("System.Recycle.DateDeleted"); } catch { }
                        bool isDir = item.IsFolder && Directory.Exists(path);
                        var it = new FileItem
                        {
                            Path = path,
                            Name = name,
                            IsDir = isDir,
                            Special = SpecialKind.TrashItem,
                            TrashOrigin = from,
                            TrashDeleted = deleted,
                            Ext = isDir ? "" : Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),
                        };
                        try { it.Size = isDir ? 0 : (long)item.Size; } catch { }
                        try { it.Modified = item.ModifyDate; } catch { }
                        it.Created = deleted;
                        if (!Path.HasExtension(name) && it.Ext.Length > 0) it.Name = name + "." + it.Ext;
                        res.Add(it);
                    }
                    catch { }
                }
            }
            catch { }
            return res;
        }

        /// <summary>Restores trash items to their original location ("Put Back").</summary>
        public static Task<int> PutBack(IEnumerable<string> trashPaths)
        {
            var set = new HashSet<string>(trashPaths, StringComparer.OrdinalIgnoreCase);
            return Native.Sta(() =>
            {
                int n = 0;
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic bin = shell.NameSpace(10);
                foreach (dynamic item in bin.Items())
                {
                    string p = item.Path;
                    if (!set.Contains(p)) continue;
                    try { item.InvokeVerb("undelete"); n++; } catch { }
                }
                return n;
            });
        }

        /// <summary>Restores items by their original paths (used for undo of Move to Trash).</summary>
        public static Task<int> RestoreOriginals(IEnumerable<string> originalPaths)
        {
            var want = new HashSet<string>(originalPaths.Select(p => p.TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);
            return Native.Sta(() =>
            {
                int n = 0;
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic bin = shell.NameSpace(10);
                var candidates = new List<(dynamic item, DateTime when)>();
                foreach (dynamic item in bin.Items())
                {
                    try
                    {
                        string from = item.ExtendedProperty("System.Recycle.DeletedFrom");
                        string name = item.Name;
                        string full = Path.Combine(from ?? "", name);
                        string path = item.Path;
                        string ext = Path.GetExtension(path);
                        if (want.Contains(full) || want.Contains(full + ext))
                        {
                            DateTime d = DateTime.MinValue;
                            try { d = (DateTime)item.ExtendedProperty("System.Recycle.DateDeleted"); } catch { }
                            candidates.Add((item, d));
                        }
                    }
                    catch { }
                }
                foreach (var c in candidates.OrderByDescending(c => c.when))
                {
                    try { c.item.InvokeVerb("undelete"); n++; } catch { }
                }
                return n;
            });
        }

        public static void DeleteTrashItemPermanently(string rPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(rPath);
                var name = Path.GetFileName(rPath);
                if (name.StartsWith("$R", StringComparison.OrdinalIgnoreCase))
                {
                    var info = Path.Combine(dir, "$I" + name.Substring(2));
                    if (File.Exists(info)) File.Delete(info);
                }
                if (Directory.Exists(rPath)) Directory.Delete(rPath, true);
                else if (File.Exists(rPath)) File.Delete(rPath);
            }
            catch { }
        }

        // ---------- Search ----------
        public static async Task Search(Location loc, bool hidden, Action<List<FileItem>> onBatch, CancellationToken ct)
        {
            var q = (loc.Query ?? "").Trim();
            if (q.Length == 0) return;
            if (loc.SearchWholePC)
            {
                bool ok = await Task.Run(() => IndexSearch(q, null, onBatch, ct), ct).ConfigureAwait(false);
                if (ok || ct.IsCancellationRequested) return;
                await Task.Run(() => WalkSearch(KnownFolders.Home, q, hidden, onBatch, ct), ct).ConfigureAwait(false);
            }
            else
            {
                await Task.Run(() => WalkSearch(loc.Path, q, hidden, onBatch, ct), ct).ConfigureAwait(false);
            }
        }

        static void WalkSearch(string root, string q, bool hidden, Action<List<FileItem>> onBatch, CancellationToken ct)
        {
            var batch = new List<FileItem>();
            var last = DateTime.UtcNow;
            int total = 0;
            var pattern = "*" + q.Replace("*", "").Replace("?", "") + "*";
            try
            {
                foreach (var fi in new DirectoryInfo(root).EnumerateFileSystemInfos(pattern, Opts(hidden, true)))
                {
                    if (ct.IsCancellationRequested) return;
                    if (!hidden && fi.Name.StartsWith(".")) continue;
                    if (!hidden && fi.FullName.Contains("\\.")) continue;
                    batch.Add(FileItem.From(fi));
                    total++;
                    if ((DateTime.UtcNow - last).TotalMilliseconds > 120 || batch.Count > 400)
                    {
                        onBatch(batch); batch = new List<FileItem>(); last = DateTime.UtcNow;
                    }
                    if (total >= 5000) break;
                }
            }
            catch { }
            if (!ct.IsCancellationRequested) onBatch(batch);
        }

        static bool IndexSearch(string q, string scope, Action<List<FileItem>> onBatch, CancellationToken ct)
        {
            try
            {
                var esc = q.Replace("'", "''").Replace("%", "[%]").Replace("_", "[_]");
                var sql = "SELECT TOP 3000 System.ItemPathDisplay FROM SystemIndex WHERE System.FileName LIKE '%" + esc + "%'";
                if (scope != null) sql += " AND SCOPE='file:" + scope.Replace("\\", "/") + "'";
                using var conn = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
                conn.Open();
                using var cmd = new OleDbCommand(sql, conn);
                using var rdr = cmd.ExecuteReader();
                var batch = new List<FileItem>();
                var last = DateTime.UtcNow;
                while (rdr.Read())
                {
                    if (ct.IsCancellationRequested) return true;
                    var p = rdr[0] as string;
                    if (p == null) continue;
                    var it = FileItem.FromPath(p);
                    if (it == null) continue;
                    batch.Add(it);
                    if ((DateTime.UtcNow - last).TotalMilliseconds > 120) { onBatch(batch); batch = new List<FileItem>(); last = DateTime.UtcNow; }
                }
                onBatch(batch);
                return true;
            }
            catch { return false; }
        }

        // ---------- unique naming ----------
        public static string UniqueName(string dir, string baseName, string ext, bool spaceNumber = true)
        {
            string candidate = Path.Combine(dir, baseName + ext);
            if (!Exists(candidate)) return candidate;
            for (int i = 2; i < 10000; i++)
            {
                candidate = Path.Combine(dir, baseName + " " + i + ext);
                if (!Exists(candidate)) return candidate;
            }
            return Path.Combine(dir, baseName + " " + Guid.NewGuid().ToString("N").Substring(0, 6) + ext);
        }

        public static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);

        public static (string baseName, string ext) SplitName(string name, bool isDir)
        {
            if (isDir) return (name, "");
            var ext = Path.GetExtension(name);
            if (ext.Length == name.Length) return (name, "");
            return (name.Substring(0, name.Length - ext.Length), ext);
        }
    }
}

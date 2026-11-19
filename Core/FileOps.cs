using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Finderly.Core
{
    public sealed class UndoOp
    {
        public string Label;
        public Func<Task> Undo;
        public Func<Task> Redo;
    }

    /// <summary>Copy / move / trash / rename / duplicate with full undo-redo, like Finder's Edit menu.</summary>
    public static class FileOps
    {
        public static event Action<IEnumerable<string>> Changed;   // directories that changed
        public static readonly Stack<UndoOp> UndoStack = new Stack<UndoOp>();
        public static readonly Stack<UndoOp> RedoStack = new Stack<UndoOp>();
        public static string ClipboardMode;   // "copy" | "cut"

        static void Notify(params string[] dirs) => Changed?.Invoke(dirs.Where(d => d != null));
        public static void NotifyExternal(params string[] dirs) => Notify(dirs);
        static string Dir(string p) => Path.GetDirectoryName(p.TrimEnd('\\'));

        public static void Push(UndoOp op) { UndoStack.Push(op); RedoStack.Clear(); }

        public static async Task DoUndo()
        {
            if (UndoStack.Count == 0) return;
            var op = UndoStack.Pop();
            try { await op.Undo(); RedoStack.Push(op); } catch { }
        }

        public static async Task DoRedo()
        {
            if (RedoStack.Count == 0) return;
            var op = RedoStack.Pop();
            try { await op.Redo(); UndoStack.Push(op); } catch { }
        }

        // ---------- clipboard ----------
        public static void SetClipboard(IEnumerable<string> paths, bool cut)
        {
            var list = paths.ToList();
            var sc = new StringCollection(); sc.AddRange(list.ToArray());
            var data = new DataObject();
            data.SetFileDropList(sc);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
            try { Clipboard.SetDataObject(data, true); } catch { }
            ClipboardMode = cut ? "cut" : "copy";
        }

        public static (List<string> paths, bool cut) GetClipboard()
        {
            try
            {
                if (!Clipboard.ContainsFileDropList()) return (new List<string>(), false);
                var l = Clipboard.GetFileDropList().Cast<string>().ToList();
                bool cut = false;
                if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms)
                {
                    var b = new byte[4]; ms.Position = 0; ms.Read(b, 0, 4);
                    cut = (BitConverter.ToInt32(b, 0) & 2) != 0;
                }
                return (l, cut);
            }
            catch { return (new List<string>(), false); }
        }

        // ---------- copy / move ----------
        public static string CopyOne(string src, string destDir, bool keepBoth = true)
        {
            bool isDir = Directory.Exists(src);
            var name = Path.GetFileName(src.TrimEnd('\\'));
            var (b, ext) = FileSystem.SplitName(name, isDir);
            string dest = Path.Combine(destDir, name);
            if (FileSystem.Exists(dest))
            {
                if (string.Equals(Path.GetFullPath(src).TrimEnd('\\'), Path.GetFullPath(dest).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || keepBoth)
                    dest = FileSystem.UniqueName(destDir, b + (string.Equals(Dir(src), destDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? " copy" : ""), ext);
            }
            if (isDir) CopyDir(src, dest); else File.Copy(src, dest, false);
            return dest;
        }

        static void CopyDir(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.EnumerateFiles(src)) { try { File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true); } catch { } }
            foreach (var d in Directory.EnumerateDirectories(src)) { try { CopyDir(d, Path.Combine(dest, Path.GetFileName(d))); } catch { } }
        }

        public static string MoveOne(string src, string destDir)
        {
            bool isDir = Directory.Exists(src);
            var name = Path.GetFileName(src.TrimEnd('\\'));
            var (b, ext) = FileSystem.SplitName(name, isDir);
            string dest = Path.Combine(destDir, name);
            if (FileSystem.Exists(dest)) dest = FileSystem.UniqueName(destDir, b, ext);
            if (isDir)
            {
                if (string.Equals(Path.GetPathRoot(src), Path.GetPathRoot(dest), StringComparison.OrdinalIgnoreCase)) Directory.Move(src, dest);
                else { CopyDir(src, dest); Directory.Delete(src, true); }
            }
            else File.Move(src, dest);
            Settings.Current.MovePathMetadata(src, dest);
            return dest;
        }

        public static Task Paste(string destDir, IEnumerable<string> paths, bool cut)
        {
            return Transfer(destDir, paths, cut ? DragDropEffects.Move : DragDropEffects.Copy);
        }

        public static async Task Transfer(string destDir, IEnumerable<string> paths, DragDropEffects effect)
        {
            var src = paths.Where(p => FileSystem.Exists(p)).ToList();
            if (src.Count == 0) return;
            var pairs = new List<(string from, string to)>();
            var dirs = new HashSet<string> { destDir };
            await Task.Run(() =>
            {
                foreach (var s in src)
                {
                    try
                    {
                        string to;
                        if (effect == DragDropEffects.Move) { to = MoveOne(s, destDir); dirs.Add(Dir(s)); }
                        else if (effect == DragDropEffects.Link)
                        {
                            to = FileSystem.UniqueName(destDir, Path.GetFileName(s.TrimEnd('\\')) + " alias", ".lnk");
                            Native.CreateLink(to, s);
                        }
                        else to = CopyOne(s, destDir);
                        pairs.Add((s, to));
                    }
                    catch { }
                }
            });
            if (pairs.Count == 0) return;
            if (effect == DragDropEffects.Move)
                Push(new UndoOp
                {
                    Label = "Move",
                    Undo = async () => { await Task.Run(() => { foreach (var p in pairs) { try { MoveBack(p.to, p.from); } catch { } } }); Notify(dirs.ToArray()); },
                    Redo = async () => { await Task.Run(() => { foreach (var p in pairs) { try { MoveBack(p.from, p.to); } catch { } } }); Notify(dirs.ToArray()); },
                });
            else
                Push(new UndoOp
                {
                    Label = effect == DragDropEffects.Link ? "Make Alias" : "Copy",
                    Undo = async () => { await Task.Run(() => { foreach (var p in pairs) Native.Recycle(new[] { p.to }); }); Notify(dirs.ToArray()); },
                    Redo = async () => { await Task.Run(() => { foreach (var p in pairs) { try { if (effect == DragDropEffects.Link) Native.CreateLink(p.to, p.from); else CopyOne(p.from, destDir); } catch { } } }); Notify(dirs.ToArray()); },
                });
            Notify(dirs.ToArray());
        }

        static void MoveBack(string from, string to)
        {
            if (!FileSystem.Exists(from)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
            Settings.Current.MovePathMetadata(from, to);
        }

        // ---------- duplicate ----------
        public static async Task Duplicate(IEnumerable<string> paths)
        {
            var created = new List<string>(); var dirs = new HashSet<string>();
            await Task.Run(() =>
            {
                foreach (var p in paths)
                {
                    try { var d = Dir(p); created.Add(CopyOne(p, d)); dirs.Add(d); } catch { }
                }
            });
            if (created.Count == 0) return;
            Push(new UndoOp { Label = "Duplicate", Undo = async () => { Native.Recycle(created.ToArray()); Notify(dirs.ToArray()); await Task.CompletedTask; }, Redo = async () => { await Task.CompletedTask; } });
            Notify(dirs.ToArray());
            LastCreated = created;
        }

        public static List<string> LastCreated;

        // ---------- trash ----------
        public static async Task MoveToTrash(IEnumerable<string> paths)
        {
            var list = paths.ToList();
            var dirs = list.Select(Dir).Distinct().ToArray();
            bool ok = await Task.Run(() => Native.Recycle(list.ToArray()));
            if (!ok) return;
            var originals = list.ToList();
            Push(new UndoOp
            {
                Label = "Move to Trash",
                Undo = async () => { await FileSystem.RestoreOriginals(originals); Notify(dirs); },
                Redo = async () => { await Task.Run(() => Native.Recycle(originals.ToArray())); Notify(dirs); },
            });
            Notify(dirs);
        }

        public static async Task DeleteImmediately(IEnumerable<string> paths)
        {
            var list = paths.ToList();
            await Task.Run(() =>
            {
                foreach (var p in list)
                {
                    try { if (Directory.Exists(p)) Directory.Delete(p, true); else File.Delete(p); } catch { }
                }
            });
            Notify(list.Select(Dir).Distinct().ToArray());
        }

        // ---------- rename ----------
        public static string Rename(string path, string newName)
        {
            var dir = Dir(path);
            var dest = Path.Combine(dir, newName);
            if (string.Equals(path, dest, StringComparison.Ordinal)) return path;
            bool caseOnly = string.Equals(path, dest, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && FileSystem.Exists(dest)) throw new IOException("exists");
            if (Directory.Exists(path))
            {
                if (caseOnly) { var tmp = path + ".tmp_" + Guid.NewGuid().ToString("N").Substring(0, 6); Directory.Move(path, tmp); Directory.Move(tmp, dest); }
                else Directory.Move(path, dest);
            }
            else
            {
                if (caseOnly) { var tmp = path + ".tmp_" + Guid.NewGuid().ToString("N").Substring(0, 6); File.Move(path, tmp); File.Move(tmp, dest); }
                else File.Move(path, dest);
            }
            Settings.Current.MovePathMetadata(path, dest);
            Thumbs.Invalidate(path);
            var from = path;
            Push(new UndoOp
            {
                Label = "Rename",
                Undo = async () => { await Task.Run(() => { try { MoveBack(dest, from); } catch { } }); Notify(dir); },
                Redo = async () => { await Task.Run(() => { try { MoveBack(from, dest); } catch { } }); Notify(dir); },
            });
            Notify(dir);
            return dest;
        }

        // ---------- new items ----------
        public static string NewFolder(string dir, IEnumerable<string> moveInto = null)
        {
            var p = FileSystem.UniqueName(dir, moveInto == null ? "untitled folder" : "New Folder with Items", "");
            Directory.CreateDirectory(p);
            if (moveInto != null) foreach (var s in moveInto) { try { MoveOne(s, p); } catch { } }
            Push(new UndoOp { Label = "New Folder", Undo = async () => { try { Directory.Delete(p, false); } catch { } Notify(dir); await Task.CompletedTask; }, Redo = async () => { try { Directory.CreateDirectory(p); } catch { } Notify(dir); await Task.CompletedTask; } });
            Notify(dir);
            return p;
        }

        public static string NewFile(string dir, string name = "untitled", string ext = ".txt")
        {
            var p = FileSystem.UniqueName(dir, name, ext);
            File.WriteAllBytes(p, new byte[0]);
            Push(new UndoOp { Label = "New File", Undo = async () => { Native.Recycle(new[] { p }); Notify(dir); await Task.CompletedTask; }, Redo = async () => { try { File.WriteAllBytes(p, new byte[0]); } catch { } Notify(dir); await Task.CompletedTask; } });
            Notify(dir);
            return p;
        }

        public static string MakeAlias(string target)
        {
            var dir = Dir(target);
            var name = Path.GetFileNameWithoutExtension(target.TrimEnd('\\'));
            var lnk = FileSystem.UniqueName(dir, Path.GetFileName(target.TrimEnd('\\')) + " alias", ".lnk");
            Native.CreateLink(lnk, target);
            Push(new UndoOp { Label = "Make Alias", Undo = async () => { Native.Recycle(new[] { lnk }); Notify(dir); await Task.CompletedTask; }, Redo = async () => { try { Native.CreateLink(lnk, target); } catch { } Notify(dir); await Task.CompletedTask; } });
            Notify(dir);
            return lnk;
        }

        // ---------- compress / extract ----------
        public static async Task<string> Compress(IList<string> paths)
        {
            if (paths.Count == 0) return null;
            var dir = Dir(paths[0]);
            string baseName = paths.Count == 1 ? Path.GetFileNameWithoutExtension(paths[0].TrimEnd('\\')) : "Archive";
            if (paths.Count == 1 && Directory.Exists(paths[0])) baseName = Path.GetFileName(paths[0].TrimEnd('\\'));
            var zip = FileSystem.UniqueName(dir, baseName, ".zip");
            await Task.Run(() =>
            {
                using var fs = new FileStream(zip, FileMode.Create);
                using var za = new ZipArchive(fs, ZipArchiveMode.Create);
                foreach (var p in paths)
                {
                    var name = Path.GetFileName(p.TrimEnd('\\'));
                    if (Directory.Exists(p)) AddDir(za, p, name);
                    else za.CreateEntryFromFile(p, name, CompressionLevel.Optimal);
                }
            });
            Push(new UndoOp { Label = "Compress", Undo = async () => { Native.Recycle(new[] { zip }); Notify(dir); await Task.CompletedTask; }, Redo = async () => { await Task.CompletedTask; } });
            Notify(dir);
            return zip;
        }

        static void AddDir(ZipArchive za, string dir, string entry)
        {
            bool any = false;
            foreach (var f in Directory.EnumerateFiles(dir)) { try { za.CreateEntryFromFile(f, entry + "/" + Path.GetFileName(f), CompressionLevel.Optimal); any = true; } catch { } }
            foreach (var d in Directory.EnumerateDirectories(dir)) { AddDir(za, d, entry + "/" + Path.GetFileName(d)); any = true; }
            if (!any) za.CreateEntry(entry + "/");
        }

        public static async Task<string> Extract(string zip)
        {
            var dir = Dir(zip);
            var dest = FileSystem.UniqueName(dir, Path.GetFileNameWithoutExtension(zip), "");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, dest));
            Notify(dir);
            return dest;
        }

        public static bool IsArchive(FileItem i) => !i.IsDir && i.Ext == "zip";
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace Finderly.Core
{
    public enum ViewMode { Icon, List, Column, Gallery }
    public enum SortKey { Name, Kind, DateModified, DateCreated, Size, Tags, DateLastOpened }
    public enum GroupKey { None, Name, Kind, DateModified, DateCreated, Size, Tags, DateLastOpened }
    public enum SpecialKind { None, Drive, AppShortcut, TrashItem }
    public enum LocKind { Folder, Recents, Applications, Computer, Tag, Search, Trash }

    public sealed class FileItem
    {
        public string Path;
        public string Name;
        public bool IsDir;
        public long Size;
        public DateTime Modified, Created, Accessed;
        public FileAttributes Attr;
        public SpecialKind Special;
        public string Ext;              // lower-case, no dot
        public string TrashOrigin;      // for trash items
        public DateTime TrashDeleted;
        public long DirSize = -1;       // computed lazily
        public int ChildCount = -1;     // computed lazily
        string _kind;

        public bool IsHidden => (Attr & FileAttributes.Hidden) != 0 || (Name.Length > 0 && Name[0] == '.');
        public bool IsLink => Ext == "lnk" || Ext == "url";
        public bool IsNavigable => IsDir;
        public bool IsReal => Special != SpecialKind.TrashItem;

        public string Kind
        {
            get
            {
                if (_kind == null) _kind = Format.KindOf(this);
                return _kind;
            }
        }

        public string DisplayName
        {
            get
            {
                if (Special == SpecialKind.AppShortcut || IsLink) return System.IO.Path.GetFileNameWithoutExtension(Name);
                if (!IsDir && !Settings.Current.ShowAllExtensions && Ext.Length > 0 && Format.IsKnownExt(Ext))
                    return System.IO.Path.GetFileNameWithoutExtension(Name);
                return Name;
            }
        }

        public static FileItem From(FileSystemInfo fi)
        {
            var it = new FileItem
            {
                Path = fi.FullName,
                Name = fi.Name,
                Attr = fi.Attributes,
                Modified = fi.LastWriteTime,
                Created = fi.CreationTime,
                Accessed = fi.LastAccessTime,
            };
            if (fi is DirectoryInfo)
            {
                it.IsDir = true; it.Ext = "";
            }
            else
            {
                it.Size = ((FileInfo)fi).Length;
                var e = fi.Extension;
                it.Ext = e.Length > 1 ? e.Substring(1).ToLowerInvariant() : "";
            }
            return it;
        }

        public static FileItem FromPath(string p)
        {
            try
            {
                if (Directory.Exists(p))
                {
                    var di = new DirectoryInfo(p);
                    if (di.Parent == null)
                        return Drive(new DriveInfo(p));
                    return From(di);
                }
                if (File.Exists(p)) return From(new FileInfo(p));
            }
            catch { }
            return null;
        }

        public static FileItem Drive(DriveInfo d)
        {
            string label = "";
            long size = 0;
            try { if (d.IsReady) { label = d.VolumeLabel; size = d.TotalSize; } } catch { }
            if (string.IsNullOrEmpty(label))
                label = d.DriveType == DriveType.Removable ? "USB Drive" : d.DriveType == DriveType.Network ? "Network Drive" : d.DriveType == DriveType.CDRom ? "CD Drive" : "Local Disk";
            return new FileItem
            {
                Path = d.RootDirectory.FullName,
                Name = label + " (" + d.Name.TrimEnd('\\') + ")",
                IsDir = true,
                Ext = "",
                Size = size,
                Special = SpecialKind.Drive,
                Modified = DateTime.MinValue,
            };
        }

        public override bool Equals(object obj) => obj is FileItem o && string.Equals(o.Path, Path, StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Path);
        public override string ToString() => Path;
    }

    public sealed class Location
    {
        public LocKind Kind;
        public string Path;     // folder path / search root
        public string Tag;      // tag name
        public string Query;    // search text
        public bool SearchWholePC;

        public static Location Folder(string p) => new Location { Kind = LocKind.Folder, Path = p };
        public static readonly Location Recents = new Location { Kind = LocKind.Recents };
        public static readonly Location Applications = new Location { Kind = LocKind.Applications };
        public static readonly Location Computer = new Location { Kind = LocKind.Computer };
        public static readonly Location Trash = new Location { Kind = LocKind.Trash };
        public static Location ForTag(string t) => new Location { Kind = LocKind.Tag, Tag = t };

        public bool IsFolder => Kind == LocKind.Folder;

        public string Title
        {
            get
            {
                switch (Kind)
                {
                    case LocKind.Folder: return Format.FolderTitle(Path);
                    case LocKind.Recents: return "Recents";
                    case LocKind.Applications: return "Applications";
                    case LocKind.Computer: return Environment.MachineName;
                    case LocKind.Trash: return "Trash";
                    case LocKind.Tag: return Tag;
                    case LocKind.Search: return "Searching “" + (SearchWholePC ? "This PC" : Format.FolderTitle(Path)) + "”";
                }
                return "";
            }
        }

        public string Key => Kind + "|" + (Path ?? "").ToLowerInvariant() + "|" + Tag + "|" + Query + "|" + SearchWholePC;
        public override bool Equals(object obj) => obj is Location l && l.Key == Key;
        public override int GetHashCode() => Key.GetHashCode();
    }
}

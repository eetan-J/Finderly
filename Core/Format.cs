using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Finderly.Core
{
    public static class Format
    {
        // ---------- sizes (decimal units like macOS) ----------
        public static string Size(long b)
        {
            if (b < 0) return "--";
            if (b == 0) return "Zero bytes";
            if (b < 1000) return b + " bytes";
            if (b < 1000 * 1000) return Math.Round(b / 1000.0) + " KB";
            if (b < 1000L * 1000 * 1000) return (b / 1e6).ToString("0.#", CultureInfo.CurrentCulture) + " MB";
            if (b < 1000L * 1000 * 1000 * 1000) return (b / 1e9).ToString("0.##", CultureInfo.CurrentCulture) + " GB";
            return (b / 1e12).ToString("0.##", CultureInfo.CurrentCulture) + " TB";
        }

        public static string Bytes(long b) => b.ToString("N0", CultureInfo.CurrentCulture) + " bytes";

        // ---------- dates ----------
        static bool DayFirst => CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern.TrimStart().StartsWith("d", StringComparison.OrdinalIgnoreCase);
        static string Time(DateTime d) => d.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern, CultureInfo.CurrentCulture);
        public static string LongDate(DateTime d) => DayFirst ? d.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) : d.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
        public static string MedDate(DateTime d) => DayFirst ? d.ToString("d MMM yyyy", CultureInfo.CurrentCulture) : d.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);

        public static string Date(DateTime d, bool relative = true)
        {
            if (d == DateTime.MinValue || d.Year < 1980) return "--";
            var today = DateTime.Today;
            if (relative)
            {
                if (d.Date == today) return "Today at " + Time(d);
                if (d.Date == today.AddDays(-1)) return "Yesterday at " + Time(d);
            }
            return MedDate(d) + " at " + Time(d);
        }

        public static string FullDate(DateTime d)
        {
            if (d == DateTime.MinValue || d.Year < 1980) return "--";
            return d.ToString("dddd", CultureInfo.CurrentCulture) + ", " + LongDate(d) + " at " + Time(d);
        }

        // ---------- titles ----------
        public static string FolderTitle(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var t = path.TrimEnd('\\');
            if (t.Length <= 2) // drive root like "C:"
            {
                try { return FileItem.Drive(new DriveInfo(path)).Name; } catch { return path; }
            }
            var n = System.IO.Path.GetFileName(t);
            return string.IsNullOrEmpty(n) ? path : n;
        }

        // ---------- kinds ----------
        static readonly Dictionary<string, string> Kinds = new Dictionary<string, string>
        {
            ["png"] = "PNG image", ["jpg"] = "JPEG image", ["jpeg"] = "JPEG image", ["gif"] = "GIF image", ["bmp"] = "Windows bitmap image",
            ["tif"] = "TIFF image", ["tiff"] = "TIFF image", ["heic"] = "HEIF image", ["heif"] = "HEIF image", ["webp"] = "WebP image",
            ["ico"] = "Windows icon image", ["svg"] = "SVG document", ["psd"] = "Adobe Photoshop document", ["raw"] = "Raw image", ["cr2"] = "Canon raw image",
            ["nef"] = "Nikon raw image", ["arw"] = "Sony raw image", ["dng"] = "Digital negative image", ["raf"] = "Fujifilm raw image",
            ["pdf"] = "PDF document", ["txt"] = "Plain Text Document", ["rtf"] = "Rich Text Document", ["md"] = "Markdown Document",
            ["html"] = "HTML text", ["htm"] = "HTML text", ["css"] = "CSS stylesheet", ["js"] = "JavaScript script", ["ts"] = "TypeScript source",
            ["json"] = "JSON document", ["xml"] = "XML text", ["csv"] = "comma-separated values", ["log"] = "Log File", ["ini"] = "Configuration settings",
            ["yml"] = "YAML document", ["yaml"] = "YAML document", ["zip"] = "ZIP archive", ["rar"] = "RAR archive", ["7z"] = "7-Zip archive",
            ["tar"] = "tar archive", ["gz"] = "gzip compressed archive", ["mp3"] = "MP3 audio", ["wav"] = "Waveform audio", ["m4a"] = "Apple MPEG-4 audio",
            ["aac"] = "AAC audio", ["flac"] = "FLAC audio", ["ogg"] = "Ogg audio", ["wma"] = "Windows Media audio", ["mp4"] = "MPEG-4 movie",
            ["m4v"] = "MPEG-4 movie", ["mov"] = "QuickTime movie", ["avi"] = "AVI movie", ["mkv"] = "Matroska video", ["wmv"] = "Windows Media video",
            ["webm"] = "WebM video", ["doc"] = "Microsoft Word 97 document", ["docx"] = "Microsoft Word document", ["xls"] = "Microsoft Excel 97 workbook",
            ["xlsx"] = "Microsoft Excel workbook", ["ppt"] = "Microsoft PowerPoint 97 presentation", ["pptx"] = "Microsoft PowerPoint presentation",
            ["pages"] = "Pages document", ["numbers"] = "Numbers spreadsheet", ["key"] = "Keynote presentation",
            ["exe"] = "Application", ["msi"] = "Windows Installer package", ["lnk"] = "Alias", ["url"] = "Web site location", ["dll"] = "Dynamic library",
            ["iso"] = "Disk Image", ["dmg"] = "Disk Image", ["img"] = "Disk Image", ["py"] = "Python script", ["cs"] = "C# source", ["c"] = "C source",
            ["cpp"] = "C++ source", ["h"] = "C header source", ["java"] = "Java source", ["rs"] = "Rust source", ["go"] = "Go source", ["sh"] = "Shell script",
            ["ps1"] = "PowerShell script", ["bat"] = "Windows batch file", ["cmd"] = "Windows command script", ["ttf"] = "TrueType font", ["otf"] = "OpenType font",
            ["woff"] = "Web font", ["woff2"] = "Web font", ["epub"] = "EPUB book", ["sketch"] = "Sketch document", ["fig"] = "Figma document",
            ["ai"] = "Adobe Illustrator document", ["eps"] = "EPS document", ["sql"] = "SQL source", ["db"] = "Database", ["sqlite"] = "SQLite database",
            ["torrent"] = "BitTorrent document", ["apk"] = "Android package", ["jar"] = "Java JAR file", ["swift"] = "Swift source", ["kt"] = "Kotlin source",
            ["php"] = "PHP script", ["rb"] = "Ruby script", ["vue"] = "Vue component", ["tsx"] = "TypeScript JSX source", ["jsx"] = "JavaScript JSX source",
            ["toml"] = "TOML document", ["cfg"] = "Configuration settings", ["sln"] = "Visual Studio solution", ["csproj"] = "C# project",
        };

        static readonly ConcurrentDictionary<string, string> ShellKinds = new ConcurrentDictionary<string, string>();

        public static bool IsKnownExt(string ext) => Kinds.ContainsKey(ext);

        public static string KindOf(FileItem it)
        {
            if (it.Special == SpecialKind.Drive) return "Volume";
            if (it.Special == SpecialKind.AppShortcut) return "Application";
            if (it.IsDir) return "Folder";
            if (string.IsNullOrEmpty(it.Ext)) return "Document";
            if (Kinds.TryGetValue(it.Ext, out var k)) return k;
            return ShellKinds.GetOrAdd(it.Ext, e =>
            {
                try { var n = Native.ShellTypeName(e); if (!string.IsNullOrWhiteSpace(n)) return n; } catch { }
                return e.ToUpperInvariant() + " document";
            });
        }

        public static readonly HashSet<string> ImageExts = new HashSet<string> { "png", "jpg", "jpeg", "gif", "bmp", "tif", "tiff", "heic", "heif", "webp", "ico", "jfif", "dng", "cr2", "nef", "arw", "raf" };
        public static readonly HashSet<string> TextExts = new HashSet<string> { "txt", "md", "json", "xml", "csv", "log", "ini", "yml", "yaml", "html", "htm", "css", "js", "ts", "tsx", "jsx", "py", "cs", "c", "cpp", "h", "hpp", "java", "rs", "go", "sh", "ps1", "bat", "cmd", "sql", "rb", "php", "swift", "kt", "toml", "cfg", "conf", "gitignore", "editorconfig", "vue", "svg", "csproj", "sln", "props", "targets", "lua", "r", "m", "pl", "env", "tex", "srt", "vtt" };
        public static readonly HashSet<string> MediaExts = new HashSet<string> { "mp3", "wav", "m4a", "aac", "wma", "flac", "mp4", "m4v", "mov", "avi", "wmv", "mkv", "webm", "3gp" };
        public static readonly HashSet<string> AudioExts = new HashSet<string> { "mp3", "wav", "m4a", "aac", "wma", "flac", "ogg" };
        public static readonly HashSet<string> AppLikeExts = new HashSet<string> { "exe", "lnk", "url", "msi", "ico", "appref-ms", "scr", "cpl" };

        public static bool IsImage(FileItem i) => !i.IsDir && ImageExts.Contains(i.Ext);
        public static bool IsPdf(FileItem i) => !i.IsDir && i.Ext == "pdf";
        public static bool IsPreviewable(FileItem i) => IsImage(i) || IsPdf(i);

        // ---------- sorting ----------
        public static int NameCmp(string a, string b) => Native.StrCmpLogicalW(a, b);

        public static Comparison<FileItem> Comparer(SortKey key, bool asc, bool foldersFirst, Func<FileItem, DateTime> lastOpened = null)
        {
            Comparison<FileItem> k;
            switch (key)
            {
                case SortKey.Kind: k = (a, b) => string.Compare(a.Kind, b.Kind, StringComparison.CurrentCultureIgnoreCase); break;
                case SortKey.DateModified: k = (a, b) => b.Modified.CompareTo(a.Modified); break;
                case SortKey.DateCreated: k = (a, b) => b.Created.CompareTo(a.Created); break;
                case SortKey.DateLastOpened: k = (a, b) => (lastOpened?.Invoke(b) ?? b.Accessed).CompareTo(lastOpened?.Invoke(a) ?? a.Accessed); break;
                case SortKey.Size: k = (a, b) => SizeOf(b).CompareTo(SizeOf(a)); break;
                case SortKey.Tags: k = (a, b) => TagRank(a).CompareTo(TagRank(b)); break;
                default: k = (a, b) => 0; break;
            }
            return (a, b) =>
            {
                if (foldersFirst && key == SortKey.Name && a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
                int r = k(a, b);
                if (r == 0) r = NameCmp(a.Name, b.Name);
                return asc ? r : -r;
            };
        }

        static long SizeOf(FileItem i) => i.IsDir ? (i.DirSize >= 0 ? i.DirSize : -1) : i.Size;

        static int TagRank(FileItem i)
        {
            var t = Settings.Current.TagsOf(i.Path);
            if (t == null || t.Count == 0) return int.MaxValue;
            var idx = Settings.Current.Tags.FindIndex(d => d.Name == t[0]);
            return idx < 0 ? 1000 : idx;
        }

        // ---------- grouping ----------
        public static (string key, int order) GroupOf(FileItem i, GroupKey g, Func<FileItem, DateTime> lastOpened = null)
        {
            switch (g)
            {
                case GroupKey.Name:
                    var c = i.DisplayName.Length > 0 ? char.ToUpperInvariant(i.DisplayName[0]) : '#';
                    if (!char.IsLetter(c)) return ("#", 0);
                    return (c.ToString(), c);
                case GroupKey.Kind:
                    return (i.Kind, 0);
                case GroupKey.DateModified: return DateGroup(i.Modified);
                case GroupKey.DateCreated: return DateGroup(i.Created);
                case GroupKey.DateLastOpened: return DateGroup(lastOpened?.Invoke(i) ?? i.Accessed);
                case GroupKey.Size:
                    if (i.IsDir) return ("Folders", 0);
                    var s = i.Size;
                    if (s > 1e9) return ("More than 1 GB", 1);
                    if (s > 100e6) return ("100 MB – 1 GB", 2);
                    if (s > 1e6) return ("1 MB – 100 MB", 3);
                    if (s > 100e3) return ("100 KB – 1 MB", 4);
                    if (s > 10e3) return ("10 KB – 100 KB", 5);
                    return ("Zero KB – 10 KB", 6);
                case GroupKey.Tags:
                    var t = Settings.Current.TagsOf(i.Path);
                    if (t == null || t.Count == 0) return ("No Tags", 10000);
                    var idx = Settings.Current.Tags.FindIndex(d => d.Name == t[0]);
                    return (t[0], idx < 0 ? 999 : idx);
            }
            return ("", 0);
        }

        static (string, int) DateGroup(DateTime d)
        {
            var today = DateTime.Today;
            if (d.Date >= today) return ("Today", 0);
            if (d.Date >= today.AddDays(-1)) return ("Yesterday", 1);
            if (d.Date >= today.AddDays(-7)) return ("Previous 7 Days", 2);
            if (d.Date >= today.AddDays(-30)) return ("Previous 30 Days", 3);
            if (d.Year == today.Year) return (d.ToString("MMMM", CultureInfo.CurrentCulture), 100 - d.Month);
            if (d.Year < 1980) return ("Unknown", 99999);
            return (d.Year.ToString(), 10000 - d.Year);
        }
    }
}

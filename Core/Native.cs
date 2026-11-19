using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Finderly.Core
{
    internal static class Native
    {
        // ---------- DWM ----------
        [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int L, R, T, B; }
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        // ---------- user32 ----------
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct NCCALCSIZE_PARAMS { public RECT r0, r1, r2; public IntPtr lppos; }
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int v);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetSystemMetricsForDpi(int i, uint dpi);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);

        // ---------- shell: natural sort ----------
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] public static extern int StrCmpLogicalW(string a, string b);

        // ---------- shell: thumbnails ----------
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);

        public const int SIIGBF_RESIZETOFIT = 0, SIIGBF_BIGGERSIZEOK = 1, SIIGBF_ICONONLY = 4, SIIGBF_THUMBNAILONLY = 8;

        [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)] struct DIBSECTION { public BITMAP dsBm; public BITMAPINFOHEADER dsBmih; public uint f0, f1, f2; public IntPtr dshSection; public uint dsOffset; }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr hbm, uint start, uint lines, IntPtr bits, ref BITMAPINFOHEADER bi, uint usage);
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, out DIBSECTION ds);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);

        static Guid IID_ImageFactory = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        /// <summary>Returns a frozen BitmapSource or null.</summary>
        public static BitmapSource ShellImage(string path, int px, int flags)
        {
            IShellItemImageFactory f = null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref IID_ImageFactory, out f) != 0 || f == null) return null;
                if (f.GetImage(new SIZE { cx = px, cy = px }, flags, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                return HBitmapToSource(hbm);
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                if (f != null) Marshal.ReleaseComObject(f);
            }
        }

        static unsafe BitmapSource HBitmapToSource(IntPtr hbm)
        {
            if (GetObject(hbm, Marshal.SizeOf<DIBSECTION>(), out DIBSECTION ds) == 0) return null;
            int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight;
            if (w <= 0 || h <= 0 || ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32)
            {
                var s = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                s.Freeze(); return s;
            }
            int stride = w * 4;
            var buf = new byte[stride * h];
            // GetDIBits with a negative height always yields top-down rows, whatever the source orientation
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr dc = GetDC(IntPtr.Zero);
            int got;
            fixed (byte* pb = buf) got = GetDIBits(dc, hbm, 0, (uint)h, (IntPtr)pb, ref bi, 0);
            ReleaseDC(IntPtr.Zero, dc);
            if (got == 0) { var s2 = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); s2.Freeze(); return s2; }
            bool anyAlpha = false;
            for (int i = 3; i < buf.Length; i += 4) if (buf[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, buf, stride);
            bmp.Freeze();
            return bmp;
        }

        // ---------- shell: file info ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO psfi, uint cb, uint flags);

        public static string ShellTypeName(string ext)
        {
            var info = new SHFILEINFO();
            SHGetFileInfo("x." + ext, 0x80, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x400 | 0x10);
            return info.szTypeName;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int AssocQueryString(int flags, int str, string assoc, string extra, StringBuilder outStr, ref int size);
        public static string DefaultAppName(string ext)
        {
            try
            {
                int size = 512; var sb = new StringBuilder(size);
                if (AssocQueryString(0x40, 4, "." + ext, null, sb, ref size) == 0) return sb.ToString();
            }
            catch { }
            return null;
        }

        // ---------- shell: execute ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHELLEXECUTEINFO
        {
            public int cbSize; public uint fMask; public IntPtr hwnd;
            public string lpVerb, lpFile, lpParameters, lpDirectory;
            public int nShow; public IntPtr hInstApp, lpIDList; public string lpClass;
            public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon, hProcess;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO i);

        public static bool ShellVerb(string path, string verb, IntPtr hwnd = default)
        {
            var i = new SHELLEXECUTEINFO { cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(), fMask = 0x0000000C /*INVOKEIDLIST*/, hwnd = hwnd, lpVerb = verb, lpFile = path, nShow = 1 };
            return ShellExecuteEx(ref i);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern void SHAddToRecentDocs(uint flags, string path);

        // ---------- recycle bin ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags;
            public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        public static bool Recycle(string[] paths)
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = 3, // FO_DELETE
                pFrom = string.Join("\0", paths) + "\0\0",
                fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400 // ALLOWUNDO | NOCONFIRMATION | SILENT | NOERRORUI
            };
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        public static long RecycleBinCount()
        {
            var i = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            return SHQueryRecycleBin(null, ref i) == 0 ? i.i64NumItems : 0;
        }

        // ---------- shell links (aliases) ----------
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink { }

        public static string ResolveLink(string lnk)
        {
            object o = null;
            try
            {
                o = new ShellLink();
                ((IPersistFile)o).Load(lnk, 0);
                var sb = new StringBuilder(1024);
                ((IShellLinkW)o).GetPath(sb, sb.Capacity, IntPtr.Zero, 0x4 /*SLGP_RAWPATH*/);
                var p = Environment.ExpandEnvironmentVariables(sb.ToString());
                return string.IsNullOrEmpty(p) ? null : p;
            }
            catch { return null; }
            finally { if (o != null) Marshal.ReleaseComObject(o); }
        }

        public static void CreateLink(string lnk, string target)
        {
            object o = new ShellLink();
            try
            {
                var l = (IShellLinkW)o;
                l.SetPath(target);
                l.SetWorkingDirectory(Path.GetDirectoryName(target) ?? "");
                ((IPersistFile)o).Save(lnk, true);
            }
            finally { Marshal.ReleaseComObject(o); }
        }

        // ---------- helpers ----------
        /// <summary>Run work on a dedicated STA thread (needed for Shell COM objects).</summary>
        public static Task<T> Sta<T>(Func<T> fn)
        {
            var tcs = new TaskCompletionSource<T>();
            var t = new Thread(() =>
            {
                try { tcs.SetResult(fn()); } catch (Exception ex) { tcs.SetException(ex); }
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            return tcs.Task;
        }
    }
}

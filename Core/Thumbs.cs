using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Finderly.Core
{
    public sealed class Thumb
    {
        public ImageSource Image;
        public bool IsThumbnail;   // true = real content preview, false = file-type icon
    }

    /// <summary>
    /// Asynchronous, prioritised shell thumbnail cache. Most-recently requested items are
    /// loaded first so whatever is on screen appears immediately while scrolling.
    /// </summary>
    public static class Thumbs
    {
        readonly struct Key : IEquatable<Key>
        {
            public readonly string Path; public readonly int Px;
            public Key(string p, int px) { Path = p.ToLowerInvariant(); Px = px; }
            public bool Equals(Key o) => Px == o.Px && Path == o.Path;
            public override bool Equals(object o) => o is Key k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(Path, Px);
        }

        sealed class Req { public Key Key; public string RealPath; public bool IconOnly; public List<Action> Callbacks = new List<Action>(1); }

        static readonly Dictionary<Key, Thumb> cache = new Dictionary<Key, Thumb>();
        static readonly LinkedList<Key> lru = new LinkedList<Key>();
        static readonly Dictionary<Key, LinkedListNode<Key>> lruNodes = new Dictionary<Key, LinkedListNode<Key>>();
        static readonly LinkedList<Req> queue = new LinkedList<Req>();
        static readonly Dictionary<Key, LinkedListNode<Req>> pending = new Dictionary<Key, LinkedListNode<Req>>();
        static readonly object lk = new object();
        static readonly SemaphoreSlim signal = new SemaphoreSlim(0);
        static bool started;
        const int MaxEntries = 4000;
        static Dispatcher ui;

        /// <summary>Recently decoded full-size previews (Quick Look); bounded so memory stays small.</summary>
        public sealed class PreviewStore
        {
            readonly System.Collections.Generic.Dictionary<string, (BitmapSource img, int w, int h)> d = new System.Collections.Generic.Dictionary<string, (BitmapSource, int, int)>(StringComparer.OrdinalIgnoreCase);
            readonly System.Collections.Generic.Queue<string> order = new System.Collections.Generic.Queue<string>();
            public bool TryGetValue(string k, out (BitmapSource img, int w, int h) v) { lock (d) return d.TryGetValue(k, out v); }
            public bool ContainsKey(string k) { lock (d) return d.ContainsKey(k); }
            public (BitmapSource img, int w, int h) this[string k]
            {
                set { lock (d) { if (!d.ContainsKey(k)) order.Enqueue(k); d[k] = value; while (order.Count > 10) d.Remove(order.Dequeue()); } }
            }
        }
        public static readonly PreviewStore PreviewCache = new PreviewStore();

        public static int Bucket(double devicePx)
        {
            if (devicePx <= 16) return 16;
            if (devicePx <= 24) return 24;
            if (devicePx <= 32) return 32;
            if (devicePx <= 48) return 48;
            if (devicePx <= 64) return 64;
            if (devicePx <= 96) return 96;
            if (devicePx <= 128) return 128;
            if (devicePx <= 256) return 256;
            if (devicePx <= 512) return 512;
            return 1024;
        }

        static void Start()
        {
            ui = Application.Current.Dispatcher;
            started = true;
            int n = Math.Max(2, Math.Min(4, Environment.ProcessorCount / 2));
            for (int i = 0; i < n; i++)
            {
                var t = new Thread(Worker) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "thumbs" + i };
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
        }

        public static Thumb Peek(string path, int px)
        {
            lock (lk) { return cache.TryGetValue(new Key(path, px), out var t) ? t : null; }
        }

        /// <summary>Gets a cached thumbnail or queues a load; onReady fires on the UI thread.</summary>
        public static Thumb Get(string path, int px, Action onReady, bool iconOnly = false)
        {
            if (!started) Start();
            var key = new Key(path, iconOnly ? -px : px);
            lock (lk)
            {
                if (cache.TryGetValue(key, out var t))
                {
                    if (lruNodes.TryGetValue(key, out var node)) { lru.Remove(node); lru.AddFirst(node); }
                    return t;
                }
                // fall back to any other cached size while the right one loads
                Thumb fallback = null;
                foreach (var alt in new[] { 1024, 512, 256, 128, 96, 64, 48, 32, 24, 16 })
                {
                    if (alt == px) continue;
                    if (!cache.TryGetValue(new Key(path, iconOnly ? -alt : alt), out var a) || a.Image == null) continue;
                    if (alt > px) fallback = a;
                    else { if (fallback == null) fallback = a; break; }
                }
                if (pending.TryGetValue(key, out var pn))
                {
                    if (pn.List == queue) { queue.Remove(pn); queue.AddFirst(pn); }
                    if (onReady != null && !pn.Value.Callbacks.Contains(onReady)) pn.Value.Callbacks.Add(onReady);
                }
                else
                {
                    var r = new Req { Key = key, RealPath = path, IconOnly = iconOnly };
                    if (onReady != null) r.Callbacks.Add(onReady);
                    pending[key] = queue.AddFirst(r);
                    while (queue.Count > 600)
                    {
                        var last = queue.Last; queue.RemoveLast(); pending.Remove(last.Value.Key);
                    }
                    signal.Release();
                }
                return fallback;
            }
        }

        public static void Invalidate(string path)
        {
            var p = path.ToLowerInvariant();
            lock (lk)
            {
                var dead = new List<Key>();
                foreach (var k in cache.Keys) if (k.Path == p) dead.Add(k);
                foreach (var k in dead) { cache.Remove(k); if (lruNodes.TryGetValue(k, out var n)) { lru.Remove(n); lruNodes.Remove(k); } }
            }
        }

        static void Worker()
        {
            while (true)
            {
                signal.Wait();
                Req r = null;
                lock (lk)
                {
                    if (queue.First != null) { r = queue.First.Value; queue.RemoveFirst(); }
                }
                if (r == null) continue;
                Thumb t = Load(r.RealPath, Math.Abs(r.Key.Px), r.IconOnly);
                List<Action> cbs;
                lock (lk)
                {
                    pending.Remove(r.Key);
                    cache[r.Key] = t;
                    lruNodes[r.Key] = lru.AddFirst(r.Key);
                    while (cache.Count > MaxEntries)
                    {
                        var old = lru.Last.Value; lru.RemoveLast(); lruNodes.Remove(old); cache.Remove(old);
                    }
                    cbs = r.Callbacks;
                }
                if (cbs.Count > 0)
                    ui.BeginInvoke(DispatcherPriority.Background, new Action(() => { foreach (var c in cbs) c(); }));
            }
        }

        static Thumb Load(string path, int px, bool iconOnly)
        {
            BitmapSource img = null;
            bool isThumb = false;
            if (!iconOnly)
            {
                img = Native.ShellImage(path, px, Native.SIIGBF_THUMBNAILONLY | Native.SIIGBF_BIGGERSIZEOK);
                isThumb = img != null;
            }
            if (img == null) img = Native.ShellImage(path, px, Native.SIIGBF_ICONONLY | Native.SIIGBF_BIGGERSIZEOK);
            if (img == null) img = Native.ShellImage(path, px, Native.SIIGBF_RESIZETOFIT);
            return new Thumb { Image = img, IsThumbnail = isThumb && img != null };
        }

        /// <summary>Synchronous full-quality decode for previews (call off the UI thread).</summary>
        public static BitmapSource LoadImage(string path, int maxPx)
        {
            try
            {
                using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
                var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                var frame = dec.Frames[0];
                int w = frame.PixelWidth, h = frame.PixelHeight;
                fs.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.StreamSource = fs;
                if (maxPx > 0 && Math.Max(w, h) > maxPx)
                {
                    if (w >= h) bi.DecodePixelWidth = maxPx; else bi.DecodePixelHeight = maxPx;
                }
                bi.Rotation = OrientationOf(frame);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch
            {
                return Native.ShellImage(path, Math.Max(256, Math.Min(maxPx, 1024)), Native.SIIGBF_RESIZETOFIT);
            }
        }

        static Rotation OrientationOf(BitmapFrame f)
        {
            try
            {
                if (f.Metadata is BitmapMetadata md && md.ContainsQuery("System.Photo.Orientation"))
                {
                    var o = md.GetQuery("System.Photo.Orientation");
                    switch (Convert.ToInt32(o))
                    {
                        case 3: return Rotation.Rotate180;
                        case 6: return Rotation.Rotate90;
                        case 8: return Rotation.Rotate270;
                    }
                }
            }
            catch { }
            return Rotation.Rotate0;
        }

        public static (int w, int h) ImageSize(string path)
        {
            try
            {
                using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                var f = dec.Frames[0];
                var r = OrientationOf(f);
                return r == Rotation.Rotate90 || r == Rotation.Rotate270 ? (f.PixelHeight, f.PixelWidth) : (f.PixelWidth, f.PixelHeight);
            }
            catch { return (0, 0); }
        }
    }
}

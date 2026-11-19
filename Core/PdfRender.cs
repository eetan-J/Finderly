using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Finderly.Core
{
    /// <summary>PDF rendering through the built-in Windows.Data.Pdf engine.</summary>
    public sealed class PdfDoc
    {
        PdfDocument doc;
        public int PageCount => doc == null ? 0 : (int)doc.PageCount;
        public bool Locked { get; private set; }
        public string Path { get; private set; }

        public static async Task<PdfDoc> Open(string path, string password = null)
        {
            var d = new PdfDoc { Path = path };
            try
            {
                var f = await StorageFile.GetFileFromPathAsync(path);
                try { d.doc = password == null ? await PdfDocument.LoadFromFileAsync(f) : await PdfDocument.LoadFromFileAsync(f, password); }
                catch { d.Locked = true; return d; }
                d.Locked = d.doc.IsPasswordProtected && password == null;
            }
            catch { return null; }
            return d;
        }

        public (double w, double h) PageSize(int index)
        {
            using var p = doc.GetPage((uint)index);
            return (p.Size.Width, p.Size.Height);
        }

        /// <summary>Render a page so its width equals widthPx (height keeps the aspect ratio).</summary>
        public async Task<BitmapSource> Render(int index, int widthPx, bool transparent = false)
        {
            if (doc == null || index < 0 || index >= PageCount) return null;
            try
            {
                using var page = doc.GetPage((uint)index);
                double ratio = page.Size.Height / page.Size.Width;
                var opt = new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(16, widthPx), DestinationHeight = (uint)Math.Max(16, widthPx * ratio) };
                if (!transparent) opt.BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                using var ms = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(ms, opt);
                ms.Seek(0);
                var bytes = new byte[ms.Size];
                await ms.ReadAsync(bytes.AsBuffer(), (uint)ms.Size, InputStreamOptions.None);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(bytes);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }
    }
}

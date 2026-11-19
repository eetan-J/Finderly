using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Finderly.Core;
using Finderly.UI;

namespace Finderly
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Run_(e.Args);
        }

        void Run_(string[] args)
        {
            // Software rendering is the default: on some GPU/driver combinations WPF's hardware path
            // presents nothing at all. Pass --gpu (or set FINDERLY_HW=1) to opt in to hardware rendering.
            bool gpu = Environment.GetEnvironmentVariable("FINDERLY_HW") == "1" || args.Any(a => a == "--gpu");
            if (!gpu)
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            Settings.Load();
            DispatcherUnhandledException += (s, e) =>
            {
                e.Handled = true;
                try
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Finderly");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "error.log"), DateTime.Now + "\n" + e.Exception + "\n\n");
                }
                catch { }
                if (MainWindow == null && Windows_Empty()) Shutdown(1);
            };
            Theme.Backdrop = WindowFrame.BackdropSupported;
            Theme.Apply(Theme.SystemIsDark());
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
                Dispatcher.BeginInvoke(new Action(() => { var d = Theme.SystemIsDark(); if (d != Theme.Dark) Theme.Apply(d); }));

            // Optional file or folder argument: folders open in the file browser, images/PDFs in Preview
            string arg = args.FirstOrDefault(a => !a.StartsWith("-"));
            if (arg != null)
            {
                arg = arg.Trim('"');
                if (Directory.Exists(arg)) { new MainWindow(Location.Folder(arg)).Show(); }
                else if (File.Exists(arg) && (Format.ImageExts.Contains(Path.GetExtension(arg).TrimStart('.').ToLowerInvariant()) || arg.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
                {
                    ShutdownMode = ShutdownMode.OnLastWindowClose;
                    var dir = Path.GetDirectoryName(arg);
                    var sibs = Directory.EnumerateFiles(dir).Where(f => Format.ImageExts.Contains(Path.GetExtension(f).TrimStart('.').ToLowerInvariant())).OrderBy(f => f, Comparer_()).ToList();
                    if (arg.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) sibs = new System.Collections.Generic.List<string> { arg };
                    new PreviewWindow(sibs, Math.Max(0, sibs.FindIndex(f => string.Equals(f, arg, StringComparison.OrdinalIgnoreCase)))).Show();
                }
                else new MainWindow(null).Show();
            }
            else new MainWindow(null).Show();

            // warm the thumbnail pipeline and common caches off the UI thread
            ThreadPool.QueueUserWorkItem(_ => { try { Format.KindOf(new FileItem { Ext = "txt", Name = "a.txt", Path = "a.txt" }); } catch { } });
            Exit += (s, ev) => Settings.SaveNow();
        }

        static bool Windows_Empty() => Current.Windows.Count == 0;

        static System.Collections.Generic.IComparer<string> Comparer_() => System.Collections.Generic.Comparer<string>.Create(Format.NameCmp);
    }
}

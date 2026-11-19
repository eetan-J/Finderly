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
using Finderly.Core;

namespace Finderly.UI
{
    /// <summary>Get Info window with collapsible sections (General, More Info, Name &amp; Extension, Comments, Open with, Preview, Sharing &amp; Permissions).</summary>
    public sealed class InfoWindow : Window
    {
        public static readonly List<InfoWindow> Open = new List<InfoWindow>();
        readonly FileItem item;
        readonly TextBlock sizeValue = new TextBlock();
        CancellationTokenSource cts = new CancellationTokenSource();

        public static void Show_(FileItem it, Window owner)
        {
            var ex = Open.FirstOrDefault(w => w.item.Equals(it));
            if (ex != null) { ex.Activate(); return; }
            var w = new InfoWindow(it) { Owner = null };
            w.Left = owner.Left + owner.ActualWidth / 2 + 40 + Open.Count * 24; w.Top = owner.Top + 80 + Open.Count * 24;
            w.Show();
        }

        public InfoWindow(FileItem it)
        {
            item = it;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Width = 300; SizeToContent = SizeToContent.Height; MaxHeight = 760;
            ShowInTaskbar = true;
            MacChrome.Setup(this);
            WindowFrame.Attach(this, false, false);
            Title = it.DisplayName + " Info";
            var stack = new StackPanel { Margin = new Thickness(14, 4, 14, 14) };
            // header
            var head = new Grid { Margin = new Thickness(0, 4, 0, 10) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = new Image { Source = IconPainter.Snapshot(it, 56), Width = 56, Height = 56 };
            head.Children.Add(icon);
            var hp = new StackPanel { Margin = new Thickness(10, 2, 0, 0) };
            Grid.SetColumn(hp, 1);
            var nm = MacChrome.Label(it.DisplayName, 14, true); nm.TextWrapping = TextWrapping.Wrap; nm.TextTrimming = TextTrimming.None;
            var sub = MacChrome.Label(it.Kind + (it.IsDir ? "" : " — " + Format.Size(it.Size)), 12, false, "TextSecondary");
            var mod = MacChrome.Label("Modified: " + Format.FullDate(it.Modified), 11.5, false, "TextSecondary");
            hp.Children.Add(nm); hp.Children.Add(sub); hp.Children.Add(mod);
            head.Children.Add(hp);
            stack.Children.Add(head);

            // tags
            // General
            var gen = Section("General", true, sp =>
            {
                Row(sp, "Kind", it.Kind);
                Row(sp, "Size", it.IsDir ? "Calculating…" : Format.Size(it.Size) + (it.Size >= 1000 ? " (" + Format.Bytes(it.Size) + ")" : ""), sizeValue);
                Row(sp, "Where", System.IO.Path.GetDirectoryName(it.Path.TrimEnd('\\')) ?? it.Path);
                Row(sp, "Created", Format.FullDate(it.Created));
                Row(sp, "Modified", Format.FullDate(it.Modified));
                Row(sp, "Last opened", Format.FullDate(it.Accessed));
                var lk = new CheckBox { Content = "Locked", IsChecked = (it.Attr & FileAttributes.ReadOnly) != 0, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                lk.Click += (s, e) => SetAttr(FileAttributes.ReadOnly, lk.IsChecked == true);
                sp.Children.Add(lk);
                var hid = new CheckBox { Content = "Hidden", IsChecked = (it.Attr & FileAttributes.Hidden) != 0, HorizontalAlignment = HorizontalAlignment.Left };
                hid.Click += (s, e) => SetAttr(FileAttributes.Hidden, hid.IsChecked == true);
                sp.Children.Add(hid);
            });
            stack.Children.Add(gen);
            if (!it.IsDir && Format.IsImage(it))
                stack.Children.Add(Section("More Info", true, sp =>
                {
                    var (w, h) = Thumbs.ImageSize(it.Path);
                    if (w > 0) { Row(sp, "Dimensions", w + " × " + h); Row(sp, "Megapixels", (w * (double)h / 1e6).ToString("0.0")); }
                }));
            if (!it.IsDir && it.Special == SpecialKind.None && it.Ext.Length > 0 || it.IsDir)
                stack.Children.Add(Section("Name & Extension", false, sp =>
                {
                    var tb = new TextBox { Text = it.Name, Height = 24, Margin = new Thickness(0, 2, 0, 4) };
                    sp.Children.Add(tb);
                    var hide = new CheckBox { Content = "Hide extension", IsChecked = false, IsEnabled = false };
                    sp.Children.Add(hide);
                    tb.KeyDown += (s, e) =>
                    {
                        if (e.Key != Key.Return || tb.Text == it.Name) return;
                        try { var np = FileOps.Rename(it.Path, tb.Text.Trim()); Close(); }
                        catch { MacAlert.Show(this, "The name “" + tb.Text + "” can’t be used.", "Please choose a different name.", new[] { "OK" }); }
                    };
                }));
            stack.Children.Add(Section("Comments", false, sp =>
            {
                var tb = new TextBox { Text = Settings.Current.Comments.TryGetValue(it.Path, out var c) ? c : "", MinHeight = 56, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(4) };
                tb.LostFocus += (s, e) => { if (string.IsNullOrEmpty(tb.Text)) Settings.Current.Comments.Remove(it.Path); else Settings.Current.Comments[it.Path] = tb.Text; Settings.Save(false); };
                sp.Children.Add(tb);
            }));
            if (!it.IsDir)
                stack.Children.Add(Section("Open with:", false, sp =>
                {
                    var app = Native.DefaultAppName(it.Ext) ?? "Default application";
                    Row(sp, "Application", app);
                }));
            if (!it.IsDir && it.Special == SpecialKind.None)
                stack.Children.Add(Section("Preview", false, sp =>
                {
                    var img = new Image { Source = IconPainter.Snapshot(it, 200), Height = 160, Stretch = Stretch.Uniform, Margin = new Thickness(0, 4, 0, 4) };
                    sp.Children.Add(img);
                }));
            stack.Children.Add(Section("Sharing & Permissions", false, sp =>
            {
                string owner = "—";
                try
                {
                    var sec = it.IsDir ? (System.Security.AccessControl.FileSystemSecurity)new DirectoryInfo(it.Path).GetAccessControl() : new FileInfo(it.Path).GetAccessControl();
                    owner = sec.GetOwner(typeof(System.Security.Principal.NTAccount))?.ToString() ?? "—";
                }
                catch { }
                Row(sp, "Owner", owner);
                var b = new Button { Content = "Open Windows Properties…", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                b.Click += (s, e) => Native.ShellVerb(it.Path, "properties");
                sp.Children.Add(b);
            }));

            var sv = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Content = MacChrome.Build(this, it.DisplayName + " Info", sv, out _);
            Open.Add(this);
            Closed += (s, e) => { Open.Remove(this); cts.Cancel(); };
            KeyDown += (s, e) => { if (e.Key == Key.Escape || (e.Key == Key.W && (Keyboard.Modifiers & ModifierKeys.Control) != 0)) Close(); };
            if (it.IsDir) Task.Run(() => FileSystem.DirectorySize(it.Path, cts.Token)).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (t.Result >= 0) sizeValue.Text = Format.Size(t.Result) + (t.Result >= 1000 ? " (" + Format.Bytes(t.Result) + ")" : "") + " on disk";
            })));
        }

        void SetAttr(FileAttributes a, bool on)
        {
            try
            {
                var cur = File.GetAttributes(item.Path);
                File.SetAttributes(item.Path, on ? cur | a : cur & ~a);
                FileOps.NotifyExternal(System.IO.Path.GetDirectoryName(item.Path.TrimEnd('\\')));
            }
            catch { }
        }

        static void Row(StackPanel sp, string k, string v, TextBlock into = null)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            var a = MacChrome.Label(k + ":", 12, false, "TextSecondary"); a.TextAlignment = TextAlignment.Right; a.Margin = new Thickness(0, 0, 8, 0);
            var b = into ?? new TextBlock(); b.Text = v; b.FontSize = 12; b.TextWrapping = TextWrapping.Wrap; b.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            Grid.SetColumn(b, 1); g.Children.Add(a); g.Children.Add(b);
            sp.Children.Add(g);
        }

        static UIElement Section(string title, bool open, Action<StackPanel> fill)
        {
            var wrap = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            var body = new StackPanel { Margin = new Thickness(16, 2, 0, 6), Visibility = open ? Visibility.Visible : Visibility.Collapsed };
            fill(body);
            var chev = new System.Windows.Shapes.Path { Data = Glyphs.Get("chevright"), Width = 12, Height = 12, Stretch = Stretch.Uniform, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(open ? 90 : 0) };
            chev.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TextSecondary");
            var lbl = MacChrome.Label(title, 12, true); lbl.Margin = new Thickness(5, 0, 0, 0);
            var hdr = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            hdr.Children.Add(chev); hdr.Children.Add(lbl);
            hdr.MouseLeftButtonDown += (s, e) =>
            {
                bool o = body.Visibility != Visibility.Visible;
                body.Visibility = o ? Visibility.Visible : Visibility.Collapsed;
                ((RotateTransform)chev.RenderTransform).Angle = o ? 90 : 0;
            };
            wrap.Children.Add(hdr); wrap.Children.Add(body);
            return wrap;
        }
    }
}

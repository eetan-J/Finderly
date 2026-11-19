using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Finderly.Core;

namespace Finderly.UI
{
    /// <summary>Quick Look: floating preview opened with Space, navigable with arrow keys.</summary>
    public sealed class QuickLookWindow : Window
    {
        public static QuickLookWindow Instance;
        List<FileItem> items = new List<FileItem>();
        int index;
        readonly Grid body = new Grid();
        readonly TextBlock titleText = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        readonly Border root = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(0.5), Margin = new Thickness(0), ClipToBounds = true };
        readonly Grid bar = new Grid { Height = 38 };
        readonly Border barFill = new Border();
        int gen;
        bool full;
        MediaElement media; DispatcherTimer mediaTimer; Slider seek; TextBlock timeText; Button playBtn; bool playing;
        public Action<FileItem> OpenWithPreview;
        public Func<FileItem, Rect> SourceRect;
        public event Action<FileItem> Navigated;
        public event Action Closed2;
        Rect restore; WindowState restoreState;

        public QuickLookWindow()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ResizeMode = ResizeMode.CanResizeWithGrip; Width = 640; Height = 520;
            MinWidth = 320; MinHeight = 240; UseLayoutRounding = true;
            FontFamily = Theme.UIFont;
            var outer = new Grid { Margin = new Thickness(22) };
            root.SetResourceReference(Border.BackgroundProperty, "QuickLookBgBrush");
            root.SetResourceReference(Border.BorderBrushProperty, "MenuBorder");
            root.Background = Theme.QuickLookBg;
            root.Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 10, Opacity = 0.4, Direction = 270 };
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            g.RowDefinitions.Add(new RowDefinition());
            // title bar
            var lights = new TrafficLights { CanMinimize = false, CanZoom = true, OnClose = () => Close(), OnZoom = ToggleFull, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            titleText.Margin = new Thickness(90, 0, 150, 0);
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            bar.Children.Add(titleText); bar.Children.Add(lights);
            var open = new Button { Style = (Style)Application.Current.Resources["ToolButton"], Content = Glyphs.Make("markup", 16), ToolTip = "Open with Preview", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 40, 0), Padding = new Thickness(7, 4, 7, 4) };
            open.Click += (s, e) => { var it = Current; if (it != null) { OpenWithPreview?.Invoke(it); Close(); } };
            var share = new Button { Style = (Style)Application.Current.Resources["ToolButton"], Content = Glyphs.Make("share", 16), ToolTip = "Share", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(7, 4, 7, 4) };
            share.Click += (s, e) => { if (Current != null) Native.ShellVerb(Current.Path, "properties", new System.Windows.Interop.WindowInteropHelper(this).Handle); };
            var fs = new Button { Style = (Style)Application.Current.Resources["ToolButton"], Content = Glyphs.Make("fullscreen", 15), ToolTip = "Full Screen", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 72, 0), Padding = new Thickness(7, 4, 7, 4) };
            fs.Click += (s, e) => ToggleFull();
            bar.Children.Add(open); bar.Children.Add(share); bar.Children.Add(fs);
            WindowFrame.MakeDragArea(bar, this, false);
            g.Children.Add(bar);
            Grid.SetRow(body, 1); g.Children.Add(body);
            var hair = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom }; hair.SetResourceReference(Border.BackgroundProperty, "Separator");
            bar.Children.Add(hair);
            root.Child = g;
            outer.Children.Add(root);
            Content = outer;
            PreviewKeyDown += OnKey;
            Deactivated += (s, e) => { };
            Closing += (s, e) =>
            {
                if (!closing && IsVisible && !full)
                {
                    e.Cancel = true; closing = true; StopMedia();
                    RunZoom(false, () => { if (isClosed) return; Instance = null; Closed2?.Invoke(); Close(); });
                    return;
                }
                StopMedia(); Instance = null; if (!closing) Closed2?.Invoke();
            };
            Loaded += (s, e) => { Focus(); Keyboard.Focus(this); }; Closed += (s, e) => isClosed = true;
            Theme.Changed += () => root.Background = Theme.QuickLookBg;
        }

        public FileItem Current => index >= 0 && index < items.Count ? items[index] : null;

        public static void Toggle(IList<FileItem> list, int start, Window owner, Func<FileItem, Rect> src = null)
        {
            if (Instance != null) { Instance.Close(); return; }
            if (list == null || list.Count == 0) return;
            var q = new QuickLookWindow { Owner = null };
            q.items = list.ToList(); q.index = Math.Max(0, Math.Min(list.Count - 1, start));
            q.SourceRect = src;
            Instance = q;
            // size the window for the content up front so it opens at its final size (no resize jump)
            var it = q.Current;
            if (it != null && !it.IsDir && Format.IsImage(it))
            {
                var (iw, ih) = Thumbs.ImageSize(it.Path);
                if (iw > 0) { var fs = FitSize(iw, ih); q.Width = fs.Width; q.Height = fs.Height; }
            }
            var wa = SystemParameters.WorkArea;
            double cx = owner.Left + owner.ActualWidth / 2, cy = owner.Top + owner.ActualHeight / 2;
            q.Left = Math.Max(wa.Left, Math.Min(wa.Right - q.Width, cx - q.Width / 2));
            q.Top = Math.Max(wa.Top, Math.Min(wa.Bottom - q.Height, cy - q.Height / 2));
            q.Opacity = 0;
            q.Show();
            q.Show_();
            q.Animate();
        }

        public void SetItems(IList<FileItem> list, int idx)
        {
            if (list == null || list.Count == 0) return;
            items = list.ToList(); index = Math.Max(0, Math.Min(items.Count - 1, idx)); Show_();
        }

        Rect SourceScreenRect()
        {
            try { return SourceRect?.Invoke(Current) ?? Rect.Empty; } catch { return Rect.Empty; }
        }

        TransformGroup zoomXf; ScaleTransform zoomScale; TranslateTransform zoomMove;
        bool closing, isClosed;

        void EnsureXf()
        {
            if (zoomXf != null) return;
            var outer = (Grid)Content; outer.RenderTransformOrigin = new Point(0.5, 0.5);
            zoomScale = new ScaleTransform(1, 1); zoomMove = new TranslateTransform();
            zoomXf = new TransformGroup(); zoomXf.Children.Add(zoomScale); zoomXf.Children.Add(zoomMove);
            outer.RenderTransform = zoomXf;
        }

        /// <summary>Finder-style zoom: the window grows out of (or shrinks back into) the file's icon.</summary>
        void RunZoom(bool opening, Action done = null)
        {
            EnsureXf();
            var src = SourceScreenRect();
            double m = 22, pw = Math.Max(1, Width - 2 * m), ph = Math.Max(1, Height - 2 * m);
            double sx, sy, dx, dy;
            if (src.IsEmpty) { sx = sy = 0.9; dx = dy = 0; }
            else
            {
                sx = Math.Max(0.02, src.Width / pw); sy = Math.Max(0.02, src.Height / ph);
                dx = (src.X + src.Width / 2) - (Left + Width / 2);
                dy = (src.Y + src.Height / 2) - (Top + Height / 2);
            }
            var dur = TimeSpan.FromMilliseconds(opening ? 320 : 230);
            System.Windows.Media.Animation.IEasingFunction ease = opening
                ? new System.Windows.Media.Animation.QuinticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                : (System.Windows.Media.Animation.IEasingFunction)new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn };
            System.Windows.Media.Animation.DoubleAnimation A(double from, double to) =>
                new System.Windows.Media.Animation.DoubleAnimation(from, to, dur) { EasingFunction = ease, FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd };
            zoomScale.BeginAnimation(ScaleTransform.ScaleXProperty, opening ? A(sx, 1) : A(1, sx));
            zoomScale.BeginAnimation(ScaleTransform.ScaleYProperty, opening ? A(sy, 1) : A(1, sy));
            zoomMove.BeginAnimation(TranslateTransform.XProperty, opening ? A(dx, 0) : A(0, dx));
            zoomMove.BeginAnimation(TranslateTransform.YProperty, opening ? A(dy, 0) : A(0, dy));
            var fade = new System.Windows.Media.Animation.DoubleAnimation(opening ? 0 : 1, opening ? 1 : 0, TimeSpan.FromMilliseconds(opening ? 120 : 160)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd };
            if (!opening) fade.BeginTime = TimeSpan.FromMilliseconds(70);
            if (done != null) dur = dur; // keep compiler quiet
            var finish = new System.Windows.Media.Animation.DoubleAnimation(0, 1, dur);
            if (done != null) { var t = new DispatcherTimer { Interval = dur }; t.Tick += (s, e) => { t.Stop(); done(); }; t.Start(); }
            BeginAnimation(OpacityProperty, fade);
        }

        void Animate() => RunZoom(true);

        void ToggleFull()
        {
            if (!full)
            {
                restore = new Rect(Left, Top, Width, Height);
                var wa = SystemParameters.WorkArea;
                Left = 0; Top = 0; Width = SystemParameters.PrimaryScreenWidth; Height = SystemParameters.PrimaryScreenHeight;
                ((Grid)Content).Margin = new Thickness(0);
                Topmost = true; full = true;
            }
            else
            {
                Left = restore.X; Top = restore.Y; Width = restore.Width; Height = restore.Height;
                ((Grid)Content).Margin = new Thickness(22); Topmost = false; full = false;
            }
        }

        void OnKey(object s, KeyEventArgs e)
        {
            var k = e.Key;
            if (k == Key.Space || k == Key.Escape)
            {
                if (full && k == Key.Escape) { ToggleFull(); } else Close();
                e.Handled = true;
            }
            else if (k == Key.Left || k == Key.Up) { Step(-1); e.Handled = true; }
            else if (k == Key.Right || k == Key.Down) { Step(1); e.Handled = true; }
            else if (k == Key.Return && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { if (Current != null) { OpenWithPreview?.Invoke(Current); Close(); } e.Handled = true; }
            else if (k == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { ToggleFull(); e.Handled = true; }
        }

        void Step(int d)
        {
            if (items.Count == 0) return;
            index = (index + d + items.Count) % items.Count;
            Show_();
            Navigated?.Invoke(Current);
        }

        void StopMedia()
        {
            mediaTimer?.Stop(); mediaTimer = null;
            if (media != null) { try { media.Stop(); media.Close(); } catch { } media = null; }
        }

        async void Show_()
        {
            int my = ++gen;
            StopMedia();
            var it = Current;
            body.Children.Clear();
            if (it == null) return;
            titleText.Text = it.DisplayName;
            Title = it.DisplayName;
            try
            {
                if (it.IsDir) { ShowInfo(it); return; }
                if (Format.IsImage(it) || it.Ext == "svg_")
                {
                    var host = new Grid(); body.Children.Add(host);
                    var img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(0) };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    host.Children.Add(img);
                    string path = it.Path;
                    if (Thumbs.PreviewCache.TryGetValue(path, out var hit))
                    {
                        img.Source = hit.img; AdaptSize(hit.w, hit.h); Prefetch(); return;
                    }
                    // 1) instant: any cached thumbnail, 2) fast shell thumbnail, 3) sharp decode sized for the screen
                    ImageSource quick = null;
                    foreach (var px in new[] { 1024, 512, 256, 128, 96, 64 }) { quick = Thumbs.Peek(path, px)?.Image; if (quick != null) break; }
                    if (quick != null) img.Source = quick;
                    var sizeTask = Task.Run(() => Thumbs.ImageSize(path));
                    var fastTask = quick == null ? Task.Run(() => Native.ShellImage(path, 512, Native.SIIGBF_THUMBNAILONLY | Native.SIIGBF_BIGGERSIZEOK)) : null;
                    var fullTask = Task.Run(() => Thumbs.LoadImage(path, FullPx()));
                    var (iw, ih) = await sizeTask;
                    if (my != gen) return;
                    if (iw > 0) AdaptSize(iw, ih);
                    if (fastTask != null)
                    {
                        var f = await fastTask;
                        if (my != gen) return;
                        if (f != null && !fullTask.IsCompleted) img.Source = f;
                    }
                    var bmp = await fullTask;
                    if (my != gen) return;
                    if (bmp != null)
                    {
                        img.Source = bmp;
                        if (iw <= 0) AdaptSize(bmp.PixelWidth, bmp.PixelHeight);
                        Thumbs.PreviewCache[path] = (bmp, iw > 0 ? iw : bmp.PixelWidth, iw > 0 ? ih : bmp.PixelHeight);
                        Prefetch();
                    }
                    else ShowInfo(it);
                    return;
                }
                if (it.Ext == "pdf") { await ShowPdf(it, my); return; }
                if (Format.MediaExts.Contains(it.Ext)) { ShowMedia(it); return; }
                if (Format.TextExts.Contains(it.Ext) || (it.Size < 2_000_000 && LooksText(it.Path))) { await ShowText(it, my); return; }
                // fall back to shell thumbnail (Office docs etc.) or info
                var thumb = await Task.Run(() => Native.ShellImage(it.Path, 1024, Native.SIIGBF_THUMBNAILONLY | Native.SIIGBF_BIGGERSIZEOK));
                if (my != gen) return;
                if (thumb != null)
                {
                    var img = new Image { Source = thumb, Stretch = Stretch.Uniform, Margin = new Thickness(12) };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    body.Children.Add(img);
                }
                else ShowInfo(it);
            }
            catch { if (my == gen) ShowInfo(it); }
        }

        FileItem ItemAt(int d) => items.Count < 2 ? null : items[(index + d + items.Count) % items.Count];

        static int FullPx() { var wa = SystemParameters.WorkArea; return (int)Math.Min(2600, Math.Max(wa.Width, wa.Height) * 1.25); }

        /// <summary>Decode the neighbouring images in the background so arrowing through them is instant.</summary>
        void Prefetch()
        {
            var list = new System.Collections.Generic.List<string>();
            for (int d = -1; d <= 1; d += 2)
            {
                var n = ItemAt(d);
                if (n != null && !n.IsDir && Format.IsImage(n) && !Thumbs.PreviewCache.ContainsKey(n.Path)) list.Add(n.Path);
            }
            foreach (var p in list)
                Task.Run(() =>
                {
                    var b = Thumbs.LoadImage(p, FullPx()); if (b == null) return;
                    var (w, h) = Thumbs.ImageSize(p);
                    Thumbs.PreviewCache[p] = (b, w > 0 ? w : b.PixelWidth, h > 0 ? h : b.PixelHeight);
                });
        }

        static Size FitSize(int w, int h)
        {
            var wa = SystemParameters.WorkArea;
            double maxW = wa.Width * 0.82, maxH = wa.Height * 0.84 - 38;
            double sc = Math.Min(1, Math.Min(maxW / w, maxH / h));
            return new Size(Math.Max(320, w * sc + 44), Math.Max(240, h * sc + 38 + 44));
        }

        void AdaptSize(int w, int h)
        {
            if (full || closing || w <= 0 || h <= 0) return;
            var wa = SystemParameters.WorkArea;
            var fz = FitSize(w, h);
            if (Math.Abs(fz.Width - Width) < 1.5 && Math.Abs(fz.Height - Height) < 1.5) return;
            double nw = fz.Width, nh = fz.Height;
            double cx = Left + Width / 2, cy = Top + Height / 2;
            var dur = TimeSpan.FromMilliseconds(160);
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            BeginAnimation(WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(nw, dur) { EasingFunction = ease });
            BeginAnimation(HeightProperty, new System.Windows.Media.Animation.DoubleAnimation(nh, dur) { EasingFunction = ease });
            BeginAnimation(LeftProperty, new System.Windows.Media.Animation.DoubleAnimation(Math.Max(0, cx - nw / 2), dur) { EasingFunction = ease });
            BeginAnimation(TopProperty, new System.Windows.Media.Animation.DoubleAnimation(Math.Max(0, cy - nh / 2), dur) { EasingFunction = ease });
        }

        static bool LooksText(string p)
        {
            try
            {
                using var fs = File.OpenRead(p);
                var buf = new byte[Math.Min(4096, fs.Length)];
                int n = fs.Read(buf, 0, buf.Length);
                for (int i = 0; i < n; i++) if (buf[i] == 0) return false;
                return n > 0;
            }
            catch { return false; }
        }

        async Task ShowText(FileItem it, int my)
        {
            string text = await Task.Run(() =>
            {
                using var fs = new FileStream(it.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buf = new byte[Math.Min(fs.Length, 400_000)];
                int n = fs.Read(buf, 0, buf.Length);
                return Encoding.UTF8.GetString(buf, 0, n) + (fs.Length > buf.Length ? "\n…" : "");
            });
            if (my != gen) return;
            var tb = new TextBox
            {
                Text = text, IsReadOnly = true, FontFamily = it.Ext == "txt" || it.Ext == "md" ? Theme.UIFont : Theme.MonoFont, FontSize = 12.5,
                TextWrapping = TextWrapping.NoWrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(18, 12, 18, 12), Style = (Style)Application.Current.Resources["BareTextBox"], AcceptsReturn = true,
            };
            tb.SetResourceReference(Control.ForegroundProperty, "Text");
            tb.Template = (ControlTemplate)XamlReaderTemplate();
            body.Children.Add(tb);
        }

        static ControlTemplate cachedTpl;
        static ControlTemplate XamlReaderTemplate()
        {
            if (cachedTpl != null) return cachedTpl;
            var xaml = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TextBox'><ScrollViewer x:Name='PART_ContentHost' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Focusable='False' Padding='{TemplateBinding Padding}'/></ControlTemplate>";
            cachedTpl = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
            return cachedTpl;
        }

        async Task ShowPdf(FileItem it, int my)
        {
            var doc = await PdfDoc.Open(it.Path);
            if (my != gen) return;
            if (doc == null || doc.PageCount == 0 || doc.Locked) { ShowInfo(it, doc?.Locked == true ? "This PDF is password protected. Open it in Preview to unlock it." : null); return; }
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
            sv.Content = sp;
            body.Children.Add(sv);
            int pages = Math.Min(doc.PageCount, 400);
            var ims = new List<Image>();
            for (int i = 0; i < pages; i++)
            {
                var (pw, ph) = doc.PageSize(i);
                var im = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(18, 0, 18, 12), Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.25 } };
                im.Height = Math.Max(200, (Math.Max(300, ActualWidth - 80)) * ph / pw);
                RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                sp.Children.Add(im); ims.Add(im);
            }
            // render lazily as pages scroll into view
            var rendered = new HashSet<int>();
            async void Pump()
            {
                for (int i = 0; i < ims.Count; i++)
                {
                    if (my != gen) return;
                    var im = ims[i];
                    var pt = im.TranslatePoint(new Point(0, 0), sv);
                    if (pt.Y + im.ActualHeight < -300 || pt.Y > sv.ActualHeight + 300 || rendered.Contains(i)) continue;
                    rendered.Add(i);
                    var bmp = await doc.Render(i, (int)Math.Max(800, (ActualWidth - 36) * 1.5));
                    if (my == gen && bmp != null) { im.Source = bmp; im.Height = double.NaN; }
                }
            }
            sv.ScrollChanged += (s, e) => Pump();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Pump));
            titleText.Text = it.DisplayName + " — " + doc.PageCount + (doc.PageCount == 1 ? " page" : " pages");
        }

        void ShowMedia(FileItem it)
        {
            bool audio = Format.AudioExts.Contains(it.Ext);
            var grid = new Grid(); body.Children.Add(grid);
            grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            media = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform, Source = new Uri(it.Path), Volume = 0.8, Visibility = audio ? Visibility.Collapsed : Visibility.Visible };
            grid.Children.Add(media);
            if (audio)
            {
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                sp.Children.Add(new Image { Source = IconPainter.Snapshot(it, 160), Width = 160, Height = 160 });
                var n = new TextBlock { Text = it.DisplayName, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center }; n.SetResourceReference(TextBlock.ForegroundProperty, "Text");
                sp.Children.Add(n);
                grid.Children.Add(sp);
            }
            var ctl = new DockPanel { Margin = new Thickness(14, 0, 14, 4), LastChildFill = true };
            Grid.SetRow(ctl, 1);
            playBtn = new Button { Style = (Style)Application.Current.Resources["ToolButton"], Content = Glyphs.Make("pause", 16), Width = 30 };
            DockPanel.SetDock(playBtn, Dock.Left);
            timeText = new TextBlock { Text = "0:00", Width = 80, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 11.5 }; timeText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            DockPanel.SetDock(timeText, Dock.Right);
            seek = new Slider { Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            ctl.Children.Add(playBtn); ctl.Children.Add(timeText); ctl.Children.Add(seek);
            grid.Children.Add(ctl);
            playing = true;
            media.MediaOpened += (s, e) => { if (media.NaturalDuration.HasTimeSpan) seek.Maximum = media.NaturalDuration.TimeSpan.TotalSeconds; if (!audio && media.NaturalVideoWidth > 0) AdaptSize(media.NaturalVideoWidth, media.NaturalVideoHeight + 44); };
            media.MediaEnded += (s, e) => { media.Position = TimeSpan.Zero; media.Pause(); playing = false; playBtn.Content = Glyphs.Make("play", 16); };
            playBtn.Click += (s, e) => { if (playing) media.Pause(); else media.Play(); playing = !playing; playBtn.Content = Glyphs.Make(playing ? "pause" : "play", 16); };
            bool dragging = false;
            seek.PreviewMouseLeftButtonDown += (s, e) => dragging = true;
            seek.PreviewMouseLeftButtonUp += (s, e) => { dragging = false; media.Position = TimeSpan.FromSeconds(seek.Value); };
            mediaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            mediaTimer.Tick += (s, e) =>
            {
                if (media == null) return;
                if (!dragging) seek.Value = media.Position.TotalSeconds;
                var tot = media.NaturalDuration.HasTimeSpan ? media.NaturalDuration.TimeSpan : TimeSpan.Zero;
                timeText.Text = Fmt(media.Position) + " / " + Fmt(tot);
            };
            mediaTimer.Start();
            media.Play();
        }

        static string Fmt(TimeSpan t) => (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");

        void ShowInfo(FileItem it, string note = null)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(30) };
            sp.Children.Add(new Image { Source = IconPainter.Snapshot(it, 160), Width = 160, Height = 160 });
            TextBlock T(string s, double sz, bool b, string br) { var t = new TextBlock { Text = s, FontSize = sz, FontWeight = b ? FontWeights.SemiBold : FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 440, Margin = new Thickness(0, 4, 0, 0) }; t.SetResourceReference(TextBlock.ForegroundProperty, br); return t; }
            sp.Children.Add(T(it.DisplayName, 17, true, "Text"));
            sp.Children.Add(T(it.Kind + (it.IsDir ? "" : " — " + Format.Size(it.Size)), 12.5, false, "TextSecondary"));
            sp.Children.Add(T("Modified " + Format.FullDate(it.Modified), 12, false, "TextSecondary"));
            if (note != null) sp.Children.Add(T(note, 12, false, "TextSecondary"));
            body.Children.Add(sp);
        }
    }
}

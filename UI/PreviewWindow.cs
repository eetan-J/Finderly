using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Finderly.Core;
using Path = System.IO.Path;

namespace Finderly.UI
{
    /// <summary>Preview-style viewer: thumbnail sidebar, zoom, rotate, markup toolbar, export.</summary>
    public sealed class PreviewWindow : Window
    {
        public static readonly List<PreviewWindow> Open = new List<PreviewWindow>();

        enum Tool { Select, Pen, Highlight, Rect, Oval, Line, Arrow, Text }

        readonly List<string> files;
        int fileIndex;
        string path;
        PdfDoc pdf;
        BitmapSource image;               // current image (for image files)
        readonly List<BitmapSource> pdfPages = new List<BitmapSource>();
        int pageIndex;
        double zoom = 1; bool fit = true;
        int rotation;                      // degrees, applied to image
        bool flipH, flipV;

        readonly Grid thumbPane = new Grid();
        readonly StackPanel thumbs = new StackPanel();
        readonly ScrollViewer viewer = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly Grid stage = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly Image pageImg = new Image { Stretch = Stretch.Fill };
        readonly Canvas shapes = new Canvas();
        readonly InkCanvas ink = new InkCanvas { Background = Brushes.Transparent, EditingMode = InkCanvasEditingMode.None };
        readonly StackPanel markupBar = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
        readonly TextBlock title = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock subtitle = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock status = new TextBlock { FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly List<ToggleButton> toolButtons = new List<ToggleButton>();
        readonly ScaleTransform stageScale = new ScaleTransform(1, 1);
        Tool tool = Tool.Select;
        Color penColor = Color.FromRgb(0xFF, 0x3B, 0x30);
        double lineWidth = 3;
        bool dirty;
        int gen;

        public PreviewWindow(IEnumerable<string> paths, int start = 0)
        {
            files = paths.ToList();
            fileIndex = Math.Max(0, Math.Min(files.Count - 1, start));
            WindowStyle = WindowStyle.None; AllowsTransparency = false; ResizeMode = ResizeMode.CanResize;
            Width = 980; Height = 700; MinWidth = 520; MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Icon = MacChrome.AppIcon(true);
            MacChrome.Setup(this);
            WindowFrame.Attach(this, false);
            Open.Add(this);
            BuildUI();
            Closing += OnClosing;
            Closed += (s, e) => Open.Remove(this);
            KeyDown += OnKey;
            Loaded += async (s, e) => await LoadFile();
        }

        // ---------- UI ----------
        void BuildUI()
        {
            var root = new Grid();
            root.SetResourceReference(Panel.BackgroundProperty, "ContentBg");
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

            // toolbar
            var tb = new Grid { Background = Brushes.Transparent };
            tb.SetResourceReference(Panel.BackgroundProperty, "PanelBg");
            var lights = new TrafficLights { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), CanZoom = true };
            tb.Children.Add(lights);
            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(82, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var sbBtn = Tool_("sidebar", "Show Sidebar", () => { thumbPane.Visibility = thumbPane.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; });
            left.Children.Add(sbBtn);
            left.Children.Add(Tool_("zoomout", "Zoom Out", () => SetZoom(zoom / 1.25)));
            left.Children.Add(Tool_("zoomin", "Zoom In", () => SetZoom(zoom * 1.25)));
            tb.Children.Add(left);
            var titles = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(250, 0, 250, 0) };
            title.HorizontalAlignment = HorizontalAlignment.Center; subtitle.HorizontalAlignment = HorizontalAlignment.Center;
            title.SetResourceReference(TextBlock.ForegroundProperty, "Text"); subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            titles.Children.Add(title); titles.Children.Add(subtitle);
            tb.Children.Add(titles);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(Tool_("rotate", "Rotate Left (Ctrl+L)", () => Rotate(-90)));
            var mk = new ToggleButton { Style = (Style)Application.Current.Resources["ToolButton"], Content = Glyphs.Make("markup", 18), ToolTip = "Show Markup Toolbar", Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(8, 5, 8, 5) };
            mk.Click += (s, e) => { markupBar.Visibility = mk.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; root.RowDefinitions[1].Height = mk.IsChecked == true ? new GridLength(38) : new GridLength(0); if (mk.IsChecked != true) SetTool(Tool.Select); };
            right.Children.Add(mk);
            right.Children.Add(Tool_("share", "Share", () => { if (File.Exists(path)) Native.ShellVerb(path, "properties"); }));
            tb.Children.Add(right);
            WindowFrame.MakeDragArea(tb, this);
            Grid.SetRow(tb, 0); root.Children.Add(tb);
            var hair = MacChrome.Hairline(); hair.VerticalAlignment = VerticalAlignment.Bottom; hair.Margin = new Thickness(0); Grid.SetRow(hair, 0); root.Children.Add(hair);

            // markup bar
            BuildMarkupBar();
            var mbHost = new Border { Child = markupBar, BorderThickness = new Thickness(0, 0, 0, 1) };
            mbHost.SetResourceReference(Border.BackgroundProperty, "ContentBg"); mbHost.SetResourceReference(Border.BorderBrushProperty, "Separator");
            Grid.SetRow(mbHost, 1); root.Children.Add(mbHost);

            // body
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            thumbPane.Width = 150;
            thumbPane.SetResourceReference(Panel.BackgroundProperty, "SidebarBg");
            var thumbScroll = new ScrollViewer { Content = thumbs, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            thumbs.Margin = new Thickness(0, 10, 0, 10);
            thumbPane.Children.Add(thumbScroll);
            var sep = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Right }; sep.SetResourceReference(Border.BackgroundProperty, "Separator"); thumbPane.Children.Add(sep);
            body.Children.Add(thumbPane);
            stage.Children.Add(pageImg); stage.Children.Add(shapes); stage.Children.Add(ink);
            stage.LayoutTransform = stageScale;
            RenderOptions.SetBitmapScalingMode(pageImg, BitmapScalingMode.HighQuality);
            var vbg = new Grid(); vbg.SetResourceReference(Panel.BackgroundProperty, "PanelBg");
            viewer.Content = new Grid { Children = { stage }, Margin = new Thickness(0) };
            ((Grid)viewer.Content).MinWidth = 0;
            vbg.Children.Add(viewer);
            Grid.SetColumn(vbg, 1); body.Children.Add(vbg);
            Grid.SetRow(body, 2); root.Children.Add(body);
            viewer.SizeChanged += (s, e) => { if (fit) FitToWindow(); else CenterStage(); };
            viewer.PreviewMouseWheel += (s, e) => { if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { SetZoom(zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1)); e.Handled = true; } };
            WireDrawing();

            // status
            var sb = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = status };
            sb.SetResourceReference(Border.BackgroundProperty, "PanelBg"); sb.SetResourceReference(Border.BorderBrushProperty, "Separator");
            status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            Grid.SetRow(sb, 3); root.Children.Add(sb);
            Content = root;
        }

        Button Tool_(string glyph, string tip, Action act)
        {
            var b = new Button { Style = (Style)Application.Current.Resources["ToolButton"], Content = Glyphs.Make(glyph, 18), ToolTip = tip, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(8, 5, 8, 5) };
            b.Click += (s, e) => act();
            return b;
        }

        void BuildMarkupBar()
        {
            markupBar.Margin = new Thickness(10, 0, 10, 0);
            markupBar.VerticalAlignment = VerticalAlignment.Center;
            void Add(Tool t, string glyph, string tip)
            {
                var b = new ToggleButton { Style = (Style)Application.Current.Resources["ToolButton"], Content = Glyphs.Make(glyph, 17), ToolTip = tip, Margin = new Thickness(2, 3, 2, 3), Padding = new Thickness(8, 4, 8, 4) };
                b.Click += (s, e) => SetTool(b.IsChecked == true ? t : Tool.Select);
                b.Tag = t; toolButtons.Add(b); markupBar.Children.Add(b);
            }
            Add(Tool.Select, "select", "Select");
            Add(Tool.Pen, "sketch", "Sketch");
            Add(Tool.Highlight, "highlight", "Highlight");
            Add(Tool.Line, "line", "Line");
            Add(Tool.Arrow, "arrow", "Arrow");
            Add(Tool.Rect, "rect", "Rectangle");
            Add(Tool.Oval, "oval", "Oval");
            Add(Tool.Text, "text", "Text");
            markupBar.Children.Add(new Border { Width = 1, Margin = new Thickness(8, 8, 8, 8), Background = Theme.Separator });
            var cols = new[] { "FF3B30", "FF9500", "FFCC00", "34C759", "007AFF", "AF52DE", "000000", "FFFFFF" };
            foreach (var c in cols)
            {
                var col = (Color)ColorConverter.ConvertFromString("#" + c);
                var sw = new Button { Style = (Style)Application.Current.Resources["PlainButton"], Margin = new Thickness(2, 0, 2, 0), Content = new Ellipse { Width = 17, Height = 17, Fill = new SolidColorBrush(col), Stroke = Theme.ControlBorderStrong, StrokeThickness = 0.75 }, Cursor = Cursors.Hand };
                sw.Click += (s, e) => { penColor = col; ink.DefaultDrawingAttributes.Color = col; };
                markupBar.Children.Add(sw);
            }
            markupBar.Children.Add(new Border { Width = 1, Margin = new Thickness(8, 8, 8, 8), Background = Theme.Separator });
            var wl = new Slider { Minimum = 1, Maximum = 16, Value = lineWidth, Width = 90, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Line width" };
            wl.ValueChanged += (s, e) => { lineWidth = wl.Value; ink.DefaultDrawingAttributes.Width = lineWidth; ink.DefaultDrawingAttributes.Height = lineWidth; };
            markupBar.Children.Add(wl);
            var undo = new Button { Style = (Style)Application.Current.Resources["ToolButton"], Content = Glyphs.Make("undo", 17), ToolTip = "Undo markup", Margin = new Thickness(8, 3, 2, 3), Padding = new Thickness(8, 4, 8, 4) };
            undo.Click += (s, e) => UndoMarkup();
            markupBar.Children.Add(undo);
        }

        // ---------- drawing ----------
        Point dragStart; Shape liveShape; bool drawing;
        readonly Stack<Action> markupUndo = new Stack<Action>();

        void SetTool(Tool t)
        {
            tool = t;
            foreach (var b in toolButtons) b.IsChecked = (Tool)b.Tag == t && t != Tool.Select ? true : (Tool)b.Tag == Tool.Select && t == Tool.Select;
            ink.DefaultDrawingAttributes = new DrawingAttributes
            {
                Color = penColor, Width = t == Tool.Highlight ? lineWidth * 4 : lineWidth, Height = t == Tool.Highlight ? lineWidth * 4 : lineWidth,
                IsHighlighter = t == Tool.Highlight, FitToCurve = true,
            };
            if (t == Tool.Highlight) ink.DefaultDrawingAttributes.Color = Color.FromArgb(255, penColor.R, penColor.G, penColor.B);
            ink.EditingMode = t == Tool.Pen || t == Tool.Highlight ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
            ink.IsHitTestVisible = true;
            shapes.IsHitTestVisible = false;
            stage.Cursor = t == Tool.Select ? Cursors.Arrow : t == Tool.Text ? Cursors.IBeam : Cursors.Cross;
        }

        void WireDrawing()
        {
            ink.StrokeCollected += (s, e) => { dirty = true; var st = e.Stroke; markupUndo.Push(() => ink.Strokes.Remove(st)); };
            ink.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (tool == Tool.Select || tool == Tool.Pen || tool == Tool.Highlight) return;
                dragStart = e.GetPosition(shapes);
                if (tool == Tool.Text) { AddText(dragStart); e.Handled = true; return; }
                var brush = new SolidColorBrush(penColor);
                switch (tool)
                {
                    case Tool.Rect: liveShape = new Rectangle { Stroke = brush, StrokeThickness = lineWidth, RadiusX = 2, RadiusY = 2 }; break;
                    case Tool.Oval: liveShape = new Ellipse { Stroke = brush, StrokeThickness = lineWidth }; break;
                    default: liveShape = new Line { Stroke = brush, StrokeThickness = lineWidth, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; break;
                }
                shapes.Children.Add(liveShape);
                drawing = true; ink.CaptureMouse(); e.Handled = true;
            };
            ink.PreviewMouseMove += (s, e) =>
            {
                if (!drawing || liveShape == null) return;
                var p = e.GetPosition(shapes);
                if (liveShape is Line l) { l.X1 = dragStart.X; l.Y1 = dragStart.Y; l.X2 = p.X; l.Y2 = p.Y; }
                else
                {
                    Canvas.SetLeft(liveShape, Math.Min(dragStart.X, p.X)); Canvas.SetTop(liveShape, Math.Min(dragStart.Y, p.Y));
                    liveShape.Width = Math.Abs(p.X - dragStart.X); liveShape.Height = Math.Abs(p.Y - dragStart.Y);
                }
            };
            ink.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (!drawing) return;
                drawing = false; ink.ReleaseMouseCapture();
                var sh = liveShape; liveShape = null;
                var p = e.GetPosition(shapes);
                if (sh == null) return;
                if ((p - dragStart).Length < 3) { shapes.Children.Remove(sh); return; }
                if (tool == Tool.Arrow)
                {
                    var ln = (Line)sh;
                    var ang = Math.Atan2(p.Y - dragStart.Y, p.X - dragStart.X);
                    double hs = 10 + lineWidth * 2;
                    var head = new Polyline { Stroke = sh.Stroke, StrokeThickness = lineWidth, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    head.Points.Add(new Point(p.X - hs * Math.Cos(ang - 0.45), p.Y - hs * Math.Sin(ang - 0.45)));
                    head.Points.Add(p);
                    head.Points.Add(new Point(p.X - hs * Math.Cos(ang + 0.45), p.Y - hs * Math.Sin(ang + 0.45)));
                    shapes.Children.Add(head);
                    markupUndo.Push(() => { shapes.Children.Remove(sh); shapes.Children.Remove(head); });
                }
                else markupUndo.Push(() => shapes.Children.Remove(sh));
                dirty = true;
            };
        }

        void AddText(Point at)
        {
            var tb = new TextBox { MinWidth = 60, FontSize = 18 + lineWidth, Foreground = new SolidColorBrush(penColor), Background = Brushes.Transparent, BorderBrush = Theme.Accent, BorderThickness = new Thickness(1), CaretBrush = new SolidColorBrush(penColor), AcceptsReturn = true, Style = (Style)Application.Current.Resources["BareTextBox"], Padding = new Thickness(2) };
            tb.Foreground = new SolidColorBrush(penColor);
            Canvas.SetLeft(tb, at.X); Canvas.SetTop(tb, at.Y);
            shapes.IsHitTestVisible = true;
            shapes.Children.Add(tb);
            tb.Loaded += (s, e) => tb.Focus();
            tb.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(tb.Text)) { shapes.Children.Remove(tb); return; }
                var t = new TextBlock { Text = tb.Text, FontSize = tb.FontSize, Foreground = tb.Foreground, FontFamily = Theme.UIFont, FontWeight = FontWeights.SemiBold };
                Canvas.SetLeft(t, at.X + 3); Canvas.SetTop(t, at.Y + 2);
                int idx = shapes.Children.IndexOf(tb); shapes.Children.Remove(tb); shapes.Children.Add(t);
                markupUndo.Push(() => shapes.Children.Remove(t));
                dirty = true; shapes.IsHitTestVisible = false;
            };
        }

        void UndoMarkup() { if (markupUndo.Count > 0) markupUndo.Pop()(); }

        // ---------- loading ----------
        async Task LoadFile()
        {
            int my = ++gen;
            path = files[fileIndex];
            title.Text = Path.GetFileName(path);
            pdf = null; image = null; pdfPages.Clear(); pageIndex = 0; rotation = 0; flipH = flipV = false;
            shapes.Children.Clear(); ink.Strokes.Clear(); markupUndo.Clear(); dirty = false;
            thumbs.Children.Clear();
            Title = Path.GetFileName(path);
            try
            {
                if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    pdf = await PdfDoc.Open(path);
                    if (my != gen) return;
                    string pw = null;
                    while (pdf != null && pdf.Locked)
                    {
                        pw = MacAlert.Prompt(this, "This document is password protected.", "Enter the password to open “" + Path.GetFileName(path) + "”.", "", "OK");
                        if (pw == null) { Close(); return; }
                        pdf = await PdfDoc.Open(path, pw);
                    }
                    if (pdf == null || pdf.PageCount == 0) { status.Text = "Can’t open this PDF."; return; }
                    subtitle.Text = "Page 1 of " + pdf.PageCount;
                    BuildPdfThumbs();
                    await ShowPage(0);
                }
                else
                {
                    var bmp = await Task.Run(() => Thumbs.LoadImage(path, 0));
                    if (my != gen) return;
                    if (bmp == null) { status.Text = "Can’t open this file."; return; }
                    image = bmp;
                    var fi = new FileInfo(path);
                    subtitle.Text = bmp.PixelWidth + " × " + bmp.PixelHeight + " — " + Format.Size(fi.Length);
                    AddThumb(0, bmp, null);
                    ShowImage();
                }
            }
            catch { status.Text = "Can’t open this file."; }
            thumbPane.Visibility = pdf != null || files.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (files.Count > 1 && pdf == null) BuildSiblingThumbs();
            FitToWindow();
            UpdateStatus();
        }

        void BuildSiblingThumbs()
        {
            thumbs.Children.Clear();
            for (int i = 0; i < files.Count; i++)
            {
                int idx = i;
                var f = files[i];
                var img = new Image { Width = 110, Height = 110, Stretch = Stretch.Uniform };
                var t = Thumbs.Get(f, 128, () => { var tt = Thumbs.Peek(f, 128); if (tt?.Image != null) img.Source = tt.Image; });
                if (t?.Image != null) img.Source = t.Image;
                var nm = new TextBlock { Text = Path.GetFileName(f), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 120, Margin = new Thickness(0, 4, 0, 0) };
                nm.SetResourceReference(TextBlock.ForegroundProperty, "Text");
                var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) }; sp.Children.Add(img); sp.Children.Add(nm);
                var b = new Border { Child = sp, CornerRadius = new CornerRadius(6), Margin = new Thickness(8, 1, 8, 1), Padding = new Thickness(6), Background = i == fileIndex ? Theme.SidebarSel : Brushes.Transparent, Cursor = Cursors.Hand, Tag = idx };
                b.MouseLeftButtonDown += async (s, e) => { if (idx == fileIndex) return; if (!ConfirmDiscard()) return; fileIndex = idx; await LoadFile(); };
                thumbs.Children.Add(b);
            }
        }

        void BuildPdfThumbs()
        {
            thumbs.Children.Clear();
            for (int i = 0; i < pdf.PageCount; i++)
            {
                int idx = i;
                var img = new Image { Width = 108, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 3) };
                var (pw, ph) = pdf.PageSize(i);
                img.Height = 108 * ph / pw;
                var num = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }; num.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                var sp = new StackPanel(); sp.Children.Add(img); sp.Children.Add(num);
                var b = new Border { Child = sp, CornerRadius = new CornerRadius(5), Margin = new Thickness(10, 3, 10, 3), Padding = new Thickness(5), Background = i == 0 ? Theme.SidebarSel : Brushes.Transparent, Cursor = Cursors.Hand, Tag = idx };
                b.MouseLeftButtonDown += async (s, e) => await ShowPage(idx);
                thumbs.Children.Add(b);
                _ = RenderThumb(img, i);
            }
        }

        async Task RenderThumb(Image img, int i) { var bmp = await pdf.Render(i, 220); if (bmp != null) img.Source = bmp; }

        void AddThumb(int i, BitmapSource bmp, string tip)
        {
            thumbs.Children.Clear();
            var img = new Image { Width = 110, Source = bmp, Stretch = Stretch.Uniform, MaxHeight = 140 };
            thumbs.Children.Add(new Border { Child = img, Margin = new Thickness(10), Padding = new Thickness(5), CornerRadius = new CornerRadius(5), Background = Theme.SidebarSel });
        }

        async Task ShowPage(int i)
        {
            if (pdf == null || i < 0 || i >= pdf.PageCount) return;
            pageIndex = i;
            int my = ++gen;
            var bmp = await pdf.Render(i, (int)Math.Max(1400, viewer.ActualWidth * 2));
            if (my != gen) return;
            image = bmp;
            ShowImage();
            subtitle.Text = "Page " + (i + 1) + " of " + pdf.PageCount;
            foreach (Border b in thumbs.Children.OfType<Border>()) b.Background = (int)b.Tag == i ? Theme.SidebarSel : Brushes.Transparent;
            if (fit) FitToWindow();
            UpdateStatus();
        }

        void ShowImage()
        {
            if (image == null) return;
            pageImg.Source = image;
            ApplyTransform();
        }

        void ApplyTransform()
        {
            if (image == null) return;
            var g = new TransformGroup();
            g.Children.Add(new ScaleTransform(flipH ? -1 : 1, flipV ? -1 : 1, 0.5, 0.5));
            pageImg.RenderTransformOrigin = new Point(0.5, 0.5);
            double w = image.PixelWidth * 96.0 / (image.DpiX <= 0 ? 96 : image.DpiX), h = image.PixelHeight * 96.0 / (image.DpiY <= 0 ? 96 : image.DpiY);
            if (pdf != null) { var (pw, ph) = pdf.PageSize(pageIndex); w = pw; h = ph; }
            bool swap = rotation % 180 != 0;
            pageImg.Width = w; pageImg.Height = h;
            stage.Width = swap ? h : w; stage.Height = swap ? w : h;
            g.Children.Add(new RotateTransform(rotation, 0.5, 0.5));
            pageImg.RenderTransform = new TransformGroup { Children = { new ScaleTransform(flipH ? -1 : 1, flipV ? -1 : 1), new RotateTransform(rotation) } };
            pageImg.HorizontalAlignment = HorizontalAlignment.Center; pageImg.VerticalAlignment = VerticalAlignment.Center;
            shapes.Width = stage.Width; shapes.Height = stage.Height; ink.Width = stage.Width; ink.Height = stage.Height;
            UpdateStatus();
        }

        void Rotate(int deg) { rotation = (rotation + deg + 360) % 360; ApplyTransform(); if (fit) FitToWindow(); dirty = true; }

        // ---------- zoom ----------
        void FitToWindow()
        {
            fit = true;
            if (stage.Width <= 0 || double.IsNaN(stage.Width) || viewer.ActualWidth <= 0) return;
            double z = Math.Min((viewer.ActualWidth - 40) / stage.Width, (viewer.ActualHeight - 40) / stage.Height);
            ApplyZoom(Math.Min(z, 1.0 + (pdf != null ? 2 : 0)));
        }

        void SetZoom(double z) { fit = false; ApplyZoom(Math.Max(0.05, Math.Min(16, z))); }

        void ApplyZoom(double z)
        {
            zoom = z; stageScale.ScaleX = z; stageScale.ScaleY = z;
            CenterStage(); UpdateStatus();
        }

        void CenterStage()
        {
            if (viewer.Content is Grid g)
            {
                g.MinWidth = Math.Max(0, viewer.ViewportWidth - 40); g.MinHeight = Math.Max(0, viewer.ViewportHeight - 40);
                g.Margin = new Thickness(20);
            }
        }

        void UpdateStatus() { status.Text = Math.Round(zoom * 100) + "%" + (dirty ? "  —  Edited" : ""); }

        // ---------- keyboard ----------
        async void OnKey(object s, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (Keyboard.FocusedElement is TextBox) return;
            if (ctrl && e.Key == Key.W) { Close(); e.Handled = true; }
            else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { SetZoom(zoom * 1.25); e.Handled = true; }
            else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { SetZoom(zoom / 1.25); e.Handled = true; }
            else if (ctrl && e.Key == Key.D0) { FitToWindow(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D1) { SetZoom(1); e.Handled = true; }
            else if (ctrl && e.Key == Key.L) { Rotate(-90); e.Handled = true; }
            else if (ctrl && e.Key == Key.R) { Rotate(90); e.Handled = true; }
            else if (ctrl && e.Key == Key.S) { SaveCurrent(); e.Handled = true; }
            else if (ctrl && shift && e.Key == Key.S) { ExportAs(); e.Handled = true; }
            else if (ctrl && e.Key == Key.Z) { UndoMarkup(); e.Handled = true; }
            else if (ctrl && e.Key == Key.C && image != null) { Clipboard.SetImage(Flatten()); e.Handled = true; }
            else if (ctrl && e.Key == Key.P) { Print(); e.Handled = true; }
            else if (e.Key == Key.Escape) { if (tool != Tool.Select) SetTool(Tool.Select); else Close(); }
            else if (e.Key == Key.Right || e.Key == Key.PageDown || e.Key == Key.Down && pdf != null)
            {
                if (pdf != null) await ShowPage(pageIndex + 1);
                else if (fileIndex + 1 < files.Count && ConfirmDiscard()) { fileIndex++; await LoadFile(); }
                e.Handled = true;
            }
            else if (e.Key == Key.Left || e.Key == Key.PageUp || e.Key == Key.Up && pdf != null)
            {
                if (pdf != null) await ShowPage(pageIndex - 1);
                else if (fileIndex > 0 && ConfirmDiscard()) { fileIndex--; await LoadFile(); }
                e.Handled = true;
            }
        }

        // ---------- save / export ----------
        BitmapSource Flatten()
        {
            double w = stage.Width, h = stage.Height;
            if (double.IsNaN(w) || w <= 0) return image;
            double scale = image != null ? Math.Max(1, image.PixelWidth / (pdf != null ? w : (stage.Width))) : 1;
            if (pdf != null) scale = Math.Max(1, image.PixelWidth / w);
            var oldT = stage.LayoutTransform; stage.LayoutTransform = Transform.Identity;
            stage.Measure(new Size(w, h)); stage.Arrange(new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(stage);
            stage.LayoutTransform = oldT;
            stage.InvalidateMeasure();
            rtb.Freeze();
            return rtb;
        }

        bool SaveCurrent()
        {
            if (pdf != null) { MacAlert.Show(this, "PDF editing isn’t supported.", "Use Export as Image to save the page with your markup.", new[] { "OK" }); return false; }
            try
            {
                var bmp = Flatten();
                var ext = Path.GetExtension(path).ToLowerInvariant();
                BitmapEncoder enc = ext == ".png" ? new PngBitmapEncoder() : ext == ".bmp" ? new BmpBitmapEncoder() : ext == ".gif" ? new GifBitmapEncoder() : ext == ".tif" || ext == ".tiff" ? new TiffBitmapEncoder() : (BitmapEncoder)new JpegBitmapEncoder { QualityLevel = 95 };
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = new FileStream(path, FileMode.Create)) enc.Save(fs);
                Thumbs.Invalidate(path);
                dirty = false; UpdateStatus();
                FileOps.NotifyExternal(Path.GetDirectoryName(path));
                return true;
            }
            catch (Exception ex) { MacAlert.Show(this, "The document couldn’t be saved.", ex.Message, new[] { "OK" }); return false; }
        }

        void ExportAs()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(path), Filter = "PNG|*.png|JPEG|*.jpg|TIFF|*.tif|BMP|*.bmp" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var bmp = Flatten();
                var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                BitmapEncoder enc = ext == ".png" ? new PngBitmapEncoder() : ext == ".bmp" ? new BmpBitmapEncoder() : ext == ".tif" ? new TiffBitmapEncoder() : (BitmapEncoder)new JpegBitmapEncoder { QualityLevel = 95 };
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = new FileStream(dlg.FileName, FileMode.Create)) enc.Save(fs);
                FileOps.NotifyExternal(Path.GetDirectoryName(dlg.FileName));
            }
            catch (Exception ex) { MacAlert.Show(this, "The export failed.", ex.Message, new[] { "OK" }); }
        }

        void Print()
        {
            try
            {
                var pd = new PrintDialog();
                if (pd.ShowDialog() != true) return;
                var bmp = Flatten();
                var img = new Image { Source = bmp, Stretch = Stretch.Uniform, Width = pd.PrintableAreaWidth, Height = pd.PrintableAreaHeight };
                pd.PrintVisual(img, Path.GetFileName(path));
            }
            catch { }
        }

        bool ConfirmDiscard()
        {
            if (!dirty) return true;
            int r = MacAlert.Show(this, "Do you want to save the changes made to “" + Path.GetFileName(path) + "”?", "Your changes will be lost if you don’t save them.", new[] { "Save", "Cancel", "Don’t Save" }, 0, cancelIndex: 1);
            if (r == 0) return SaveCurrent();
            return r == 2;
        }

        void OnClosing(object s, System.ComponentModel.CancelEventArgs e)
        {
            if (!ConfirmDiscard()) e.Cancel = true;
        }
    }
}

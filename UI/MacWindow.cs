using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Finderly.Core;

namespace Finderly.UI
{
    /// <summary>Removes the Windows title bar, keeps resize/snap/shadow, and turns on acrylic + rounded corners.</summary>
    public static class WindowFrame
    {
        public static bool BackdropSupported => Environment.OSVersion.Version.Build >= 22621 && Environment.GetEnvironmentVariable("FINDERLY_NOBACKDROP") != "1";

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        public static void Attach(Window w, bool backdrop, bool resizable = true)
        {
            backdrop &= BackdropSupported;
            if (Environment.GetEnvironmentVariable("FINDERLY_NOFRAME") == "1") { w.SetResourceReference(Control.BackgroundProperty, "ContentBg"); return; }
            w.Background = backdrop ? Brushes.Transparent : null;
            if (!backdrop) w.SetResourceReference(Control.BackgroundProperty, "ContentBg");
            w.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(w).Handle;
                var src = HwndSource.FromHwnd(h);
                if (backdrop) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                src.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) => Hook(hwnd, msg, wp, lp, ref handled, resizable));
                int pref = 2; Native.DwmSetWindowAttribute(h, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, 4);
                SetDark(h, Theme.Dark);
                var m = backdrop ? new Native.MARGINS { L = -1, R = -1, T = -1, B = -1 } : new Native.MARGINS { L = 0, R = 0, T = 1, B = 0 };
                Native.DwmExtendFrameIntoClientArea(h, ref m);
                if (backdrop) { int t = 3; Native.DwmSetWindowAttribute(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref t, 4); }
                SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x0020 | 0x0002 | 0x0001 | 0x0004 | 0x0010);
            };
            Theme.Changed += () =>
            {
                var h = new WindowInteropHelper(w).Handle;
                if (h != IntPtr.Zero) SetDark(h, Theme.Dark);
            };
        }

        public static void SetDark(IntPtr h, bool dark)
        {
            int v = dark ? 1 : 0;
            Native.DwmSetWindowAttribute(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4);
        }

        static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled, bool resizable)
        {
            if (msg == 0x0083 && wParam != IntPtr.Zero) // WM_NCCALCSIZE
            {
                if (Native.IsZoomed(hwnd))
                {
                    var p = Marshal.PtrToStructure<Native.NCCALCSIZE_PARAMS>(lParam);
                    uint dpi = Native.GetDpiForWindow(hwnd);
                    int fx = Native.GetSystemMetricsForDpi(32, dpi) + Native.GetSystemMetricsForDpi(92, dpi);
                    int fy = Native.GetSystemMetricsForDpi(33, dpi) + Native.GetSystemMetricsForDpi(92, dpi);
                    p.r0.Left += fx; p.r0.Right -= fx; p.r0.Top += fy; p.r0.Bottom -= fy;
                    Marshal.StructureToPtr(p, lParam, false);
                }
                handled = true;
                return IntPtr.Zero;
            }
            // Stop Windows repainting the classic frame when the window (de)activates: that is the white edge bar.
            if (msg == 0x0086) { handled = true; return (IntPtr)1; }   // WM_NCACTIVATE
            if (msg == 0x0085) { handled = true; return IntPtr.Zero; }  // WM_NCPAINT
            if (msg == 0x0084 && resizable && !Native.IsZoomed(hwnd)) // WM_NCHITTEST
            {
                int x = (short)((long)lParam & 0xFFFF), y = (short)(((long)lParam >> 16) & 0xFFFF);
                Native.GetWindowRect(hwnd, out var r);
                double scale = Native.GetDpiForWindow(hwnd) / 96.0;
                int b = (int)Math.Round(6 * scale);
                bool l = x < r.Left + b, rt = x >= r.Right - b, t = y < r.Top + b, bt = y >= r.Bottom - b;
                int ht = 0;
                if (t && l) ht = 13; else if (t && rt) ht = 14; else if (bt && l) ht = 16; else if (bt && rt) ht = 17;
                else if (l) ht = 10; else if (rt) ht = 11; else if (t) ht = 12; else if (bt) ht = 15;
                if (ht != 0) { handled = true; return (IntPtr)ht; }
            }
            return IntPtr.Zero;
        }

        public static void ToggleMax(Window w) => w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        /// <summary>Makes an element act as a title bar (drag to move, double-click to zoom).</summary>
        public static void MakeDragArea(UIElement el, Window w, bool zoomOnDoubleClick = true)
        {
            el.MouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject d && IsInteractive(d, el)) return;
                if (e.ClickCount == 2) { if (zoomOnDoubleClick && w.ResizeMode != ResizeMode.NoResize) ToggleMax(w); e.Handled = true; return; }
                try { w.DragMove(); } catch { }
            };
        }

        static bool IsInteractive(DependencyObject d, UIElement stop)
        {
            while (d != null && d != stop)
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase || d is TextBox || d is TrafficLights || d is Slider) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }
    }

    /// <summary>The red / yellow / green window buttons.</summary>
    public sealed class TrafficLights : FrameworkElement
    {
        public bool CanMinimize = true, CanZoom = true;
        public Action OnClose, OnZoom;
        int hot = -1, pressed = -1;
        bool hoverGroup;
        Window win;

        static readonly Color[] Fill = { Color.FromRgb(0xFF, 0x5F, 0x57), Color.FromRgb(0xFE, 0xBC, 0x2E), Color.FromRgb(0x28, 0xC8, 0x40) };
        static readonly Color[] Edge = { Color.FromRgb(0xE2, 0x46, 0x3F), Color.FromRgb(0xE1, 0xA1, 0x16), Color.FromRgb(0x14, 0xAE, 0x2C) };
        static readonly Color[] Glyph = { Color.FromRgb(0x8C, 0x1A, 0x10), Color.FromRgb(0x98, 0x57, 0x12), Color.FromRgb(0x0B, 0x65, 0x0D) };

        public TrafficLights()
        {
            Width = 54; Height = 14;
            Cursor = Cursors.Arrow;
            Loaded += (s, e) =>
            {
                win = Window.GetWindow(this);
                if (win != null)
                {
                    win.Activated += (a, b) => InvalidateVisual();
                    win.Deactivated += (a, b) => InvalidateVisual();
                }
            };
            Theme.Changed += InvalidateVisual;
        }

        int Hit(Point p)
        {
            for (int i = 0; i < 3; i++)
            {
                var c = new Point(7 + i * 20, 7);
                if ((p - c).Length <= 7.5) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e) { hoverGroup = true; var h = Hit(e.GetPosition(this)); if (h != hot) { hot = h; } InvalidateVisual(); }
        protected override void OnMouseLeave(MouseEventArgs e) { hoverGroup = false; hot = -1; pressed = -1; InvalidateVisual(); }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { pressed = Hit(e.GetPosition(this)); CaptureMouse(); InvalidateVisual(); e.Handled = true; }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            ReleaseMouseCapture();
            var h = Hit(e.GetPosition(this));
            var p = pressed; pressed = -1; InvalidateVisual();
            e.Handled = true;
            if (h != p || h < 0 || win == null) return;
            if (h == 0) { if (OnClose != null) OnClose(); else win.Close(); }
            else if (h == 1 && CanMinimize) win.WindowState = WindowState.Minimized;
            else if (h == 2 && CanZoom) { if (OnZoom != null) OnZoom(); else WindowFrame.ToggleMax(win); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            bool active = win == null || win.IsActive || hoverGroup;
            for (int i = 0; i < 3; i++)
            {
                var c = new Point(7 + i * 20, 7);
                bool enabled = i == 0 || (i == 1 ? CanMinimize : CanZoom);
                Brush fill, edge;
                if (!active || !enabled) { fill = Theme.LightsInactive; edge = Theme.Separator; }
                else
                {
                    var f = Fill[i];
                    if (pressed == i) f = Color.FromRgb((byte)(f.R * 0.8), (byte)(f.G * 0.8), (byte)(f.B * 0.8));
                    fill = new SolidColorBrush(f); edge = new SolidColorBrush(Edge[i]);
                }
                dc.DrawEllipse(fill, new Pen(edge, 0.5), c, 6, 6);
                if (hoverGroup && enabled)
                {
                    var pen = new Pen(new SolidColorBrush(Glyph[i]), 1.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    if (i == 0) { dc.DrawLine(pen, new Point(c.X - 2.6, c.Y - 2.6), new Point(c.X + 2.6, c.Y + 2.6)); dc.DrawLine(pen, new Point(c.X + 2.6, c.Y - 2.6), new Point(c.X - 2.6, c.Y + 2.6)); }
                    else if (i == 1) { dc.DrawLine(new Pen(pen.Brush, 1.3), new Point(c.X - 3.2, c.Y), new Point(c.X + 3.2, c.Y)); }
                    else
                    {
                        var g = new StreamGeometry();
                        using (var ctx = g.Open())
                        {
                            ctx.BeginFigure(new Point(c.X - 3, c.Y - 3), true, true); ctx.LineTo(new Point(c.X + 1.6, c.Y - 3), true, false); ctx.LineTo(new Point(c.X - 3, c.Y + 1.6), true, false);
                            ctx.BeginFigure(new Point(c.X + 3, c.Y + 3), true, true); ctx.LineTo(new Point(c.X - 1.6, c.Y + 3), true, false); ctx.LineTo(new Point(c.X + 3, c.Y - 1.6), true, false);
                        }
                        dc.DrawGeometry(pen.Brush, null, g);
                    }
                }
            }
        }
    }

    public static class MacChrome
    {
        /// <summary>Builds a small utility window layout: title bar with traffic lights + content.</summary>
        public static Grid Build(Window w, string title, UIElement content, out TextBlock titleBlock, bool canZoom = false)
        {
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            g.RowDefinitions.Add(new RowDefinition());
            g.SetResourceReference(Panel.BackgroundProperty, "PanelBg");
            var bar = new Grid { Background = Brushes.Transparent };
            var lights = new TrafficLights { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), CanMinimize = w.ResizeMode != ResizeMode.NoResize, CanZoom = canZoom };
            titleBlock = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(70, 0, 70, 0) };
            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            bar.Children.Add(titleBlock);
            bar.Children.Add(lights);
            WindowFrame.MakeDragArea(bar, w, canZoom);
            g.Children.Add(bar);
            Grid.SetRow((UIElement)content, 1);
            g.Children.Add(content);
            return g;
        }

        public static void Setup(Window w)
        {
            w.FontFamily = Theme.UIFont;
            w.FontSize = 13;
            w.UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
            w.SetResourceReference(Control.ForegroundProperty, "Text");
        }

        public static ImageSource AppIcon(bool preview = false)
        {
            try
            {
                var b = new BitmapImage(new Uri("pack://application:,,,/Assets/" + (preview ? "Preview" : "Finderly") + ".png"));
                b.Freeze();
                return b;
            }
            catch { return FolderArt.Get(null, 128); }
        }

        public static TextBlock Label(string s, double size = 13, bool bold = false, string brush = "Text")
        {
            var t = new TextBlock { Text = s, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        public static Border Hairline(bool vertical = false)
        {
            var b = new Border { SnapsToDevicePixels = true };
            if (vertical) b.Width = 1; else b.Height = 1;
            b.SetResourceReference(Border.BackgroundProperty, "Separator");
            return b;
        }

        /// <summary>A popup button (label + chevrons) that opens a menu of choices.</summary>
        public static Button PopupButton(IList<string> options, int selected, Action<int> onPick, double minWidth = 140)
        {
            var label = new TextBlock { Text = selected >= 0 && selected < options.Count ? options[selected] : "", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var chev = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,4 L3,1 L6,4 M0,8 L3,11 L6,8"), StrokeThickness = 1.3, Width = 7, Height = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            chev.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Text");
            var dp = new DockPanel { MinWidth = minWidth - 30 };
            DockPanel.SetDock(chev, Dock.Right);
            dp.Children.Add(chev); dp.Children.Add(label);
            var b = new Button { Content = dp, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(9, 2, 7, 2), MinWidth = minWidth, HorizontalAlignment = HorizontalAlignment.Left };
            int cur = selected;
            b.Click += (s, e) =>
            {
                var cm = new ContextMenu { PlacementTarget = b, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                for (int i = 0; i < options.Count; i++)
                {
                    int idx = i;
                    var mi = new MenuItem { Header = options[i], IsChecked = i == cur };
                    mi.Click += (a, c) => { cur = idx; label.Text = options[idx]; onPick(idx); };
                    cm.Items.Add(mi);
                }
                cm.IsOpen = true;
            };
            return b;
        }
    }

    /// <summary>macOS-style alert sheet.</summary>
    public static class MacAlert
    {
        public static int Show(Window owner, string title, string message, string[] buttons, int defaultIndex = 0, string checkbox = null, Action<bool> checkResult = null, ImageSource icon = null, int cancelIndex = -1)
        {
            int result = cancelIndex >= 0 ? cancelIndex : buttons.Length - 1;
            var w = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowInTaskbar = owner == null,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                Title = title,
            };
            MacChrome.Setup(w);
            var panel = new StackPanel { Width = 260, Margin = new Thickness(16, 18, 16, 16) };
            panel.Children.Add(new Image { Source = icon ?? MacChrome.AppIcon(), Width = 64, Height = 64, Margin = new Thickness(0, 0, 0, 12) });
            var t = MacChrome.Label(title, 13, true);
            t.TextWrapping = TextWrapping.Wrap; t.TextAlignment = TextAlignment.Center; t.TextTrimming = TextTrimming.None;
            panel.Children.Add(t);
            if (!string.IsNullOrEmpty(message))
            {
                var m = MacChrome.Label(message, 11.5);
                m.TextWrapping = TextWrapping.Wrap; m.TextAlignment = TextAlignment.Center; m.TextTrimming = TextTrimming.None; m.Margin = new Thickness(0, 6, 0, 0);
                panel.Children.Add(m);
            }
            CheckBox cb = null;
            if (checkbox != null)
            {
                cb = new CheckBox { Content = checkbox, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
                panel.Children.Add(cb);
            }
            Panel btns;
            if (buttons.Length <= 2)
            {
                var ug = new UniformGrid2(buttons.Length) { Margin = new Thickness(0, 16, 0, 0) };
                btns = ug;
            }
            else btns = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var order = buttons.Length == 2 ? new[] { (defaultIndex == 0 ? 1 : 0), defaultIndex } : Enumerable.Range(0, buttons.Length).OrderBy(i => i == defaultIndex ? 0 : 1).ToArray();
            foreach (var i in order)
            {
                int idx = i;
                var b = new Button { Content = buttons[i], IsDefault = i == defaultIndex, IsCancel = i == cancelIndex, Margin = new Thickness(buttons.Length <= 2 ? 4 : 0, 3, buttons.Length <= 2 ? 4 : 0, 3), MinHeight = 26 };
                b.Click += (s, e) => { result = idx; w.Close(); };
                btns.Children.Add(b);
            }
            panel.Children.Add(btns);
            var border = new Border { CornerRadius = new CornerRadius(12), Child = panel, Margin = new Thickness(20), BorderThickness = new Thickness(0.5) };
            border.SetResourceReference(Border.BackgroundProperty, "PanelBg");
            border.SetResourceReference(Border.BorderBrushProperty, "MenuBorder");
            border.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.35, Direction = 270 };
            border.MouseLeftButtonDown += (s, e) => { try { w.DragMove(); } catch { } };
            w.Content = border;
            w.KeyDown += (s, e) => { if (e.Key == Key.Escape) { result = cancelIndex >= 0 ? cancelIndex : buttons.Length - 1; w.Close(); } };
            w.ShowDialog();
            checkResult?.Invoke(cb?.IsChecked == true);
            return result;
        }

        public static string Prompt(Window owner, string title, string message, string initial, string ok = "OK")
        {
            string result = null;
            var w = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, Owner = owner, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            MacChrome.Setup(w);
            var panel = new StackPanel { Width = 300, Margin = new Thickness(18) };
            panel.Children.Add(MacChrome.Label(title, 13, true));
            if (message != null) { var m = MacChrome.Label(message, 11.5); m.TextWrapping = TextWrapping.Wrap; m.Margin = new Thickness(0, 4, 0, 8); panel.Children.Add(m); }
            var tb = new TextBox { Text = initial ?? "", Margin = new Thickness(0, 6, 0, 0), Height = 24 };
            panel.Children.Add(tb);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
            var okB = new Button { Content = ok, IsDefault = true };
            cancel.Click += (s, e) => w.Close();
            okB.Click += (s, e) => { result = tb.Text; w.Close(); };
            sp.Children.Add(cancel); sp.Children.Add(okB);
            panel.Children.Add(sp);
            var border = new Border { CornerRadius = new CornerRadius(12), Child = panel, Margin = new Thickness(20), BorderThickness = new Thickness(0.5) };
            border.SetResourceReference(Border.BackgroundProperty, "PanelBg");
            border.SetResourceReference(Border.BorderBrushProperty, "MenuBorder");
            border.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.35, Direction = 270 };
            w.Content = border;
            w.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
            w.ShowDialog();
            return result;
        }
    }

    /// <summary>Simple equal-width horizontal panel.</summary>
    public sealed class UniformGrid2 : Panel
    {
        readonly int cols;
        public UniformGrid2(int c) { cols = Math.Max(1, c); }
        protected override Size MeasureOverride(Size a)
        {
            double w = double.IsInfinity(a.Width) ? 0 : a.Width / cols, h = 0, mw = 0;
            foreach (UIElement c in InternalChildren) { c.Measure(new Size(w > 0 ? w : double.PositiveInfinity, a.Height)); h = Math.Max(h, c.DesiredSize.Height); mw = Math.Max(mw, c.DesiredSize.Width); }
            return new Size(w > 0 ? a.Width : mw * cols, h);
        }
        protected override Size ArrangeOverride(Size f)
        {
            double w = f.Width / cols; int i = 0;
            foreach (UIElement c in InternalChildren) c.Arrange(new Rect(i++ * w, 0, w, f.Height));
            return f;
        }
    }
}

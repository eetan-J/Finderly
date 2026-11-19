using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Finderly.Core;

namespace Finderly.UI
{
    /// <summary>Translucent icon + name that follows the cursor while dragging (Finder drag image).</summary>
    public static class DragGhost
    {
        static Window win;
        static double scale = 1;

        public static void Begin(List<FileItem> items, double dpi)
        {
            End();
            scale = dpi;
            var first = items[0];
            var grid = new Grid { Opacity = 0.75, IsHitTestVisible = false };
            var sp = new StackPanel();
            sp.Children.Add(new Image { Source = IconPainter.Snapshot(first, 56), Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center });
            var name = new TextBlock { Text = first.DisplayName, Foreground = Brushes.White, FontSize = 12, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
            sp.Children.Add(new Border { Child = name, Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x63, 0xD8)), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), HorizontalAlignment = HorizontalAlignment.Center });
            grid.Children.Add(sp);
            if (items.Count > 1)
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30)),
                    CornerRadius = new CornerRadius(10),
                    MinWidth = 20, Height = 20,
                    Padding = new Thickness(5, 0, 5, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -4, 30, 0),
                    Child = new TextBlock { Text = items.Count.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
                };
                grid.Children.Add(badge);
            }
            win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                IsHitTestVisible = false,
                Focusable = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = grid,
                FontFamily = Theme.UIFont,
                Width = 160,
            };
            win.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(win).Handle;
                int ex = Native.GetWindowLong(h, -20);
                Native.SetWindowLong(h, -20, ex | 0x20 | 0x80000 | 0x08000000 | 0x80); // TRANSPARENT | LAYERED | NOACTIVATE | TOOLWINDOW
            };
            Move();
            win.Show();
        }

        public static void Move()
        {
            if (win == null) return;
            Native.GetCursorPos(out var p);
            double w = win.ActualWidth > 0 ? win.ActualWidth : 160;
            win.Left = p.X / scale - w / 2;
            win.Top = p.Y / scale - 30;
        }

        public static void End()
        {
            if (win != null) { win.Close(); win = null; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Finderly.Core;
using Finderly.UI;

namespace Finderly.Views
{
    public sealed class Entry
    {
        public FileItem Item;
        public string Header;
        public int Depth;
        public bool Expanded;
        public Rect Bounds;
        public bool IsHeader => Item == null;
    }

    /// <summary>
    /// Base for all custom-drawn file views. Only visible entries are drawn each frame,
    /// which keeps scrolling smooth even for folders with tens of thousands of items.
    /// </summary>
    public abstract class ItemsSurface : FrameworkElement
    {
        protected List<Entry> entries = new List<Entry>();
        protected readonly List<int> items = new List<int>();           // entry indices that are items
        protected Dictionary<FileItem, int> posOf = new Dictionary<FileItem, int>();
        public readonly HashSet<FileItem> Selection = new HashSet<FileItem>();
        protected int focusPos = -1, anchorPos = -1;
        protected double offX, offY, tgtX, tgtY, extentW, extentH;
        protected double dpi = 1;

        public bool ForceActiveLook;
        public bool DimSelection;     // column view: selection in non-focused column
        public bool AllowBackgroundDrop = true;
        public HashSet<string> CutPaths;   // dim items that are cut

        public event Action SelectionChanged;
        public event Action<FileItem, bool> ItemActivated;
        public event Action<FileItem> ContextRequested;
        public event Action<FileItem> RenameRequested;
        public event Action<string, IDataObject, DragDropEffects> DropRequested;
        public event Action<FileItem> SpringLoad;
        public event Action<int> ZoomRequested;
        public event Action Scrolled;
        public event Action<List<FileItem>> DragStarting;

        protected virtual bool HorizontalScroll => false;
        protected virtual double TopInset => 0;
        protected virtual double BottomInset => 0;

        protected ItemsSurface()
        {
            Focusable = true;
            FocusVisualStyle = null;
            AllowDrop = true;
            ClipToBounds = true;
            SnapsToDevicePixels = true;
            IsKeyboardFocusWithinChanged += (s, e) => InvalidateVisual();
            Theme.Changed += InvalidateVisual;
            Loaded += (s, e) => { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; Relayout(); };
        }

        public IReadOnlyList<Entry> Entries => entries;
        public int ItemCount => items.Count;
        public FileItem ItemAt(int pos) => pos >= 0 && pos < items.Count ? entries[items[pos]].Item : null;
        public FileItem FocusedItem => ItemAt(focusPos);
        public bool LooksActive => IsKeyboardFocusWithin || ForceActiveLook;
        protected double VpW => Math.Max(0, ActualWidth);
        protected double VpH => Math.Max(0, ActualHeight - TopInset - BottomInset);

        public List<FileItem> SelectedInOrder()
        {
            var l = new List<FileItem>(Selection.Count);
            foreach (var i in items) if (Selection.Contains(entries[i].Item)) l.Add(entries[i].Item);
            return l;
        }

        public void SetEntries(List<Entry> list, bool resetScroll)
        {
            var oldFocus = FocusedItem;
            var oldAnchor = ItemAt(anchorPos);
            entries = list;
            items.Clear();
            posOf = new Dictionary<FileItem, int>(list.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].IsHeader) continue;
                if (!posOf.ContainsKey(entries[i].Item)) posOf[entries[i].Item] = items.Count;
                items.Add(i);
            }
            int before = Selection.Count;
            var keep = new List<FileItem>();
            foreach (var s in Selection) if (posOf.TryGetValue(s, out var p)) keep.Add(entries[items[p]].Item);
            Selection.Clear();
            foreach (var k in keep) Selection.Add(k);
            focusPos = oldFocus != null && posOf.TryGetValue(oldFocus, out var fp) ? fp : -1;
            anchorPos = oldAnchor != null && posOf.TryGetValue(oldAnchor, out var ap) ? ap : focusPos;
            if (resetScroll) { offX = offY = tgtX = tgtY = 0; }
            Relayout();
            if (before != Selection.Count) SelectionChanged?.Invoke();
        }

        public void Relayout()
        {
            if (ActualWidth <= 0) { InvalidateVisual(); return; }
            DoLayout(VpW, VpH);
            Clamp();
            InvalidateVisual();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); Relayout(); }

        protected abstract void DoLayout(double width, double height);
        protected abstract void DrawEntry(DrawingContext dc, int entryIndex, Entry e, bool selected);
        protected virtual void DrawUnderlay(DrawingContext dc, Rect visible) { }
        protected virtual void DrawOverlay(DrawingContext dc) { }
        protected virtual bool HitEntry(Entry e, Point content) => e.Bounds.Contains(content);
        protected virtual bool IntersectsEntry(Entry e, Rect r) => e.Bounds.IntersectsWith(r);
        public virtual Rect EditRect(FileItem it) => Rect.Empty;   // in surface coordinates
        protected virtual bool HandleClick(Point p, MouseButtonEventArgs e) => false;
        protected virtual bool IsNameHit(Entry e, Point content) => true;

        // ---------- coordinates ----------
        public Point ToContent(Point p) => new Point(p.X + offX, p.Y - TopInset + offY);
        protected virtual Rect IconBox(Entry e) => e.Bounds;

        /// <summary>Screen rectangle (DIPs) of an item's icon, or null. Used to zoom Quick Look out of the icon.</summary>
        public Rect? ItemScreenRect(FileItem it)
        {
            try
            {
                if (it == null || !posOf.TryGetValue(it, out var p)) return null;
                var b = IconBox(entries[items[p]]);
                var tl = ToSurface(b.TopLeft);
                var src = PresentationSource.FromVisual(this); if (src == null) return null;
                var m = src.CompositionTarget.TransformFromDevice;
                var a = m.Transform(PointToScreen(tl));
                var c = m.Transform(PointToScreen(new Point(tl.X + b.Width, tl.Y + b.Height)));
                var r = new Rect(a, c);
                return r.Width < 2 || r.Height < 2 ? (Rect?)null : r;
            }
            catch { return null; }
        }

        public Point ToSurface(Point c) => new Point(c.X - offX, c.Y + TopInset - offY);

        protected int HitTest(Point surfacePoint)
        {
            if (surfacePoint.Y < TopInset || surfacePoint.Y > ActualHeight - BottomInset) return -1;
            var c = ToContent(surfacePoint);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.IsHeader) continue;
                if (e.Bounds.Bottom < c.Y - 400 || e.Bounds.Top > c.Y + 400) continue;
                if (HitEntry(e, c)) return i;
            }
            return -1;
        }

        public FileItem ItemAtPoint(Point p) { var i = HitTest(p); return i >= 0 ? entries[i].Item : null; }

        // ---------- selection API ----------
        public void SelectOnly(FileItem it, bool scroll = true)
        {
            Selection.Clear();
            if (it != null && posOf.TryGetValue(it, out var p)) { Selection.Add(entries[items[p]].Item); focusPos = anchorPos = p; if (scroll) ScrollIntoView(p); }
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        public void SelectMany(IEnumerable<FileItem> list)
        {
            Selection.Clear();
            int first = -1;
            foreach (var it in list)
                if (posOf.TryGetValue(it, out var p)) { Selection.Add(entries[items[p]].Item); if (first < 0 || p < first) first = p; }
            focusPos = anchorPos = first;
            if (first >= 0) ScrollIntoView(first);
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        public void SelectAll()
        {
            Selection.Clear();
            foreach (var i in items) Selection.Add(entries[i].Item);
            InvalidateVisual(); SelectionChanged?.Invoke();
        }

        public void ClearSelection()
        {
            if (Selection.Count == 0) return;
            Selection.Clear(); InvalidateVisual(); SelectionChanged?.Invoke();
        }

        protected void SetFocusPos(int p, bool extend)
        {
            if (items.Count == 0) return;
            p = Math.Max(0, Math.Min(items.Count - 1, p));
            if (extend && anchorPos >= 0)
            {
                Selection.Clear();
                int a = Math.Min(anchorPos, p), b = Math.Max(anchorPos, p);
                for (int i = a; i <= b; i++) Selection.Add(ItemAt(i));
            }
            else
            {
                Selection.Clear(); Selection.Add(ItemAt(p)); anchorPos = p;
            }
            focusPos = p;
            ScrollIntoView(p);
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        public void ScrollIntoView(int pos, bool center = false)
        {
            if (pos < 0 || pos >= items.Count) return;
            var b = entries[items[pos]].Bounds;
            if (HorizontalScroll)
            {
                if (center) tgtX = b.X + b.Width / 2 - VpW / 2;
                else if (b.Left < offX) tgtX = b.Left - 8;
                else if (b.Right > offX + VpW) tgtX = b.Right - VpW + 8;
                Clamp(); offX = tgtX;
            }
            else
            {
                if (b.Top < offY) tgtY = b.Top - 4;
                else if (b.Bottom > offY + VpH) tgtY = b.Bottom - VpH + 4;
                Clamp(); offY = tgtY;
            }
            InvalidateVisual();
            Scrolled?.Invoke();
        }

        public void ScrollIntoView(FileItem it, bool center = false) { if (it != null && posOf.TryGetValue(it, out var p)) ScrollIntoView(p, center); }

        public (double x, double y) ScrollOffset { get => (offX, offY); set { offX = tgtX = value.x; offY = tgtY = value.y; Clamp(); InvalidateVisual(); } }

        protected void Clamp()
        {
            double maxY = Math.Max(0, extentH - VpH), maxX = Math.Max(0, extentW - VpW);
            tgtY = Math.Max(0, Math.Min(maxY, tgtY)); offY = Math.Max(0, Math.Min(maxY, offY));
            tgtX = Math.Max(0, Math.Min(maxX, tgtX)); offX = Math.Max(0, Math.Min(maxX, offX));
        }

        // ---------- rendering ----------
        bool redrawQueued;
        protected void RequestRedraw()
        {
            if (redrawQueued) return;
            redrawQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => { redrawQueued = false; InvalidateVisual(); }));
        }
        protected Action redrawAction;
        protected Action Redraw => redrawAction ??= RequestRedraw;

        protected bool IsSelected(Entry e) => e.Item != null && Selection.Contains(e.Item);

        protected override void OnRender(DrawingContext dc)
        {
            var size = RenderSize;
            dc.DrawRectangle(Theme.ContentBg, null, new Rect(size));
            var vis = new Rect(offX, offY, VpW, VpH);
            dc.PushClip(new RectangleGeometry(new Rect(0, TopInset, size.Width, Math.Max(0, size.Height - TopInset - BottomInset))));
            dc.PushTransform(new TranslateTransform(-offX, -offY + TopInset));
            DrawUnderlay(dc, vis);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Bounds.Bottom < vis.Top || e.Bounds.Top > vis.Bottom) continue;
                if (e.Bounds.Right < vis.Left || e.Bounds.Left > vis.Right) continue;
                DrawEntry(dc, i, e, IsSelected(e));
            }
            if (dropEntry >= 0 && dropEntry < entries.Count)
            {
                var b = entries[dropEntry].Bounds;
                dc.DrawRoundedRectangle(Theme.DropHighlight, new Pen(Theme.Accent, 1.5), new Rect(b.X + 2, b.Y + 1, Math.Max(0, b.Width - 4), Math.Max(0, b.Height - 2)), 5, 5);
            }
            if (rubber)
            {
                var r = new Rect(rubberStart, rubberEnd);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0x7F, 0x7F, 0x7F)), new Pen(new SolidColorBrush(Color.FromArgb(120, 0x80, 0x80, 0x80)), 1), r);
            }
            dc.Pop();
            dc.Pop();
            DrawOverlay(dc);
            if (dropBackground)
                dc.DrawRoundedRectangle(null, new Pen(Theme.Accent, 3), new Rect(1.5, TopInset + 1.5, Math.Max(0, size.Width - 3), Math.Max(0, size.Height - TopInset - BottomInset - 3)), 4, 4);
            DrawScrollbars(dc);
        }

        // ---------- overlay scrollbars ----------
        double sbAlpha; DateTime lastScroll = DateTime.MinValue; bool sbHover, sbDrag; double sbDragStart, sbDragOff;
        bool animating;

        Rect VThumb()
        {
            if (extentH <= VpH + 0.5 || HorizontalScroll) return Rect.Empty;
            double len = Math.Max(24, VpH * VpH / extentH);
            double pos = (VpH - len) * (offY / Math.Max(1, extentH - VpH));
            double w = sbHover || sbDrag ? 9 : 7;
            return new Rect(ActualWidth - w - 3, TopInset + pos + 2, w, Math.Max(0, len - 4));
        }

        Rect HThumb()
        {
            if (extentW <= VpW + 0.5) return Rect.Empty;
            double len = Math.Max(24, VpW * VpW / extentW);
            double pos = (VpW - len) * (offX / Math.Max(1, extentW - VpW));
            double h = sbHover || sbDrag ? 9 : 7;
            return new Rect(pos + 2, ActualHeight - BottomInset - h - 3, Math.Max(0, len - 4), h);
        }

        void DrawScrollbars(DrawingContext dc)
        {
            if (sbAlpha <= 0.01) return;
            dc.PushOpacity(sbAlpha);
            var v = VThumb();
            if (!v.IsEmpty)
            {
                if (sbHover || sbDrag) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(18, 128, 128, 128)), null, new Rect(ActualWidth - 15, TopInset, 15, VpH));
                dc.DrawRoundedRectangle(Theme.ScrollThumb, null, v, v.Width / 2, v.Width / 2);
            }
            var h = HThumb();
            if (!h.IsEmpty) dc.DrawRoundedRectangle(Theme.ScrollThumb, null, h, h.Height / 2, h.Height / 2);
            dc.Pop();
        }

        protected void ShowScrollbars() { lastScroll = DateTime.Now; StartAnim(); }

        void StartAnim()
        {
            if (animating) return;
            animating = true;
            CompositionTarget.Rendering += Tick;
        }

        void Tick(object s, EventArgs e)
        {
            bool busy = false;
            double dy = tgtY - offY, dx = tgtX - offX;
            if (Math.Abs(dy) > 0.4) { offY += dy * 0.32; busy = true; } else offY = tgtY;
            if (Math.Abs(dx) > 0.4) { offX += dx * 0.32; busy = true; } else offX = tgtX;
            var since = (DateTime.Now - lastScroll).TotalMilliseconds;
            double want = since < 900 || sbHover || sbDrag ? 1 : 0;
            if (Math.Abs(sbAlpha - want) > 0.02) { sbAlpha += (want - sbAlpha) * (want > sbAlpha ? 0.5 : 0.12); busy = true; }
            else sbAlpha = want;
            if (since < 1000 || sbHover || sbDrag) busy = true;
            if (Math.Abs(dy) > 0.4 || Math.Abs(dx) > 0.4) Scrolled?.Invoke();
            InvalidateVisual();
            if (!busy) { CompositionTarget.Rendering -= Tick; animating = false; }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { ZoomRequested?.Invoke(e.Delta > 0 ? 1 : -1); e.Handled = true; return; }
            bool horiz = HorizontalScroll || (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (horiz) tgtX -= e.Delta * 0.9; else tgtY -= e.Delta * 0.9;
            Clamp();
            ShowScrollbars();
            e.Handled = true;
        }

        // ---------- mouse ----------
        int pressEntry = -1; Point pressPoint; bool deferSelectOnly; bool pressCtrl;
        bool rubber; Point rubberStart, rubberEnd; HashSet<FileItem> rubberBase;
        DispatcherTimer renameTimer;
        FileItem renameCandidate;
        DateTime lastClick; FileItem lastClickItem;

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            var p = e.GetPosition(this);
            renameTimer?.Stop();
            // scrollbar
            var v = VThumb();
            if (!v.IsEmpty && p.X >= ActualWidth - 15 && p.Y >= TopInset && p.Y <= ActualHeight - BottomInset)
            {
                if (v.Contains(p) || true)
                {
                    if (!v.Contains(p)) { tgtY = offY = (p.Y - TopInset - v.Height / 2) / Math.Max(1, VpH - v.Height) * (extentH - VpH); Clamp(); }
                    sbDrag = true; sbDragStart = p.Y; sbDragOff = offY; CaptureMouse(); ShowScrollbars(); e.Handled = true; return;
                }
            }
            var h = HThumb();
            if (!h.IsEmpty && p.Y >= ActualHeight - BottomInset - 15 && p.Y <= ActualHeight - BottomInset && HorizontalScroll)
            {
                sbDrag = true; sbDragStart = p.X; sbDragOff = offX; CaptureMouse(); ShowScrollbars(); e.Handled = true; return;
            }
            if (HandleClick(p, e)) { e.Handled = true; return; }

            int idx = HitTest(p);
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (e.ClickCount >= 2 && idx >= 0)
            {
                renameCandidate = null;
                ItemActivated?.Invoke(entries[idx].Item, ctrl);
                e.Handled = true;
                return;
            }
            if (idx >= 0)
            {
                var it = entries[idx].Item;
                int pos = posOf[it];
                pressCtrl = ctrl;
                if (ctrl)
                {
                    if (!Selection.Remove(it)) Selection.Add(it);
                    focusPos = anchorPos = pos;
                    SelectionChanged?.Invoke();
                }
                else if (shift && anchorPos >= 0)
                {
                    Selection.Clear();
                    int a = Math.Min(anchorPos, pos), b = Math.Max(anchorPos, pos);
                    for (int i = a; i <= b; i++) Selection.Add(ItemAt(i));
                    focusPos = pos;
                    SelectionChanged?.Invoke();
                }
                else if (!Selection.Contains(it))
                {
                    Selection.Clear(); Selection.Add(it); focusPos = anchorPos = pos;
                    SelectionChanged?.Invoke();
                }
                else
                {
                    deferSelectOnly = Selection.Count > 1;
                    // click on the name of an already-selected single item → rename after a pause (Finder behaviour)
                    if (Selection.Count == 1 && IsNameHit(entries[idx], ToContent(p)) && (DateTime.Now - lastClick).TotalMilliseconds > 500 && lastClickItem != null && lastClickItem.Equals(it))
                        renameCandidate = it;
                    focusPos = pos;
                }
                lastClick = DateTime.Now; lastClickItem = it;
                pressEntry = idx; pressPoint = p;
                CaptureMouse();
                InvalidateVisual();
            }
            else
            {
                if (!ctrl && !shift) ClearSelection();
                rubber = true;
                rubberBase = ctrl || shift ? new HashSet<FileItem>(Selection) : new HashSet<FileItem>();
                rubberStart = rubberEnd = ToContent(p);
                pressPoint = p;
                CaptureMouse();
            }
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            bool nearBar = p.X >= ActualWidth - 15 && extentH > VpH && !HorizontalScroll;
            if (nearBar != sbHover) { sbHover = nearBar; if (nearBar) ShowScrollbars(); InvalidateVisual(); }
            if (sbDrag)
            {
                if (HorizontalScroll || extentH <= VpH)
                {
                    var hh = HThumb();
                    tgtX = offX = sbDragOff + (p.X - sbDragStart) / Math.Max(1, VpW - hh.Width) * (extentW - VpW);
                }
                else
                {
                    var vv = VThumb();
                    tgtY = offY = sbDragOff + (p.Y - sbDragStart) / Math.Max(1, VpH - vv.Height) * (extentH - VpH);
                }
                Clamp(); ShowScrollbars(); InvalidateVisual(); Scrolled?.Invoke();
                return;
            }
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (pressEntry >= 0)
            {
                if (Math.Abs(p.X - pressPoint.X) > 4 || Math.Abs(p.Y - pressPoint.Y) > 4)
                {
                    int pe = pressEntry; pressEntry = -1; deferSelectOnly = false; renameCandidate = null;
                    ReleaseMouseCapture();
                    BeginDrag();
                }
                return;
            }
            if (rubber)
            {
                rubberEnd = ToContent(p);
                // autoscroll
                if (p.Y < TopInset + 10) { tgtY = offY = Math.Max(0, offY - 12); }
                else if (p.Y > ActualHeight - BottomInset - 10) { tgtY = offY = offY + 12; }
                if (HorizontalScroll) { if (p.X < 10) tgtX = offX = offX - 12; else if (p.X > ActualWidth - 10) tgtX = offX = offX + 12; }
                Clamp();
                rubberEnd = ToContent(p);
                var r = new Rect(rubberStart, rubberEnd);
                var sel = new HashSet<FileItem>(rubberBase);
                foreach (var i in items)
                {
                    var en = entries[i];
                    if (IntersectsEntry(en, r)) { if (!sel.Remove(en.Item)) sel.Add(en.Item); }
                }
                if (!sel.SetEquals(Selection))
                {
                    Selection.Clear(); foreach (var s in sel) Selection.Add(s);
                    SelectionChanged?.Invoke();
                }
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (sbDrag) { sbDrag = false; ReleaseMouseCapture(); ShowScrollbars(); return; }
            if (pressEntry >= 0 && deferSelectOnly)
            {
                var it = entries[pressEntry].Item;
                Selection.Clear(); Selection.Add(it); anchorPos = focusPos = posOf[it];
                SelectionChanged?.Invoke();
            }
            if (pressEntry >= 0 && renameCandidate != null)
            {
                var cand = renameCandidate;
                renameTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
                renameTimer.Stop();
                EventHandler h = null;
                h = (s, a) => { renameTimer.Stop(); renameTimer.Tick -= h; if (Selection.Count == 1 && Selection.Contains(cand)) RenameRequested?.Invoke(cand); };
                renameTimer.Tick += h;
                renameTimer.Start();
            }
            renameCandidate = null;
            pressEntry = -1; deferSelectOnly = false;
            if (rubber) { rubber = false; InvalidateVisual(); }
            ReleaseMouseCapture();
        }

        protected override void OnMouseLeave(MouseEventArgs e) { if (sbHover) { sbHover = false; InvalidateVisual(); } }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            var p = e.GetPosition(this);
            if (p.Y < TopInset) { base.OnMouseRightButtonDown(e); return; }
            int idx = HitTest(p);
            if (idx >= 0)
            {
                var it = entries[idx].Item;
                if (!Selection.Contains(it)) { Selection.Clear(); Selection.Add(it); focusPos = anchorPos = posOf[it]; SelectionChanged?.Invoke(); }
                InvalidateVisual();
                ContextRequested?.Invoke(it);
            }
            else
            {
                ClearSelection();
                ContextRequested?.Invoke(null);
            }
            e.Handled = true;
        }

        // ---------- drag & drop ----------
        void BeginDrag()
        {
            var sel = SelectedInOrder().Where(i => i.Special != SpecialKind.Drive).ToList();
            if (sel.Count == 0) return;
            DragStarting?.Invoke(sel);
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, sel.Select(i => i.Path).ToArray());
            data.SetData("FinderlySource", "1");
            DragGhost.Begin(sel, dpi);
            GiveFeedbackEventHandler fb = (s, a) => DragGhost.Move();
            GiveFeedback += fb;
            try { DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            catch { }
            finally { GiveFeedback -= fb; DragGhost.End(); }
        }

        int dropEntry = -1; bool dropBackground;
        DispatcherTimer springTimer; int springEntry = -1;

        public static DragDropEffects EffectFor(DragEventArgs e, string target)
        {
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0 || target == null) return DragDropEffects.None;
            foreach (var p in paths)
            {
                if (string.Equals(System.IO.Path.GetDirectoryName(p.TrimEnd('\\')), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && (e.KeyStates & DragDropKeyStates.AltKey) == 0) return DragDropEffects.None;
                if (string.Equals(p.TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return DragDropEffects.None;
                if (target.StartsWith(p.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return DragDropEffects.None;
            }
            bool alt = (e.KeyStates & DragDropKeyStates.AltKey) != 0, ctrl = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
            if (alt && ctrl) return DragDropEffects.Link;
            if (alt) return DragDropEffects.Copy;
            if (ctrl) return DragDropEffects.Move;
            bool same = string.Equals(System.IO.Path.GetPathRoot(paths[0]), System.IO.Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase);
            return same ? DragDropEffects.Move : DragDropEffects.Copy;
        }

        public string BackgroundDropTarget;   // set by host: current folder path

        protected override void OnDragOver(DragEventArgs e)
        {
            var p = e.GetPosition(this);
            int idx = HitTest(p);
            string target = null; int newDrop = -1; bool bg = false;
            if (idx >= 0 && entries[idx].Item.IsDir && entries[idx].Item.IsReal && !(Selection.Contains(entries[idx].Item) && e.Data.GetDataPresent("FinderlySource")))
            { target = entries[idx].Item.Path; newDrop = idx; }
            else if (AllowBackgroundDrop && BackgroundDropTarget != null) { target = BackgroundDropTarget; bg = true; }
            e.Effects = EffectFor(e, target);
            if (e.Effects == DragDropEffects.None) { newDrop = -1; bg = false; }
            if (newDrop != dropEntry || bg != dropBackground) { dropEntry = newDrop; dropBackground = bg; InvalidateVisual(); }
            // spring-loaded folders
            if (newDrop >= 0)
            {
                if (springEntry != newDrop)
                {
                    springEntry = newDrop;
                    springTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
                    springTimer.Stop();
                    springTimer.Tick -= SpringTick; springTimer.Tick += SpringTick;
                    springTimer.Start();
                }
            }
            else { springEntry = -1; springTimer?.Stop(); }
            // autoscroll
            if (p.Y < TopInset + 16) { tgtY = offY = Math.Max(0, offY - 10); Clamp(); InvalidateVisual(); }
            else if (p.Y > ActualHeight - BottomInset - 16) { tgtY = offY = offY + 10; Clamp(); InvalidateVisual(); }
            e.Handled = true;
        }

        void SpringTick(object s, EventArgs e)
        {
            springTimer.Stop();
            if (springEntry >= 0 && springEntry < entries.Count && entries[springEntry].Item != null)
            {
                var it = entries[springEntry].Item;
                springEntry = -1; dropEntry = -1;
                SpringLoad?.Invoke(it);
            }
        }

        protected override void OnDragLeave(DragEventArgs e) { dropEntry = -1; dropBackground = false; springTimer?.Stop(); springEntry = -1; InvalidateVisual(); }

        protected override void OnDrop(DragEventArgs e)
        {
            springTimer?.Stop();
            var p = e.GetPosition(this);
            string target = dropEntry >= 0 && dropEntry < entries.Count ? entries[dropEntry].Item?.Path : (dropBackground ? BackgroundDropTarget : null);
            var eff = EffectFor(e, target);
            dropEntry = -1; dropBackground = false; InvalidateVisual();
            if (target != null && eff != DragDropEffects.None)
            {
                e.Effects = eff;
                DropRequested?.Invoke(target, e.Data, eff);
            }
            e.Handled = true;
        }

        // ---------- keyboard ----------
        string typeBuf = ""; DateTime typeTime;
        public bool IsTypeSelecting => typeBuf.Length > 0 && (DateTime.Now - typeTime).TotalMilliseconds < 1000;

        protected virtual int Navigate(int from, Key key)
        {
            int n = items.Count;
            switch (key)
            {
                case Key.Up: case Key.Left: return from < 0 ? n - 1 : from - 1;
                case Key.Down: case Key.Right: return from < 0 ? 0 : from + 1;
            }
            return from;
        }

        protected virtual bool OnKey(KeyEventArgs e) => false;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (OnKey(e)) { e.Handled = true; return; }
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (ctrl || alt) return;
            switch (key)
            {
                case Key.Up: case Key.Down: case Key.Left: case Key.Right:
                    SetFocusPos(Navigate(focusPos, key), shift); e.Handled = true; break;
                case Key.Home: SetFocusPos(0, shift); e.Handled = true; break;
                case Key.End: SetFocusPos(items.Count - 1, shift); e.Handled = true; break;
                case Key.PageDown: tgtY = offY + VpH; Clamp(); ShowScrollbars(); e.Handled = true; break;
                case Key.PageUp: tgtY = offY - VpH; Clamp(); ShowScrollbars(); e.Handled = true; break;
                case Key.Tab:
                    {
                        // Tab / Shift+Tab: next item alphabetically
                        if (items.Count == 0) break;
                        var cur = FocusedItem;
                        var sorted = items.Select(i => entries[i].Item).OrderBy(i => i.Name, Comparer<string>.Create(Format.NameCmp)).ToList();
                        int ix = cur == null ? -1 : sorted.IndexOf(cur);
                        ix = shift ? (ix <= 0 ? sorted.Count - 1 : ix - 1) : (ix + 1) % sorted.Count;
                        SelectOnly(sorted[ix]);
                        e.Handled = true; break;
                    }
                case Key.Return:
                    if (Selection.Count == 1) { RenameRequested?.Invoke(Selection.First()); e.Handled = true; }
                    break;
                case Key.Escape:
                    if (rubber) { rubber = false; InvalidateVisual(); }
                    break;
            }
        }

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
            if (e.Text == " " && !IsTypeSelecting) return;
            if ((DateTime.Now - typeTime).TotalMilliseconds > 1000) typeBuf = "";
            typeBuf += e.Text; typeTime = DateTime.Now;
            FileItem best = null;
            foreach (var i in items)
            {
                var it = entries[i].Item;
                if (it.DisplayName.StartsWith(typeBuf, StringComparison.CurrentCultureIgnoreCase))
                {
                    if (best == null || Format.NameCmp(it.Name, best.Name) < 0) best = it;
                }
            }
            if (best == null)
            {
                // closest following match like Finder
                foreach (var i in items)
                {
                    var it = entries[i].Item;
                    if (string.Compare(it.DisplayName, typeBuf, StringComparison.CurrentCultureIgnoreCase) >= 0 && (best == null || Format.NameCmp(it.Name, best.Name) < 0)) best = it;
                }
            }
            if (best != null) SelectOnly(best);
            e.Handled = true;
        }

        // helpers for subclasses
        protected static StreamGeometry RoundedRect(Rect r, double tl, double tr, double br, double bl)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(r.Left + tl, r.Top), true, true);
                c.LineTo(new Point(r.Right - tr, r.Top), false, false);
                if (tr > 0) c.ArcTo(new Point(r.Right, r.Top + tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(r.Right, r.Bottom - br), false, false);
                if (br > 0) c.ArcTo(new Point(r.Right - br, r.Bottom), new Size(br, br), 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(r.Left + bl, r.Bottom), false, false);
                if (bl > 0) c.ArcTo(new Point(r.Left, r.Bottom - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(r.Left, r.Top + tl), false, false);
                if (tl > 0) c.ArcTo(new Point(r.Left + tl, r.Top), new Size(tl, tl), 0, false, SweepDirection.Clockwise, false, false);
            }
            g.Freeze();
            return g;
        }

        protected void DrawTagDots(DrawingContext dc, FileItem it, Point at, double d = 8, bool rightToLeft = false)
        {
            var tags = Settings.Current.TagsOf(it.Path);
            if (tags == null || tags.Count == 0) return;
            int n = Math.Min(3, tags.Count);
            for (int i = 0; i < n; i++)
            {
                var def = Settings.Current.TagDefOf(tags[i]);
                var b = Theme.TagBrush(def?.Color ?? 7);
                double x = rightToLeft ? at.X - i * (d * 0.6) : at.X + i * (d * 0.6);
                dc.DrawEllipse(b, new Pen(Theme.ContentBg, 1.2), new Point(x + d / 2, at.Y), d / 2, d / 2);
            }
        }

        public static double TagDotsWidth(FileItem it, double d = 8)
        {
            var tags = Settings.Current.TagsOf(it.Path);
            if (tags == null || tags.Count == 0) return 0;
            return d + (Math.Min(3, tags.Count) - 1) * d * 0.6 + 4;
        }

        protected bool IsCut(FileItem it) => CutPaths != null && CutPaths.Contains(it.Path);
    }
}

using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Finderly.Core;
using Finderly.Views;

namespace Finderly.UI
{
    public sealed partial class MainWindow
    {
        void WireCommands()
        {
            PreviewKeyDown += OnPreviewKey;
            MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.XButton1) GoBack();
                else if (e.ChangedButton == MouseButton.XButton2) GoForward();
            };
        }

        void OnPreviewKey(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            bool typing = Keyboard.FocusedElement is System.Windows.Controls.TextBox;
            if (key == Key.ImeProcessed) return;

            // shortcuts that work even while typing
            if (ctrl && !shift && !alt && key == Key.F) { FocusSearch(); e.Handled = true; return; }
            if (typing && !(ctrl && (key == Key.T || key == Key.N || key == Key.W || key == Key.Tab))) return;

            e.Handled = true;
            if (ctrl && !shift && !alt)
            {
                switch (key)
                {
                    case Key.D1: SetMode(ViewMode.Icon); return;
                    case Key.D2: SetMode(ViewMode.List); return;
                    case Key.D3: SetMode(ViewMode.Column); return;
                    case Key.D4: SetMode(ViewMode.Gallery); return;
                    case Key.N: NewWindow(); return;
                    case Key.T: NewTab(CurrentLocation ?? StartLocation(), true); return;
                    case Key.W: CloseTab(tab); return;
                    case Key.O: OpenSelection(); return;
                    case Key.Down: OpenSelection(); return;
                    case Key.Up: GoUp(); return;
                    case Key.I: GetInfo(); return;
                    case Key.D: _ = DuplicateSelection(); return;
                    case Key.C: CopySelection(); return;
                    case Key.X: CutSelection(); return;
                    case Key.V: _ = PasteHere(); return;
                    case Key.A: tab.View.SelectAll(); return;
                    case Key.Z: _ = FileOps.DoUndo(); return;
                    case Key.Y: _ = FileOps.DoRedo(); return;
                    case Key.J: ViewOptionsWindow.ShowIt(this, tab.View); return;
                    case Key.OemComma: SettingsWindow.ShowIt(this); return;
                    case Key.R: ShowOriginalOrReload(); return;
                    case Key.M: WindowState = WindowState.Minimized; return;
                    case Key.H: WindowState = WindowState.Minimized; return;
                    case Key.Q: Application.Current.Shutdown(); return;
                    case Key.OemPlus: case Key.Add: Zoom(1); return;
                    case Key.OemMinus: case Key.Subtract: Zoom(-1); return;
                    case Key.Tab: NextTab(1); return;
                    case Key.OemQuestion: Settings.Current.ShowStatusBar = !Settings.Current.ShowStatusBar; Settings.Save(); ApplyChromeVisibility(); return;
                    case Key.OemOpenBrackets: GoBack(); return;
                    case Key.OemCloseBrackets: GoForward(); return;
                    case Key.Back: _ = TrashSelection(); return;
                }
            }
            else if (ctrl && shift && !alt)
            {
                switch (key)
                {
                    case Key.N: NewFolder(); return;
                    case Key.G: GoToFolder(); return;
                    case Key.H: NavigateTo(Location.Folder(KnownFolders.Home)); return;
                    case Key.D: NavigateTo(Location.Folder(KnownFolders.Desktop)); return;
                    case Key.O: NavigateTo(Location.Folder(KnownFolders.Documents)); return;
                    case Key.F: NavigateTo(Location.Recents); return;
                    case Key.A: NavigateTo(Location.Applications); return;
                    case Key.C: NavigateTo(Location.Computer); return;
                    case Key.W: Close(); return;
                    case Key.T: Settings.Current.AlwaysShowTabBar = !(Settings.Current.AlwaysShowTabBar || tabs.Count > 1); Settings.Save(); ApplyChromeVisibility(); return;
                    case Key.P: Settings.Current.ShowPreview = !Settings.Current.ShowPreview; Settings.Save(); ApplyChromeVisibility(); return;
                    case Key.OemPeriod: Settings.Current.ShowHidden = !Settings.Current.ShowHidden; Settings.Save(); tab.View.Reload(); return;
                    case Key.Z: _ = FileOps.DoRedo(); return;
                    case Key.Tab: NextTab(-1); return;
                    case Key.Delete: EmptyTrash(); return;
                }
            }
            else if (ctrl && alt && !shift)
            {
                switch (key)
                {
                    case Key.A: MakeAlias(); return;
                    case Key.C: CopyPath(); return;
                    case Key.I: tab.View.InvertSelection(); return;
                    case Key.V: _ = PasteHere(true); return;
                    case Key.T: Settings.Current.ShowToolbar = !Settings.Current.ShowToolbar; Settings.Save(); ApplyChromeVisibility(); return;
                    case Key.P: Settings.Current.ShowPathBar = !Settings.Current.ShowPathBar; Settings.Save(); ApplyChromeVisibility(); return;
                    case Key.S: ToggleSidebar(); return;
                    case Key.L: NavigateTo(Location.Folder(KnownFolders.Downloads)); return;
                }
            }
            else if (alt && !ctrl && !shift)
            {
                switch (key)
                {
                    case Key.Left: GoBack(); return;
                    case Key.Right: GoForward(); return;
                    case Key.Up: GoUp(); return;
                    case Key.Down: OpenSelection(); return;
                }
            }
            else if (!ctrl && !alt)
            {
                switch (key)
                {
                    case Key.Space:
                        if (tab.View.Surface != null && tab.View.Surface.IsTypeSelecting) { e.Handled = false; return; }
                        ToggleQuickLook(); return;
                    case Key.Delete:
                        if (shift) _ = DeleteSelection(); else _ = TrashSelection();
                        return;
                    case Key.F2: RenameSelection(); return;
                    case Key.F5: tab.View.Reload(); return;
                    case Key.F11: ToggleFullScreen(); return;
                    case Key.Back: GoBack(); return;
                    case Key.Escape:
                        if (QuickLookWindow.Instance != null) { QuickLookWindow.Instance.Close(); return; }
                        e.Handled = false; return;
                }
            }
            e.Handled = false;
        }

        void ShowOriginalOrReload()
        {
            var s = Selected.FirstOrDefault();
            if (s != null && s.IsLink) ShowOriginal(); else tab.View.Reload();
        }
    }
}

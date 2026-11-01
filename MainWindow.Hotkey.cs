
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

using WinRT.Interop;

namespace WinSearch
{
    public sealed partial class MainWindow
    {
        // =====================================
        // GLOBAL SHORTCUT
        // =====================================

        private const int HotkeyId = 0x5341;

        private const uint WM_HOTKEY = 0x0312;
        private const uint WM_ACTIVATE = 0x0006;
        private const uint WA_INACTIVE = 0;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_SPACE = 0x20;

        private const nuint HotkeySubclassId = 1;

        // =====================================
        // WINDOW POSITIONING
        // =====================================

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private static readonly IntPtr HWND_TOPMOST =
            new IntPtr(-1);

        private static readonly IntPtr HWND_NOTOPMOST =
            new IntPtr(-2);

        // =====================================
        // STATE
        // =====================================

        private IntPtr _hotkeyHwnd;

        private SubclassProc? _hotkeyCallback;

        private bool _hotkeyRegistered;
        private bool _subclassInstalled;

        private bool _searchHidden;
        private bool _searchClosing;

        // Prevent delayed focus-loss callbacks from
        // hiding a newly reopened window.
        private long _visibilityVersion;

        // =====================================
        // NATIVE WINDOWS APIs
        // =====================================

        private delegate IntPtr SubclassProc(
            IntPtr hwnd,
            uint message,
            nuint wParam,
            nint lParam,
            nuint subclassId,
            nuint referenceData);

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(
            IntPtr hwnd,
            int id,
            uint modifiers,
            uint key);

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(
            IntPtr hwnd,
            int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(
            IntPtr hwnd);

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hwnd,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport(
            "comctl32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(
            IntPtr hwnd,
            SubclassProc callback,
            nuint subclassId,
            nuint referenceData);

        [DllImport("comctl32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(
            IntPtr hwnd,
            SubclassProc callback,
            nuint subclassId);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(
            IntPtr hwnd,
            uint message,
            nuint wParam,
            nint lParam);

        // =====================================
        // INITIALIZATION
        // =====================================

        private void InitializeGlobalHotkey()
        {
            if (_subclassInstalled || _searchClosing)
                return;

            IntPtr hwnd =
                WindowNative.GetWindowHandle(this);

            if (hwnd == IntPtr.Zero)
                return;

            _hotkeyHwnd = hwnd;

            _hotkeyCallback =
                HotkeyWindowProcedure;

            _subclassInstalled = SetWindowSubclass(
                hwnd,
                _hotkeyCallback,
                HotkeySubclassId,
                0);

            if (!_subclassInstalled)
            {
                Debug.WriteLine(
                    "Win Search: Window subclass setup failed.");

                return;
            }

            _hotkeyRegistered = RegisterHotKey(
                hwnd,
                HotkeyId,
                MOD_ALT | MOD_NOREPEAT,
                VK_SPACE);

            if (!_hotkeyRegistered)
            {
                Debug.WriteLine(
                    "Win Search: Alt+Space registration failed. " +
                    $"Windows error: {Marshal.GetLastWin32Error()}");
            }
            else
            {
                Debug.WriteLine(
                    "Win Search: Alt+Space registered.");
            }

            Closed += CleanupGlobalHotkey;
        }

        // =====================================
        // WINDOW MESSAGE HANDLING
        // =====================================

        private IntPtr HotkeyWindowProcedure(
            IntPtr hwnd,
            uint message,
            nuint wParam,
            nint lParam,
            nuint subclassId,
            nuint referenceData)
        {
            // Global Alt + Space shortcut.
            if (message == WM_HOTKEY &&
                wParam == (nuint)HotkeyId)
            {
                ToggleSearchWindow();

                return IntPtr.Zero;
            }

            // Detect when another application
            // or the desktop becomes active.
            if (message == WM_ACTIVATE)
            {
                uint activationState =
                    (uint)(wParam & (nuint)0xFFFF);

                if (activationState == WA_INACTIVE &&
                    !_searchHidden &&
                    !_searchClosing)
                {
                    long capturedVersion =
                        _visibilityVersion;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_searchClosing || _searchHidden)
                            return;

                        // Ignore a dismissal event that
                        // belongs to an older visibility state.
                        if (capturedVersion !=
                            _visibilityVersion)
                        {
                            return;
                        }

                        if (GetForegroundWindow() !=
                            _hotkeyHwnd)
                        {
                            HideSearchWindow();
                        }
                    });
                }
            }

            return DefSubclassProc(
                hwnd,
                message,
                wParam,
                lParam);
        }

        // =====================================
        // ALT + SPACE TOGGLE
        // =====================================

        private void ToggleSearchWindow()
        {
            if (_searchClosing)
                return;

            if (_searchHidden)
            {
                ShowSearchWindow();
            }
            else
            {
                HideSearchWindow();
            }
        }

        // =====================================
        // SHOW WINDOW
        // =====================================

        private void ShowSearchWindow()
        {
            if (_searchClosing)
                return;

            _visibilityVersion++;
            _searchHidden = false;

            // Always start with a fresh search.
            // The text change also refreshes results
            // through SearchBox_TextChanged.
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = string.Empty;
            }

            // Ensure the empty search bar is
            // displayed in its collapsed layout.
            UpdateResults();

            // Restore its correct dimensions and
            // original Spotlight-style position.
            // QueueResize is triggered by UpdateResults.

            AppWindow.Show();

            // Raise the panel above other windows.
            bool raised = SetWindowPos(
                _hotkeyHwnd,
                HWND_TOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE);

            if (!raised)
            {
                Debug.WriteLine(
                    "Win Search: Failed to raise window. " +
                    $"Error: {Marshal.GetLastWin32Error()}");
            }

            // Activate it for keyboard input.
            SetForegroundWindow(_hotkeyHwnd);

            Activate();

            SearchBox.Focus(
                FocusState.Programmatic);

            SearchBox.SelectionStart = 0;

            Debug.WriteLine(
                "Win Search: Opened with an empty query.");
        }

        // =====================================
        // HIDE WINDOW
        // =====================================

        private void HideSearchWindow()
        {
            if (_searchClosing || _searchHidden)
                return;

            _visibilityVersion++;
            _searchHidden = true;

            // Clear the search immediately on hide
            // so the next opening starts fresh.
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = string.Empty;
            }

            // Return the hidden window to normal
            // Z-order so it does not interfere
            // with other applications.
            SetWindowPos(
                _hotkeyHwnd,
                HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE |
                SWP_NOSIZE |
                SWP_NOACTIVATE);

            // Hide without closing the app.
            // Search indexes and icon cache remain.
            AppWindow.Hide();

            Debug.WriteLine(
                "Win Search: Hidden and search cleared.");
        }

        // =====================================
        // CLEANUP
        // =====================================

        private void CleanupGlobalHotkey(
            object sender,
            WindowEventArgs args)
        {
            _searchClosing = true;
            _visibilityVersion++;

            if (_hotkeyRegistered)
            {
                UnregisterHotKey(
                    _hotkeyHwnd,
                    HotkeyId);

                _hotkeyRegistered = false;
            }

            if (_subclassInstalled &&
                _hotkeyCallback != null)
            {
                RemoveWindowSubclass(
                    _hotkeyHwnd,
                    _hotkeyCallback,
                    HotkeySubclassId);

                _subclassInstalled = false;
            }

            _hotkeyCallback = null;
        }
    }
}

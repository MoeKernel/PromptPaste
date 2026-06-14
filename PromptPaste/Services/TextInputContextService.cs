using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace PromptPaste.Services;

public sealed record TextInputContext(nint TargetHwnd, Point PopupPoint);

public static class TextInputContextService
{
    public static TextInputContext? TryGetExternalTextInputContext()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == nint.Zero)
                return null;

            var popupPoint = TryGetWin32CaretPoint(foreground)
                          ?? TryGetAutomationSelectionPoint()
                          ?? TryGetAutomationFocusPoint()
                          ?? GetMouseScreenPoint();
            return new TextInputContext(foreground, popupPoint);
        }
        catch
        {
            var foreground = GetForegroundWindow();
            return foreground == nint.Zero ? null : new TextInputContext(foreground, GetMouseScreenPoint());
        }
    }

    private static Point? TryGetWin32CaretPoint(nint foreground)
    {
        try
        {
            var threadId = GetWindowThreadProcessId(foreground, out _);
            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (!GetGUIThreadInfo(threadId, ref info) && !GetGUIThreadInfo(0, ref info))
                return null;

            var caretHwnd = info.hwndCaret != nint.Zero ? info.hwndCaret : info.hwndFocus;
            if (caretHwnd == nint.Zero || IsOwnProcessWindow(caretHwnd)) return null;
            if (info.rcCaret.Left == 0 && info.rcCaret.Top == 0 && info.rcCaret.Right == 0 && info.rcCaret.Bottom == 0)
                return null;

            var point = new POINT
            {
                X = info.rcCaret.Right != info.rcCaret.Left ? info.rcCaret.Right : info.rcCaret.Left,
                Y = info.rcCaret.Bottom > info.rcCaret.Top ? info.rcCaret.Bottom : info.rcCaret.Top
            };

            if (!ClientToScreen(caretHwnd, ref point)) return null;
            if (point.X <= 0 && point.Y <= 0) return null;
            return new Point(point.X + 2, point.Y + 6);
        }
        catch
        {
            return null;
        }
    }

    private static Point? TryGetAutomationSelectionPoint()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return null;

            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) && pattern is TextPattern textPattern)
            {
                foreach (var range in textPattern.GetSelection())
                {
                    var point = GetFirstUsablePoint(range.GetBoundingRectangles());
                    if (point != null) return point;
                }
            }
        }
        catch
        {
            // UI Automation support varies across applications; fall through to other strategies.
        }

        return null;
    }

    private static Point? TryGetAutomationFocusPoint()
    {
        try
        {
            var rect = AutomationElement.FocusedElement?.Current.BoundingRectangle;
            if (rect is { IsEmpty: false } && rect.Value.Left > -30000 && rect.Value.Top > -30000)
                return new Point(rect.Value.Left + 12, rect.Value.Bottom + 6);
        }
        catch
        {
            // Ignore UIA failures and fall back to mouse position.
        }

        return null;
    }

    private static Point? GetFirstUsablePoint(IEnumerable<Rect>? rectangles)
    {
        if (rectangles == null) return null;

        foreach (var rect in rectangles)
        {
            if (rect.IsEmpty || double.IsNaN(rect.Left) || double.IsNaN(rect.Top) || rect.Left <= -30000 || rect.Top <= -30000)
                continue;

            var x = rect.Right > rect.Left ? rect.Right : rect.Left;
            var y = rect.Bottom > rect.Top ? rect.Bottom : rect.Top + 16;
            return new Point(x + 2, y + 4);
        }

        return null;
    }

    private static Point GetMouseScreenPoint()
        => GetCursorPos(out var point) ? new Point(point.X + 8, point.Y + 8) : new Point(200, 200);

    private static bool IsOwnProcessWindow(nint hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var processId);
        return processId == Environment.ProcessId;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out int processId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}

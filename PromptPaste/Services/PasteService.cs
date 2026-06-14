using System.Runtime.InteropServices;
using System.Windows;

namespace PromptPaste.Services;

public static class PasteService
{
    public static void PasteToForeWindow(nint targetHwnd, string text)
    {
        if (targetHwnd == nint.Zero) return;

        string? previousText = null;
        try
        {
            if (Clipboard.ContainsText()) previousText = Clipboard.GetText();
        }
        catch { }

        ClipboardWatcher.MarkInternalCopy(text);
        try { Clipboard.SetText(text); } catch { return; }

        // Switch to target and inject Ctrl+V
        SetForegroundWindow(targetHwnd);
        System.Threading.Thread.Sleep(100);

        const uint KEYEVENTF_KEYUP = 0x0002;
        keybd_event(0x11, 0, 0, 0); // Ctrl down
        keybd_event(0x56, 0, 0, 0); // V down
        keybd_event(0x56, 0, KEYEVENTF_KEYUP, 0); // V up
        keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0); // Ctrl up

        System.Threading.Thread.Sleep(30);
        try
        {
            if (previousText != null)
            {
                ClipboardWatcher.MarkInternalCopy(previousText);
                Clipboard.SetText(previousText);
            }
        }
        catch { }
    }

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
}

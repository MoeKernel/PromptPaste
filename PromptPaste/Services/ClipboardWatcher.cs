using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PromptPaste.Services;

public class ClipboardWatcher : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly TimeSpan InternalCopyIgnoreWindow = TimeSpan.FromSeconds(2);

    private readonly Action<string> _onChanged;
    private nint _hwnd;
    private HwndSource? _source;
    private bool _running;

    public static bool IsInternalCopy { get; set; }
    private static string? _lastInternalCopyText;
    private static DateTime _lastInternalCopyExpiresUtc;

    public static void MarkInternalCopy(string text)
    {
        IsInternalCopy = true;
        _lastInternalCopyText = text;
        _lastInternalCopyExpiresUtc = DateTime.UtcNow.Add(InternalCopyIgnoreWindow);
    }

    public ClipboardWatcher(Action<string> onChanged)
    {
        _onChanged = onChanged;
    }

    public void Start(Window window)
    {
        if (_running) return;
        _running = true;

        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        if (!AddClipboardFormatListener(_hwnd))
            Debug.WriteLine("[ClipboardWatcher] AddClipboardFormatListener failed");
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;

        RemoveClipboardFormatListener(_hwnd);
        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(text))
                        return nint.Zero;

                    if (ShouldIgnoreInternalCopy(text))
                        return nint.Zero;

                    _onChanged(text);
                }
            }
            catch
            {
                // Clipboard may be locked by another process
            }
        }
        return nint.Zero;
    }

    private static bool ShouldIgnoreInternalCopy(string text)
    {
        var now = DateTime.UtcNow;
        if (_lastInternalCopyText != null && now > _lastInternalCopyExpiresUtc)
        {
            IsInternalCopy = false;
            _lastInternalCopyText = null;
        }

        if (_lastInternalCopyText != null &&
            now <= _lastInternalCopyExpiresUtc &&
            string.Equals(text, _lastInternalCopyText, StringComparison.Ordinal))
        {
            IsInternalCopy = false;
            return true;
        }

        if (IsInternalCopy)
        {
            IsInternalCopy = false;
            return true;
        }

        return false;
    }

    public void Dispose() => Stop();

    [DllImport("user32.dll")]
    private static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(nint hwnd);
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PromptPaste.Services;

public class HotKeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_ALT = 0x0001;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_WIN = 0x0008;
    private const int MOD_NOREPEAT = 0x4000;

    private static int _nextId = 0x1000;

    private readonly int _id;
    private readonly Action _callback;
    private readonly Window _window;
    private HwndSource? _source;
    private bool _registered;

    public HotKeyService(Window window, Action callback)
    {
        _window = window;
        _callback = callback;
        _id = Interlocked.Increment(ref _nextId);
    }

    public bool Register(string hotKey = "Ctrl+Shift+M", bool enabled = true)
    {
        Unregister();
        if (!enabled) return true;

        if (!TryParseHotKey(hotKey, out var modifiers, out var virtualKey))
            return false;

        _source = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
        _source?.AddHook(WndProc);
        if (_source == null) return false;

        _registered = RegisterHotKey(_source.Handle, _id, modifiers | MOD_NOREPEAT, virtualKey);
        if (!_registered)
        {
            var error = Marshal.GetLastWin32Error();
            Debug.WriteLine($"[HotKey] RegisterHotKey failed: {error}");
            LogService.Info($"RegisterHotKey failed. Id={_id}, HotKey={hotKey}, LastError={error}");
            _source.RemoveHook(WndProc);
            _source = null;
        }

        return _registered;
    }

    public void Unregister()
    {
        if (_source != null)
        {
            if (_registered)
                UnregisterHotKey(_source.Handle, _id);
            _source.RemoveHook(WndProc);
            _source = null;
        }
        _registered = false;
    }

    public static bool TryParseHotKey(string? hotKey, out int modifiers, out int virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(hotKey)) return false;

        Key key = Key.None;
        foreach (var rawPart in hotKey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_CONTROL;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_ALT;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_SHIFT;
            }
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_WIN;
            }
            else if (!Enum.TryParse(part, true, out key))
            {
                return false;
            }
        }

        if (modifiers == 0 || key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return false;

        virtualKey = KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && (int)wParam == _id)
        {
            LogService.Info($"RegisterHotKey triggered. Id={_id}");
            _callback();
            handled = true;
        }
        return nint.Zero;
    }

    public void Dispose() => Unregister();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hwnd, int id, int modifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}

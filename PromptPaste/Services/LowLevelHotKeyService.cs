using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace PromptPaste.Services;

public sealed class LowLevelHotKeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_SHIFT = 0x10;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int KEY_PRESSED = 0x8000;

    private readonly Action _callback;
    private readonly LowLevelKeyboardProc _proc;
    private nint _hook;
    private int _modifiers;
    private int _virtualKey;
    private bool _enabled;
    private bool _isPressed;

    public LowLevelHotKeyService(Action callback)
    {
        _callback = callback;
        _proc = HookCallback;
    }

    public bool Register(string hotKey, bool enabled = true)
    {
        Unregister();
        if (!enabled) return true;
        if (!HotKeyService.TryParseHotKey(hotKey, out _modifiers, out _virtualKey)) return false;

        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = currentModule?.ModuleName == null ? nint.Zero : GetModuleHandle(currentModule.ModuleName);

        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);
        _enabled = _hook != nint.Zero;
        LogService.Info($"Quick paste keyboard hook register result. HotKey={hotKey}, Hook={_hook}, ModuleHandle={moduleHandle}, LastError={Marshal.GetLastWin32Error()}");
        return _enabled;
    }

    public void Unregister()
    {
        if (_hook != nint.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = nint.Zero;
        }
        _enabled = false;
        _isPressed = false;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && _enabled)
        {
            var message = (int)wParam;
            var vkCode = Marshal.ReadInt32(lParam);
            if ((message == WM_KEYDOWN || message == WM_SYSKEYDOWN) && vkCode == _virtualKey && ModifiersMatch())
            {
                if (!_isPressed)
                {
                    _isPressed = true;
                    LogService.Info($"Quick paste keyboard hook triggered. Vk={vkCode}, Modifiers={_modifiers}");
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        LogService.Info("Quick paste callback invoked from keyboard hook");
                        _callback();
                    });
                }
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            if ((message == WM_KEYUP || message == WM_SYSKEYUP) && vkCode == _virtualKey)
                _isPressed = false;
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool ModifiersMatch()
    {
        var ctrl = IsKeyDown(VK_CONTROL);
        var alt = IsKeyDown(VK_MENU);
        var shift = IsKeyDown(VK_SHIFT);
        var win = IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);

        return HasModifier(0x0002) == ctrl
            && HasModifier(0x0001) == alt
            && HasModifier(0x0004) == shift
            && HasModifier(0x0008) == win;
    }

    private bool HasModifier(int modifier) => (_modifiers & modifier) == modifier;

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & KEY_PRESSED) != 0;

    public void Dispose() => Unregister();

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint GetModuleHandle(string lpModuleName);

}

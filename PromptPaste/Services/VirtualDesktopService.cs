using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PromptPaste.Services;

public static class VirtualDesktopService
{
    public static bool IsWindowOnCurrentDesktop(nint hwnd)
    {
        if (hwnd == nint.Zero) return true;

        try
        {
            var manager = CreateManager();
            return manager.IsWindowOnCurrentVirtualDesktop(hwnd, out var isOnCurrentDesktop) == 0
                && isOnCurrentDesktop;
        }
        catch
        {
            return true;
        }
    }

    public static void MoveWindowToCurrentDesktop(Window window)
    {
        try
        {
            var targetHwnd = new WindowInteropHelper(window).EnsureHandle();
            if (targetHwnd == nint.Zero || IsWindowOnCurrentDesktop(targetHwnd)) return;

            var manager = CreateManager();
            var currentDesktopId = GetCurrentDesktopId(manager);
            if (currentDesktopId == Guid.Empty) return;

            _ = manager.MoveWindowToDesktop(targetHwnd, ref currentDesktopId);
        }
        catch
        {
            // Virtual desktop APIs are best-effort. Fallback to the previous Show/Activate behavior.
        }
    }

    private static Guid GetCurrentDesktopId(IVirtualDesktopManager manager)
    {
        var probe = new Window
        {
            Width = 1,
            Height = 1,
            Left = -32000,
            Top = -32000,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Opacity = 0
        };

        try
        {
            probe.Show();
            var probeHwnd = new WindowInteropHelper(probe).EnsureHandle();
            return manager.GetWindowDesktopId(probeHwnd, out var desktopId) == 0
                ? desktopId
                : Guid.Empty;
        }
        finally
        {
            probe.Close();
        }
    }

    private static IVirtualDesktopManager CreateManager()
    {
        var type = Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"), throwOnError: true)!;
        return (IVirtualDesktopManager)Activator.CreateInstance(type)!;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
    }
}

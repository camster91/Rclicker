using RClicker.Native;

namespace RClicker.Presentation;

/// <summary>Reads which app and window class currently has keyboard focus. Read-only; never changes focus.</summary>
internal sealed class Win32ForegroundWindowProvider : IForegroundWindowProvider
{
    public unsafe ForegroundWindowInfo GetForegroundWindow()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return ForegroundWindowInfo.Unknown;
        }

        string? windowClass = null;
        char* buffer = stackalloc char[256];
        int length = NativeMethods.GetClassName(hwnd, buffer, 256);
        if (length > 0)
        {
            windowClass = new string(buffer, 0, length);
        }

        string? processName = null;
        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != 0)
        {
            processName = NativeMethods.GetProcessName(pid);
        }

        return new ForegroundWindowInfo(processName, windowClass);
    }
}

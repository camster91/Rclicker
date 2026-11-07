using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RClicker.Native;

/// <summary>The handful of Win32 calls the receiver needs. No shelling out, no scripting hosts.</summary>
internal static partial class NativeMethods
{
    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint MAPVK_VK_TO_VSC = 0;

    internal const ushort VK_CONTROL = 0x11;
    internal const ushort VK_L = 0x4C;
    internal const ushort VK_NEXT = 0x22;
    internal const ushort VK_PRIOR = 0x21;
    internal const ushort VK_ESCAPE = 0x1B;
    internal const ushort VK_LEFT = 0x25;
    internal const ushort VK_RIGHT = 0x27;
    internal const ushort VK_F5 = 0x74;
    internal const ushort VK_B = 0x42;

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint MapVirtualKey(uint uCode, uint uMapType);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint ERROR_INSUFFICIENT_BUFFER = 122;
    internal const int INITIAL_PROCESS_PATH_BUFFER_LENGTH = 260;
    internal const int MAX_PROCESS_PATH_BUFFER_LENGTH = 32 * 1024;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial SafeProcessHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool QueryFullProcessImageName(
        SafeProcessHandle hProcess,
        uint dwFlags,
        char* lpExeName,
        ref uint lpdwSize);

    /// <summary>
    /// Reads a process name through the least-privileged process query API.
    /// An inaccessible or exited process is deliberately reported as unknown.
    /// </summary>
    internal static unsafe string? GetProcessName(uint processId)
    {
        using var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsInvalid)
        {
            return null;
        }

        var buffer = new char[INITIAL_PROCESS_PATH_BUFFER_LENGTH];
        while (true)
        {
            uint length = (uint)buffer.Length;
            fixed (char* path = buffer)
            {
                if (QueryFullProcessImageName(process, 0, path, ref length))
                {
                    var fullPath = new ReadOnlySpan<char>(path, checked((int)length));
                    int separator = fullPath.LastIndexOfAny('\\', '/');
                    var fileName = fullPath[(separator + 1)..];
                    int extension = fileName.LastIndexOf('.');
                    if (extension > 0 && fileName[extension..].Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = fileName[..extension];
                    }

                    return fileName.Length == 0 ? null : new string(fileName);
                }
            }

            if (Marshal.GetLastPInvokeError() != ERROR_INSUFFICIENT_BUFFER)
            {
                return null;
            }

            int nextLength = NextProcessPathBufferLength(buffer.Length);
            if (nextLength == buffer.Length)
            {
                return null;
            }

            Array.Resize(ref buffer, nextLength);
        }
    }

    internal static int NextProcessPathBufferLength(int currentLength)
    {
        return currentLength >= MAX_PROCESS_PATH_BUFFER_LENGTH
            ? MAX_PROCESS_PATH_BUFFER_LENGTH
            : Math.Min(checked(currentLength * 2), MAX_PROCESS_PATH_BUFFER_LENGTH);
    }

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static unsafe partial int GetClassName(IntPtr hWnd, char* lpClassName, int nMaxCount);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllocConsole();

    internal const uint ES_SYSTEM_REQUIRED = 0x00000001;
    internal const uint ES_DISPLAY_REQUIRED = 0x00000002;
    internal const uint ES_CONTINUOUS = 0x80000000;

    /// <summary>Keeps the display and system awake (or releases that) for the calling thread.</summary>
    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint SetThreadExecutionState(uint esFlags);

    // INPUT must be the full Win32 size (40 bytes on 64-bit, 28 on 32-bit) or SendInput
    // rejects it, so the union includes MOUSEINPUT, its largest member.
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;

        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}

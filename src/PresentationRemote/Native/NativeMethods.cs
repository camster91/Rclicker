using System.Runtime.InteropServices;

namespace PresentationRemote.Native;

/// <summary>The handful of Win32 calls the receiver needs. No shelling out, no scripting hosts.</summary>
internal static partial class NativeMethods
{
    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint MAPVK_VK_TO_VSC = 0;

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

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static unsafe partial int GetClassName(IntPtr hWnd, char* lpClassName, int nMaxCount);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllocConsole();

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

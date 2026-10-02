using System.Runtime.InteropServices;
using PresentationRemote.Native;

namespace PresentationRemote.Presentation;

/// <summary>
/// Presses one of the five allowed keys using the Win32 SendInput API. The input goes to
/// the foreground window, exactly like a USB presentation clicker.
/// </summary>
internal sealed class Win32KeySender : IKeySender
{
    private static readonly int InputSize = Marshal.SizeOf<NativeMethods.INPUT>();

    public bool TrySend(PresentationKey key, out string? failureReason)
    {
        var (vk, extended) = key switch
        {
            PresentationKey.RightArrow => (NativeMethods.VK_RIGHT, true),
            PresentationKey.LeftArrow => (NativeMethods.VK_LEFT, true),
            PresentationKey.F5 => (NativeMethods.VK_F5, false),
            PresentationKey.B => (NativeMethods.VK_B, false),
            PresentationKey.Escape => (NativeMethods.VK_ESCAPE, false),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };

        var scan = (ushort)NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC);
        uint flags = extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0;

        // Key down and key up in one call so nothing else can interleave.
        var inputs = new[]
        {
            KeyInput(vk, scan, flags),
            KeyInput(vk, scan, flags | NativeMethods.KEYEVENTF_KEYUP),
        };

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent != inputs.Length)
        {
            int error = Marshal.GetLastPInvokeError();
            failureReason = $"Windows blocked the key press (error {error}). If PowerPoint runs as administrator, run Presentation Remote as administrator too.";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static NativeMethods.INPUT KeyInput(ushort vk, ushort scan, uint flags) => new()
    {
        Type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KEYBDINPUT { VirtualKey = vk, ScanCode = scan, Flags = flags },
        },
    };
}

using System.Runtime.InteropServices;
using RClicker.Native;

namespace RClicker.Presentation;

/// <summary>
/// Presses a fixed allowed key or chord using the Win32 SendInput API. The input goes to
/// the foreground window, exactly like a USB presentation clicker.
/// </summary>
internal sealed class Win32KeySender : IKeySender
{
    private static readonly int InputSize = Marshal.SizeOf<NativeMethods.INPUT>();

    public bool TrySend(PresentationKey key, out string? failureReason)
    {
        var inputs = BuildInputs(key);

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent != inputs.Length)
        {
            int error = Marshal.GetLastPInvokeError();
            if (key == PresentationKey.ControlL && sent > 0)
            {
                // A partial chord must not leave Ctrl held down for the user's next key.
                var release = new[] { KeyInput(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP) };
                _ = NativeMethods.SendInput(1, release, InputSize);
            }
            failureReason = $"Windows blocked the key press (error {error}). If the presentation app runs as administrator, run rclicker as administrator too.";
            return false;
        }

        failureReason = null;
        return true;
    }

    internal static NativeMethods.INPUT[] BuildInputs(PresentationKey key)
    {
        if (key == PresentationKey.ControlL)
        {
            return [
                KeyInput(NativeMethods.VK_CONTROL, 0, 0),
                KeyInput(NativeMethods.VK_L, 0, 0),
                KeyInput(NativeMethods.VK_L, 0, NativeMethods.KEYEVENTF_KEYUP),
                KeyInput(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP),
            ];
        }
        var (vk, extended) = key switch
        {
            PresentationKey.RightArrow => (NativeMethods.VK_RIGHT, true),
            PresentationKey.LeftArrow => (NativeMethods.VK_LEFT, true),
            PresentationKey.F5 => (NativeMethods.VK_F5, false),
            PresentationKey.B => (NativeMethods.VK_B, false),
            PresentationKey.PageDown => (NativeMethods.VK_NEXT, true),
            PresentationKey.PageUp => (NativeMethods.VK_PRIOR, true),
            PresentationKey.Escape => (NativeMethods.VK_ESCAPE, false),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };

        var scan = (ushort)NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC);
        uint flags = extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0;

        // Key down and key up in one call so nothing else can interleave.
        return new[]
        {
            KeyInput(vk, scan, flags),
            KeyInput(vk, scan, flags | NativeMethods.KEYEVENTF_KEYUP),
        };

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

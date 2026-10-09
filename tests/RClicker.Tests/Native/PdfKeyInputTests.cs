using RClicker.Native;
using RClicker.Presentation;

namespace RClicker.Tests.Native;

public class PdfKeyInputTests
{
    [Fact]
    public void FullScreenChordPressesAndReleasesControlInOrder()
    {
        var inputs = Win32KeySender.BuildInputs(PresentationKey.ControlL);
        Assert.Equal(new ushort[] { NativeMethods.VK_CONTROL, NativeMethods.VK_L, NativeMethods.VK_L, NativeMethods.VK_CONTROL },
            inputs.Select(i => i.U.Keyboard.VirtualKey));
        Assert.Equal(new uint[] { 0, 0, NativeMethods.KEYEVENTF_KEYUP, NativeMethods.KEYEVENTF_KEYUP },
            inputs.Select(i => i.U.Keyboard.Flags));
        Assert.All(inputs, i => Assert.Equal(NativeMethods.INPUT_KEYBOARD, i.Type));
    }

    [Theory]
    [InlineData(PresentationKey.PageDown, NativeMethods.VK_NEXT)]
    [InlineData(PresentationKey.PageUp, NativeMethods.VK_PRIOR)]
    public void PageKeysAreExtendedAndReleased(PresentationKey key, ushort vk)
    {
        var inputs = Win32KeySender.BuildInputs(key);
        Assert.Equal(2, inputs.Length);
        Assert.All(inputs, i => Assert.Equal(vk, i.U.Keyboard.VirtualKey));
        Assert.Equal(NativeMethods.KEYEVENTF_EXTENDEDKEY, inputs[0].U.Keyboard.Flags);
        Assert.Equal(NativeMethods.KEYEVENTF_EXTENDEDKEY | NativeMethods.KEYEVENTF_KEYUP, inputs[1].U.Keyboard.Flags);
    }
}

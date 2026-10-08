using System.Diagnostics;
using RClicker.Native;

namespace RClicker.Tests.Native;

public class NativeMethodsTests
{
    [Fact]
    public void CurrentProcessName_ComesFromLimitedQueryImageName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var expected = Process.GetCurrentProcess().ProcessName;
        var actual = NativeMethods.GetProcessName((uint)Environment.ProcessId);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InaccessibleProcess_IsReportedAsUnknown()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Null(NativeMethods.GetProcessName(uint.MaxValue));
    }

    [Fact]
    public void ProcessPathBuffer_GrowsToMaximumWithoutSkippingIt()
    {
        var lengths = new List<int>();
        int length = NativeMethods.INITIAL_PROCESS_PATH_BUFFER_LENGTH;
        do
        {
            lengths.Add(length);
            length = NativeMethods.NextProcessPathBufferLength(length);
        }
        while (length != NativeMethods.MAX_PROCESS_PATH_BUFFER_LENGTH);

        lengths.Add(length);

        Assert.Equal(
            new[] { 260, 520, 1040, 2080, 4160, 8320, 16640, 32768 },
            lengths);
        Assert.Equal(
            NativeMethods.MAX_PROCESS_PATH_BUFFER_LENGTH,
            NativeMethods.NextProcessPathBufferLength(NativeMethods.MAX_PROCESS_PATH_BUFFER_LENGTH));
    }
}

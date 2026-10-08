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
}

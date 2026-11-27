using Xunit;

namespace PurviewCopilot.Tests;

/// <summary>
/// Marks a test that exercises Windows DPAPI at-rest protection. DPAPI is provided by
/// crypt32.dll, so the test is skipped on a non-Windows audit host instead of failing there.
/// The assertions are unchanged and the test still runs in full on Windows CI.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows-only: DPAPI at-rest protection requires crypt32.dll.";
        }
    }
}

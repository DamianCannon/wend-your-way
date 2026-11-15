using System.Runtime.InteropServices;

namespace WendYourWay.Cli;

/// <summary>
/// Keeps Windows from sleeping while wend works: a Great Britain build was paused for 14 hours by
/// Modern Standby while its clock counted on (T035). Idle sleep only: closing the lid still
/// sleeps. Elsewhere than Windows, it does nothing.
/// </summary>
public sealed class KeepAwake : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    private readonly bool _held;

    private KeepAwake(bool held) => _held = held;

    /// <summary>Asks Windows not to sleep until this is disposed.</summary>
    public static KeepAwake WhileRunning() =>
        new(OperatingSystem.IsWindows() && SetThreadExecutionState(EsContinuous | EsSystemRequired) != 0);

    /// <summary>Whether Windows has been asked to stay awake, by this or anything else.</summary>
    public static bool SystemRequired()
    {
        if (!OperatingSystem.IsWindows()) return false;
        uint state = 0;
        // SystemExecutionState (16): the execution state flags in force for the whole system.
        return CallNtPowerInformation(16, IntPtr.Zero, 0, ref state, sizeof(uint)) == 0 && (state & EsSystemRequired) != 0;
    }

    public void Dispose()
    {
        if (_held) _ = SetThreadExecutionState(EsContinuous);
    }

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint SetThreadExecutionState(uint flags);

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, uint inputBufferLength, ref uint outputBuffer, uint outputBufferLength);
}

using System.Runtime.InteropServices;

namespace WendYourWay.Cli;

/// <summary>
/// The laptop kept from sleeping while a long job runs (#142, T138, STORY-049 AC9; user chose "Keep
/// it awake"): Windows asked not to sleep for as long as the job runs, the user's power settings
/// unchanged, the screen free to turn off. Windows ties the request to the thread that made it, so
/// it's held on a thread of its own and let go when the job ends. A closed lid may still sleep it.
/// </summary>
public sealed partial class StayingAwake : IDisposable
{
    private const uint Continuous = 0x80000000;
    private const uint SystemRequired = 0x00000001;

    private readonly ManualResetEventSlim _done = new();
    private readonly ManualResetEventSlim _asked = new();
    private readonly Thread? _holding;

    /// <summary>Whether Windows took the request (false off Windows).</summary>
    public bool Granted { get; private set; }

    public StayingAwake()
    {
        if (!OperatingSystem.IsWindows()) return;
        _holding = new Thread(() =>
        {
            Granted = SetThreadExecutionState(Continuous | SystemRequired) != 0;
            _asked.Set();
            _done.Wait();
            _ = SetThreadExecutionState(Continuous);
        })
        { IsBackground = true, Name = "Staying awake" };
        _holding.Start();
        _asked.Wait(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        _done.Set();
        _holding?.Join(TimeSpan.FromSeconds(2));
        _done.Dispose();
        _asked.Dispose();
    }

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint SetThreadExecutionState(uint flags);
}

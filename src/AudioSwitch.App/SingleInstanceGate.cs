using System.Threading;

namespace AudioSwitch.App;

/// <summary>Keeps one running instance: a second start signals the first one to open its panel, then exits.</summary>
public sealed class SingleInstanceGate : IDisposable
{
    private const string MutexName = "Local\\AudioSwitch.SingleInstance.v1";
    private const string SignalName = "Local\\AudioSwitch.Wake.v1";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _wakeSignal;

    public bool IsPrimary { get; }

    public SingleInstanceGate()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        IsPrimary = createdNew;
        _wakeSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        if (!IsPrimary) _wakeSignal.Set();
    }

    public bool ConsumeWakeSignal() => _wakeSignal.WaitOne(0);

    public void Dispose()
    {
        _wakeSignal.Dispose();
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }
}

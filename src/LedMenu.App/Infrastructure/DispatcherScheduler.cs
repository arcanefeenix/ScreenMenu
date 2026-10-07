using System.Windows.Threading;
using LedMenu.Core.Autosave;

namespace LedMenu.App.Infrastructure;

/// <summary>Runs delayed actions on the UI thread with a DispatcherTimer.</summary>
public sealed class DispatcherScheduler : IScheduler
{
    private sealed class Handle : IDisposable
    {
        public required DispatcherTimer Timer { get; init; }
        public void Dispose() => Timer.Stop();
    }

    public IDisposable ScheduleOnce(TimeSpan delay, Action callback)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); callback(); };
        timer.Start();
        return new Handle { Timer = timer };
    }
}

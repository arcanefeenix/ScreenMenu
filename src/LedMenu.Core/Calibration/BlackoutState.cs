namespace LedMenu.Core.Calibration;

/// <summary>
/// Blackout is a switch laid over whatever the output is showing: while it is on the LED output is pure black, but the
/// menus, screens, output mode and page clock underneath carry on untouched, so switching it off restores exactly what
/// was there. It is only available while output is running (there is nothing to black out otherwise), it is never saved,
/// and stopping output always switches it off so it cannot be waiting as a surprise at the next start.
/// </summary>
public sealed class BlackoutState
{
    public bool IsActive { get; private set; }

    /// <summary>True when the toggle does something: output is running, or blackout is on and must be switchable off.</summary>
    public bool CanToggle(bool outputRunning) => outputRunning || IsActive;

    /// <summary>Flips blackout. Returns false (and changes nothing) when it is not available.</summary>
    public bool Toggle(bool outputRunning)
    {
        if (!CanToggle(outputRunning)) return false;
        IsActive = !IsActive;
        return true;
    }

    /// <summary>Output stopped: blackout is cleared. Returns true if it had been on.</summary>
    public bool OutputStopped()
    {
        var was = IsActive;
        IsActive = false;
        return was;
    }

    /// <summary>What actually goes on the LED output: nothing (pure black) during blackout, otherwise the frame.</summary>
    public T? Apply<T>(T? frame) where T : class => IsActive ? null : frame;
}

namespace LedMenu.Core.Display;

public enum DisplayChangeAction
{
    /// <summary>Nothing to do.</summary>
    None,
    /// <summary>The output display is gone or no longer certain: stop output. Never move it to another display.</summary>
    Stop,
    /// <summary>The same display changed size or position: move and resize the output window to match.</summary>
    Reposition,
}

/// <summary>
/// The decisions made when the set of connected displays changes while output is running, or after it was
/// stopped because its display disappeared. Pure, so each rule can be tested without a monitor to unplug.
/// </summary>
public static class OutputDisplayWatch
{
    /// <summary>What to do about a running output given the display scan that just happened.</summary>
    public static DisplayChangeAction WhileRunning(MatchResult match, DisplayInfo? runningOn)
    {
        // Only an exact device-path match keeps output alive; a lookalike or a missing display stops it.
        if (match.Kind != MatchKind.Exact || match.Display is null) return DisplayChangeAction.Stop;
        if (runningOn is null) return DisplayChangeAction.None;
        var now = match.Display;
        return runningOn.X != now.X || runningOn.Y != now.Y || runningOn.Width != now.Width || runningOn.Height != now.Height
            ? DisplayChangeAction.Reposition
            : DisplayChangeAction.None;
    }

    /// <summary>
    /// Whether output that was stopped by losing its display should start again by itself.
    /// Only when the very same display is back (exact match), the start would need no confirmation prompt,
    /// and the stop was caused by the display's loss (not by the operator).
    /// </summary>
    public static bool ShouldRestore(bool stoppedByDisplayLoss, OutputStartDecision decision) =>
        stoppedByDisplayLoss && decision.CanStart && decision.Warnings.Count == 0;
}

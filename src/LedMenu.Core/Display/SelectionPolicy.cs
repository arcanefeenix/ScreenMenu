namespace LedMenu.Core.Display;

/// <summary>
/// Decides whether choosing a display for a role needs an explicit "are you sure".
/// An empty result means the choice is safe to apply directly.
/// </summary>
public static class SelectionPolicy
{
    /// <param name="candidate">The display the operator wants as LED output.</param>
    /// <param name="all">Everything currently connected.</param>
    /// <param name="operatorDisplay">The operator display match, if one is chosen and connected.</param>
    /// <param name="operatorWindowMonitor">Handle of the monitor the operator window is on, or 0 if unknown.</param>
    public static IReadOnlyList<string> CheckLedOutput(
        DisplayInfo candidate, IReadOnlyList<DisplayInfo> all,
        DisplayInfo? operatorDisplay, long operatorWindowMonitor)
    {
        var warnings = new List<string>();

        if (all.Count == 1)
            warnings.Add("This is the only display connected. LED output would cover the operator screen and hide this application.");
        if (operatorDisplay is not null && SameDisplay(operatorDisplay, candidate))
            warnings.Add("This is the display you selected as the Operator display.");
        if (operatorWindowMonitor != 0 && operatorWindowMonitor == candidate.Handle)
            warnings.Add("The operator window is currently on this display.");
        if (candidate.IsPrimary)
            warnings.Add("This is the Windows primary display (taskbar and desktop icons are normally here).");

        return warnings;
    }

    /// <summary>Choosing the same display as both Operator and LED output is not allowed without confirmation.</summary>
    public static IReadOnlyList<string> CheckOperator(DisplayInfo candidate, DisplayInfo? ledOutput)
    {
        var warnings = new List<string>();
        if (ledOutput is not null && SameDisplay(ledOutput, candidate))
            warnings.Add("This is the display currently selected as LED output.");
        return warnings;
    }

    private static bool SameDisplay(DisplayInfo a, DisplayInfo b) =>
        a.Handle == b.Handle || string.Equals(a.DevicePath, b.DevicePath, StringComparison.OrdinalIgnoreCase);
}

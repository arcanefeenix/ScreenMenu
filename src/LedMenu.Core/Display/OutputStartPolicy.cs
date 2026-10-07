namespace LedMenu.Core.Display;

public sealed record OutputStartDecision(
    bool CanStart,
    DisplayInfo? Display,
    string? Refusal,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Decides whether LED output may start right now. Output starts only on a display that was matched
/// by its stable device ID and is connected at this moment. There is no fallback to any other display.
/// </summary>
public static class OutputStartPolicy
{
    public static OutputStartDecision Evaluate(
        MatchResult output, IReadOnlyList<DisplayInfo> all, long operatorWindowMonitor)
    {
        switch (output.Kind)
        {
            case MatchKind.NotConfigured:
                return Refuse("No LED output display has been selected. Choose one in the Displays list first.");
            case MatchKind.NotFound:
                return Refuse("The saved LED output display is not connected, so output was not started. " +
                              "No other display was used. Reconnect it or choose a different display.");
            case MatchKind.Fallback:
                return Refuse("The saved LED output display needs to be confirmed before output can start " +
                              "(Windows changed its device ID). Click \"Confirm this display\" first.");
        }

        var display = output.Display!;
        if (display.Width <= 0 || display.Height <= 0)
            return Refuse("The LED output display reports an invalid size, so output was not started.");

        return new OutputStartDecision(true, display, null, StartWarnings(display, all, operatorWindowMonitor));
    }

    /// <summary>
    /// Only the conditions that can strand the operator are re-checked at every start. Warnings the operator
    /// already accepted when choosing the display (e.g. "it is the primary display") are not repeated.
    /// </summary>
    private static IReadOnlyList<string> StartWarnings(DisplayInfo display, IReadOnlyList<DisplayInfo> all, long operatorWindowMonitor)
    {
        var warnings = new List<string>();
        if (all.Count == 1)
            warnings.Add("This is the only display connected. LED output will cover the operator screen and hide this application. " +
                         "Press Ctrl+Shift+F12 to stop output.");
        else if (operatorWindowMonitor != 0 && operatorWindowMonitor == display.Handle)
            warnings.Add("The operator window is currently on this display. LED output will cover it. " +
                         "Press Ctrl+Shift+F12 to stop output, or move the operator window first.");
        return warnings;
    }

    private static OutputStartDecision Refuse(string reason) =>
        new(false, null, reason, Array.Empty<string>());
}

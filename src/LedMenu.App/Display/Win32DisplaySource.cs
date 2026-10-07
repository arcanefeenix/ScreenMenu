using System.Runtime.InteropServices;
using LedMenu.Core.Display;
using LedMenu.Core.Logging;
using static LedMenu.App.Display.NativeMethods;

namespace LedMenu.App.Display;

/// <summary>
/// Enumerates active displays. Geometry and DPI come from the monitor APIs (physical pixels because
/// the process is PerMonitorV2). Friendly name and stable device path come from the display
/// configuration API, keyed by Windows' GDI name; if that is unavailable the display is still listed
/// with a generic name and an empty path, which the matcher treats as "cannot match by path".
/// </summary>
public sealed class Win32DisplaySource : IDisplaySource
{
    private readonly IAppLog _log;

    public Win32DisplaySource(IAppLog log) => _log = log;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var targets = TryGetTargetNames();
        var result = new List<DisplayInfo>();

        MonitorEnumProc proc = (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
        {
            try
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (!GetMonitorInfo(hMonitor, ref info)) return true;

                uint dpiX = 96, dpiY = 96;
                if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dx, out var dy) == 0 && dx > 0)
                { dpiX = dx; dpiY = dy; }

                targets.TryGetValue(info.szDevice, out var target);
                var r = info.rcMonitor;
                result.Add(new DisplayInfo(
                    DevicePath: target.path ?? "",
                    DeviceName: info.szDevice,
                    FriendlyName: string.IsNullOrWhiteSpace(target.name) ? "Generic display" : target.name!,
                    X: r.Left, Y: r.Top, Width: r.Right - r.Left, Height: r.Bottom - r.Top,
                    DpiX: (int)dpiX, DpiY: (int)dpiY,
                    IsPrimary: (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                    Handle: hMonitor.ToInt64()));
            }
            catch (Exception ex)
            {
                _log.Warn("Could not read one display; skipping it.", ex);
            }
            return true;
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero))
            _log.Error("EnumDisplayMonitors failed.");
        GC.KeepAlive(proc);
        return result;
    }

    /// <summary>Maps GDI device name (\\.\DISPLAYn) to (friendly name, stable monitor device path).</summary>
    private Dictionary<string, (string? name, string? path)> TryGetTargetNames()
    {
        var map = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
                return map;
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return map;

            for (var i = 0; i < pathCount; i++)
            {
                var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                src.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                src.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                src.header.adapterId = paths[i].sourceInfo.adapterId;
                src.header.id = paths[i].sourceInfo.id;
                if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;

                var tgt = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                tgt.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                tgt.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                tgt.header.adapterId = paths[i].targetInfo.adapterId;
                tgt.header.id = paths[i].targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref tgt) != 0) continue;

                map[src.viewGdiDeviceName] = (tgt.monitorFriendlyDeviceName, tgt.monitorDevicePath);
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Display configuration query failed; monitors will have generic names and no stable ID.", ex);
        }
        return map;
    }

    /// <summary>Handle of the monitor a window is on, or 0.</summary>
    public static long MonitorOfWindow(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? 0 : MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST).ToInt64();
}

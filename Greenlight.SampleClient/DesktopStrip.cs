using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Greenlight.SampleClient;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct StripBounds(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>Where to put the road.</summary>
public enum StripPlacement
{
    /// <summary>Resting on the taskbar's top edge, on the desktop. The cars never cover a taskbar button.</summary>
    AboveTaskbar,

    /// <summary>Over the taskbar itself, driving across it.</summary>
    OverTaskbar,

    /// <summary>Ignore the taskbar entirely and sit at the bottom of the desktop.</summary>
    BottomOfScreen,
}

/// <summary>
/// Works out where the cars drive: relative to the taskbar when the taskbar is along the
/// bottom and actually on screen, and along the bottom of the desktop otherwise.
/// </summary>
/// <remarks>
/// The fallback is not a failure case. A taskbar down the left-hand side is an ordinary
/// setup, and a vertical column of cars would be a worse answer than the bottom of the
/// screen; an auto-hiding taskbar is treated the same way, because cars that vanish with it
/// would look broken rather than clever. The fallback uses the work area rather than the
/// raw screen, so a side-mounted taskbar does not end up underneath the traffic.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class DesktopStrip
{
    private const uint AbmGetTaskbarPos = 0x00000005;
    private const uint AbmGetState = 0x00000004;
    private const uint AbsAutoHide = 0x00000001;
    private const uint AbeBottom = 3;

    private const uint SpiGetWorkArea = 0x0030;

    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public Rect rc;
        public int lParam;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref AppBarData pData);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, ref Rect pvParam, uint fWinIni);

    /// <summary>Where the cars should drive, in physical pixels.</summary>
    /// <param name="placement">What to do when there is a usable taskbar.</param>
    /// <param name="fallbackHeight">Strip height when the taskbar cannot be used.</param>
    public static StripBounds Find(StripPlacement placement, int fallbackHeight)
    {
        if (placement == StripPlacement.BottomOfScreen) return BottomOfDesktop(fallbackHeight);

        var taskbar = BottomTaskbar();
        if (taskbar.IsEmpty) return BottomOfDesktop(fallbackHeight);

        if (placement == StripPlacement.OverTaskbar) return taskbar;

        // Above it: the same width and the same height, sitting on its top edge. Same height
        // deliberately — the cars are sized from the strip, and "above the taskbar" should
        // not also mean "suddenly a different size".
        var height = Math.Clamp(taskbar.Height, 24, 200);
        return new StripBounds(taskbar.X, taskbar.Y - height, taskbar.Width, height);
    }

    /// <summary>The taskbar's rectangle, but only if it is along the bottom and on screen.</summary>
    private static StripBounds BottomTaskbar()
    {
        try
        {
            var data = new AppBarData { cbSize = (uint)Marshal.SizeOf<AppBarData>() };

            if (SHAppBarMessage(AbmGetTaskbarPos, ref data) == IntPtr.Zero) return default;
            if (data.uEdge != AbeBottom) return default;

            var state = new AppBarData { cbSize = (uint)Marshal.SizeOf<AppBarData>() };
            if (((uint)SHAppBarMessage(AbmGetState, ref state) & AbsAutoHide) != 0) return default;

            var width = data.rc.Right - data.rc.Left;
            var height = data.rc.Bottom - data.rc.Top;
            if (width <= 0 || height <= 0) return default;

            return new StripBounds(data.rc.Left, data.rc.Top, width, height);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// The bottom of the usable desktop. The work area, not the whole screen, so a taskbar
    /// pinned to the left or the right is not driven over.
    /// </summary>
    private static StripBounds BottomOfDesktop(int fallbackHeight)
    {
        var work = new Rect();
        if (SystemParametersInfoW(SpiGetWorkArea, 0, ref work, 0))
        {
            var width = work.Right - work.Left;
            var height = work.Bottom - work.Top;
            if (width > 0 && height > 0)
            {
                var strip = Math.Min(fallbackHeight, height);
                return new StripBounds(work.Left, work.Bottom - strip, width, strip);
            }
        }

        var screenWidth = GetSystemMetrics(SmCxScreen);
        var screenHeight = GetSystemMetrics(SmCyScreen);
        if (screenWidth <= 0 || screenHeight <= 0) return new StripBounds(0, 0, 1280, fallbackHeight);

        return new StripBounds(0, screenHeight - fallbackHeight, screenWidth, fallbackHeight);
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Greenlight.CarsClient;

/// <summary>
/// Makes the window furniture rather than an application: the mouse falls straight through
/// it, it never takes focus, and it stays out of Alt+Tab.
/// </summary>
/// <remarks>
/// The same four styles Greenlight's own desktop stoplight uses, for the same reason. The
/// one that is easy to leave out is <c>WS_EX_LAYERED</c>: Avalonia's windows are composited,
/// and on a composited window <c>WS_EX_TRANSPARENT</c> alone does not take it out of hit
/// testing — clicks land on the cars instead of on the taskbar button underneath.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class ClickThroughNative
{
    private const int GwlExStyle = -20;

    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;

    private const uint LwaAlpha = 0x00000002;

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    public static void Apply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        SetBits(hwnd, WsExTransparent | WsExNoActivate | WsExToolWindow | WsExLayered);

        // A window that has just become layered needs its alpha stated once. 255 means "no
        // uniform fade"; the cars' own translucency comes from how they are drawn.
        SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
    }

    /// <summary>
    /// Put the window back at the top of the z-order. Worth doing on a timer rather than
    /// once.
    /// </summary>
    /// <remarks>
    /// "Topmost" is not a rank, it is a band, and the taskbar is in the same band — so the
    /// last window to claim it wins, and the shell claims it often. Setting the flag once at
    /// startup gets you cars that are on top until the first time somebody clicks the
    /// taskbar and then silently behind it forever, which looks exactly like the app having
    /// crashed.
    /// </remarks>
    public static void KeepOnTop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private static void SetBits(IntPtr hwnd, int bits)
    {
        // Widened through uint first: style bits are a bit pattern, not a number, and a
        // 32-bit signed value with the top bit set would sign-extend into the 64-bit style
        // word and turn on every high bit — WS_EX_NOACTIVATE among them.
        var mask = (long)(uint)bits;

        var current = (long)GetWindowLongPtrW(hwnd, GwlExStyle);
        var updated = current | mask;
        if (updated != current) SetWindowLongPtrW(hwnd, GwlExStyle, (IntPtr)updated);
    }
}

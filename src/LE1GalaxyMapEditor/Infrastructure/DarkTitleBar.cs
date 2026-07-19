using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LE1GalaxyMapEditor.Infrastructure;

/// <summary>Requests native dark caption chrome while retaining Windows resize, snap and accessibility behaviour.</summary>
public static class DarkTitleBar
{
    private const int EraseBackgroundMessage = 0x0014;
    private const int ImmersiveDarkMode = 20;
    private const int ImmersiveDarkModeLegacy = 19;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private const uint RedrawInvalidate = 0x0001;
    private const uint RedrawErase = 0x0004;
    private const uint RedrawUpdateNow = 0x0100;
    private const int AppBackgroundColorRef = 0x0018100A; // #0A1018 as a Win32 COLORREF.

    public static void Apply(Window window)
    {
        void ApplyNow(object? sender = null, EventArgs? args = null)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            if (HwndSource.FromHwnd(handle) is { } source)
            {
                source.CompositionTarget.BackgroundColor =
                    System.Windows.Media.Color.FromRgb(0x0A, 0x10, 0x18);
                source.AddHook(PaintAppBackground);

                // WPF can spend a noticeable amount of time measuring complex
                // content after the HWND exists. Ensure any native erase that
                // occurs before its first render is already the application navy.
                RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero,
                    RedrawInvalidate | RedrawErase | RedrawUpdateNow);
            }
            var enabled = 1;
            if (DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, ImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            var caption = 0x0021170D; // #0D1721 as a Win32 COLORREF.
            var text = 0x00F5F0E8;    // #E8F0F5
            var border = 0x00493A2A;  // #2A3A49
            DwmSetWindowAttribute(handle, CaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, TextColor, ref text, sizeof(int));
            DwmSetWindowAttribute(handle, BorderColor, ref border, sizeof(int));
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) ApplyNow();
        else window.SourceInitialized += ApplyNow;
    }

    private static IntPtr PaintAppBackground(
        IntPtr window,
        int message,
        IntPtr deviceContext,
        IntPtr parameter,
        ref bool handled)
    {
        if (message != EraseBackgroundMessage || deviceContext == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (GetClientRect(window, out var clientRect))
        {
            var brush = CreateSolidBrush(AppBackgroundColorRef);
            if (brush != IntPtr.Zero)
            {
                FillRect(deviceContext, ref clientRect, brush);
                DeleteObject(brush);
            }
        }

        handled = true;
        return new IntPtr(1);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        IntPtr window,
        IntPtr updateRect,
        IntPtr updateRegion,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect clientRect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr deviceContext, ref NativeRect rect, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int colorRef);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

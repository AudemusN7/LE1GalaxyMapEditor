using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LE1GalaxyMapEditor.Infrastructure;

/// <summary>Requests native dark caption chrome while retaining Windows resize, snap and accessibility behaviour.</summary>
public static class DarkTitleBar
{
    private const int ImmersiveDarkMode = 20;
    private const int ImmersiveDarkModeLegacy = 19;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    public static void Apply(Window window)
    {
        void ApplyNow(object? sender = null, EventArgs? args = null)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}

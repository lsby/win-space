using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace win_space;

partial class HiddenWindow
{
    private IntPtr 获取焦点显示器()
    {
        IntPtr 焦点窗口 = NativeMethods.GetForegroundWindow();
        if (焦点窗口 == IntPtr.Zero)
        {
            NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
            return NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        }
        return NativeMethods.MonitorFromWindow(焦点窗口, NativeMethods.MONITOR_DEFAULTTONEAREST);
    }

    private BitmapSource? 截取显示器画面(IntPtr hMonitor)
    {
        int x = 0, y = 0, w = 0, h = 0;
        try
        {
            NativeMethods.MONITORINFO mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(mi);
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return null;

            x = mi.rcMonitor.left;
            y = mi.rcMonitor.top;
            w = mi.rcMonitor.right - mi.rcMonitor.left;
            h = mi.rcMonitor.bottom - mi.rcMonitor.top;

            if (w <= 0 || h <= 0) return null;

            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);
            return ConvertBitmapToBitmapSource(bmp);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    private NativeMethods.RECT 获取显示器矩形(IntPtr hMonitor)
    {
        NativeMethods.MONITORINFO mi = new NativeMethods.MONITORINFO();
        mi.cbSize = Marshal.SizeOf(mi);
        NativeMethods.GetMonitorInfo(hMonitor, ref mi);
        return mi.rcMonitor;
    }

    private void UpdateTaskbarVisibility()
    {
        Action<IntPtr> checkAndSetVisibility = (hwnd) =>
        {
            if (hwnd == IntPtr.Zero) return;
            
            IntPtr hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var manager = SpaceManager.Instance;
            
            if (manager.分组字典.TryGetValue(hMonitor, out var group))
            {
                bool isSpaceActive = group.CurrentIndex != -1;
                int cmd = isSpaceActive ? NativeMethods.SW_HIDE : NativeMethods.SW_SHOW;
                NativeMethods.ShowWindow(hwnd, cmd);
            }
            else
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            }
        };

        IntPtr trayHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        checkAndSetVisibility(trayHwnd);

        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            StringBuilder sb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
            if (sb.ToString() == "Shell_SecondaryTrayWnd")
            {
                checkAndSetVisibility(hwnd);
            }
            return true;
        }, IntPtr.Zero);
    }

    private void SetTaskbarVisibility(bool visible)
    {
        int cmd = visible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE;
        
        IntPtr trayHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (trayHwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(trayHwnd, cmd);
        }

        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            StringBuilder sb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
            if (sb.ToString() == "Shell_SecondaryTrayWnd")
            {
                NativeMethods.ShowWindow(hwnd, cmd);
            }
            return true;
        }, IntPtr.Zero);
    }
}

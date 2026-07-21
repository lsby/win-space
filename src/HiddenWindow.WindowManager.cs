using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace win_space;

partial class HiddenWindow
{
    /// <summary>
    /// 将窗口铺满屏幕（覆盖任务栏，保留标题栏）
    /// </summary>
    private void 设置窗口全屏(IntPtr hwnd, IntPtr hMonitor)
    {
        NativeMethods.MONITORINFO mi = new NativeMethods.MONITORINFO();
        mi.cbSize = Marshal.SizeOf(mi);
        if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
        {
            int x = mi.rcMonitor.left;
            int y = mi.rcMonitor.top;
            int width = mi.rcMonitor.right - mi.rcMonitor.left;
            int height = mi.rcMonitor.bottom - mi.rcMonitor.top;

            // 如果原本是最大化状态，先恢复，否则 SetWindowPos 可能会被操作系统的最大化限制约束
            if (NativeMethods.IsZoomed(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            }

            // 先第一步：不改变样式，先将窗口安全移动并调整大小到目标显示器上
            IntPtr HWND_NOTOPMOST = new IntPtr(-2);
            NativeMethods.SetWindowPos(hwnd, HWND_NOTOPMOST, x, y, width, height, NativeMethods.SWP_SHOWWINDOW);

            // 去除可调整大小的边框
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            style &= ~NativeMethods.WS_THICKFRAME;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style);

            // 禁用拖动：从系统菜单中移除“移动”选项
            IntPtr hMenu = NativeMethods.GetSystemMenu(hwnd, false);
            if (hMenu != IntPtr.Zero)
            {
                NativeMethods.RemoveMenu(hMenu, NativeMethods.SC_MOVE, NativeMethods.MF_BYCOMMAND);
            }

            // 第二步：触发非客户区重绘 (SWP_FRAMECHANGED)，此时不改变位置 (NOMOVE, NOSIZE)
            // 分成两步走可以极大避免某些应用（如 Chromium/Edge）在跨负数坐标屏幕时，
            // 因为瞬间丢失边框又大跨度移动造成的内部布局溢出崩溃。
            NativeMethods.SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_FRAMECHANGED);
        }
    }

    private void 显示黑底(IntPtr hMonitor, IntPtr targetHwnd)
    {
        var 分组 = SpaceManager.Instance.获取或创建分组(hMonitor);
        if (分组.BlackBackground == null)
        {
            分组.BlackBackground = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.Black,
                Topmost = false,
                ShowActivated = false
            };
            分组.BlackBackground.SourceInitialized += (s, e) => {
                var hwndBg = new WindowInteropHelper(分组.BlackBackground).Handle;
                int exstyle = NativeMethods.GetWindowLong(hwndBg, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwndBg, NativeMethods.GWL_EXSTYLE, exstyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
            };
        }

        var rect = 获取显示器矩形(hMonitor);

        double dpiScaleX = 1.0;
        double dpiScaleY = 1.0;
        if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.Monitor_DPI_Type.MDT_Effective_DPI, out uint dpiX, out uint dpiY) == 0)
        {
            dpiScaleX = dpiX / 96.0;
            dpiScaleY = dpiY / 96.0;
        }

        分组.BlackBackground.Left = rect.left / dpiScaleX;
        分组.BlackBackground.Top = rect.top / dpiScaleY;
        分组.BlackBackground.Width = (rect.right - rect.left) / dpiScaleX;
        分组.BlackBackground.Height = (rect.bottom - rect.top) / dpiScaleY;

        分组.BlackBackground.Show();

        IntPtr bgHwnd = new WindowInteropHelper(分组.BlackBackground).Handle;
        if (bgHwnd != IntPtr.Zero && targetHwnd != IntPtr.Zero)
        {
            // 强制使用物理坐标来定位和缩放，同时设置 Z 轴 (不使用 NOMOVE 和 NOSIZE)
            NativeMethods.SetWindowPos(bgHwnd, targetHwnd, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, NativeMethods.SWP_NOACTIVATE);
        }
    }

    private void UnlockAllWindows()
    {
        SetTaskbarVisibility(true);

        // 恢复所有显示器分组中的窗口
        foreach (var 分组 in SpaceManager.Instance.分组字典.Values)
        {
            分组.BlackBackground?.Close();
            foreach (var space in 分组.Spaces)
            {
                IntPtr hwnd = space.Hwnd;
                if (hwnd != IntPtr.Zero)
                {
                    // 恢复可调整大小的边框
                    int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
                    style |= NativeMethods.WS_THICKFRAME;
                    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style);

                    // 恢复系统菜单（重新允许拖动）
                    NativeMethods.GetSystemMenu(hwnd, true);

                    IntPtr HWND_NOTOPMOST = new IntPtr(-2);
                    if (!space.WasMaximized)
                    {
                        // 原本不是最大化的，恢复原大小
                        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

                        int width = space.OriginalRect.right - space.OriginalRect.left;
                        int height = space.OriginalRect.bottom - space.OriginalRect.top;

                        uint flags = NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW;
                        if (width <= 0 || height <= 0)
                        {
                            flags |= 0x0001 | 0x0002;
                        }

                        NativeMethods.SetWindowPos(hwnd, HWND_NOTOPMOST, space.OriginalRect.left, space.OriginalRect.top, width, height, flags);
                    }
                    else
                    {
                        // 恢复最大化
                        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
                        NativeMethods.SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);
                    }
                }
            }
        }

        // 增加兜底逻辑：遍历系统中所有的可见窗口，如果其带有标题栏，强制恢复其边框和可移动属性
        // 这样可以解决意外情况下，部分窗口丢失了在 SpaceManager 中的记录但依然处于无边框锁定的问题
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd))
            {
                int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);

                // 只有带有标题栏的主窗口才强制恢复，避免影响系统的各种无边框弹窗、菜单
                if ((style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION)
                {
                    // 恢复可调整大小边框
                    if ((style & NativeMethods.WS_THICKFRAME) == 0)
                    {
                        style |= NativeMethods.WS_THICKFRAME;
                        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style);
                    }

                    // 恢复系统菜单（重新允许拖动等操作）
                    NativeMethods.GetSystemMenu(hwnd, true);

                    // 取消顶层置顶（HWND_NOTOPMOST），并触发框架刷新
                    NativeMethods.SetWindowPos(hwnd, new IntPtr(-2), 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);
                }
            }
            return true;
        }, IntPtr.Zero);

        SpaceManager.Instance.ClearState();
        SpaceManager.Instance.分组字典.Clear();
    }
}

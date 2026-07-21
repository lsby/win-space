using System;
using System.Text;
using System.Windows.Interop;

namespace win_space;

partial class HiddenWindow
{
    private void ShowMissionControl()
    {
        if (_mcWindow != null) return;

        IntPtr hMonitor = 获取焦点显示器();
        var manager = SpaceManager.Instance;
        var 分组 = manager.获取或创建分组(hMonitor);

        var currentSnapshot = 截取显示器画面(hMonitor);
        if (分组.CurrentIndex == -1) 分组.DesktopSnapshot = currentSnapshot;
        else 分组.Spaces[分组.CurrentIndex].Snapshot = currentSnapshot;

        var 显示器矩形 = 获取显示器矩形(hMonitor);

        _mcWindow = new MissionControlWindow(
            hMonitor,
            分组,
            显示器矩形,
            onSelect: (targetIndex) =>
            {
                _mcWindow = null;
                if (targetIndex != 分组.CurrentIndex)
                {
                    SwitchSpace(hMonitor, targetIndex);
                }
            },
            onCloseSpace: (index) =>
            {
                CloseSpace(hMonitor, index);
            },
            onReorder: (from, to) =>
            {
                ReorderSpace(hMonitor, from, to);
            }
        );

        _mcWindow.Closed += (s, e) => { _mcWindow = null; };
        _mcWindow.Show();
    }

    private void CloseSpace(IntPtr hMonitor, int index)
    {
        var manager = SpaceManager.Instance;
        var 分组 = manager.获取或创建分组(hMonitor);
        if (index < 0 || index >= 分组.Spaces.Count) return;

        var space = 分组.Spaces[index];
        IntPtr hwnd = space.Hwnd;

        if (NativeMethods.IsWindow(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);

            // 恢复可调整大小的边框
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            style |= NativeMethods.WS_THICKFRAME;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style);

            // 恢复系统菜单（重新允许拖动）
            NativeMethods.GetSystemMenu(hwnd, true);

            IntPtr HWND_NOTOPMOST = new IntPtr(-2);
            if (!space.WasMaximized)
            {
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
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
                NativeMethods.SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);
            }
        }

        分组.Spaces.RemoveAt(index);
        manager.SaveState();

        if (分组.CurrentIndex == index)
        {
            分组.CurrentIndex = -1;
            分组.BlackBackground?.Hide();
        }
        else if (分组.CurrentIndex > index)
        {
            分组.CurrentIndex--;
        }

        if (_mcWindow == null)
        {
            var 显示器矩形 = 获取显示器矩形(hMonitor);
            new OsdWindow(分组.CurrentIndex, 分组.Spaces, 显示器矩形).Show();
        }

        UpdateTaskbarVisibility();
        _lastSwitchTime = DateTime.Now;
    }

    private void ReorderSpace(IntPtr hMonitor, int from, int to)
    {
        var manager = SpaceManager.Instance;
        var 分组 = manager.获取或创建分组(hMonitor);
        var item = 分组.Spaces[from];
        分组.Spaces.RemoveAt(from);
        分组.Spaces.Insert(to, item);
        manager.SaveState();

        if (分组.CurrentIndex == from)
        {
            分组.CurrentIndex = to;
        }
        else if (from < 分组.CurrentIndex && to >= 分组.CurrentIndex)
        {
            分组.CurrentIndex--;
        }
        else if (from > 分组.CurrentIndex && to <= 分组.CurrentIndex)
        {
            分组.CurrentIndex++;
        }
    }

    private void 按焦点切换空间(int 方向)
    {
        IntPtr hMonitor = 获取焦点显示器();
        var manager = SpaceManager.Instance;
        var 分组 = manager.获取或创建分组(hMonitor);
        SwitchSpace(hMonitor, 分组.CurrentIndex + 方向);
    }

    private void SwitchSpace(IntPtr hMonitor, int targetIndex)
    {
        _lastSwitchTime = DateTime.Now;
        var manager = SpaceManager.Instance;
        var 分组 = manager.获取或创建分组(hMonitor);

        if (targetIndex < -1) targetIndex = -1;
        if (targetIndex >= 分组.Spaces.Count) targetIndex = 分组.Spaces.Count - 1;
        if (targetIndex == 分组.CurrentIndex) return;

        var currentSnapshot = 截取显示器画面(hMonitor);
        if (分组.CurrentIndex == -1) 分组.DesktopSnapshot = currentSnapshot;
        else 分组.Spaces[分组.CurrentIndex].Snapshot = currentSnapshot;

        if (分组.CurrentIndex != -1) NativeMethods.ShowWindow(分组.Spaces[分组.CurrentIndex].Hwnd, NativeMethods.SW_HIDE);
        if (targetIndex != -1)
        {
            var space = 分组.Spaces[targetIndex];
            设置窗口全屏(space.Hwnd, hMonitor);
            显示黑底(hMonitor, space.Hwnd);
            NativeMethods.SetForegroundWindow(space.Hwnd);
        }
        else
        {
            分组.BlackBackground?.Hide();
        }

        分组.CurrentIndex = targetIndex;

        var 显示器矩形 = 获取显示器矩形(hMonitor);
        new OsdWindow(targetIndex, 分组.Spaces, 显示器矩形).Show();

        UpdateTaskbarVisibility();
        _lastSwitchTime = DateTime.Now;
    }

    private async void MakeForegroundWindowFullscreen()
    {
        _lastSwitchTime = DateTime.Now;
        try
        {
            IntPtr targetHwnd = NativeMethods.GetForegroundWindow();
            if (targetHwnd == IntPtr.Zero || targetHwnd == _windowHandle || (_mcWindow != null && targetHwnd == new WindowInteropHelper(_mcWindow).Handle)) return;

            var manager = SpaceManager.Instance;

            // 如果窗口已经全屏，则执行反向操作（取消全屏并恢复原状）
            var existingGroup = manager.查找窗口所在分组(targetHwnd);
            if (existingGroup != null)
            {
                int index = existingGroup.Spaces.FindIndex(s => s.Hwnd == targetHwnd);
                if (index != -1)
                {
                    CloseSpace(existingGroup.显示器句柄, index);
                }
                return;
            }

            IntPtr hMonitor = NativeMethods.MonitorFromWindow(targetHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var 分组 = manager.获取或创建分组(hMonitor);

            if (分组.CurrentIndex == -1)
            {
                // 禁用动画，让隐藏操作瞬间完成，避免截取到带有半透明残影的桌面
                int disableAnimation = 1;
                NativeMethods.DwmSetWindowAttribute(targetHwnd, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLE, ref disableAnimation, sizeof(int));

                // 暂时隐藏窗口，让桌面重新绘制，这样截取的桌面缩略图就不包含该窗口
                NativeMethods.ShowWindow(targetHwnd, NativeMethods.SW_HIDE);
                await System.Threading.Tasks.Task.Delay(50); // 稍微等待 DWM 重绘

                分组.DesktopSnapshot = 截取显示器画面(hMonitor);

                // 恢复窗口、重新启用动画，并设为前台
                NativeMethods.ShowWindow(targetHwnd, NativeMethods.SW_SHOW);
                int enableAnimation = 0;
                NativeMethods.DwmSetWindowAttribute(targetHwnd, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLE, ref enableAnimation, sizeof(int));
                NativeMethods.SetForegroundWindow(targetHwnd);
            }

            var sb = new StringBuilder(256);
            NativeMethods.GetWindowText(targetHwnd, sb, sb.Capacity);

            NativeMethods.GetWindowRect(targetHwnd, out NativeMethods.RECT winRect);
            bool wasMaximized = NativeMethods.IsZoomed(targetHwnd);

            manager.AddSpace(hMonitor, targetHwnd, sb.ToString(), winRect, wasMaximized);
            manager.SaveState();

            设置窗口全屏(targetHwnd, hMonitor);
            显示黑底(hMonitor, targetHwnd);

            if (_mcWindow == null)
            {
                var 显示器矩形 = 获取显示器矩形(hMonitor);
                new OsdWindow(分组.CurrentIndex, 分组.Spaces, 显示器矩形).Show();
            }

            UpdateTaskbarVisibility();
            _lastSwitchTime = DateTime.Now;
        }
        catch
        {
        }
    }
}

using System;
using System.Linq;
using System.Text;
using System.Windows.Interop;

namespace win_space;

partial class HiddenWindow
{
    private void OnForegroundWindowChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if ((DateTime.Now - _lastSwitchTime).TotalMilliseconds < 200) return;
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd)) return;

        // 忽略我们自己的 UI 窗口
        if (hwnd == _windowHandle || (_mcWindow != null && hwnd == new WindowInteropHelper(_mcWindow).Handle)) return;

        var manager = SpaceManager.Instance;

        // 忽略用于遮挡桌面的黑底窗口
        if (manager.分组字典.Values.Any(g => g.BlackBackground != null && new WindowInteropHelper(g.BlackBackground).Handle == hwnd)) return;

        // 过滤一些不应触发切换的杂项窗口 (如 Tooltip 或右键菜单)
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return;

        StringBuilder className = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        string cName = className.ToString();

        // 忽略桌面背景、标准右键菜单、任务栏、开始菜单等系统 UI
        if (cName == "Progman" || 
            cName == "WorkerW" || 
            cName == "#32768" || 
            cName == "Shell_TrayWnd" || 
            cName == "Shell_SecondaryTrayWnd" || 
            cName == "Windows.UI.Core.CoreWindow" || 
            cName == "Xaml_WindowedPopupClass") 
            return;

        // 检查这个激活的窗口是否有 Owner (比如是某个全屏应用内部弹出的另存为对话框)
        IntPtr ownerHwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);
        if (ownerHwnd != IntPtr.Zero)
        {
            var ownerGroup = manager.查找窗口所在分组(ownerHwnd);
            if (ownerGroup != null)
            {
                // 如果是当前空间弹出的子窗口，则留在当前空间
                int tIndex = ownerGroup.Spaces.FindIndex(s => s.Hwnd == ownerHwnd);
                if (ownerGroup.CurrentIndex != tIndex)
                {
                    SwitchSpace(ownerGroup.显示器句柄, tIndex);
                }
                return;
            }
        }

        // 查找该窗口是否在我们的管理分组中
        var spaceGroup = manager.查找窗口所在分组(hwnd);
        if (spaceGroup != null)
        {
            int targetIndex = spaceGroup.Spaces.FindIndex(s => s.Hwnd == hwnd);
            if (spaceGroup.CurrentIndex != targetIndex)
            {
                SwitchSpace(spaceGroup.显示器句柄, targetIndex);
            }
        }
        else
        {
            // 这是一个不被我们管理的窗口 (例如新打开的 QQ，或者任务栏等桌面元素)
            // 我们应该切换到桌面，以确保用户能看到它
            IntPtr hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var group = manager.获取或创建分组(hMonitor);

            // 如果当前不是在桌面，切回桌面
            if (group.CurrentIndex != -1)
            {
                SwitchSpace(hMonitor, -1);
            }
        }
    }

    /// <summary>
    /// 窗口销毁事件回调 — 检查被销毁的窗口是否在我们的管理列表中
    /// </summary>
    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != 0) return;
        if (hwnd == IntPtr.Zero) return;

        var manager = SpaceManager.Instance;
        var 所在分组 = manager.查找窗口所在分组(hwnd);
        if (所在分组 == null) return;

        int 被移除索引 = 所在分组.Spaces.FindIndex(s => s.Hwnd == hwnd);
        bool 是当前活动 = 所在分组.CurrentIndex == 被移除索引;

        manager.移除失效窗口(hwnd);
        manager.SaveState();

        if (是当前活动)
        {
            所在分组.BlackBackground?.Hide();
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == NativeMethods.HOTKEY_ID) { MakeForegroundWindowFullscreen(); handled = true; }
            else if (id == NativeMethods.HOTKEY_LEFT) { 按焦点切换空间(-1); handled = true; }
            else if (id == NativeMethods.HOTKEY_RIGHT) { 按焦点切换空间(1); handled = true; }
            else if (id == NativeMethods.HOTKEY_UP) { ShowMissionControl(); handled = true; }
        }
        return IntPtr.Zero;
    }
}

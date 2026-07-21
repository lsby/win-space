using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;

namespace win_space;

class HiddenWindow : Window
{
    private IntPtr _windowHandle;
    private HwndSource? _source;
    private NotifyIcon _notifyIcon;
    private MissionControlWindow? _mcWindow = null;
    private IntPtr _winEventHook = IntPtr.Zero;
    private NativeMethods.WinEventDelegate? _winEventDelegate;
    private static readonly string _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "win-space-debug.log");

    private void Log(string 消息)
    {
        try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {消息}\n"); } catch { }
    }

    /// <summary>
    /// 将窗口铺满屏幕（覆盖任务栏，保留标题栏）
    /// </summary>
    private void 设置窗口全屏(IntPtr hwnd, IntPtr hMonitor)
    {
        Log($"[设置全屏] 开始 hwnd=0x{hwnd:X}");
        
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

            // 去除可调整大小的边框(WS_THICKFRAME)，这样可以：
            // 1. 防止鼠标放在边缘变成拖动调节大小的图标
            // 2. 消除 Windows 10/11 默认的 7 像素隐形调整边框，让窗口真正贴合屏幕边缘
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            style &= ~NativeMethods.WS_THICKFRAME;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style);

            // 禁用拖动：从系统菜单中移除“移动”选项
            IntPtr hMenu = NativeMethods.GetSystemMenu(hwnd, false);
            if (hMenu != IntPtr.Zero)
            {
                NativeMethods.RemoveMenu(hMenu, NativeMethods.SC_MOVE, NativeMethods.MF_BYCOMMAND);
            }

            // 设为显示器完全一致的尺寸，Windows 通常会自动将其提升到任务栏上方
            IntPtr HWND_TOPMOST = new IntPtr(-1);
            NativeMethods.SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);
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
                Topmost = true,
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

    public HiddenWindow()
    {
        Visibility = Visibility.Hidden;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.None;
        Width = 0; Height = 0;

        _notifyIcon = new NotifyIcon();
        _notifyIcon.Icon = SystemIcons.Application;
        _notifyIcon.Text = "WinSpace";
        _notifyIcon.Visible = true;
        
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("设置", null, (s, e) => OpenSettings());
        contextMenu.Items.Add("退出", null, (s, e) => Application.Current.Shutdown());
        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void OpenSettings()
    {
        var settingsWindow = new SettingsWindow();
        if (settingsWindow.ShowDialog() == true)
        {
            RegisterAllHotkeys();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(HwndHook);

        RegisterAllHotkeys();

        // 监听窗口销毁事件
        _winEventDelegate = new NativeMethods.WinEventDelegate(OnWinEvent);
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY,
            IntPtr.Zero, _winEventDelegate, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    private void RegisterAllHotkeys()
    {
        if (_windowHandle == IntPtr.Zero) return;
        
        NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_ID);
        NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_LEFT);
        NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_RIGHT);
        NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_UP);

        var s = SettingsManager.Instance.Settings;
        NativeMethods.RegisterHotKey(_windowHandle, NativeMethods.HOTKEY_ID, s.MainHotkey.Modifiers, s.MainHotkey.Key);
        NativeMethods.RegisterHotKey(_windowHandle, NativeMethods.HOTKEY_LEFT, s.LeftHotkey.Modifiers, s.LeftHotkey.Key);
        NativeMethods.RegisterHotKey(_windowHandle, NativeMethods.HOTKEY_RIGHT, s.RightHotkey.Modifiers, s.RightHotkey.Key);
        NativeMethods.RegisterHotKey(_windowHandle, NativeMethods.HOTKEY_UP, s.UpHotkey.Modifiers, s.UpHotkey.Key);
        
        _notifyIcon.Text = $"WinSpace ({s.MainHotkey})";
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

        Log($"检测到已管理窗口被销毁: hwnd=0x{hwnd:X}");

        int 被移除索引 = 所在分组.Spaces.FindIndex(s => s.Hwnd == hwnd);
        bool 是当前活动 = 所在分组.CurrentIndex == 被移除索引;

        manager.移除失效窗口(hwnd);
        manager.SaveState();

        if (是当前活动)
        {
            Log($"被销毁的窗口是当前活动空间，已切回桌面");
            所在分组.BlackBackground?.Hide();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_winEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _source?.RemoveHook(HwndHook);
        NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_ID);
        NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_LEFT);
        NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_RIGHT);
        NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_UP);

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

                    if (!space.WasMaximized)
                    {
                        // 原本不是最大化的，恢复原大小
                        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                    }
                    
                    IntPtr HWND_NOTOPMOST = new IntPtr(-2);
                    int width = space.OriginalRect.right - space.OriginalRect.left;
                    int height = space.OriginalRect.bottom - space.OriginalRect.top;
                    
                    uint flags = NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW;
                    if (width <= 0 || height <= 0)
                    {
                        flags |= 0x0001 | 0x0002;
                    }
                    
                    NativeMethods.SetWindowPos(hwnd, HWND_NOTOPMOST, space.OriginalRect.left, space.OriginalRect.top, width, height, flags);
                }
            }
        }

        SpaceManager.Instance.ClearState();

        base.OnClosed(e);
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

    private BitmapSource 截取显示器画面(IntPtr hMonitor)
    {
        NativeMethods.MONITORINFO mi = new NativeMethods.MONITORINFO();
        mi.cbSize = Marshal.SizeOf(mi);
        NativeMethods.GetMonitorInfo(hMonitor, ref mi);

        int x = mi.rcMonitor.left;
        int y = mi.rcMonitor.top;
        int w = mi.rcMonitor.right - mi.rcMonitor.left;
        int h = mi.rcMonitor.bottom - mi.rcMonitor.top;

        using var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
        return ConvertBitmapToBitmapSource(bmp);
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

            if (!space.WasMaximized)
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            }
            
            IntPtr HWND_NOTOPMOST = new IntPtr(-2);
            int width = space.OriginalRect.right - space.OriginalRect.left;
            int height = space.OriginalRect.bottom - space.OriginalRect.top;
            
            uint flags = NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW;
            if (width <= 0 || height <= 0)
            {
                flags |= 0x0001 | 0x0002;
            }
            
            NativeMethods.SetWindowPos(hwnd, HWND_NOTOPMOST, space.OriginalRect.left, space.OriginalRect.top, width, height, flags); 
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
    }

    private async void MakeForegroundWindowFullscreen()
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
    }
}

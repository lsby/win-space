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

partial class HiddenWindow : Window
{
    private IntPtr _windowHandle;
    private HwndSource? _source;
    private NotifyIcon _notifyIcon;
    private MissionControlWindow? _mcWindow = null;
    private IntPtr _winEventHook = IntPtr.Zero;
    private NativeMethods.WinEventDelegate? _winEventDelegate;
    private IntPtr _fgWinEventHook = IntPtr.Zero;
    private NativeMethods.WinEventDelegate? _fgWinEventDelegate;

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
        contextMenu.Items.Add("重置", null, (s, e) => UnlockAllWindows());
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

        // 启动时执行一次重置
        UnlockAllWindows();

        // 监听窗口销毁事件
        _winEventDelegate = new NativeMethods.WinEventDelegate(OnWinEvent);
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY,
            IntPtr.Zero, _winEventDelegate, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);

        // 监听前台窗口切换事件
        _fgWinEventDelegate = new NativeMethods.WinEventDelegate(OnForegroundWindowChanged);
        _fgWinEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _fgWinEventDelegate, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
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

    protected override void OnClosed(EventArgs e)
    {
        if (_winEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }

        if (_fgWinEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_fgWinEventHook);
            _fgWinEventHook = IntPtr.Zero;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _source?.RemoveHook(HwndHook);
        NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_ID);
        NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_LEFT);
        NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_RIGHT);
        NativeMethods.UnregisterHotKey(_windowHandle, NativeMethods.HOTKEY_UP);

        UnlockAllWindows();

        base.OnClosed(e);
    }
}

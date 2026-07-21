using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

namespace win_space;

class AppSpace
{
    public IntPtr Hwnd;
    public BitmapSource? Snapshot;
    public string Title = "";
    public NativeMethods.RECT OriginalRect;
    public bool WasMaximized;
}

class 显示器空间分组
{
    public IntPtr 显示器句柄;
    public List<AppSpace> Spaces = new List<AppSpace>();
    public int CurrentIndex = -1;
    public BitmapSource? DesktopSnapshot;
    public System.Windows.Window? BlackBackground;
}

class SpaceManager
{
    public static SpaceManager Instance = new SpaceManager();

    public Dictionary<IntPtr, 显示器空间分组> 分组字典 = new Dictionary<IntPtr, 显示器空间分组>();

    public 显示器空间分组 获取或创建分组(IntPtr hMonitor)
    {
        if (!分组字典.TryGetValue(hMonitor, out var 分组))
        {
            分组 = new 显示器空间分组 { 显示器句柄 = hMonitor };
            分组字典[hMonitor] = 分组;
        }
        return 分组;
    }

    public void AddSpace(IntPtr hMonitor, IntPtr hwnd, string title, NativeMethods.RECT rect, bool wasMaximized)
    {
        var 分组 = 获取或创建分组(hMonitor);
        if (分组.Spaces.Any(s => s.Hwnd == hwnd)) return;
        分组.Spaces.Add(new AppSpace
        {
            Hwnd = hwnd,
            Title = title,
            OriginalRect = rect,
            WasMaximized = wasMaximized
        });
        分组.CurrentIndex = 分组.Spaces.Count - 1;
    }

    public 显示器空间分组? 查找窗口所在分组(IntPtr hwnd)
    {
        foreach (var 分组 in 分组字典.Values)
        {
            if (分组.Spaces.Any(s => s.Hwnd == hwnd))
                return 分组;
        }
        return null;
    }

    public List<(IntPtr 显示器, IntPtr 窗口, int 索引)> 移除失效窗口(IntPtr hwnd)
    {
        var 结果 = new List<(IntPtr, IntPtr, int)>();
        foreach (var kvp in 分组字典)
        {
            var 分组 = kvp.Value;
            for (int i = 分组.Spaces.Count - 1; i >= 0; i--)
            {
                if (分组.Spaces[i].Hwnd == hwnd)
                {
                    结果.Add((kvp.Key, hwnd, i));
                    分组.Spaces.RemoveAt(i);

                    if (分组.CurrentIndex == i)
                    {
                        分组.CurrentIndex = -1;
                    }
                    else if (分组.CurrentIndex > i)
                    {
                        分组.CurrentIndex--;
                    }
                }
            }
        }
        return 结果;
    }

    private string GetStateFilePath()
    {
        return System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "win-space-state.txt");
    }

    public void SaveState()
    {
        try
        {
            var path = GetStateFilePath();
            var lines = 分组字典.Values
                .SelectMany(g => g.Spaces.Select(s => s.Hwnd.ToString()))
                .ToArray();
            System.IO.File.WriteAllLines(path, lines);
        }
        catch { }
    }

    public void ClearState()
    {
        try
        {
            var path = GetStateFilePath();
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch { }
    }

    public void RecoverState()
    {
        try
        {
            var path = GetStateFilePath();
            if (System.IO.File.Exists(path))
            {
                var lines = System.IO.File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    if (IntPtr.TryParse(line, out IntPtr hwnd))
                    {
                        if (NativeMethods.IsWindow(hwnd))
                        {
                            RestoreWindow(hwnd);
                        }
                    }
                }
                ClearState();
            }
        }
        catch { }
    }

    private void RestoreWindow(IntPtr hwnd)
    {
        // 恢复时只是重新显示出来（退出时由 HiddenWindow 负责取消最大化等操作）
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
    }
}

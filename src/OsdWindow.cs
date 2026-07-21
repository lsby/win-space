using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace win_space;

class OsdWindow : Window
{
    private static OsdWindow? _当前实例;

    public OsdWindow(int 当前索引, List<AppSpace> 空间列表, NativeMethods.RECT 显示器矩形)
    {
        _当前实例?.Close();
        _当前实例 = this;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        // 不使用 CenterScreen，而是手动定位到指定显示器中心
        WindowStartupLocation = WindowStartupLocation.Manual;

        var 容器 = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 24, 24, 28)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(28, 18, 28, 18),
            Margin = new Thickness(32),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 0,
                Opacity = 0.6,
                Color = Colors.Black
            }
        };

        var 布局 = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // 指示点行
        var 指示行 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var 高亮色 = Color.FromRgb(96, 165, 250);
        var 暗淡色 = Color.FromArgb(80, 255, 255, 255);

        // 桌面指示器
        指示行.Children.Add(创建指示点(当前索引 == -1, 高亮色, 暗淡色));

        // 各空间指示点
        for (int i = 0; i < 空间列表.Count; i++)
        {
            指示行.Children.Add(创建指示点(i == 当前索引, 高亮色, 暗淡色));
        }

        布局.Children.Add(指示行);

        // 标题
        string 标题文本 = 当前索引 == -1 ? "桌面" : (空间列表[当前索引].Title ?? "未知");
        var 标题 = new TextBlock
        {
            Text = 标题文本,
            Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
            FontSize = 13,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 280
        };
        布局.Children.Add(标题);

        容器.Child = 布局;

        var 根容器 = new Grid { Background = Brushes.Transparent };
        根容器.Children.Add(容器);
        Content = 根容器;

        SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
        };

        // 需要在 Loaded 后才能获取实际宽高来居中定位
        Loaded += (s, e) =>
        {
            // 获取 DPI 缩放
            var presentationSource = PresentationSource.FromVisual(this);
            double dpiScaleX = presentationSource?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiScaleY = presentationSource?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            double 显示器宽 = (显示器矩形.right - 显示器矩形.left) / dpiScaleX;
            double 显示器高 = (显示器矩形.bottom - 显示器矩形.top) / dpiScaleY;
            double 显示器X = 显示器矩形.left / dpiScaleX;
            double 显示器Y = 显示器矩形.top / dpiScaleY;

            Left = 显示器X + (显示器宽 - ActualWidth) / 2;
            Top = 显示器Y + (显示器高 - ActualHeight) / 2;
        };

        // 淡入
        Opacity = 0;
        var 淡入 = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(100));
        BeginAnimation(OpacityProperty, 淡入);

        // 停留后淡出
        var 计时器 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        计时器.Tick += (s, e) =>
        {
            计时器.Stop();
            var 淡出 = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            淡出.Completed += (s2, e2) => Close();
            BeginAnimation(OpacityProperty, 淡出);
        };
        计时器.Start();
    }

    private static Border 创建指示点(bool 是当前, Color 高亮色, Color 暗淡色)
    {
        return new Border
        {
            Width = 是当前 ? 24 : 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(是当前 ? 高亮色 : 暗淡色),
            Margin = new Thickness(3, 0, 3, 0)
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_当前实例 == this) _当前实例 = null;
        base.OnClosed(e);
    }
}

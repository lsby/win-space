using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace win_space;

class MissionControlWindow : Window
{
    private Action<int> _onSelect;
    private Action<int> _onCloseSpace;
    private Action<int, int> _onReorder;
    private StackPanel _panel;
    private ScrollViewer _scrollViewer;
    private 显示器空间分组 _分组;
    private NativeMethods.RECT _显示器矩形;
    
    public MissionControlWindow(IntPtr hMonitor, 显示器空间分组 分组, NativeMethods.RECT 显示器矩形, Action<int> onSelect, Action<int> onCloseSpace, Action<int, int> onReorder)
    {
        _onSelect = onSelect;
        _onCloseSpace = onCloseSpace;
        _onReorder = onReorder;
        _分组 = 分组;
        _显示器矩形 = 显示器矩形;
        
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0));

        // 定位到指定显示器而非默认最大化
        WindowStartupLocation = WindowStartupLocation.Manual;

        string scrollBarStyle = @"
<Style TargetType='ScrollBar' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
                <Grid Height='12' Background='Transparent' Margin='0,0,0,10'>
                    <Border Background='#222222' CornerRadius='6' />
                    <Track Name='PART_Track' IsDirectionReversed='False'>
                        <Track.Thumb>
                            <Thumb>
                                <Thumb.Template>
                                    <ControlTemplate TargetType='Thumb'>
                                        <Border Background='#666666' CornerRadius='4' Margin='2' />
                                    </ControlTemplate>
                                </Thumb.Template>
                            </Thumb>
                        </Track.Thumb>
                    </Track>
                </Grid>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>";
        var style = (Style)System.Windows.Markup.XamlReader.Parse(scrollBarStyle);
        Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), style);

        double dpiScaleX = 1.0;
        double dpiScaleY = 1.0;
        if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.Monitor_DPI_Type.MDT_Effective_DPI, out uint dpiX, out uint dpiY) == 0)
        {
            dpiScaleX = dpiX / 96.0;
            dpiScaleY = dpiY / 96.0;
        }

        Left = 显示器矩形.left / dpiScaleX;
        Top = 显示器矩形.top / dpiScaleY;
        Width = (显示器矩形.right - 显示器矩形.left) / dpiScaleX;
        Height = (显示器矩形.bottom - 显示器矩形.top) / dpiScaleY;

        _panel = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            VerticalAlignment = VerticalAlignment.Center
        };

        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = _panel
        };

        _scrollViewer.PreviewMouseWheel += (s, e) =>
        {
            if (e.Delta != 0)
            {
                _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        };

        _scrollViewer.Loaded += (s, e) =>
        {
            int targetIndex = _分组.CurrentIndex + 1;
            if (targetIndex >= 0 && targetIndex < _panel.Children.Count)
            {
                var targetItem = _panel.Children[targetIndex] as FrameworkElement;
                if (targetItem != null)
                {
                    var transform = targetItem.TransformToAncestor(_scrollViewer);
                    var position = transform.Transform(new System.Windows.Point(0, 0));
                    double center = position.X + targetItem.ActualWidth / 2;
                    double offset = _scrollViewer.HorizontalOffset + center - _scrollViewer.ViewportWidth / 2;
                    _scrollViewer.ScrollToHorizontalOffset(offset);
                }
            }
        };

        Content = _scrollViewer;

        RenderSpaces();

        MouseLeftButtonDown += (s, e) => Close();
        KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
        source?.AddHook(WndProc);

        if (source != null)
        {
            NativeMethods.SetWindowPos(source.Handle, IntPtr.Zero, 
                _显示器矩形.left, _显示器矩形.top, 
                _显示器矩形.right - _显示器矩形.left, _显示器矩形.bottom - _显示器矩形.top, 
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEHWHEEL = 0x020E;
        if (msg == WM_MOUSEHWHEEL)
        {
            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.HorizontalOffset + delta);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void RenderSpaces()
    {
        _panel.Children.Clear();
        
        AddThumb(_分组.DesktopSnapshot, "Desktop", -1);
        for (int i = 0; i < _分组.Spaces.Count; i++)
        {
            AddThumb(_分组.Spaces[i].Snapshot, _分组.Spaces[i].Title, i);
        }
    }

    private void AddThumb(BitmapSource? bmp, string label, int index)
    {
        var container = new StackPanel { Margin = new Thickness(20) };
        
        var imgGrid = new Grid();
        var imgBtn = new Button 
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        imgBtn.Click += (s, e) => { Close(); _onSelect(index); };
        
        // 缩略图大小基于当前显示器尺寸和项的数量动态调整
        double 显示器宽 = _显示器矩形.right - _显示器矩形.left;
        double 显示器高 = _显示器矩形.bottom - _显示器矩形.top;
        
        int count = _分组.Spaces.Count + 1;
        double targetWidth = 显示器宽 / Math.Max(4.0, count) * 0.8;
        double targetHeight = targetWidth / (显示器宽 / 显示器高);
        
        double minHeight = 显示器高 * 0.15;
        double maxHeight = 显示器高 * 0.35;
        double height = Math.Max(minHeight, Math.Min(maxHeight, targetHeight));
        double width = height * (显示器宽 / 显示器高);

        if (bmp != null) 
        {
            imgBtn.Content = new Image { Source = bmp, Height = height, Width = width, Stretch = Stretch.Fill };
        }
        else 
        {
            imgBtn.Content = new TextBlock { Text = "No Snapshot", Foreground = Brushes.White, FontSize = 24, Margin = new Thickness(20) };
        }
        
        imgGrid.Children.Add(imgBtn);
        container.Children.Add(imgGrid);
        
        string displayLabel = label.Length > 20 ? label.Substring(0, 18) + "..." : label;
        container.Children.Add(new TextBlock 
        { 
            Text = displayLabel, 
            Foreground = Brushes.White, 
            FontSize = 18, 
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        });

        if (index != -1)
        {
            var closeBtn = new Button
            {
                Content = "✕",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Width = 30, Height = 30, FontSize = 16,
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Hidden
            };
            closeBtn.Click += (s, e) => { _onCloseSpace(index); Close(); };
            imgGrid.MouseEnter += (s, e) => closeBtn.Visibility = Visibility.Visible;
            imgGrid.MouseLeave += (s, e) => closeBtn.Visibility = Visibility.Hidden;
            imgGrid.Children.Add(closeBtn);

            var arrowPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };
            if (index > 0)
            {
                var leftBtn = new Button { Content = " ⬅️ ", Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, FontSize = 20 };
                leftBtn.Click += (s, e) => { _onReorder(index, index - 1); RenderSpaces(); };
                arrowPanel.Children.Add(leftBtn);
            }
            if (index < _分组.Spaces.Count - 1)
            {
                var rightBtn = new Button { Content = " ➡️ ", Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, FontSize = 20 };
                rightBtn.Click += (s, e) => { _onReorder(index, index + 1); RenderSpaces(); };
                arrowPanel.Children.Add(rightBtn);
            }
            container.Children.Add(arrowPanel);
        }

        _panel.Children.Add(container);
    }
}

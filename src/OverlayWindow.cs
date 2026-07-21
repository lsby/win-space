using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;

namespace win_space;

class OverlayWindow : Window
{
    private Canvas _canvas;
    private Image _imgCurrent;
    private Image _imgTarget;
    private int _direction;
    private Action _onComplete;

    public OverlayWindow(BitmapSource current, BitmapSource target, int direction, Action onComplete)
    {
        _direction = direction;
        _onComplete = onComplete;
        
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        WindowState = WindowState.Maximized;

        _canvas = new Canvas();
        Content = _canvas;
        
        _imgCurrent = new Image { Source = current, Stretch = Stretch.Fill };
        _imgTarget = new Image { Source = target, Stretch = Stretch.Fill };
        
        _canvas.Children.Add(_imgCurrent);
        _canvas.Children.Add(_imgTarget);
        
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        _imgCurrent.Width = w;
        _imgCurrent.Height = h;
        _imgTarget.Width = w;
        _imgTarget.Height = h;
        
        Canvas.SetLeft(_imgCurrent, 0);
        Canvas.SetLeft(_imgTarget, _direction > 0 ? w : -w);
        
        double targetCurrentLeft = _direction > 0 ? -w : w;
        var animCurrent = new DoubleAnimation(0, targetCurrentLeft, TimeSpan.FromMilliseconds(300));
        var animTarget = new DoubleAnimation(_direction > 0 ? w : -w, 0, TimeSpan.FromMilliseconds(300));
        
        animCurrent.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
        animTarget.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
        
        animTarget.Completed += (s2, e2) => _onComplete?.Invoke();
        
        _imgCurrent.BeginAnimation(Canvas.LeftProperty, animCurrent);
        _imgTarget.BeginAnimation(Canvas.LeftProperty, animTarget);
    }
}

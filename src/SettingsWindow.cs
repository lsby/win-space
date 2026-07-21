using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Cursors = System.Windows.Input.Cursors;

namespace win_space;

public class SettingsWindow : Window
{
    private HotkeySetting _mainHotkey;
    private HotkeySetting _leftHotkey;
    private HotkeySetting _rightHotkey;
    private HotkeySetting _upHotkey;

    public SettingsWindow()
    {
        Title = "WinSpace 设置";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30));
        Foreground = Brushes.White;

        _mainHotkey = Clone(SettingsManager.Instance.Settings.MainHotkey);
        _leftHotkey = Clone(SettingsManager.Instance.Settings.LeftHotkey);
        _rightHotkey = Clone(SettingsManager.Instance.Settings.RightHotkey);
        _upHotkey = Clone(SettingsManager.Instance.Settings.UpHotkey);

        var grid = new StackPanel { Margin = new Thickness(20) };
        
        grid.Children.Add(new TextBlock { Text = "快捷键设置", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 20) });

        grid.Children.Add(CreateHotkeyInput("全屏/恢复当前窗口:", _mainHotkey));
        grid.Children.Add(CreateHotkeyInput("向左切换空间:", _leftHotkey));
        grid.Children.Add(CreateHotkeyInput("向右切换空间:", _rightHotkey));
        grid.Children.Add(CreateHotkeyInput("控制中心:", _upHotkey));

        var saveButton = new Button
        {
            Content = "保存并应用",
            Width = 100,
            Height = 35,
            Margin = new Thickness(0, 20, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        saveButton.Click += SaveButton_Click;
        grid.Children.Add(saveButton);

        Content = grid;
    }

    private HotkeySetting Clone(HotkeySetting s) => new HotkeySetting { Modifiers = s.Modifiers, Key = s.Key };

    private UIElement CreateHotkeyInput(string label, HotkeySetting setting)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 15) };
        var textBlock = new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(textBlock);

        var textBox = new TextBox
        {
            Text = setting.ToString(),
            IsReadOnly = true,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(5),
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
            Cursor = Cursors.Arrow
        };
        textBox.GotFocus += (s, e) => textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(96, 165, 250));
        textBox.LostFocus += (s, e) => textBox.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100));

        textBox.PreviewKeyDown += (s, e) =>
        {
            e.Handled = true;
            
            // Ignore if only modifiers are pressed
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LWin || e.Key == Key.RWin ||
                e.Key == Key.System && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt))
            {
                return;
            }

            // Get modifiers
            uint mods = 0;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= NativeMethods.MOD_CONTROL;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= NativeMethods.MOD_ALT;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= 0x0004; // MOD_SHIFT
            if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) mods |= 0x0008; // MOD_WIN
            
            // Get virtual key
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

            setting.Modifiers = mods;
            setting.Key = vk;
            textBox.Text = setting.ToString();
        };

        panel.Children.Add(textBox);
        return panel;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = SettingsManager.Instance.Settings;
        settings.MainHotkey = _mainHotkey;
        settings.LeftHotkey = _leftHotkey;
        settings.RightHotkey = _rightHotkey;
        settings.UpHotkey = _upHotkey;
        
        SettingsManager.Instance.Save();
        
        DialogResult = true;
        Close();
    }
}

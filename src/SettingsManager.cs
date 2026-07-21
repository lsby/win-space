using System;
using System.IO;
using System.Text.Json;

namespace win_space;

public class HotkeySetting
{
    public uint Modifiers { get; set; }
    public uint Key { get; set; }
    
    public override string ToString()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((Modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & 0x0004) != 0) parts.Add("Shift"); // 0x0004 is MOD_SHIFT
        if ((Modifiers & 0x0008) != 0) parts.Add("Win");   // 0x0008 is MOD_WIN
        
        string keyStr = Key switch
        {
            NativeMethods.VK_LEFT => "Left",
            NativeMethods.VK_RIGHT => "Right",
            NativeMethods.VK_UP => "Up",
            NativeMethods.VK_DOWN => "Down",
            _ => ((System.Windows.Forms.Keys)Key).ToString()
        };
        parts.Add(keyStr);
        return string.Join(" + ", parts);
    }
}

public class AppSettings
{
    public HotkeySetting MainHotkey { get; set; } = new HotkeySetting { Modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Key = NativeMethods.VK_F };
    public HotkeySetting LeftHotkey { get; set; } = new HotkeySetting { Modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Key = NativeMethods.VK_LEFT };
    public HotkeySetting RightHotkey { get; set; } = new HotkeySetting { Modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Key = NativeMethods.VK_RIGHT };
    public HotkeySetting UpHotkey { get; set; } = new HotkeySetting { Modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Key = NativeMethods.VK_UP };
}

public class SettingsManager
{
    public static SettingsManager Instance { get; } = new SettingsManager();

    public AppSettings Settings { get; private set; }

    private readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "win-space-settings.json");

    private SettingsManager()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                Settings = new AppSettings();
            }
        }
        catch
        {
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch { }
    }
}

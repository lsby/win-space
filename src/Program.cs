using System;
using System.Windows;
using Application = System.Windows.Application;

namespace win_space;

class Program
{
    [STAThread]
    static void Main()
    {
        SpaceManager.Instance.RecoverState();
        var app = new Application();
        var window = new HiddenWindow();
        window.Show(); 
        app.Run();
    }
}

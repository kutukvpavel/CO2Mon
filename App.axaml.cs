using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CO2Mon.ViewModels;
using CO2Mon.Views;

using LLibrary;

namespace CO2Mon;

public partial class App : Application
{
    private static readonly L Logger = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if ((desktop.Args?.Length ?? 0) < 1)
            {
                Console.WriteLine("Argument required: port name");
            }
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(desktop.Args[0]),
            };
        }

        CO2Mon.Models.MH_Z19B.OnLog += Controller_Log;

        base.OnFrameworkInitializationCompleted();
    }

    private void Controller_Log(object? sender, string e)
    {
        string s = $"{sender}: {e}";
        Console.WriteLine(s);
        Logger.Info(s);
    }
}
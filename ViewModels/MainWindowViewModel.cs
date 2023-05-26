using System;
using System.Collections.Generic;

using ScottPlot;
using Avalonia;

using CO2Mon.Models;
using Avalonia.Threading;
using System.ComponentModel;
using ReactiveUI;

namespace CO2Mon.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public event EventHandler? OnNewDataReceived;
    public event EventHandler? OnConnected;
    public event EventHandler? OnDisconnected;

    public MainWindowViewModel(string port)
    {
        CreateController(port);
    }

    public MH_Z19B? Controller { get; private set; }
    public bool IsConnected => Controller?.IsConnected ?? false;
    public bool IsPolling { 
        get => Controller?.IsPolling ?? false;
        set {
            if (Controller == null) return;
            if (value == Controller?.IsPolling) return;
            if (value) Controller?.StartPoll();
            else Controller?.StopPoll();
            this.RaisePropertyChanged(nameof(IsPolling));
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }
    public string StatusText 
    {
        get {
            if (!IsConnected) return "Offline";
            return IsPolling ? "Online, Polling" : "Online";
        }
    }

    public void CreateController(string port)
    {
        Controller?.Dispose();
        Controller = new MH_Z19B(port);
        Controller.OnNewDataReceived += Controller_NewDataReceived;
        Controller.OnConnected += Controller_Connected;
        Controller.OnDisconnected += Controller_Disconnected;
    }
    public async void CalibrateSensor()
    {
        if (Controller != null) await Controller.ExecuteCommand((byte)MH_Z19B_Commands.CalibrateOffset);
    }

    private void Controller_NewDataReceived(object? sender, MH_Z19B_DataPoint e)
    {
        Dispatcher.UIThread.Post(() => OnNewDataReceived?.Invoke(this, new EventArgs()));
    }
    private void Controller_Connected(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            this.RaisePropertyChanged(nameof(IsConnected));
            this.RaisePropertyChanged(nameof(IsPolling));
            this.RaisePropertyChanged(nameof(StatusText));
            OnConnected?.Invoke(this, new EventArgs());
        });
    }
    private void Controller_Disconnected(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            this.RaisePropertyChanged(nameof(IsConnected));
            this.RaisePropertyChanged(nameof(IsPolling));
            this.RaisePropertyChanged(nameof(StatusText));
            OnDisconnected?.Invoke(this, new EventArgs());
        });
    }
}

using System;
using Avalonia.Controls;
using CO2Mon.ViewModels;
using ScottPlot;

namespace CO2Mon.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        this.Opened += MainWindow_Opened;
    }
    
    MainWindowViewModel? viewModel;

    void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (DataContext is not ViewModels.MainWindowViewModel vm) throw new InvalidOperationException("ViewModel can not be null");
        viewModel = vm;
        vm.OnNewDataReceived += ViewModel_NewDataReceived;
        vm.OnConnected += ViewModel_Connected;
        vm.OnDisconnected += ViewModel_Disconnected;
    }

    void ViewModel_NewDataReceived(object? sender, EventArgs e)
    {
        pltMain.Refresh();
    }
    void ViewModel_Connected(object? sender, EventArgs e)
    {
        if (viewModel == null || viewModel.Controller == null) throw new InvalidOperationException("Controller can not be null here");
        pltMain.Plot.Clear();
        pltMain.Plot.Add.Scatter(viewModel.Controller.PointsLimited);
        pltMain.Plot.Add.Scatter(viewModel.Controller.PointsUnlimited);
    }
    void ViewModel_Disconnected(object? sender, EventArgs e)
    {
        
    }
}
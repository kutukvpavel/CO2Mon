using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

        pltMain.Plot.Axes.DateTimeTicks(Edge.Bottom);
    }

    public async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel?.Controller == null) return;
        SaveFileDialog dlg = new SaveFileDialog();
        FileDialogFilter flt = new FileDialogFilter() { Name = "Comma-separated values" };
        flt.Extensions.Add("csv");
        dlg.Filters = new System.Collections.Generic.List<FileDialogFilter>(new FileDialogFilter[] { flt });
        var filePath = await dlg.ShowAsync(this);
        if ((filePath?.Length ?? 0) > 0)
        {
            await StorageProvider.StoreAsCsv(viewModel.Controller, filePath);
        }
    }
    public async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel?.Controller != null) await viewModel.Controller.Connect();
    }
    public void Disconnect_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel?.Controller != null) viewModel.Controller.Disconnect();
    }
    public async void Clear_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel?.Controller != null) await viewModel.Controller.ClearPoints();
    }
    public void Poll_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel?.Controller != null) 
        {
            viewModel.IsPolling = !viewModel.IsPolling;
        }
    }
    public async void Calibrate_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel?.Controller != null) await viewModel.Controller.ClearPoints();
    }

    void ViewModel_NewDataReceived(object? sender, EventArgs e)
    {
        pltMain.Plot.AutoScale();
        pltMain.Refresh();
    }
    void ViewModel_Connected(object? sender, EventArgs e)
    {
        if (viewModel == null || viewModel.Controller == null) throw new InvalidOperationException("Controller can not be null here");
        pltMain.Plot.Clear();
        var lim = pltMain.Plot.Add.Scatter(viewModel.Controller.PointsLimited);
        lim.Label = "CO2 limited, ppm";
        lim.MarkerStyle = MarkerStyle.None;
        var unlim = pltMain.Plot.Add.Scatter(viewModel.Controller.PointsUnlimited);
        unlim.Label = "CO2 unlimited";
        unlim.MarkerStyle = MarkerStyle.None;
        pltMain.Refresh();
    }
    void ViewModel_Disconnected(object? sender, EventArgs e)
    {
        
    }
}
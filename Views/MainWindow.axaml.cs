using Avalonia.Controls;
using ScottPlot;

namespace CO2Mon.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Plot p;
        p.Add.Scatter()
    }
}
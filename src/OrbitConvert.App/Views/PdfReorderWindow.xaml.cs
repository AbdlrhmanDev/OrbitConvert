using System.Windows;
using OrbitConvert.App.ViewModels;

namespace OrbitConvert.App.Views;

public partial class PdfReorderWindow : Window
{
    public PdfReorderWindow(PdfReorderViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

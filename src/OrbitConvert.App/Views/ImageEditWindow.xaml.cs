using System.Windows;
using OrbitConvert.App.ViewModels;

namespace OrbitConvert.App.Views;

public partial class ImageEditWindow : Window
{
    public ImageEditWindow(ImageEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

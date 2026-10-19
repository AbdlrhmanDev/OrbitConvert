using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OrbitConvert.App.ViewModels;

namespace OrbitConvert.App.Views;

public partial class BackgroundEditorWindow : Window
{
    public BackgroundEditorWindow(BackgroundEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                viewModel.CancelCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { TemplatedParent: Button } && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OrbitConvert.App.ViewModels;

namespace OrbitConvert.App.Views;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    private HomeViewModel? ViewModel => DataContext as HomeViewModel;

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            DashedBorder.Stroke = (System.Windows.Media.Brush)FindResource("Brush.Accent");
            DropArea.Background = (System.Windows.Media.Brush)FindResource("Brush.DropHighlight");
            e.Handled = true; // the drop area has its own highlight; skip the window overlay
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e) => ResetHighlight();

    private void OnDrop(object sender, DragEventArgs e)
    {
        ResetHighlight();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            ViewModel?.AcceptDrop(files);
        }
        e.Handled = true;
    }

    private void OnDropAreaClick(object sender, MouseButtonEventArgs e)
    {
        // The inner "Select files" button handles its own click.
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is Button)
            {
                return;
            }
            if (ReferenceEquals(node, DropArea))
            {
                break;
            }
        }
        ViewModel?.SelectFilesCommand.Execute(null);
    }

    private void ResetHighlight()
    {
        DashedBorder.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.BorderStrong");
        DropArea.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
    }
}

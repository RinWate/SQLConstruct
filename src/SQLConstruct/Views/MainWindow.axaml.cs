using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SQLConstruct.ViewModels;

namespace SQLConstruct.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnSchemaTreeDoubleTapped(object? sender, RoutedEventArgs e)
    {
        switch ((e.Source as Visual)?.DataContext)
        {
            case TableNodeViewModel table:
                table.AddCommand.Execute(null);
                e.Handled = true;
                break;
            case ColumnNodeViewModel column:
                column.AddCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}

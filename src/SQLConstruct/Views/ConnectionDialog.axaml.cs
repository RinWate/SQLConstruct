using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SQLConstruct.ViewModels;

namespace SQLConstruct.Views;

public partial class ConnectionDialog : Window
{
    public ConnectionDialog()
    {
        InitializeComponent();
    }

    private async void OnBrowseSqlite(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите файл базы SQLite",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType("База SQLite")
                    {
                        Patterns = new List<string> { "*.db", "*.sqlite", "*.sqlite3", "*.db3", "*.*" }
                    }
                }
            });
            if (DataContext is ConnectionDialogViewModel vm && files.Count > 0)
                vm.SqlitePath = files[0].TryGetLocalPath() ?? "";
        }
        catch
        {
            // диалог файлового пикера недоступен — пользователь может ввести путь вручную
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConnectionDialogViewModel vm && vm.Apply())
            Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}

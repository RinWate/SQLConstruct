using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SQLConstruct.ViewModels;
using SQLConstruct.Views;

namespace SQLConstruct.Services;

public interface IDialogService
{
    Task<ConnectionSettings?> ShowConnectionDialogAsync();
    Task ShowErrorAsync(string title, string message);
    Task<string?> PickOpenFileAsync(string title, string typeName, params string[] patterns);
    Task<string?> PickSaveFileAsync(string title, string typeName, string suggestedName, params string[] patterns);
    void CloseMainWindow();
}

public sealed class DialogService : IDialogService
{
    public Window? Main { get; set; }

    public async Task<ConnectionSettings?> ShowConnectionDialogAsync()
    {
        if (Main is null)
            return null;
        var vm = new ConnectionDialogViewModel();
        var dialog = new ConnectionDialog { DataContext = vm };
        await dialog.ShowDialog(Main);
        return vm.Result;
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        if (Main is null)
            return;
        var dialog = new MessageDialog { Title = title };
        dialog.SetMessage(message);
        await dialog.ShowDialog(Main);
    }

    public async Task<string?> PickOpenFileAsync(string title, string typeName, params string[] patterns)
    {
        if (Main is null)
            return null;
        var fileType = new FilePickerFileType(typeName) { Patterns = patterns.ToList() };
        var files = await Main.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType> { fileType }
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string typeName, string suggestedName, params string[] patterns)
    {
        if (Main is null)
            return null;
        var fileType = new FilePickerFileType(typeName) { Patterns = patterns.ToList() };
        var file = await Main.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = new List<FilePickerFileType> { fileType }
        });
        return file?.TryGetLocalPath();
    }

    public void CloseMainWindow() => Main?.Close();
}

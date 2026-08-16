using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SQLConstruct.Views;

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public void SetMessage(string message) => MessageText.Text = message;

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}

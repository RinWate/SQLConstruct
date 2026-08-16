using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SQLConstruct.ViewModels;

namespace SQLConstruct.Views;

public partial class QueryBuilderControl : UserControl
{
    private ResultsViewModel? _subscribedResults;

    public QueryBuilderControl()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SubscribeResults();
        Loaded += (_, _) => SubscribeResults();
    }

    private QueryBuilderViewModel? Vm => DataContext as QueryBuilderViewModel;

    private void SubscribeResults()
    {
        if (_subscribedResults is not null)
        {
            _subscribedResults.PropertyChanged -= OnResultsChanged;
            _subscribedResults = null;
        }
        _subscribedResults = Vm?.Results;
        if (_subscribedResults is not null)
            _subscribedResults.PropertyChanged += OnResultsChanged;
        RebuildResultColumns();
    }

    private void OnResultsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsViewModel.Columns))
            RebuildResultColumns();
    }

    private void RebuildResultColumns()
    {
        var grid = ResultsGrid;
        var columns = Vm?.Results.Columns ?? Array.Empty<string>();
        grid.Columns.Clear();
        for (var i = 0; i < columns.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i],
                Binding = new Binding($"[{i}]")
            });
        }
    }

    private async void OnCopySql(object? sender, RoutedEventArgs e)
    {
        try
        {
            var sql = Vm?.SqlText;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null || string.IsNullOrEmpty(sql))
                return;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(sql));
            await clipboard.SetDataAsync(transfer);
        }
        catch
        {
            // буфер обмена недоступен (например, заблокирован другим процессом) — игнорируем
        }
    }

    private void OnAvailableFieldDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if ((e.Source as Visual)?.DataContext is FieldRefViewModel field && Vm is not null)
        {
            Vm.AddField(field);
            e.Handled = true;
        }
    }
}

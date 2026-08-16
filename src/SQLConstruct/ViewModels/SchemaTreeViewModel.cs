using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLConstruct.Models;

namespace SQLConstruct.ViewModels;

/// <summary>Приёмник событий дерева схемы: добавление таблиц/полей в текущий запрос.</summary>
public interface IQuerySink
{
    void AddTableToQuery(TableSchema table);
    void AddFieldToQuery(TableSchema table, ColumnSchema column);
}

/// <summary>Общий интерфейс узла дерева схемы — позволяет использовать один TreeDataTemplate на обоих уровнях.</summary>
public interface ISchemaNode
{
    string Name { get; }
    string Hint { get; }
    IReadOnlyList<ISchemaNode> Children { get; }
}

public partial class SchemaTreeViewModel : ObservableObject
{
    private readonly List<TableNodeViewModel> _all = new();
    private readonly IQuerySink _sink;

    public SchemaTreeViewModel(DatabaseSchema schema, IQuerySink sink)
    {
        _sink = sink;
        foreach (var t in schema.Tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            _all.Add(new TableNodeViewModel(t, sink));
        ApplyFilter();
    }

    public ObservableCollection<TableNodeViewModel> Tables { get; } = new();

    [ObservableProperty] private string? _filter;

    partial void OnFilterChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        var f = (Filter ?? "").Trim();
        Tables.Clear();
        foreach (var t in _all)
        {
            if (f.Length == 0 || t.Name.Contains(f, StringComparison.OrdinalIgnoreCase))
                Tables.Add(t);
        }
    }
}

public partial class TableNodeViewModel : ObservableObject, ISchemaNode
{
    private readonly IQuerySink _sink;

    public TableNodeViewModel(TableSchema model, IQuerySink sink)
    {
        _sink = sink;
        Model = model;
        Columns = model.Columns
            .Select(c => new ColumnNodeViewModel(model, c, sink))
            .ToArray();
    }

    public TableSchema Model { get; }

    public string Name => Model.Name;

    public override string ToString() => Name;

    public string Hint => Model.IsTemp
        ? $"временная таблица · {Model.Columns.Count} полей"
        : $"{(Model.IsView ? "представление" : "таблица")} · {Model.Columns.Count} полей";

    public IReadOnlyList<ColumnNodeViewModel> Columns { get; }

    public IReadOnlyList<ISchemaNode> Children => Columns;

    [RelayCommand]
    private void Add() => _sink.AddTableToQuery(Model);
}

public partial class ColumnNodeViewModel : ObservableObject, ISchemaNode
{
    private readonly TableSchema _table;
    private readonly IQuerySink _sink;

    public ColumnNodeViewModel(TableSchema table, ColumnSchema model, IQuerySink sink)
    {
        _table = table;
        _sink = sink;
        Model = model;
    }

    public ColumnSchema Model { get; }

    public string Name => Model.Name;

    public override string ToString() => Name;

    public string Hint => TypeHint;

    public IReadOnlyList<ISchemaNode> Children => Array.Empty<ISchemaNode>();

    public string TypeHint
    {
        get
        {
            var parts = new List<string>();
            if (Model.DbType.Length > 0)
                parts.Add(Model.DbType);
            if (Model.IsPrimaryKey)
                parts.Add("PK");
            if (!Model.IsNullable)
                parts.Add("NOT NULL");
            return string.Join(" · ", parts);
        }
    }

    [RelayCommand]
    private void Add() => _sink.AddFieldToQuery(_table, Model);
}

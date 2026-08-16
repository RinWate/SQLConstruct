using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLConstruct.Models;

namespace SQLConstruct.ViewModels;

/// <summary>Ссылка на поле таблицы из запроса (для списков выбора в условиях и сортировке).</summary>
public sealed class FieldRefViewModel
{
    public FieldRefViewModel(string tableAlias, string tableName, string column,
        AggregateFunction aggregate, string columnType)
    {
        TableAlias = tableAlias;
        TableName = tableName;
        Column = column;
        Aggregate = aggregate;
        ColumnType = columnType;
    }

    public string TableAlias { get; }
    public string TableName { get; }
    public string Column { get; }
    public AggregateFunction Aggregate { get; }
    public string ColumnType { get; }

    public string Display => Aggregate == AggregateFunction.None
        ? $"{TableName}.{Column}"
        : $"{Titles.Short(Aggregate)}({TableName}.{Column})";
}

public partial class QueryTableItemViewModel : ObservableObject
{
    private readonly QueryBuilderViewModel _owner;

    public QueryTableItemViewModel(QueryBuilderViewModel owner, QueryTable model)
    {
        _owner = owner;
        Model = model;
    }

    public QueryTable Model { get; }

    public string Alias => Model.Alias;

    public string Display => TableSchema.IsDefaultSchema(Model.TableSchema)
        ? Model.TableName
        : $"{Model.TableSchema}.{Model.TableName}";

    public string ListDisplay => $"{Alias} — {Display}";

    [RelayCommand]
    private void Remove() => _owner.RemoveTable(this);
}

public partial class FieldItemViewModel : ObservableObject
{
    private readonly QueryBuilderViewModel _owner;

    public FieldItemViewModel(QueryBuilderViewModel owner, QueryField model)
    {
        _owner = owner;
        Model = model;
    }

    public QueryField Model { get; }

    public string Display => Model.Aggregate == AggregateFunction.None
        ? $"{Model.TableName}.{Model.Column}"
        : $"{Titles.Short(Model.Aggregate)}({Model.TableName}.{Model.Column})";

    public IReadOnlyList<string> AggregateTitles => Titles.Aggregates;

    public string? OutputAlias
    {
        get => Model.OutputAlias;
        set
        {
            if (value == Model.OutputAlias) return;
            Model.OutputAlias = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(OutputAlias)));
        }
    }

    public int AggregateIndex
    {
        get => (int)Model.Aggregate;
        set
        {
            if ((int)Model.Aggregate == value) return;
            Model.Aggregate = (AggregateFunction)value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(AggregateIndex)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Display)));
            _owner.RefreshDerived();
        }
    }

    public bool GroupBy
    {
        get => Model.GroupBy;
        set
        {
            if (Model.GroupBy == value) return;
            Model.GroupBy = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(GroupBy)));
        }
    }

    [RelayCommand]
    private void Remove() => _owner.SelectedFields.Remove(this);
}

public partial class JoinItemViewModel : ObservableObject
{
    private readonly QueryBuilderViewModel _owner;

    public JoinItemViewModel(QueryBuilderViewModel owner, QueryJoin model)
    {
        _owner = owner;
        Model = model;
    }

    public QueryJoin Model { get; }

    public IReadOnlyList<string> TableOptions => _owner.TableAliases;
    public IReadOnlyList<string> JoinTitles => Titles.Joins;

    public IReadOnlyList<string> LeftColumns => _owner.ColumnsOf(Model.SourceAlias);
    public IReadOnlyList<string> RightColumns => _owner.ColumnsOf(Model.TargetAlias);

    public string? LeftAlias
    {
        get => Model.SourceAlias;
        set
        {
            if (value == Model.SourceAlias) return;
            Model.SourceAlias = value ?? "";
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(LeftAlias)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(LeftColumns)));
        }
    }

    public string? LeftColumn
    {
        get => Model.SourceColumn;
        set
        {
            if (value == Model.SourceColumn) return;
            Model.SourceColumn = value ?? "";
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(LeftColumn)));
        }
    }

    public int TypeIndex
    {
        get => (int)Model.Type;
        set
        {
            if ((int)Model.Type == value) return;
            Model.Type = (JoinType)value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(TypeIndex)));
        }
    }

    public string? RightAlias
    {
        get => Model.TargetAlias;
        set
        {
            if (value == Model.TargetAlias) return;
            Model.TargetAlias = value ?? "";
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(RightAlias)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(RightColumns)));
        }
    }

    public string? RightColumn
    {
        get => Model.TargetColumn;
        set
        {
            if (value == Model.TargetColumn) return;
            Model.TargetColumn = value ?? "";
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(RightColumn)));
        }
    }

    public void NotifyTablesChanged()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(TableOptions)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(LeftColumns)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(RightColumns)));
    }

    [RelayCommand]
    private void Remove() => _owner.Joins.Remove(this);
}

public partial class OrderItemViewModel : ObservableObject
{
    private readonly QueryBuilderViewModel _owner;

    public OrderItemViewModel(QueryBuilderViewModel owner, QueryOrder model)
    {
        _owner = owner;
        Model = model;
    }

    public QueryOrder Model { get; }

    public IReadOnlyList<FieldRefViewModel> Fields => _owner.OrderFields;
    public IReadOnlyList<string> DirectionTitles => Titles.Directions;

    public FieldRefViewModel? Field
    {
        get => _owner.OrderFields.FirstOrDefault(Matches);
        set
        {
            if (value is null) return; // защита от сброса при обновлении списка полей
            if (Matches(value)) return;
            Model.TableAlias = value.TableAlias;
            Model.TableName = value.TableName;
            Model.Column = value.Column;
            Model.Aggregate = value.Aggregate;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Field)));
        }
    }

    public int DirIndex
    {
        get => (int)Model.Dir;
        set
        {
            if ((int)Model.Dir == value) return;
            Model.Dir = (SortDirection)value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(DirIndex)));
        }
    }

    private bool Matches(FieldRefViewModel f) =>
        string.Equals(f.TableAlias, Model.TableAlias, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(f.Column, Model.Column, StringComparison.OrdinalIgnoreCase) &&
        f.Aggregate == Model.Aggregate;

    public void NotifyFieldsChanged()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Fields)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Field)));
    }

    [RelayCommand]
    private void Remove() => _owner.Orders.Remove(this);
}

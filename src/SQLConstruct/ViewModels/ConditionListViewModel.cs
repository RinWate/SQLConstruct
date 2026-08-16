using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLConstruct.Models;

namespace SQLConstruct.ViewModels;

/// <summary>Список условий (отборы WHERE или условия итогов HAVING) — редактор «как в 1С».</summary>
public partial class ConditionListViewModel : ObservableObject
{
    private readonly QueryBuilderViewModel _owner;
    private IReadOnlyList<FieldRefViewModel> _fields = Array.Empty<FieldRefViewModel>();

    public ConditionListViewModel(QueryBuilderViewModel owner)
    {
        _owner = owner;
        Items.CollectionChanged += OnItemsChanged;
    }

    public ObservableCollection<ConditionItemViewModel> Items { get; } = new();

    public ConditionGroup Group { get; } = new();

    public IReadOnlyList<FieldRefViewModel> Fields => _fields;

    public IReadOnlyList<string> LogicTitles => Titles.Logic;

    public int LogicIndex
    {
        get => (int)Group.Logic;
        set
        {
            if ((int)Group.Logic == value) return;
            Group.Logic = (ConditionLogic)value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(LogicIndex)));
        }
    }

    public bool HasItems => Items.Count > 0;

    public ConditionGroup BuildGroup()
    {
        Group.Items = Items.Select(i => i.Model).ToList();
        return Group;
    }

    public void SetFields(IReadOnlyList<FieldRefViewModel> fields)
    {
        _fields = fields;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Fields)));
        foreach (var item in Items)
            item.NotifyFieldsChanged();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (INotifyPropertyChanged old in e.OldItems)
                old.PropertyChanged -= OnItemMutated;
        if (e.NewItems != null)
            foreach (INotifyPropertyChanged @new in e.NewItems)
                @new.PropertyChanged += OnItemMutated;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasItems)));
    }

    private void OnItemMutated(object? sender, PropertyChangedEventArgs e) => _owner.NotifyChanged();

    internal void RemoveItem(ConditionItemViewModel item) => Items.Remove(item);

    [RelayCommand]
    private void Add() => Items.Add(new ConditionItemViewModel(this, new QueryCondition()));
}

public partial class ConditionItemViewModel : ObservableObject
{
    private readonly ConditionListViewModel _owner;

    public ConditionItemViewModel(ConditionListViewModel owner, QueryCondition model)
    {
        _owner = owner;
        Model = model;
    }

    public QueryCondition Model { get; }

    public IReadOnlyList<FieldRefViewModel> Fields => _owner.Fields;
    public IReadOnlyList<string> OperatorTitles => Titles.Operators;

    public FieldRefViewModel? Field
    {
        get => _owner.Fields.FirstOrDefault(Matches);
        set
        {
            if (value is null) return; // защита от сброса при обновлении списка полей
            if (Matches(value)) return;
            Model.TableAlias = value.TableAlias;
            Model.TableName = value.TableName;
            Model.Column = value.Column;
            Model.ColumnType = value.ColumnType;
            Model.Aggregate = value.Aggregate;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Field)));
        }
    }

    public int OpIndex
    {
        get => (int)Model.Op;
        set
        {
            if ((int)Model.Op == value) return;
            Model.Op = (ComparisonOperator)value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(OpIndex)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowValue)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowValue2)));
        }
    }

    public bool ShowValue => Model.Op is not (ComparisonOperator.IsNull or ComparisonOperator.IsNotNull);

    public bool ShowValue2 => Model.Op is ComparisonOperator.Between;

    public string? Value
    {
        get => Model.Value;
        set
        {
            if (value == Model.Value) return;
            Model.Value = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public string? Value2
    {
        get => Model.Value2;
        set
        {
            if (value == Model.Value2) return;
            Model.Value2 = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Value2)));
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
    private void Remove() => _owner.RemoveItem(this);
}

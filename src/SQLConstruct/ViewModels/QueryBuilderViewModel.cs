using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLConstruct.Models;
using SQLConstruct.Services;

namespace SQLConstruct.ViewModels;

/// <summary>Основная логика конструктора запросов: таблицы, поля, связи, отборы, группировки, порядок,
/// подготовительный скрипт (пакет) с временными таблицами.</summary>
public partial class QueryBuilderViewModel : ObservableObject
{
    private readonly DatabaseSchema _schema;
    private readonly Func<string, bool, Task<QueryResult>>? _executor;
    private readonly List<TableSchema> _scriptTables = new();
    private int _aliasCounter;
    private bool _suspend;

    public QueryBuilderViewModel(DatabaseSchema schema, Func<string, bool, Task<QueryResult>>? executor)
    {
        _schema = schema;
        _executor = executor;

        WhereList = new ConditionListViewModel(this);
        HavingList = new ConditionListViewModel(this);
        WhereList.PropertyChanged += OnNestedMutated;
        HavingList.PropertyChanged += OnNestedMutated;

        Attach(Tables, RefreshDerived);
        Attach(SelectedFields, RefreshDerived);
        Attach(Joins, null);
        Attach(Orders, null);

        Rebuild();
    }

    public ObservableCollection<QueryTableItemViewModel> Tables { get; } = new();
    public ObservableCollection<FieldItemViewModel> SelectedFields { get; } = new();
    public ObservableCollection<JoinItemViewModel> Joins { get; } = new();
    public ObservableCollection<OrderItemViewModel> Orders { get; } = new();
    public ConditionListViewModel WhereList { get; }
    public ConditionListViewModel HavingList { get; }
    public ResultsViewModel Results { get; } = new();

    [ObservableProperty] private QueryTableItemViewModel? _selectedTable;
    [ObservableProperty] private FieldRefViewModel? _selectedAvailableField;
    [ObservableProperty] private bool _useGrouping;
    [ObservableProperty] private bool _distinct;
    [ObservableProperty] private string _limitText = "500";
    [ObservableProperty] private string _sqlText = "";
    [ObservableProperty] private string _executeStatus = "";

    /// <summary>Подготовительный скрипт: выполняется перед запросом одним пакетом.</summary>
    [ObservableProperty] private string _script = "";

    partial void OnScriptChanged(string value) => Rebuild();

    /// <summary>Схема изменилась (в скрипте появились/исчезли временные таблицы) — дерево нужно перестроить.</summary>
    public event Action? SchemaTablesChanged;

    public IReadOnlyList<FieldRefViewModel> AvailableFields { get; private set; } = Array.Empty<FieldRefViewModel>();
    public IReadOnlyList<FieldRefViewModel> OrderFields { get; private set; } = Array.Empty<FieldRefViewModel>();
    public IReadOnlyList<string> TableAliases => Tables.Select(t => t.Alias).ToList();

    public bool HasSelectedFields => SelectedFields.Count > 0;
    public bool HasJoins => Joins.Count > 0;
    public bool HasOrders => Orders.Count > 0;

    partial void OnUseGroupingChanged(bool value) => Rebuild();
    partial void OnDistinctChanged(bool value) => Rebuild();
    partial void OnLimitTextChanged(string value) => Rebuild();

    private void Attach<T>(ObservableCollection<T> collection, Action? after) where T : class, INotifyPropertyChanged
    {
        collection.CollectionChanged += (_, e) =>
        {
            if (e.OldItems != null)
                foreach (INotifyPropertyChanged old in e.OldItems)
                    old.PropertyChanged -= OnItemMutated;
            if (e.NewItems != null)
                foreach (INotifyPropertyChanged @new in e.NewItems)
                    @new.PropertyChanged += OnItemMutated;
            after?.Invoke();
            Rebuild();
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasSelectedFields)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasJoins)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasOrders)));
        };
    }

    private void OnItemMutated(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void OnNestedMutated(object? sender, PropertyChangedEventArgs e) => Rebuild();

    internal void NotifyChanged() => Rebuild();

    // ---------- работа с таблицами и полями ----------

    public void AddTable(TableSchema table)
    {
        if (Tables.Any(t => SameTable(t.Model, table)))
            return;
        var alias = NextAlias();
        var model = new QueryTable { TableSchema = table.Schema, TableName = table.Name, Alias = alias };
        Tables.Add(new QueryTableItemViewModel(this, model));
        SelectedTable = Tables.Last();
        TryAutoJoin(table, alias);
    }

    public void AddField(TableSchema table, ColumnSchema column)
    {
        var item = Tables.FirstOrDefault(t => SameTable(t.Model, table));
        if (item is null)
        {
            AddTable(table);
            item = Tables.Last();
        }
        AddField(new FieldRefViewModel(item.Alias, table.DisplayName, column.Name, AggregateFunction.None, column.DbType));
    }

    public void AddField(FieldRefViewModel fieldRef)
    {
        if (SelectedFields.Any(f =>
                string.Equals(f.Model.TableAlias, fieldRef.TableAlias, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Model.Column, fieldRef.Column, StringComparison.OrdinalIgnoreCase) &&
                f.Model.Aggregate == fieldRef.Aggregate))
            return;
        var model = new QueryField
        {
            TableAlias = fieldRef.TableAlias,
            TableName = fieldRef.TableName,
            Column = fieldRef.Column,
            Aggregate = fieldRef.Aggregate,
            ColumnType = fieldRef.ColumnType
        };
        SelectedFields.Add(new FieldItemViewModel(this, model));
    }

    public void RemoveTable(QueryTableItemViewModel item)
    {
        var alias = item.Alias;
        _suspend = true;
        foreach (var f in SelectedFields.Where(f => Eq(f.Model.TableAlias, alias)).ToList())
            SelectedFields.Remove(f);
        foreach (var j in Joins.Where(j => Eq(j.Model.TargetAlias, alias) || Eq(j.Model.SourceAlias, alias)).ToList())
            Joins.Remove(j);
        foreach (var c in WhereList.Items.Where(c => Eq(c.Model.TableAlias, alias)).ToList())
            WhereList.Items.Remove(c);
        foreach (var c in HavingList.Items.Where(c => Eq(c.Model.TableAlias, alias)).ToList())
            HavingList.Items.Remove(c);
        foreach (var o in Orders.Where(o => Eq(o.Model.TableAlias, alias)).ToList())
            Orders.Remove(o);
        Tables.Remove(item);
        _suspend = false;
        RefreshDerived();
        Rebuild();
    }

    /// <summary>При добавлении таблицы автоматически создаёт связь по внешнему ключу, если он есть.</summary>
    private void TryAutoJoin(TableSchema newTable, string newAlias)
    {
        var newKey = SchemaKey(newTable.Schema, newTable.Name);
        foreach (var existing in Tables)
        {
            if (Eq(existing.Alias, newAlias))
                continue;
            var existingTable = FindSchemaTable(existing.Model);
            if (existingTable is null)
                continue;
            var existingKey = SchemaKey(existingTable.Schema, existingTable.Name);

            var fk = _schema.Tables
                .SelectMany(t => t.ForeignKeys)
                .FirstOrDefault(f =>
                    (Eq(f.SourceTable, newKey) && Eq(f.TargetTable, existingKey)) ||
                    (Eq(f.SourceTable, existingKey) && Eq(f.TargetTable, newKey)));
            if (fk is null)
                continue;

            QueryJoin join;
            if (Eq(fk.SourceTable, newKey))
            {
                join = new QueryJoin
                {
                    SourceAlias = existing.Alias,
                    SourceColumn = fk.TargetColumn,
                    TargetAlias = newAlias,
                    TargetColumn = fk.SourceColumn
                };
            }
            else
            {
                join = new QueryJoin
                {
                    SourceAlias = existing.Alias,
                    SourceColumn = fk.SourceColumn,
                    TargetAlias = newAlias,
                    TargetColumn = fk.TargetColumn
                };
            }

            if (Joins.Any(j => Eq(j.Model.TargetAlias, newAlias)))
                break;
            Joins.Add(new JoinItemViewModel(this, join));
            break;
        }
    }

    private string NextAlias()
    {
        _aliasCounter++;
        while (Tables.Any(t => Eq(t.Alias, "t" + _aliasCounter)))
            _aliasCounter++;
        return "t" + _aliasCounter;
    }

    private static bool SameTable(QueryTable a, TableSchema b) =>
        Eq(SchemaKey(a.TableSchema, a.TableName), SchemaKey(b.Schema, b.Name));

    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static string SchemaKey(string schema, string name) =>
        TableSchema.IsDefaultSchema(schema) ? name : $"{schema}.{name}";

    private TableSchema? FindSchemaTable(QueryTable t) =>
        _schema.Tables.FirstOrDefault(x => Eq(SchemaKey(x.Schema, x.Name), SchemaKey(t.TableSchema, t.TableName)));

    public IReadOnlyList<string> ColumnsOf(string? alias)
    {
        if (string.IsNullOrEmpty(alias))
            return Array.Empty<string>();
        var item = Tables.FirstOrDefault(t => Eq(t.Alias, alias));
        if (item is null)
            return Array.Empty<string>();
        var table = FindSchemaTable(item.Model);
        return table?.Columns.Select(c => c.Name).ToArray() ?? Array.Empty<string>();
    }

    /// <summary>Пересчитывает производные списки (доступные поля, поля для условий/сортировки/итогов).</summary>
    public void RefreshDerived()
    {
        var plain = new List<FieldRefViewModel>();
        foreach (var t in Tables)
        {
            var table = FindSchemaTable(t.Model);
            if (table is null) continue;
            foreach (var c in table.Columns)
                plain.Add(new FieldRefViewModel(t.Alias, table.DisplayName, c.Name, AggregateFunction.None, c.DbType));
        }
        AvailableFields = plain;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AvailableFields)));
        WhereList.SetFields(plain);

        var orderFields = SelectedFields
            .Select(f => new FieldRefViewModel(f.Model.TableAlias, f.Model.TableName, f.Model.Column, f.Model.Aggregate, f.Model.ColumnType))
            .ToList();
        OrderFields = orderFields;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(OrderFields)));
        foreach (var o in Orders)
            o.NotifyFieldsChanged();

        HavingList.SetFields(orderFields.Where(r => r.Aggregate != AggregateFunction.None).ToList());

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(TableAliases)));
        foreach (var j in Joins)
            j.NotifyTablesChanged();
    }

    // ---------- построение SQL ----------

    private void Rebuild()
    {
        if (_suspend)
            return;
        var query = QuerySqlBuilder.Build(BuildDocument(), QuerySqlBuilder.DialectFor(_schema.Provider));
        if (string.IsNullOrWhiteSpace(Script))
        {
            SqlText = query;
            return;
        }
        var script = Script.Trim();
        if (!script.EndsWith(";"))
            script += ";";
        SqlText = script + "\n\n" + query;
    }

    public QueryDocument BuildDocument() => new()
    {
        Script = Script,
        Tables = Tables.Select(t => t.Model).ToList(),
        Joins = Joins.Select(j => j.Model).ToList(),
        Fields = SelectedFields.Select(f => f.Model).ToList(),
        Distinct = Distinct,
        UseGrouping = UseGrouping,
        Where = WhereList.BuildGroup(),
        Having = HavingList.BuildGroup(),
        Orders = Orders.Select(o => o.Model).ToList(),
        Limit = int.TryParse(LimitText?.Trim(), out var n) && n > 0 ? n : null
    };

    public void LoadDocument(QueryDocument document)
    {
        _suspend = true;
        Tables.Clear();
        SelectedFields.Clear();
        Joins.Clear();
        Orders.Clear();
        WhereList.Items.Clear();
        HavingList.Items.Clear();

        foreach (var t in document.Tables)
            Tables.Add(new QueryTableItemViewModel(this, t));
        foreach (var f in document.Fields)
            SelectedFields.Add(new FieldItemViewModel(this, f));
        foreach (var j in document.Joins)
            Joins.Add(new JoinItemViewModel(this, j));
        foreach (var o in document.Orders)
            Orders.Add(new OrderItemViewModel(this, o));
        foreach (var c in document.Where.Items)
            WhereList.Items.Add(new ConditionItemViewModel(WhereList, c));
        foreach (var c in document.Having.Items)
            HavingList.Items.Add(new ConditionItemViewModel(HavingList, c));

        WhereList.LogicIndex = (int)document.Where.Logic;
        HavingList.LogicIndex = (int)document.Having.Logic;
        Distinct = document.Distinct;
        UseGrouping = document.UseGrouping;
        LimitText = document.Limit?.ToString() ?? "";
        Script = document.Script;

        _aliasCounter = Tables
            .Select(t => t.Alias.StartsWith('t') && int.TryParse(t.Alias[1..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        _suspend = false;
        RefreshScriptTables();
    }

    // ---------- команды ----------

    [RelayCommand]
    private void AddSelectedField()
    {
        if (SelectedAvailableField is not null)
            AddField(SelectedAvailableField);
    }

    [RelayCommand]
    private void AddAllFields()
    {
        foreach (var field in AvailableFields.ToList())
            AddField(field);
    }

    [RelayCommand]
    private void RemoveSelectedTable()
    {
        if (SelectedTable is not null)
            RemoveTable(SelectedTable);
    }

    [RelayCommand]
    private void AddJoin() => Joins.Add(new JoinItemViewModel(this, new QueryJoin()));

    [RelayCommand]
    private void AddOrder() => Orders.Add(new OrderItemViewModel(this, new QueryOrder()));

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ExecuteQueryAsync()
    {
        if (_executor is null)
            return;
        var isBatch = !string.IsNullOrWhiteSpace(Script);
        if (isBatch)
            RefreshScriptTables(); // актуализируем временные таблицы перед выполнением
        var sql = SqlText;
        ExecuteStatus = "Выполнение запроса…";
        var result = await _executor(sql, isBatch);
        Results.Set(result);
        ExecuteStatus = result.Error is null
            ? $"Выполнено: {result.Rows.Count} строк за {result.ElapsedMs} мс"
            : "Ошибка выполнения — см. таблицу результата";
    }

    public bool CanExecuteQuery => _executor is not null;

    // ---------- подготовительный скрипт и временные таблицы ----------

    /// <summary>Разбирает CREATE TABLE из скрипта и добавляет временные таблицы в схему (и дерево).</summary>
    [RelayCommand]
    private void ParseScript() => RefreshScriptTables();

    private void RefreshScriptTables()
    {
        var parsed = string.IsNullOrWhiteSpace(Script)
            ? new List<TableSchema>()
            : DdlSchemaParser.Parse(Script, "Скрипт").Tables;
        foreach (var table in parsed)
            table.IsTemp = true;

        // дерево перестраиваем только если состав таблиц действительно изменился
        if (_scriptTables.Count == parsed.Count
            && _scriptTables.Zip(parsed, (a, b) => ScriptTableSignature(a) == ScriptTableSignature(b)).All(eq => eq))
            return;

        foreach (var table in _scriptTables)
            _schema.Tables.Remove(table);
        _scriptTables.Clear();
        foreach (var table in parsed)
        {
            _scriptTables.Add(table);
            _schema.Tables.Add(table);
        }

        RefreshDerived();
        Rebuild();
        SchemaTablesChanged?.Invoke();
    }

    /// <summary>Убирает временные таблицы этого запроса из общей схемы (при смене/закрытии запроса).</summary>
    public void DetachScriptTables()
    {
        if (_scriptTables.Count == 0)
            return;
        foreach (var table in _scriptTables)
            _schema.Tables.Remove(table);
        _scriptTables.Clear();
    }

    private static string ScriptTableSignature(TableSchema table) =>
        table.Name + "(" + string.Join(",", table.Columns.Select(c => c.Name + ":" + c.DbType.ToLowerInvariant())) + ")";
}

public partial class ResultsViewModel : ObservableObject
{
    [ObservableProperty] private IReadOnlyList<string> _columns = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string?[]> _rows = Array.Empty<string?[]>();

    public void Set(QueryResult result)
    {
        if (result.Error is not null)
        {
            Columns = new[] { "Ошибка" };
            Rows = new[] { new string?[] { result.Error } };
            return;
        }
        Columns = result.Columns;
        Rows = result.Rows;
    }
}

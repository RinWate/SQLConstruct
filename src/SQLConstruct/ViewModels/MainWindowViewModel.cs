using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLConstruct.Models;
using SQLConstruct.Services;

namespace SQLConstruct.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IQuerySink
{
    private static readonly JsonSerializerOptions QueryJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IDialogService _dialogs;
    private DatabaseSchema _schema = new();
    private ConnectionSettings? _connection;

    public MainWindowViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        Query = CreateQuery();
        Query.SchemaTablesChanged += OnSchemaTablesChanged;
        SchemaTree = new SchemaTreeViewModel(_schema, this);
    }

    public QueryBuilderViewModel Query { get; private set; }

    public SchemaTreeViewModel SchemaTree { get; private set; }

    [ObservableProperty] private string _status = "Нет схемы: подключитесь к базе или загрузите схему из файла";

    public bool IsConnected => _connection is not null;

    // ---------- IQuerySink ----------

    public void AddTableToQuery(TableSchema table) => Query.AddTable(table);

    public void AddFieldToQuery(TableSchema table, ColumnSchema column) => Query.AddField(table, column);

    // ---------- схема ----------

    private QueryBuilderViewModel CreateQuery()
    {
        var connection = _connection;
        return new QueryBuilderViewModel(
            _schema,
            connection is null
                ? null
                : (sql, isBatch) => DbExecutor.ExecuteAsync(connection, sql, isBatch));
    }

    private void SetQuery(QueryBuilderViewModel query)
    {
        if (Query is not null)
        {
            Query.DetachScriptTables(); // временные таблицы старого запроса не должны остаться в схеме
            Query.SchemaTablesChanged -= OnSchemaTablesChanged;
        }
        Query = query;
        Query.SchemaTablesChanged += OnSchemaTablesChanged;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Query)));
    }

    /// <summary>В скрипте появились/исчезли временные таблицы — перестраиваем дерево схемы.</summary>
    private void OnSchemaTablesChanged()
    {
        SchemaTree = new SchemaTreeViewModel(_schema, this);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SchemaTree)));
    }

    private void ApplySchema(DatabaseSchema schema, ConnectionSettings? connection)
    {
        _schema = schema;
        _connection = connection;
        SetQuery(CreateQuery());
        SchemaTree = new SchemaTreeViewModel(schema, this);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SchemaTree)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsConnected)));
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var settings = await _dialogs.ShowConnectionDialogAsync();
        if (settings is null)
            return;
        Status = "Чтение схемы базы…";
        try
        {
            var schema = await Task.Run(() => DatabaseSchemaLoader.Load(settings));
            ApplySchema(schema, settings);
            Status = $"Подключено: {settings.DisplayName} — {schema.Tables.Count} таблиц(ы)";
        }
        catch (Exception ex)
        {
            Status = "Ошибка подключения";
            await _dialogs.ShowErrorAsync("Ошибка подключения", ex.Message);
        }
    }

    [RelayCommand]
    private async Task LoadSchemaFromFileAsync()
    {
        var path = await _dialogs.PickOpenFileAsync(
            "Загрузить схему базы",
            "Схема базы (SQL, JSON)",
            "*.sql", "*.ddl", "*.txt", "*.json");
        if (path is null)
            return;

        try
        {
            var text = await File.ReadAllTextAsync(path);
            DatabaseSchema schema;
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                schema = JsonSerializer.Deserialize<DatabaseSchema>(text, JsonSchemaStore.Options)
                         ?? throw new InvalidOperationException("Не удалось прочитать JSON схемы.");
                if (string.IsNullOrEmpty(schema.Name))
                    schema.Name = Path.GetFileName(path);
            }
            else
            {
                schema = DdlSchemaParser.Parse(text, Path.GetFileName(path));
            }

            ApplySchema(schema, null);
            Status = $"Схема из файла {Path.GetFileName(path)} — {schema.Tables.Count} таблиц(ы). Выполнение запросов недоступно.";
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Ошибка загрузки схемы", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveSchemaAsync()
    {
        if (_schema.Tables.Count == 0)
        {
            await _dialogs.ShowErrorAsync("Схема пуста", "Сначала подключитесь к базе или загрузите схему из файла.");
            return;
        }
        var path = await _dialogs.PickSaveFileAsync(
            "Сохранить схему в JSON",
            "Схема базы (JSON)",
            "schema",
            "*.json");
        if (path is null)
            return;
        try
        {
            JsonSchemaStore.Save(path, _schema);
            Status = $"Схема сохранена: {path}";
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Ошибка сохранения", ex.Message);
        }
    }

    // ---------- запрос ----------

    [RelayCommand]
    private void NewQuery()
    {
        SetQuery(CreateQuery());
        Status = "Новый запрос";
    }

    [RelayCommand]
    private async Task OpenQueryAsync()
    {
        var path = await _dialogs.PickOpenFileAsync(
            "Открыть запрос",
            "Запрос SQLConstruct",
            "*.sqj", "*.json");
        if (path is null)
            return;
        try
        {
            var document = JsonSerializer.Deserialize<QueryDocument>(await File.ReadAllTextAsync(path), QueryJsonOptions)
                           ?? throw new InvalidOperationException("Файл не содержит запрос.");
            var query = CreateQuery();
            query.LoadDocument(document);
            SetQuery(query);
            Status = $"Открыт запрос: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Ошибка открытия запроса", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveQueryAsync()
    {
        var path = await _dialogs.PickSaveFileAsync(
            "Сохранить запрос",
            "Запрос SQLConstruct",
            "query",
            "*.sqj");
        if (path is null)
            return;
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(Query.BuildDocument(), QueryJsonOptions));
            Status = $"Запрос сохранён: {path}";
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Ошибка сохранения", ex.Message);
        }
    }

    [RelayCommand]
    private void Exit() => _dialogs.CloseMainWindow();
}

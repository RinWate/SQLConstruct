namespace SQLConstruct.Models;

/// <summary>Сериализуемая модель запроса (сохраняется в файл и восстанавливается из него).</summary>
public sealed class QueryDocument
{
    /// <summary>Подготовительный скрипт: выполняется перед основным запросом одним пакетом
    /// (создание временных таблиц, наполнение и т.п.). Результатом считается последний SELECT.</summary>
    public string Script { get; set; } = "";
    public List<QueryTable> Tables { get; set; } = new();
    public List<QueryJoin> Joins { get; set; } = new();
    public List<QueryField> Fields { get; set; } = new();
    public bool Distinct { get; set; }
    public bool UseGrouping { get; set; }
    public ConditionGroup Where { get; set; } = new();
    public ConditionGroup Having { get; set; } = new();
    public List<QueryOrder> Orders { get; set; } = new();
    public int? Limit { get; set; }
}

/// <summary>Таблица в запросе с псевдонимом (t1, t2, ...).</summary>
public sealed class QueryTable
{
    public string TableSchema { get; set; } = "";
    public string TableName { get; set; } = "";
    public string Alias { get; set; } = "";
}

/// <summary>Соединение: SourceAlias.SourceColumn = TargetAlias.TargetColumn, Target — присоединяемая таблица.</summary>
public sealed class QueryJoin
{
    public string SourceAlias { get; set; } = "";
    public string SourceColumn { get; set; } = "";
    public string TargetAlias { get; set; } = "";
    public string TargetColumn { get; set; } = "";
    public JoinType Type { get; set; } = JoinType.Inner;
}

/// <summary>Поле в списке выборки.</summary>
public sealed class QueryField
{
    public string TableAlias { get; set; } = "";
    public string TableName { get; set; } = "";
    public string Column { get; set; } = "";
    public string ColumnType { get; set; } = "";
    public string? OutputAlias { get; set; }
    public AggregateFunction Aggregate { get; set; } = AggregateFunction.None;
    public bool GroupBy { get; set; }
}

public sealed class ConditionGroup
{
    public ConditionLogic Logic { get; set; } = ConditionLogic.And;
    public List<QueryCondition> Items { get; set; } = new();
}

public sealed class QueryCondition
{
    public string TableAlias { get; set; } = "";
    public string TableName { get; set; } = "";
    public string Column { get; set; } = "";
    public string ColumnType { get; set; } = "";
    public AggregateFunction Aggregate { get; set; } = AggregateFunction.None;
    public ComparisonOperator Op { get; set; } = ComparisonOperator.Equal;
    public string? Value { get; set; }
    public string? Value2 { get; set; }
}

public sealed class QueryOrder
{
    public string TableAlias { get; set; } = "";
    public string TableName { get; set; } = "";
    public string Column { get; set; } = "";
    public AggregateFunction Aggregate { get; set; } = AggregateFunction.None;
    public SortDirection Dir { get; set; } = SortDirection.Asc;
}

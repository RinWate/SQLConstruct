namespace SQLConstruct.Models;

public enum DbProvider
{
    Unknown,
    Sqlite,
    Postgres,
    SqlServer
}

/// <summary>Схема базы данных: таблицы, колонки, внешние ключи.</summary>
public sealed class DatabaseSchema
{
    public string Name { get; set; } = "";
    public DbProvider Provider { get; set; } = DbProvider.Unknown;
    public List<TableSchema> Tables { get; set; } = new();
}

public sealed class TableSchema
{
    /// <summary>Схемы по умолчанию, которые не включаются в квалифицированное имя таблицы
    /// (main — SQLite, public — PostgreSQL, dbo — MS SQL Server).</summary>
    public static bool IsDefaultSchema(string? schema) =>
        string.IsNullOrWhiteSpace(schema) || schema is "main" or "public" or "dbo";

    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsView { get; set; }

    /// <summary>Временная таблица, объявленная в подготовительном скрипте запроса.</summary>
    public bool IsTemp { get; set; }

    public List<ColumnSchema> Columns { get; set; } = new();
    public List<ForeignKeySchema> ForeignKeys { get; set; } = new();

    public string DisplayName => IsDefaultSchema(Schema) ? Name : $"{Schema}.{Name}";
}

public sealed class ColumnSchema
{
    public string Name { get; set; } = "";
    public string DbType { get; set; } = "";
    public bool IsNullable { get; set; } = true;
    public bool IsPrimaryKey { get; set; }
}

/// <summary>Внешний ключ: Source.Column ссылается на Target.Column.</summary>
public sealed class ForeignKeySchema
{
    public string SourceTable { get; set; } = "";
    public string SourceColumn { get; set; } = "";
    public string TargetTable { get; set; } = "";
    public string TargetColumn { get; set; } = "";
}

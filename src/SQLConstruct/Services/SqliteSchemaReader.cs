using System.Data;
using Microsoft.Data.Sqlite;
using SQLConstruct.Models;

namespace SQLConstruct.Services;

public static class SqliteSchemaReader
{
    public static DatabaseSchema Read(ConnectionSettings settings)
    {
        using var conn = new SqliteConnection(settings.ToSqliteConnectionString());
        conn.Open();

        var schema = new DatabaseSchema
        {
            Name = Path.GetFileName(settings.SqlitePath),
            Provider = DbProvider.Sqlite
        };

        var tables = new List<(string Name, bool IsView)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT name, type
                FROM sqlite_master
                WHERE type IN ('table','view')
                  AND name NOT LIKE 'sqlite@_%' ESCAPE '@'
                ORDER BY name
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                tables.Add((rd.GetString(0), rd.GetString(1) == "view"));
        }

        foreach (var (name, isView) in tables)
        {
            var table = new TableSchema { Name = name, IsView = isView };
            ReadColumns(conn, table);
            ReadForeignKeys(conn, table);
            schema.Tables.Add(table);
        }

        ResolveForeignKeyColumns(schema);
        return schema;
    }

    /// <summary>В старых SQLite PRAGMA foreign_key_list не возвращает целевую колонку (NULL) — берём первичный ключ целевой таблицы.</summary>
    private static void ResolveForeignKeyColumns(DatabaseSchema schema)
    {
        foreach (var table in schema.Tables)
        {
            foreach (var fk in table.ForeignKeys)
            {
                if (fk.TargetColumn.Length > 0)
                    continue;
                var target = schema.Tables.FirstOrDefault(t =>
                    t.Name.Equals(fk.TargetTable, StringComparison.OrdinalIgnoreCase));
                fk.TargetColumn = target?.Columns.FirstOrDefault(c => c.IsPrimaryKey)?.Name ?? "id";
            }
        }
    }

    private static void ReadColumns(SqliteConnection conn, TableSchema table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table.Name.Replace("\"", "\"\"")}\")";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            table.Columns.Add(new ColumnSchema
            {
                Name = rd.GetString(1),
                DbType = rd.IsDBNull(2) ? "" : rd.GetString(2),
                IsNullable = !rd.GetBoolean(3),
                IsPrimaryKey = rd.GetInt64(5) > 0
            });
        }
    }

    private static void ReadForeignKeys(SqliteConnection conn, TableSchema table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list(\"{table.Name.Replace("\"", "\"\"")}\")";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.GetInt64(1) != 0)
                continue; // составные ключи: берем только первую колонку
            table.ForeignKeys.Add(new ForeignKeySchema
            {
                SourceTable = table.Name,
                SourceColumn = rd.GetString(3),
                TargetTable = rd.GetString(2),
                TargetColumn = rd.IsDBNull(4) ? "" : rd.GetString(4)
            });
        }
    }
}

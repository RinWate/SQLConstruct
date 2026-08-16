using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace SQLConstruct.Services;

public sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<string?[]> Rows,
    string? Error,
    long ElapsedMs);

/// <summary>Выполняет построенный запрос против подключённой базы и возвращает первые строки результата.</summary>
public static partial class DbExecutor
{
    [GeneratedRegex(@"\bLIMIT\b|\bTOP\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex RowLimitRegex();

    /// <summary>Выполняет SQL. isBatch — текст содержит пакет из нескольких операторов
    /// (подготовительный скрипт + запрос): пакет не оборачивается в подзапрос,
    /// а результатом считается последний вернувший строки SELECT.</summary>
    public static async Task<QueryResult> ExecuteAsync(ConnectionSettings settings, string sql, bool isBatch = false)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var effective = sql.Trim().TrimEnd(';');
            // Если в одиночном запросе нет ограничения строк — на всякий случай ограничиваем выборку.
            if (!isBatch && !RowLimitRegex().IsMatch(effective))
            {
                effective = settings.Kind == ConnectionKind.SqlServer
                    ? $"SELECT TOP (1000) *\nFROM (\n{effective}\n) AS __result"
                    : $"SELECT *\nFROM (\n{effective}\n) AS __result\nLIMIT 1000";
            }

            using DbConnection conn = settings.Kind switch
            {
                ConnectionKind.Sqlite => new SqliteConnection(settings.ToSqliteConnectionString(readOnly: false)),
                ConnectionKind.SqlServer => new SqlConnection(settings.ToSqlServerConnectionString()),
                _ => new NpgsqlConnection(settings.ToPostgresConnectionString())
            };
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = effective;
            cmd.CommandTimeout = 60;

            using var reader = await cmd.ExecuteReaderAsync();
            // Пакет из нескольких операторов: показываем последний набор строк.
            // Наборы без колонок (DDL, INSERT, счётчики строк) пропускаем.
            IReadOnlyList<string> lastColumns = Array.Empty<string>();
            IReadOnlyList<string?[]> lastRows = Array.Empty<string?[]>();
            while (true)
            {
                if (reader.FieldCount > 0)
                {
                    var columns = new List<string>(reader.FieldCount);
                    for (var i = 0; i < reader.FieldCount; i++)
                        columns.Add(reader.GetName(i));

                    var rows = new List<string?[]>();
                    while (await reader.ReadAsync())
                    {
                        var row = new string?[reader.FieldCount];
                        for (var i = 0; i < reader.FieldCount; i++)
                        {
                            var value = reader.GetValue(i);
                            row[i] = value is DBNull or null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
                        }
                        rows.Add(row);
                    }
                    lastColumns = columns;
                    lastRows = rows;
                }
                if (!await reader.NextResultAsync())
                    break;
            }

            return new QueryResult(lastColumns, lastRows, null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return new QueryResult(
                Array.Empty<string>(),
                Array.Empty<string?[]>(),
                ex.Message,
                sw.ElapsedMilliseconds);
        }
    }
}

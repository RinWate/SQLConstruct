using Microsoft.Data.SqlClient;
using SQLConstruct.Models;

namespace SQLConstruct.Services;

public static class SqlServerSchemaReader
{
    public static DatabaseSchema Read(ConnectionSettings settings)
    {
        using var conn = new SqlConnection(settings.ToSqlServerConnectionString());
        conn.Open();

        var schema = new DatabaseSchema
        {
            Name = settings.DisplayName,
            Provider = DbProvider.SqlServer
        };

        var byKey = new Dictionary<string, TableSchema>(StringComparer.OrdinalIgnoreCase);

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT table_schema, table_name, table_type
                FROM information_schema.tables
                WHERE table_type IN ('BASE TABLE','VIEW')
                  AND table_schema NOT IN ('INFORMATION_SCHEMA','guest','sys')
                  AND table_schema NOT LIKE 'db[_]%'
                ORDER BY table_schema, table_name
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var table = new TableSchema
                {
                    Schema = rd.GetString(0),
                    Name = rd.GetString(1),
                    IsView = rd.GetString(2) == "VIEW"
                };
                schema.Tables.Add(table);
                byKey[Key(table.Schema, table.Name)] = table;
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT table_schema, table_name, column_name, data_type, is_nullable
                FROM information_schema.columns
                WHERE table_schema NOT IN ('INFORMATION_SCHEMA','guest','sys')
                  AND table_schema NOT LIKE 'db[_]%'
                ORDER BY table_schema, table_name, ordinal_position
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                if (!byKey.TryGetValue(Key(rd.GetString(0), rd.GetString(1)), out var table))
                    continue;
                table.Columns.Add(new ColumnSchema
                {
                    Name = rd.GetString(2),
                    DbType = rd.GetString(3),
                    IsNullable = rd.GetString(4) == "YES"
                });
            }
        }

        var pkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT tc.table_schema, tc.table_name, kcu.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON kcu.constraint_name = tc.constraint_name
                 AND kcu.table_schema = tc.table_schema
                 AND kcu.table_name = tc.table_name
                WHERE tc.constraint_type = 'PRIMARY KEY'
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                pkColumns.Add($"{Key(rd.GetString(0), rd.GetString(1))}.{rd.GetString(2)}");
        }

        foreach (var table in schema.Tables)
            foreach (var column in table.Columns)
                column.IsPrimaryKey = pkColumns.Contains($"{Key(table.Schema, table.Name)}.{column.Name}");

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT tc.table_schema, tc.table_name, kcu.column_name,
                       ccu.table_schema, ccu.table_name, ccu.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON kcu.constraint_name = tc.constraint_name
                 AND kcu.table_schema = tc.table_schema
                 AND kcu.table_name = tc.table_name
                JOIN information_schema.constraint_column_usage ccu
                  ON ccu.constraint_name = tc.constraint_name
                 AND ccu.table_schema = tc.table_schema
                WHERE tc.constraint_type = 'FOREIGN KEY'
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                if (!byKey.TryGetValue(Key(rd.GetString(0), rd.GetString(1)), out var table))
                    continue;
                table.ForeignKeys.Add(new ForeignKeySchema
                {
                    SourceTable = Key(rd.GetString(0), rd.GetString(1)),
                    SourceColumn = rd.GetString(2),
                    TargetTable = Key(rd.GetString(3), rd.GetString(4)),
                    TargetColumn = rd.IsDBNull(5) ? "" : rd.GetString(5)
                });
            }
        }

        return schema;
    }

    private static string Key(string schema, string name) =>
        TableSchema.IsDefaultSchema(schema) ? name : $"{schema}.{name}";
}

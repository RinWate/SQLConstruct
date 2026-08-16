using SQLConstruct.Models;

namespace SQLConstruct.Services;

public static class DatabaseSchemaLoader
{
    public static DatabaseSchema Load(ConnectionSettings settings) => settings.Kind switch
    {
        ConnectionKind.Sqlite => SqliteSchemaReader.Read(settings),
        ConnectionKind.Postgres => PostgresSchemaReader.Read(settings),
        ConnectionKind.SqlServer => SqlServerSchemaReader.Read(settings),
        _ => throw new NotSupportedException("Неизвестный тип подключения: " + settings.Kind)
    };
}

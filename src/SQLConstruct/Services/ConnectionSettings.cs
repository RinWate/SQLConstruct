namespace SQLConstruct.Services;

public enum ConnectionKind
{
    Sqlite,
    Postgres,
    SqlServer
}

public sealed class ConnectionSettings
{
    public ConnectionKind Kind { get; set; }
    public string SqlitePath { get; set; } = "";
    public string Host { get; set; } = "localhost";
    public string Port { get; set; } = "5432";
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool UseWindowsAuth { get; set; }

    /// <summary>readOnly — только чтение (чтение схемы); false — выполнение (нужно для CREATE TEMP TABLE).</summary>
    public string ToSqliteConnectionString(bool readOnly = true) =>
        $"Data Source={SqlitePath};Mode={(readOnly ? "ReadOnly" : "ReadWrite")}";

    public string ToPostgresConnectionString() =>
        $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}";

    /// <summary>Server может содержать «хост», «хост,порт» или «хост\имяЭкземпляра».</summary>
    public string ToSqlServerConnectionString() => UseWindowsAuth
        ? $"Server={Host};Database={Database};Integrated Security=True;TrustServerCertificate=True"
        : $"Server={Host};Database={Database};User Id={Username};Password={Password};TrustServerCertificate=True";

    public string DisplayName => Kind switch
    {
        ConnectionKind.Sqlite => Path.GetFileName(SqlitePath),
        ConnectionKind.Postgres => $"{Host}:{Port}/{Database}",
        ConnectionKind.SqlServer => $"{Host}/{Database}",
        _ => Host
    };
}

using SQLConstruct.Models;
using SQLConstruct.Services;
using SQLConstruct.ViewModels;

var failures = 0;
void Check(bool condition, string name)
{
    Console.WriteLine((condition ? "[ OK ] " : "[FAIL] ") + name);
    if (!condition) failures++;
}

// ---------- схема ----------
var users = new TableSchema
{
    Schema = "public",
    Name = "users",
    Columns =
    {
        new ColumnSchema { Name = "id", DbType = "integer", IsPrimaryKey = true, IsNullable = false },
        new ColumnSchema { Name = "name", DbType = "text", IsNullable = false },
        new ColumnSchema { Name = "age", DbType = "integer" },
        new ColumnSchema { Name = "city_id", DbType = "integer" }
    }
};
users.ForeignKeys.Add(new ForeignKeySchema
{
    SourceTable = "users",
    SourceColumn = "city_id",
    TargetTable = "cities",
    TargetColumn = "id"
});
var cities = new TableSchema
{
    Name = "cities",
    Columns =
    {
        new ColumnSchema { Name = "id", DbType = "integer", IsPrimaryKey = true, IsNullable = false },
        new ColumnSchema { Name = "title", DbType = "text", IsNullable = false }
    }
};
var schema = new DatabaseSchema { Name = "test", Tables = { users, cities } };

// ---------- документ запроса ----------
var doc = new QueryDocument
{
    Tables =
    {
        new QueryTable { TableSchema = "public", TableName = "users", Alias = "t1" },
        new QueryTable { TableName = "cities", Alias = "t2" }
    },
    Joins =
    {
        new QueryJoin { SourceAlias = "t1", SourceColumn = "city_id", TargetAlias = "t2", TargetColumn = "id" }
    },
    Fields =
    {
        new QueryField { TableAlias = "t1", TableName = "users", Column = "name", ColumnType = "text", OutputAlias = "Имя", GroupBy = true },
        new QueryField { TableAlias = "t2", TableName = "cities", Column = "title", ColumnType = "text", OutputAlias = "Город", GroupBy = true },
        new QueryField { TableAlias = "t1", TableName = "users", Column = "id", ColumnType = "integer", OutputAlias = "Количество", Aggregate = AggregateFunction.Count }
    },
    Distinct = true,
    UseGrouping = true,
    Where = new ConditionGroup
    {
        Logic = ConditionLogic.And,
        Items =
        {
            new QueryCondition { TableAlias = "t1", Column = "age", ColumnType = "integer", Op = ComparisonOperator.GreaterOrEqual, Value = "18" },
            new QueryCondition { TableAlias = "t1", Column = "name", ColumnType = "text", Op = ComparisonOperator.Contains, Value = "ов" },
            new QueryCondition { TableAlias = "t1", Column = "city_id", ColumnType = "integer", Op = ComparisonOperator.Between, Value = "1", Value2 = "5" },
            new QueryCondition { TableAlias = "t1", Column = "name", ColumnType = "text", Op = ComparisonOperator.InList, Value = "Иван, Пётр" }
        }
    },
    Having = new ConditionGroup
    {
        Logic = ConditionLogic.And,
        Items =
        {
            new QueryCondition { TableAlias = "t1", Column = "id", ColumnType = "integer", Aggregate = AggregateFunction.Count, Op = ComparisonOperator.Greater, Value = "3" }
        }
    },
    Orders =
    {
        new QueryOrder { TableAlias = "t1", Column = "id", Aggregate = AggregateFunction.Count, Dir = SortDirection.Desc }
    },
    Limit = 100
};

var sql = QuerySqlBuilder.Build(doc);
Console.WriteLine();
Console.WriteLine(sql);
Console.WriteLine();

Check(sql.Contains("SELECT DISTINCT"), "SELECT DISTINCT");
Check(sql.Contains("COUNT("), "агрегат COUNT");
Check(sql.Contains("AS \"Количество\""), "псевдоним поля");
Check(sql.Contains("FROM \"users\" AS \"t1\""), "FROM с псевдонимом (схема public опущена)");
Check(sql.Contains("INNER JOIN \"cities\" AS \"t2\""), "INNER JOIN по внешнему ключу");
Check(sql.Contains("\"t1\".\"city_id\" = \"t2\".\"id\""), "условие соединения");
Check(sql.Contains("WHERE"), "есть WHERE");
Check(sql.Contains(">= 18"), "значение числа без кавычек");
Check(sql.Contains("LIKE '%ов%'"), "СОДЕРЖИТ -> LIKE %..%");
Check(sql.Contains("BETWEEN 1 AND 5"), "МЕЖДУ -> BETWEEN");
Check(sql.Contains("IN ('Иван', 'Пётр')"), "В СПИСКЕ -> IN");
Check(sql.Contains("GROUP BY"), "GROUP BY");
Check(sql.Contains("HAVING"), "HAVING по агрегату");
Check(sql.Contains("ORDER BY \"Количество\" DESC"), "ORDER BY по псевдониму с DESC");
Check(sql.Contains("LIMIT 100"), "LIMIT");
Check(sql.TrimEnd().EndsWith(";"), "точка с запятой в конце");

// пустой запрос
Check(QuerySqlBuilder.Build(new QueryDocument()).StartsWith("--"), "пустой запрос -> комментарий");

// экранирование строк
Check(QuerySqlBuilder.FormatValue("text", "о'кей").Contains("о''кей"), "экранирование одинарной кавычки");
Check(QuerySqlBuilder.FormatValue("", null) == "NULL", "пустое значение -> NULL");

// ---------- DDL-парсер ----------
const string ddl = """
    -- демонстрационная схема
    CREATE TABLE IF NOT EXISTS public.users (
        id SERIAL PRIMARY KEY,
        name VARCHAR(100) NOT NULL,
        balance NUMERIC(12,2) DEFAULT 0,
        city_id INTEGER REFERENCES cities(id)
    );

    CREATE TABLE cities (
        id INTEGER PRIMARY KEY,
        title TEXT NOT NULL,
        region_id INTEGER,
        CONSTRAINT fk_region FOREIGN KEY (region_id) REFERENCES regions (id)
    );

    CREATE INDEX ix_users_name ON users (name); /* индекс игнорируется */
    CREATE TABLE regions (id INTEGER PRIMARY KEY, name TEXT);
    """;

var parsed = DdlSchemaParser.Parse(ddl);
var pUsers = parsed.Tables.FirstOrDefault(t => t.Name == "users");
var pCities = parsed.Tables.FirstOrDefault(t => t.Name == "cities");

Check(parsed.Tables.Count == 3, "DDL: 3 таблицы");
Check(pUsers is not null && pUsers.Schema == "public", "DDL: схема public распознана");
Check(pUsers is not null && pUsers.Columns.Count == 4, "DDL: 4 колонки у users");
Check(pUsers?.Columns.FirstOrDefault(c => c.Name == "name")?.IsNullable == false, "DDL: NOT NULL");
Check(pUsers?.Columns.FirstOrDefault(c => c.Name == "id")?.IsPrimaryKey == true, "DDL: PRIMARY KEY колонки");
Check(pUsers?.Columns.FirstOrDefault(c => c.Name == "balance")?.DbType == "NUMERIC(12,2)", "DDL: тип с длиной");
Check(pUsers?.ForeignKeys.Count == 1 && pUsers.ForeignKeys[0].TargetTable == "cities" && pUsers.ForeignKeys[0].TargetColumn == "id",
    "DDL: встроенный REFERENCES");
Check(pCities?.ForeignKeys.Count == 1 && pCities.ForeignKeys[0].SourceColumn == "region_id" && pCities.ForeignKeys[0].TargetColumn == "id",
    "DDL: табличный CONSTRAINT FOREIGN KEY");
Check(pCities?.Columns.FirstOrDefault(c => c.Name == "id")?.IsPrimaryKey == true, "DDL: PRIMARY KEY (id) в cities");

// ---------- команда «Выбрать все поля» ----------
var qb = new QueryBuilderViewModel(schema, null);
qb.AddTable(schema.Tables[0]); // users
qb.AddTable(schema.Tables[1]); // cities
qb.AddAllFieldsCommand.Execute(null);
Check(qb.SelectedFields.Count == users.Columns.Count + cities.Columns.Count,
    "«Выбрать все поля»: добавлены все поля всех таблиц");
var countBeforeRepeat = qb.SelectedFields.Count;
qb.AddAllFieldsCommand.Execute(null);
Check(qb.SelectedFields.Count == countBeforeRepeat, "«Выбрать все поля»: повтор не дублирует поля");
Check(qb.SqlText.Contains("\"t1\".\"city_id\"") && qb.SqlText.Contains("\"t2\".\"title\""),
    "«Выбрать все поля»: SQL содержит поля обеих таблиц");

// ---------- диалект MS SQL Server (TOP вместо LIMIT) ----------
var topSql = QuerySqlBuilder.Build(doc, SqlDialect.Top);
Console.WriteLine();
Console.WriteLine(topSql);
Console.WriteLine();
Check(topSql.Contains("SELECT DISTINCT TOP (100)"), "MSSQL: SELECT DISTINCT TOP (n)");
Check(!topSql.Contains("\nLIMIT "), "MSSQL: нет LIMIT в конце");
Check(sql.Contains("\nLIMIT 100"), "SQLite/PG: LIMIT на месте (диалект по умолчанию)");
Check(QuerySqlBuilder.DialectFor(DbProvider.SqlServer) == SqlDialect.Top, "DialectFor: SqlServer -> Top");
Check(QuerySqlBuilder.DialectFor(DbProvider.Postgres) == SqlDialect.Limit, "DialectFor: Postgres -> Limit");
var dboDoc = new QueryDocument
{
    Tables = { new QueryTable { TableSchema = "dbo", TableName = "users", Alias = "t1" } },
    Fields = { new QueryField { TableAlias = "t1", TableName = "users", Column = "name" } }
};
Check(QuerySqlBuilder.Build(dboDoc, SqlDialect.Top).Contains("FROM \"users\""),
    "MSSQL: схема dbo опускается в FROM");

// ---------- чтение схемы SQLite ----------
var dbPath = Path.Combine(Path.GetTempPath(), $"sqlconstruct_audit_{Guid.NewGuid():N}.db");
try
{
    using (var setup = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
    {
        setup.Open();
        var cmd = setup.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE cities (id INTEGER PRIMARY KEY, title TEXT NOT NULL);
            CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL, city_id INTEGER REFERENCES cities(id));
            CREATE VIEW v_users AS SELECT id, name FROM users;
            """;
        cmd.ExecuteNonQuery();
    }

    var sqliteSchema = SqliteSchemaReader.Read(new ConnectionSettings
    {
        Kind = ConnectionKind.Sqlite,
        SqlitePath = dbPath
    });
    var sUsers = sqliteSchema.Tables.First(t => t.Name == "users");
    var sCities = sqliteSchema.Tables.First(t => t.Name == "cities");
    Check(sqliteSchema.Provider == DbProvider.Sqlite, "SQLite: провайдер схемы");
    Check(sqliteSchema.Tables.Count(t => !t.IsView) == 2, "SQLite: две таблицы (служебные пропущены)");
    Check(sqliteSchema.Tables.Any(t => t.IsView && t.Name == "v_users"), "SQLite: представление распознано");
    Check(sCities.Columns.First(c => c.Name == "title").IsNullable == false, "SQLite: NOT NULL");
    Check(sUsers.Columns.First(c => c.Name == "id").IsPrimaryKey, "SQLite: PRIMARY KEY");
    Check(sUsers.ForeignKeys.Count == 1
        && sUsers.ForeignKeys[0].TargetTable == "cities"
        && sUsers.ForeignKeys[0].TargetColumn == "id"
        && sUsers.ForeignKeys[0].SourceColumn == "city_id",
        "SQLite: внешний ключ распознан полностью");
}
finally
{
    // пул соединений Sqlite держит файл открытым даже после Dispose — сбрасываем перед удалением
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { File.Delete(dbPath); } catch { /* лучший усилие — файл во временной папке */ }
}

// ---------- подготовительный скрипт и временные таблицы ----------
var tempDdl = DdlSchemaParser.Parse("""
    DROP TABLE IF EXISTS #report;
    CREATE TABLE #report (id INT PRIMARY KEY, name NVARCHAR(100) NOT NULL, amount NUMERIC(10,2));
    CREATE TEMP TABLE tt_cache (key TEXT PRIMARY KEY);
    INSERT INTO #report (id, name, amount) VALUES (1, 'a', 10);
    """);
Check(tempDdl.Tables.Count == 2, "Скрипт: CREATE TABLE #имя и CREATE TEMP TABLE распознаны");
var tReport = tempDdl.Tables.FirstOrDefault(t => t.Name == "#report");
Check(tReport?.Columns.Count == 3 && tReport.Columns.First(c => c.Name == "name").IsNullable == false,
    "Скрипт: колонки временной таблицы #report");
Check(tempDdl.Tables.Any(t => t.Name == "tt_cache" && t.Columns.Count == 1),
    "Скрипт: CREATE TEMP TABLE без #");

var scriptQb = new QueryBuilderViewModel(schema, null);
scriptQb.AddTable(schema.Tables[0]);
scriptQb.Script = "CREATE TEMP TABLE t_prep (x INT);\nCREATE TABLE #tmp (id INT)";
var composed = scriptQb.SqlText;
Check(composed.StartsWith("CREATE TEMP TABLE t_prep (x INT);"), "Скрипт: пакет начинается со скрипта");
Check(composed.Contains("\nSELECT"), "Скрипт: основной запрос входит в пакет");
Check(composed.Contains("#tmp"), "Скрипт: временная таблица из скрипта не ломает текст");
scriptQb.ParseScriptCommand.Execute(null);
Check(scriptQb.BuildDocument().Script == scriptQb.Script, "Скрипт: сохраняется в документе запроса");
Check(schema.Tables.Any(t => t.IsTemp && t.Name == "#tmp" && t.Columns.Count == 1),
    "Скрипт: временные таблицы добавлены в схему");
scriptQb.Script = "";
scriptQb.ParseScriptCommand.Execute(null);
Check(!schema.Tables.Any(t => t.IsTemp), "Скрипт: очистка скрипта убирает временные таблицы");

// событие перестройки дерева — только при реальном изменении состава таблиц
var sigSchema = new DatabaseSchema { Name = "sig" };
var sigQb = new QueryBuilderViewModel(sigSchema, null);
var raised = 0;
sigQb.SchemaTablesChanged += () => raised++;
sigQb.Script = "CREATE TABLE #a (id INT, name TEXT)";
sigQb.ParseScriptCommand.Execute(null);
var raisedAfterFirst = raised;
sigQb.ParseScriptCommand.Execute(null);
Check(raisedAfterFirst == 1 && raised == 1, "Скрипт: без изменений дерево не перестраивается");
sigQb.DetachScriptTables();
Check(!sigSchema.Tables.Any(t => t.IsTemp), "Скрипт: DetachScriptTables убирает таблицы из схемы");

// ---------- выполнение пакета с временной таблицей (живой SQLite) ----------
var batchPath = Path.Combine(Path.GetTempPath(), $"sqlconstruct_batch_{Guid.NewGuid():N}.db");
try
{
    using (var setup = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={batchPath}"))
    {
        setup.Open();
        var cmd = setup.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
            INSERT INTO users (id, name) VALUES (1, 'a'), (2, 'b');
            """;
        cmd.ExecuteNonQuery();
    }

    var batchSettings = new ConnectionSettings { Kind = ConnectionKind.Sqlite, SqlitePath = batchPath };
    var batchSql = """
        CREATE TEMP TABLE tt (id INTEGER);
        INSERT INTO tt SELECT id FROM users;
        SELECT COUNT(*) AS cnt FROM tt;
        """;
    var batchResult = await DbExecutor.ExecuteAsync(batchSettings, batchSql, isBatch: true);
    Check(batchResult.Error is null, "Пакет: выполнение без ошибок" + (batchResult.Error is null ? "" : " — " + batchResult.Error));
    Check(batchResult.Columns.Count == 1 && batchResult.Columns[0] == "cnt", "Пакет: колонки последнего SELECT");
    Check(batchResult.Rows.Count == 1 && batchResult.Rows[0][0] == "2", "Пакет: строки последнего SELECT (временная таблица работает)");

    var singleResult = await DbExecutor.ExecuteAsync(batchSettings, "SELECT id, name FROM users");
    Check(singleResult.Error is null && singleResult.Rows.Count == 2 && singleResult.Columns.Count == 2,
        "Одиночный запрос без LIMIT: сработала защитная обёртка");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { File.Delete(batchPath); } catch { /* лучший усилие — файл во временной папке */ }
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ВСЕ ТЕСТЫ ПРОЙДЕНЫ" : $"ПРОВАЛЕНО ПРОВЕРОК: {failures}");
return failures == 0 ? 0 : 1;

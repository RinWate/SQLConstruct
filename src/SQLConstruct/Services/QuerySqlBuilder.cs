using System.Globalization;
using System.Text;
using SQLConstruct.Models;

namespace SQLConstruct.Services;

/// <summary>Диалект ограничения числа строк: LIMIT (SQLite, PostgreSQL) или TOP (MS SQL Server).</summary>
public enum SqlDialect
{
    Limit,
    Top
}

/// <summary>Строит текст SQL-запроса из модели QueryDocument для SQLite, PostgreSQL и MS SQL Server.</summary>
public static class QuerySqlBuilder
{
    public static string Build(QueryDocument d) => Build(d, SqlDialect.Limit);

    public static SqlDialect DialectFor(DbProvider provider) =>
        provider == DbProvider.SqlServer ? SqlDialect.Top : SqlDialect.Limit;

    public static string Build(QueryDocument d, SqlDialect dialect)
    {
        if (d.Tables.Count == 0)
            return "-- Добавьте хотя бы одну таблицу на панели «Схема базы»";

        var sb = new StringBuilder();

        // SELECT (в MSSQL ограничение строк задается TOP сразу после SELECT/DISTINCT)
        var topClause = dialect == SqlDialect.Top && d.Limit is > 0 ? $" TOP ({d.Limit.Value})" : "";
        var items = d.Fields.Count == 0
            ? new List<string> { "*" }
            : d.Fields.Select(SelectExpr).ToList();
        sb.Append(d.Distinct ? "SELECT DISTINCT" : "SELECT")
          .Append(topClause)
          .Append('\n')
          .Append("    ")
          .AppendJoin(",\n    ", items)
          .Append('\n');

        // FROM + JOIN
        var first = d.Tables[0];
        sb.Append("FROM ").Append(TableRef(first)).Append(" AS ").Append(Q(first.Alias));
        foreach (var t in d.Tables.Skip(1))
        {
            var joins = d.Joins
                .Where(j => string.Equals(j.TargetAlias, t.Alias, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (joins.Count == 0)
            {
                sb.Append("\nCROSS JOIN ").Append(TableRef(t)).Append(" AS ").Append(Q(t.Alias));
                continue;
            }

            var head = joins[0];
            var keyword = head.Type switch
            {
                JoinType.Left => "LEFT JOIN",
                JoinType.Right => "RIGHT JOIN",
                JoinType.Full => "FULL JOIN",
                JoinType.Cross => "CROSS JOIN",
                _ => "INNER JOIN"
            };
            sb.Append('\n').Append(keyword).Append(' ').Append(TableRef(t)).Append(" AS ").Append(Q(t.Alias));

            var conditions = joins
                .Where(j => j.Type != JoinType.Cross
                            && !string.IsNullOrEmpty(j.SourceAlias)
                            && !string.IsNullOrEmpty(j.SourceColumn)
                            && !string.IsNullOrEmpty(j.TargetColumn))
                .Select(j => $"{Q(j.SourceAlias)}.{Q(j.SourceColumn)} = {Q(t.Alias)}.{Q(j.TargetColumn)}")
                .ToList();
            if (conditions.Count > 0)
                sb.Append(" ON ").Append(string.Join(" AND ", conditions));
        }
        sb.Append('\n');

        // WHERE
        var where = RenderConditions(d.Where);
        if (where is not null)
            sb.Append("WHERE ").Append(where).Append('\n');

        // GROUP BY
        if (d.UseGrouping)
        {
            var groupColumns = d.Fields
                .Where(f => f.GroupBy && !string.IsNullOrEmpty(f.Column))
                .Select(f => ColRef(f.TableAlias, f.Column))
                .ToList();
            if (groupColumns.Count > 0)
                sb.Append("GROUP BY ").Append(string.Join(", ", groupColumns)).Append('\n');
        }

        // HAVING (условия итогов)
        var having = RenderConditions(d.Having);
        if (having is not null)
            sb.Append("HAVING ").Append(having).Append('\n');

        // ORDER BY
        var orderParts = new List<string>();
        foreach (var o in d.Orders)
        {
            if (string.IsNullOrEmpty(o.Column) || string.IsNullOrEmpty(o.TableAlias))
                continue;
            var field = d.Fields.FirstOrDefault(f =>
                string.Equals(f.TableAlias, o.TableAlias, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Column, o.Column, StringComparison.OrdinalIgnoreCase));
            string expr;
            if (!string.IsNullOrEmpty(field?.OutputAlias))
                expr = Q(field.OutputAlias);
            else if (field is not null && d.UseGrouping && field.Aggregate != AggregateFunction.None)
                expr = Agg(field.Aggregate, ColRef(field.TableAlias, field.Column));
            else
                expr = ColRef(o.TableAlias, o.Column);
            orderParts.Add(expr + (o.Dir == SortDirection.Desc ? " DESC" : " ASC"));
        }
        if (orderParts.Count > 0)
            sb.Append("ORDER BY ").Append(string.Join(", ", orderParts)).Append('\n');

        if (dialect == SqlDialect.Limit && d.Limit is > 0)
            sb.Append("LIMIT ").Append(d.Limit.Value).Append('\n');

        sb.Append(';');
        return sb.ToString();
    }

    private static string SelectExpr(QueryField f)
    {
        var expr = Agg(f.Aggregate, ColRef(f.TableAlias, f.Column));
        return string.IsNullOrWhiteSpace(f.OutputAlias) ? expr : $"{expr} AS {Q(f.OutputAlias)}";
    }

    private static string? RenderConditions(ConditionGroup group)
    {
        var parts = new List<string>();
        foreach (var c in group.Items)
        {
            if (string.IsNullOrEmpty(c.Column) || string.IsNullOrEmpty(c.TableAlias))
                continue;
            var left = c.Aggregate != AggregateFunction.None
                ? Agg(c.Aggregate, ColRef(c.TableAlias, c.Column))
                : ColRef(c.TableAlias, c.Column);
            parts.Add(RenderCondition(left, c));
        }
        if (parts.Count == 0)
            return null;
        return string.Join(group.Logic == ConditionLogic.Or ? "\n  OR " : "\n  AND ", parts);
    }

    private static string RenderCondition(string left, QueryCondition c)
    {
        var v1 = FormatValue(c.ColumnType, c.Value);
        switch (c.Op)
        {
            case ComparisonOperator.Equal:
                return $"{left} = {v1}";
            case ComparisonOperator.NotEqual:
                return $"{left} <> {v1}";
            case ComparisonOperator.Greater:
                return $"{left} > {v1}";
            case ComparisonOperator.GreaterOrEqual:
                return $"{left} >= {v1}";
            case ComparisonOperator.Less:
                return $"{left} < {v1}";
            case ComparisonOperator.LessOrEqual:
                return $"{left} <= {v1}";
            case ComparisonOperator.Contains:
                return $"{left} LIKE {LikeValue(c.Value, true, true)}";
            case ComparisonOperator.NotContains:
                return $"{left} NOT LIKE {LikeValue(c.Value, true, true)}";
            case ComparisonOperator.StartsWith:
                return $"{left} LIKE {LikeValue(c.Value, false, true)}";
            case ComparisonOperator.NotStartsWith:
                return $"{left} NOT LIKE {LikeValue(c.Value, false, true)}";
            case ComparisonOperator.EndsWith:
                return $"{left} LIKE {LikeValue(c.Value, true, false)}";
            case ComparisonOperator.InList:
                return $"{left} IN ({FormatList(c.ColumnType, c.Value)})";
            case ComparisonOperator.NotInList:
                return $"{left} NOT IN ({FormatList(c.ColumnType, c.Value)})";
            case ComparisonOperator.Between:
                return $"{left} BETWEEN {v1} AND {FormatValue(c.ColumnType, c.Value2)}";
            case ComparisonOperator.IsNull:
                return $"{left} IS NULL";
            case ComparisonOperator.IsNotNull:
                return $"{left} IS NOT NULL";
            default:
                return $"{left} = {v1}";
        }
    }

    private static string LikeValue(string? raw, bool percentBefore, bool percentAfter)
    {
        var v = (raw ?? "").Trim();
        var pattern = (percentBefore ? "%" : "") + v + (percentAfter ? "%" : "");
        return "'" + pattern.Replace("'", "''") + "'";
    }

    private static string FormatList(string? columnType, string? raw)
    {
        var values = (raw ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(v => FormatValue(columnType, v));
        var joined = string.Join(", ", values);
        return joined.Length == 0 ? "NULL" : joined;
    }

    /// <summary>Форматирует значение по типу колонки: числа без кавычек, остальное в одинарных кавычках.</summary>
    public static string FormatValue(string? columnType, string? raw)
    {
        var v = raw?.Trim();
        if (string.IsNullOrEmpty(v))
            return "NULL";

        var t = (columnType ?? "").ToLowerInvariant();
        var numericType = t.Contains("int") || t.Contains("numeric") || t.Contains("decimal") ||
                          t.Contains("real") || t.Contains("double") || t.Contains("float") ||
                          t.Contains("money") || t.Contains("serial");
        if (numericType && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            return v;
        if (t.Contains("bool") && bool.TryParse(v, out var b))
            return b ? "TRUE" : "FALSE";
        return "'" + v.Replace("'", "''") + "'";
    }

    private static string TableRef(QueryTable t) =>
        TableSchema.IsDefaultSchema(t.TableSchema)
            ? Q(t.TableName)
            : Q(t.TableSchema) + "." + Q(t.TableName);

    private static string Q(string? identifier) => "\"" + (identifier ?? "").Replace("\"", "\"\"") + "\"";

    private static string ColRef(string alias, string column) => Q(alias) + "." + Q(column);

    private static string Agg(AggregateFunction a, string expr) => a switch
    {
        AggregateFunction.Count => $"COUNT({expr})",
        AggregateFunction.CountDistinct => $"COUNT(DISTINCT {expr})",
        AggregateFunction.Sum => $"SUM({expr})",
        AggregateFunction.Avg => $"AVG({expr})",
        AggregateFunction.Min => $"MIN({expr})",
        AggregateFunction.Max => $"MAX({expr})",
        _ => expr
    };
}

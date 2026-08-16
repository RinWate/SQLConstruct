using System.Text;
using SQLConstruct.Models;

namespace SQLConstruct.Services;

/// <summary>Разбор схемы из DDL-скрипта (CREATE TABLE ...): понимает SQLite/PostgreSQL/MySQL-подобный синтаксис.</summary>
public static class DdlSchemaParser
{
    public static DatabaseSchema Parse(string ddl, string name = "Схема из DDL")
    {
        var schema = new DatabaseSchema { Name = name };
        var tokens = Tokenize(ddl);

        var i = 0;
        while (i < tokens.Count)
        {
            if (tokens[i].IsWord("CREATE"))
            {
                var j = i + 1;
                while (j < tokens.Count && tokens[j].IsWordIn("TEMP", "TEMPORARY", "GLOBAL", "LOCAL", "UNLOGGED"))
                    j++;
                if (j < tokens.Count && tokens[j].IsWord("TABLE"))
                {
                    j++;
                    var table = ParseTable(tokens, ref j);
                    if (table is not null)
                        schema.Tables.Add(table);
                    i = j;
                    continue;
                }
            }
            i++;
        }

        ResolveForeignKeyColumns(schema);
        return schema;
    }

    private static TableSchema? ParseTable(List<Tok> tokens, ref int i)
    {
        // IF NOT EXISTS
        if (i < tokens.Count && tokens[i].IsWord("IF"))
            i += 3;

        var (schemaName, tableName) = ReadQualifiedIdentifier(tokens, ref i);
        if (tableName.Length == 0)
            return null;

        while (i < tokens.Count && !tokens[i].IsPunct("("))
            i++; // ищем открывающую скобку
        if (i >= tokens.Count)
            return null;
        i++; // пропускаем '('

        var table = new TableSchema { Schema = schemaName, Name = tableName };

        var part = new List<Tok>();
        var depth = 0;
        while (i < tokens.Count)
        {
            var t = tokens[i];
            i++;
            if (t.IsPunct("(")) { depth++; part.Add(t); continue; }
            if (t.IsPunct(")"))
            {
                if (depth == 0) break;
                depth--; part.Add(t); continue;
            }
            if (depth == 0 && t.IsPunct(","))
            {
                ParsePart(part, table);
                part = new List<Tok>();
                continue;
            }
            part.Add(t);
        }
        ParsePart(part, table);
        // хвостовые опции (WITHOUT ROWID и т.п.) пропускаются естественным образом при поиске следующего CREATE
        return table;
    }

    private static void ParsePart(List<Tok> part, TableSchema table)
    {
        if (part.Count == 0)
            return;

        var first = part[0];
        if (first.Kind == TokKind.Word && first.IsWordIn("CONSTRAINT", "PRIMARY", "FOREIGN", "UNIQUE", "CHECK", "EXCLUDE", "LIKE"))
        {
            ParseTableConstraint(part, table);
            return;
        }

        // --- определение колонки ---
        var columnName = first.Text;
        var column = new ColumnSchema { Name = columnName };
        var k = 1;

        var type = new StringBuilder();
        while (k < part.Count && !IsColumnConstraintStart(part[k]))
        {
            if (type.Length > 0 && part[k].Kind != TokKind.Punct && part[k - 1].Text is not ("(" or ","))
                type.Append(' ');
            type.Append(part[k].Text);
            k++;
        }
        column.DbType = type.ToString().Trim();

        while (k < part.Count)
        {
            var t = part[k];
            if (t.IsWord("NOT") && k + 1 < part.Count && part[k + 1].IsWord("NULL"))
            {
                column.IsNullable = false;
                k += 2;
            }
            else if (t.IsWord("PRIMARY"))
            {
                column.IsPrimaryKey = true;
                k = SkipParentheses(part, k + 1);
            }
            else if (t.IsWord("REFERENCES"))
            {
                k++;
                var (targetSchema, targetTable) = ReadQualifiedIdentifier(part, ref k);
                var remote = ReadParenIdentifierList(part, k);
                table.ForeignKeys.Add(new ForeignKeySchema
                {
                    SourceTable = table.DisplayName,
                    SourceColumn = column.Name,
                    TargetTable = string.IsNullOrEmpty(targetSchema) ? targetTable : $"{targetSchema}.{targetTable}",
                    TargetColumn = remote.FirstOrDefault() ?? ""
                });
            }
            else if (t.IsWord("DEFAULT"))
            {
                k++;
                if (k < part.Count && part[k].IsPunct("("))
                    k = SkipParentheses(part, k);
                else if (k + 1 < part.Count && part[k].IsPunct("-"))
                    k += 2; // отрицательное число: знак и значение
                else
                    k++;
            }
            else if (t.IsWord("CHECK") || t.IsWord("AS"))
            {
                k = SkipParentheses(part, k + 1);
            }
            else if (t.IsWord("GENERATED"))
            {
                k++;
                if (k < part.Count && part[k].IsWord("ALWAYS"))
                    k++;
                if (k < part.Count && part[k].IsWord("AS"))
                    k++;
                k = SkipParentheses(part, k);
                if (k < part.Count && part[k].IsWordIn("STORED", "VIRTUAL"))
                    k++;
            }
            else
            {
                k++; // NULL, UNIQUE, AUTOINCREMENT, IDENTITY, COLLATE x, ON UPDATE ... и прочее — пропускаем
            }
        }

        table.Columns.Add(column);
    }

    private static void ParseTableConstraint(List<Tok> part, TableSchema table)
    {
        var k = 0;
        if (part[k].IsWord("CONSTRAINT"))
            k += 2; // CONSTRAINT <имя>

        if (k < part.Count && part[k].IsWord("PRIMARY"))
        {
            var pk = ReadParenIdentifierList(part, k + 1);
            foreach (var name in pk)
            {
                var column = table.Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (column is not null)
                    column.IsPrimaryKey = true;
            }
            return;
        }

        if (k < part.Count && part[k].IsWord("FOREIGN"))
        {
            var local = ReadParenIdentifierList(part, k + 1);
            var r = IndexOfWord(part, "REFERENCES", k);
            if (r < 0 || local.Count == 0)
                return;
            var idx = r + 1;
            var (targetSchema, targetTable) = ReadQualifiedIdentifier(part, ref idx);
            var remote = ReadParenIdentifierList(part, idx);
            table.ForeignKeys.Add(new ForeignKeySchema
            {
                SourceTable = table.DisplayName,
                SourceColumn = local[0],
                TargetTable = string.IsNullOrEmpty(targetSchema) ? targetTable : $"{targetSchema}.{targetTable}",
                TargetColumn = remote.FirstOrDefault() ?? ""
            });
        }
    }

    /// <summary>Если у внешнего ключа не указана целевая колонка — берём первичный ключ целевой таблицы.</summary>
    private static void ResolveForeignKeyColumns(DatabaseSchema schema)
    {
        foreach (var table in schema.Tables)
        {
            foreach (var fk in table.ForeignKeys)
            {
                if (fk.TargetColumn.Length > 0)
                    continue;
                var target = schema.Tables.FirstOrDefault(t =>
                    t.DisplayName.Equals(fk.TargetTable, StringComparison.OrdinalIgnoreCase));
                fk.TargetColumn = target?.Columns.FirstOrDefault(c => c.IsPrimaryKey)?.Name ?? "id";
            }
        }
    }

    private static bool IsColumnConstraintStart(Tok t) =>
        t.Kind == TokKind.Word &&
        t.IsWordIn("CONSTRAINT", "PRIMARY", "NOT", "NULL", "UNIQUE", "REFERENCES", "DEFAULT",
                   "CHECK", "COLLATE", "GENERATED", "AUTOINCREMENT", "IDENTITY", "AS", "ON", "COMMENT", "SPARSE");

    private static int IndexOfWord(List<Tok> part, string word, int from)
    {
        for (var i = from; i < part.Count; i++)
            if (part[i].IsWord(word))
                return i;
        return -1;
    }

    /// <summary>Возвращает позицию после закрывающей скобки; start указывает на слово перед '('.</summary>
    private static int SkipParentheses(List<Tok> part, int start)
    {
        var i = start;
        while (i < part.Count && !part[i].IsPunct("("))
            i++;
        var depth = 0;
        while (i < part.Count)
        {
            if (part[i].IsPunct("(")) depth++;
            else if (part[i].IsPunct(")"))
            {
                depth--;
                if (depth == 0) return i + 1;
            }
            i++;
        }
        return i;
    }

    private static List<string> ReadParenIdentifierList(List<Tok> part, int start)
    {
        var result = new List<string>();
        var i = start;
        while (i < part.Count && !part[i].IsPunct("("))
        {
            i++;
            if (result.Count == 0 && i - start > 2) break;
        }
        if (i >= part.Count || !part[i].IsPunct("("))
            return result;
        i++;
        while (i < part.Count && !part[i].IsPunct(")"))
        {
            if (part[i].Kind != TokKind.Punct && !part[i].IsWordIn("ASC", "DESC"))
                result.Add(part[i].Text);
            i++;
        }
        return result;
    }

    private static (string Schema, string Name) ReadQualifiedIdentifier(List<Tok> tokens, ref int i)
    {
        if (i >= tokens.Count || tokens[i].Kind == TokKind.Punct)
            return ("", "");
        var first = tokens[i].Text;
        i++;
        if (i + 1 < tokens.Count && tokens[i].IsPunct(".") && tokens[i + 1].Kind != TokKind.Punct)
        {
            var second = tokens[i + 1].Text;
            i += 2;
            return (first, second);
        }
        return ("", first);
    }

    private enum TokKind { Word, Quoted, String, Punct }

    private sealed class Tok
    {
        public Tok(string text, TokKind kind) { Text = text; Kind = kind; }
        public string Text { get; }
        public TokKind Kind { get; }
        public bool IsPunct(string p) => Kind == TokKind.Punct && Text == p;
        public bool IsWord(string w) => Kind == TokKind.Word && Text.Equals(w, StringComparison.OrdinalIgnoreCase);
        public bool IsWordIn(params string[] words)
        {
            foreach (var w in words)
                if (IsWord(w)) return true;
            return false;
        }
    }

    private static List<Tok> Tokenize(string source)
    {
        var result = new List<Tok>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < source.Length && source[i + 1] == '-')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                i = Math.Min(i + 2, source.Length);
                continue;
            }
            if (c is '"' or '`')
            {
                i++;
                var b = new StringBuilder();
                while (i < source.Length)
                {
                    if (source[i] == c)
                    {
                        if (i + 1 < source.Length && source[i + 1] == c) { b.Append(c); i += 2; continue; }
                        i++;
                        break;
                    }
                    b.Append(source[i++]);
                }
                result.Add(new Tok(b.ToString(), TokKind.Quoted));
                continue;
            }
            if (c == '[')
            {
                i++;
                var b = new StringBuilder();
                while (i < source.Length && source[i] != ']')
                    b.Append(source[i++]);
                i++;
                result.Add(new Tok(b.ToString(), TokKind.Quoted));
                continue;
            }
            if (c == '\'')
            {
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '\'')
                    {
                        if (i + 1 < source.Length && source[i + 1] == '\'') { i += 2; continue; }
                        break;
                    }
                    i++;
                }
                i++;
                result.Add(new Tok("", TokKind.String));
                continue;
            }
            if (char.IsLetter(c) || c is '_' or '#' or '$')
            {
                var b = new StringBuilder();
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '$' || source[i] == '#'))
                    b.Append(source[i++]);
                result.Add(new Tok(b.ToString(), TokKind.Word));
                continue;
            }
            if (char.IsDigit(c))
            {
                var b = new StringBuilder();
                while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.' || source[i] == 'e' || source[i] == 'E'))
                    b.Append(source[i++]);
                result.Add(new Tok(b.ToString(), TokKind.Word));
                continue;
            }
            result.Add(new Tok(c.ToString(), TokKind.Punct));
            i++;
        }
        return result;
    }
}

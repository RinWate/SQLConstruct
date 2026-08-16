namespace SQLConstruct.Models;

public enum JoinType
{
    Inner = 0,
    Left = 1,
    Right = 2,
    Full = 3,
    Cross = 4
}

public enum AggregateFunction
{
    None = 0,
    Count = 1,
    CountDistinct = 2,
    Sum = 3,
    Avg = 4,
    Min = 5,
    Max = 6
}

public enum SortDirection
{
    Asc = 0,
    Desc = 1
}

public enum ConditionLogic
{
    And = 0,
    Or = 1
}

/// <summary>Вид сравнения — как «ВидСравнения» в конструкторе запросов 1С.</summary>
public enum ComparisonOperator
{
    Equal = 0,
    NotEqual = 1,
    Greater = 2,
    GreaterOrEqual = 3,
    Less = 4,
    LessOrEqual = 5,
    Contains = 6,
    NotContains = 7,
    StartsWith = 8,
    NotStartsWith = 9,
    EndsWith = 10,
    InList = 11,
    NotInList = 12,
    Between = 13,
    IsNull = 14,
    IsNotNull = 15
}

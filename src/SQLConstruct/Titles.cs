using SQLConstruct.Models;

namespace SQLConstruct;

/// <summary>Русские подписи для элементов интерфейса. Индексы массивов строго совпадают
/// с порядком значений соответствующих перечислений.</summary>
public static class Titles
{
    public static readonly string[] Joins =
    {
        "Внутреннее (INNER)",
        "Левое (LEFT)",
        "Правое (RIGHT)",
        "Полное (FULL)",
        "Перекрестное (CROSS)"
    };

    public static readonly string[] Aggregates =
    {
        "Без функции",
        "COUNT (количество)",
        "COUNT(DISTINCT)",
        "SUM (сумма)",
        "AVG (среднее)",
        "MIN (минимум)",
        "MAX (максимум)"
    };

    public static readonly string[] Operators =
    {
        "Равно (=)",
        "Не равно (<>)",
        "Больше (>)",
        "Больше или равно (>=)",
        "Меньше (<)",
        "Меньше или равно (<=)",
        "Содержит",
        "Не содержит",
        "Начинается с",
        "Не начинается с",
        "Оканчивается на",
        "В списке (IN)",
        "Не в списке (NOT IN)",
        "Между (BETWEEN)",
        "Пусто (IS NULL)",
        "Не пусто (IS NOT NULL)"
    };

    public static readonly string[] Directions =
    {
        "По возрастанию",
        "По убыванию"
    };

    public static readonly string[] Logic =
    {
        "И (AND)",
        "ИЛИ (OR)"
    };

    public static string Short(AggregateFunction a) => a switch
    {
        AggregateFunction.Count => "COUNT",
        AggregateFunction.CountDistinct => "COUNT(DISTINCT)",
        AggregateFunction.Sum => "SUM",
        AggregateFunction.Avg => "AVG",
        AggregateFunction.Min => "MIN",
        AggregateFunction.Max => "MAX",
        _ => ""
    };
}

namespace ActivityPub.Misskey.Blazor.Components;

public sealed record CalendarWidgetSnapshot(
    int Year,
    int Month,
    int Day,
    int WeekDay,
    double DayProgress,
    double MonthProgress,
    double YearProgress)
{
    public static CalendarWidgetSnapshot From(DateTimeOffset value)
    {
        DateTimeOffset dayStart = new(value.Year, value.Month, value.Day, 0, 0, 0, value.Offset);
        DateTimeOffset monthStart = new(value.Year, value.Month, 1, 0, 0, 0, value.Offset);
        DateTimeOffset nextMonth = monthStart.AddMonths(1);
        DateTimeOffset yearStart = new(value.Year, 1, 1, 0, 0, 0, value.Offset);
        DateTimeOffset nextYear = yearStart.AddYears(1);
        return new(
            value.Year,
            value.Month,
            value.Day,
            (int)value.DayOfWeek,
            (value - dayStart).TotalMilliseconds / TimeSpan.FromDays(1).TotalMilliseconds * 100,
            (value - monthStart).TotalMilliseconds / (nextMonth - monthStart).TotalMilliseconds * 100,
            (value - yearStart).TotalMilliseconds / (nextYear - yearStart).TotalMilliseconds * 100);
    }

    public void Validate()
    {
        if (Year is < 1 or > 9_999 || Month is < 1 or > 12 ||
            Day is < 1 or > 31 || WeekDay is < 0 or > 6 ||
            !double.IsFinite(DayProgress) || !double.IsFinite(MonthProgress) || !double.IsFinite(YearProgress) ||
            DayProgress is < 0 or > 100 || MonthProgress is < 0 or > 100 || YearProgress is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(CalendarWidgetSnapshot));
        }
    }
}

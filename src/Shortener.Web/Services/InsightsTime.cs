namespace Shortener.Web.Services;

public static class InsightsTime
{
    public static IReadOnlyDictionary<(int Day, int Hour), long> ByLocalWeekdayHour(
        IReadOnlyList<InsightPoint> utcHours, TimeZoneInfo zone)
    {
        var counts = new Dictionary<(int Day, int Hour), long>();
        foreach (var point in utcHours)
        {
            var local = TimeZoneInfo.ConvertTime(point.BucketStartUtc, zone);
            var day = ((int)local.DayOfWeek + 6) % 7;
            counts[(day, local.Hour)] = counts.GetValueOrDefault((day, local.Hour)) + point.Clicks;
        }
        return counts;
    }
}

namespace DevAtlas.Models;

using DevAtlas.Services;

public static class DateRangeFilterExtensions
{
    public static string DisplayName(this DateRangeFilter filter)
    {
        var lm = LanguageManager.Instance;
        return filter switch
        {
            DateRangeFilter.Week => lm["DateRange7Days"],
            DateRangeFilter.Month => lm["DateRange30Days"],
            DateRangeFilter.Year => lm["DateRangeYear"],
            DateRangeFilter.AllTime => lm["DateRangeAllTime"],
            _ => lm["DateRange30Days"]
        };
    }

    public static int? Days(this DateRangeFilter filter) => filter switch
    {
        DateRangeFilter.Week => 7,
        DateRangeFilter.Month => 30,
        DateRangeFilter.Year => 365,
        DateRangeFilter.AllTime => null,
        _ => 30
    };
}

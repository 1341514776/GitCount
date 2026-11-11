namespace GitCount.Models;

public sealed record DateRangePreset(string Key, string DisplayName);

public static class DateRangePresets
{
    public const string All = "all";
    public const string Last7Days = "last7";
    public const string Last30Days = "last30";
    public const string Last90Days = "last90";
    public const string ThisYear = "thisYear";
    public const string Custom = "custom";

    public static IReadOnlyList<DateRangePreset> AllOptions { get; } =
    [
        new(All, "全部时间"),
        new(Last7Days, "最近 7 天"),
        new(Last30Days, "最近 30 天"),
        new(Last90Days, "最近 90 天"),
        new(ThisYear, "今年"),
        new(Custom, "自定义")
    ];
}

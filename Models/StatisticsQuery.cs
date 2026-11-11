namespace GitCount.Models;

public sealed record StatisticsQuery(DateTime? Since, DateTime? Until, bool AllBranches);

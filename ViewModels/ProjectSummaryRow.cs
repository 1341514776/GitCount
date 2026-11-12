namespace GitCount.ViewModels;

public sealed class ProjectSummaryRow
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required int CommitCount { get; init; }
    public required int FilteredCommitCount { get; init; }
    public required long Additions { get; init; }
    public required long Deletions { get; init; }
    public required int AuthorCount { get; init; }
    public required bool HasError { get; init; }
    public required string? ErrorMessage { get; init; }

    public long Net => Additions - Deletions;
    public string Status => HasError ? "统计失败" : CommitCount == 0 ? "无提交" : "统计完成";
    public string ErrorToolTip => ErrorMessage ?? string.Empty;
}

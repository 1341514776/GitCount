namespace GitCount.ViewModels;

public sealed class ProjectAuthorRow
{
    public required int ProjectSortIndex { get; init; }
    public required string ProjectName { get; init; }
    public required string ProjectPath { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public int Commits { get; init; }
    public int FilteredCommits { get; init; }
    public long Additions { get; init; }
    public long Deletions { get; init; }
    public int ChangedFiles { get; init; }
    public long Net => Additions - Deletions;
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Email : Name;
    public string EmailDisplay => string.IsNullOrWhiteSpace(Email) ? "未记录邮箱" : Email;
    public string Initial => AuthorRow.GetInitial(DisplayName);
}

namespace GitCount.Models;

public sealed class ProjectStatistics
{
    public required ProjectConfig Project { get; init; }
    public List<AuthorStatistics> Authors { get; init; } = [];
    public string? ErrorMessage { get; init; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public int CommitCount => Authors.Sum(author => author.Commits);
    public long Additions => Authors.Sum(author => author.Additions);
    public long Deletions => Authors.Sum(author => author.Deletions);
    public int ChangedFiles => Authors.Sum(author => author.ChangedFiles);
    public int FilteredCommitCount => Authors.Sum(author => author.FilteredCommits);
    public long Net => Additions - Deletions;

    public static ProjectStatistics Failed(ProjectConfig project, string message) =>
        new() { Project = project, ErrorMessage = message };
}

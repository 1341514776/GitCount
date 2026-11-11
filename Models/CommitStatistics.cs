namespace GitCount.Models;

public sealed class CommitStatistics
{
    public required string Hash { get; init; }
    public required string Subject { get; init; }
    public required string AuthorIdentityKey { get; init; }
    public required string AuthorName { get; init; }
    public required string AuthorEmail { get; init; }
    public required string ProjectName { get; init; }
    public required string ProjectPath { get; init; }
    public DateTimeOffset? CommittedAt { get; init; }
    public long Additions { get; set; }
    public long Deletions { get; set; }
    public int ChangedFiles { get; set; }
    public long Net => Additions - Deletions;
}

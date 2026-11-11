namespace GitCount.Models;

public sealed class AuthorStatistics
{
    public required string IdentityKey { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public List<CommitStatistics> CommitDetails { get; } = [];
    public int Commits { get; set; }
    public long Additions { get; set; }
    public long Deletions { get; set; }
    public int ChangedFiles { get; set; }
    public long Net => Additions - Deletions;
}

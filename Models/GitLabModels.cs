namespace GitCount.Models;

public sealed record GitLabConnection(string BaseUrl, string Token);

public sealed record GitLabConnectionInfo(string Version, string Username, string DisplayName);

public sealed class GitLabProjectInfo
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public required string PathWithNamespace { get; init; }
    public required string WebUrl { get; init; }
    public string DefaultBranch { get; init; } = string.Empty;
    public DateTimeOffset? LastActivityAt { get; init; }
}

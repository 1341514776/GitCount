using GitCount.Models;

namespace GitCount.ViewModels;

public sealed class AuthorRow
{
    public AuthorRow(string identityKey, string name, string email)
    {
        IdentityKey = identityKey;
        Name = name;
        Email = email;
    }

    public string IdentityKey { get; }
    public string Name { get; set; }
    public string Email { get; set; }
    public List<CommitStatistics> CommitDetails { get; } = [];
    public int Commits { get; set; }
    public long Additions { get; set; }
    public long Deletions { get; set; }
    public int ChangedFiles { get; set; }
    public int ProjectCount { get; set; }
    public long Net => Additions - Deletions;
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Email : Name;
    public string EmailDisplay => string.IsNullOrWhiteSpace(Email) ? "未记录邮箱" : Email;
    public string Initial => GetInitial(DisplayName);

    public static string GetInitial(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "?";
        }

        var trimmed = value.Trim();
        return trimmed[..1].ToUpperInvariant();
    }
}

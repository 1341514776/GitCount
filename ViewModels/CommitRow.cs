using System.Globalization;
using GitCount.Models;

namespace GitCount.ViewModels;

public sealed class CommitRow
{
    private const double MaxBarHeight = 108;

    public CommitRow(CommitStatistics commit, double chartMaximum)
    {
        Hash = commit.Hash;
        ShortHash = commit.Hash.Length > 7 ? commit.Hash[..7] : commit.Hash;
        Subject = string.IsNullOrWhiteSpace(commit.Subject) ? "(无提交信息)" : commit.Subject;
        ProjectName = commit.ProjectName;
        ProjectPath = commit.ProjectPath;
        AuthorName = commit.AuthorName;
        AuthorEmail = commit.AuthorEmail;
        CommittedAt = commit.CommittedAt?.ToLocalTime();
        Additions = commit.Additions;
        Deletions = commit.Deletions;
        ChangedFiles = commit.ChangedFiles;
        AddBarHeight = ScaleBar(commit.Additions, chartMaximum);
        DeleteBarHeight = ScaleBar(commit.Deletions, chartMaximum);
    }

    public string Hash { get; }
    public string ShortHash { get; }
    public string Subject { get; }
    public string ProjectName { get; }
    public string ProjectPath { get; }
    public string AuthorName { get; }
    public string AuthorEmail { get; }
    public DateTimeOffset? CommittedAt { get; }
    public long Additions { get; }
    public long Deletions { get; }
    public int ChangedFiles { get; }
    public long Net => Additions - Deletions;
    public double AddBarHeight { get; }
    public double DeleteBarHeight { get; }

    public string DateText => CommittedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) ?? "未知时间";
    public string AxisDateText => CommittedAt?.ToString("MM-dd", CultureInfo.CurrentCulture) ?? "--";
    public string AxisTimeText => CommittedAt?.ToString("HH:mm", CultureInfo.CurrentCulture) ?? string.Empty;
    public string AuthorDisplayName => string.IsNullOrWhiteSpace(AuthorName) ? AuthorEmail : AuthorName;

    public string ToolTipText =>
        $"{DateText}{Environment.NewLine}" +
        $"{ProjectName}{Environment.NewLine}" +
        $"{ShortHash} {Subject}{Environment.NewLine}" +
        $"新增 +{Additions:N0} · 删除 -{Deletions:N0} · 净变化 {Net:+#,##0;-#,##0;0}";

    private static double ScaleBar(long value, double chartMaximum)
    {
        if (value <= 0 || chartMaximum <= 0)
        {
            return 0;
        }

        return Math.Clamp(value / chartMaximum * MaxBarHeight, 2, MaxBarHeight);
    }
}

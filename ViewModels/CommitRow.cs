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
        RawAdditions = commit.Additions;
        RawDeletions = commit.Deletions;
        CountedAdditions = commit.CountedAdditions;
        CountedDeletions = commit.CountedDeletions;
        ChangedFiles = commit.ChangedFiles;
        IsLineCountFiltered = commit.IsLineCountFiltered;
        FilterReason = commit.FilterReason;
        AddBarHeight = ScaleBar(commit.CountedAdditions, chartMaximum);
        DeleteBarHeight = ScaleBar(commit.CountedDeletions, chartMaximum);
    }

    public string Hash { get; }
    public string ShortHash { get; }
    public string Subject { get; }
    public string ProjectName { get; }
    public string ProjectPath { get; }
    public string AuthorName { get; }
    public string AuthorEmail { get; }
    public DateTimeOffset? CommittedAt { get; }
    public long RawAdditions { get; }
    public long RawDeletions { get; }
    public long CountedAdditions { get; }
    public long CountedDeletions { get; }
    public int ChangedFiles { get; }
    public bool IsLineCountFiltered { get; }
    public string? FilterReason { get; }
    public long Net => CountedAdditions - CountedDeletions;
    public double AddBarHeight { get; }
    public double DeleteBarHeight { get; }

    public string DateText => CommittedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) ?? "未知时间";
    public string AxisDateText => CommittedAt?.ToString("MM-dd", CultureInfo.CurrentCulture) ?? "--";
    public string AxisTimeText => CommittedAt?.ToString("HH:mm", CultureInfo.CurrentCulture) ?? string.Empty;
    public string AuthorDisplayName => string.IsNullOrWhiteSpace(AuthorName) ? AuthorEmail : AuthorName;

    public string FilterStatusText => IsLineCountFiltered ? "已过滤" : string.Empty;

    public string ToolTipText
    {
        get
        {
            var lines = new List<string>
            {
                DateText,
                ProjectName,
                $"{ShortHash} {Subject}"
            };

            if (IsLineCountFiltered)
            {
                lines.Add("本次提交已命中大提交过滤，新增/删除行数不计入汇总。");
                lines.Add($"原始：新增 +{RawAdditions:N0} · 删除 -{RawDeletions:N0}");
                lines.Add($"原因：{FilterReason}");
            }
            else
            {
                lines.Add($"新增 +{RawAdditions:N0} · 删除 -{RawDeletions:N0} · 净变化 {Net:+#,##0;-#,##0;0}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    private static double ScaleBar(long value, double chartMaximum)
    {
        if (value <= 0 || chartMaximum <= 0)
        {
            return 0;
        }

        return Math.Clamp(value / chartMaximum * MaxBarHeight, 2, MaxBarHeight);
    }
}

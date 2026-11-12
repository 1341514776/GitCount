using System.Globalization;
using System.IO;
using System.Text;

namespace GitCount.ViewModels;

public sealed partial class MainViewModel
{
    public async Task ExportCsvAsync(string path)
    {
        if (!CanExport)
        {
            throw new InvalidOperationException("还没有可导出的统计结果。");
        }

        await using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await writer.WriteLineAsync(Csv("GitCount 统计导出"));
        await WritePairAsync(writer, "导出时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture));
        await WritePairAsync(writer, "统计范围", RangeCaption);
        await WritePairAsync(writer, "项目数", _lastProjectStats.Count.ToString(CultureInfo.InvariantCulture));
        await WritePairAsync(writer, "提交数", _totalCommits.ToString(CultureInfo.InvariantCulture));
        await WritePairAsync(writer, "新增行数", _totalAdditions.ToString(CultureInfo.InvariantCulture));
        await WritePairAsync(writer, "删除行数", _totalDeletions.ToString(CultureInfo.InvariantCulture));
        await WritePairAsync(writer, "净变化", TotalNet.ToString(CultureInfo.InvariantCulture));
        await WritePairAsync(writer, "过滤大提交", LargeCommitFilterEnabled ? "启用" : "关闭");
        await WritePairAsync(writer, "大提交文件阈值", LargeCommitFileThreshold.ToString(CultureInfo.InvariantCulture));
        await WritePairAsync(writer, "大提交新增阈值", LargeCommitAdditionThreshold.ToString(CultureInfo.InvariantCulture));
        await WritePairAsync(writer, "已过滤大提交数", TotalFilteredCommits.ToString(CultureInfo.InvariantCulture));
        await writer.WriteLineAsync();

        await writer.WriteLineAsync(Csv("人员汇总"));
        await WriteRowAsync(writer,
            "统计范围", "作者", "邮箱", "提交数", "过滤提交数", "新增行数", "删除行数", "净变化", "变更文件数", "涉及项目数");
        foreach (var author in _authorRows)
        {
            await WriteRowAsync(writer,
                "全部选中项目",
                author.DisplayName,
                author.Email,
                author.Commits.ToString(CultureInfo.InvariantCulture),
                author.FilteredCommits.ToString(CultureInfo.InvariantCulture),
                author.Additions.ToString(CultureInfo.InvariantCulture),
                author.Deletions.ToString(CultureInfo.InvariantCulture),
                author.Net.ToString(CultureInfo.InvariantCulture),
                author.ChangedFiles.ToString(CultureInfo.InvariantCulture),
                author.ProjectCount.ToString(CultureInfo.InvariantCulture));
        }

        await writer.WriteLineAsync();
        await writer.WriteLineAsync(Csv("分项目汇总"));
        await WriteRowAsync(writer,
            "项目", "路径", "提交数", "过滤提交数", "新增行数", "删除行数", "净变化", "贡献者数", "状态", "错误信息");
        foreach (var project in _projectSummaryRows)
        {
            await WriteRowAsync(writer,
                project.Name,
                project.Path,
                project.CommitCount.ToString(CultureInfo.InvariantCulture),
                project.FilteredCommitCount.ToString(CultureInfo.InvariantCulture),
                project.Additions.ToString(CultureInfo.InvariantCulture),
                project.Deletions.ToString(CultureInfo.InvariantCulture),
                project.Net.ToString(CultureInfo.InvariantCulture),
                project.AuthorCount.ToString(CultureInfo.InvariantCulture),
                project.Status,
                project.ErrorMessage ?? string.Empty);
        }

        await writer.WriteLineAsync();
        await writer.WriteLineAsync(Csv("分项目人员明细"));
        await WriteRowAsync(writer,
            "项目", "路径", "作者", "邮箱", "提交数", "过滤提交数", "新增行数", "删除行数", "净变化", "变更文件数");
        foreach (var author in _projectAuthorRows)
        {
            await WriteRowAsync(writer,
                author.ProjectName,
                author.ProjectPath,
                author.DisplayName,
                author.Email,
                author.Commits.ToString(CultureInfo.InvariantCulture),
                author.FilteredCommits.ToString(CultureInfo.InvariantCulture),
                author.Additions.ToString(CultureInfo.InvariantCulture),
                author.Deletions.ToString(CultureInfo.InvariantCulture),
                author.Net.ToString(CultureInfo.InvariantCulture),
                author.ChangedFiles.ToString(CultureInfo.InvariantCulture));
        }

        await writer.WriteLineAsync();
        await writer.WriteLineAsync(Csv("提交明细"));
        await WriteRowAsync(writer,
            "作者", "邮箱", "项目", "时间", "短 Hash", "完整 Hash", "提交信息", "是否过滤", "过滤原因", "原始新增", "原始删除", "计入新增", "计入删除", "计入净变化", "变更文件数");
        foreach (var author in _authorRows)
        {
            foreach (var commit in author.CommitDetails
                         .OrderByDescending(commit => commit.CommittedAt ?? DateTimeOffset.MinValue)
                         .ThenByDescending(commit => commit.Hash, StringComparer.Ordinal))
            {
                await WriteRowAsync(writer,
                    author.DisplayName,
                    author.Email,
                    commit.ProjectName,
                    commit.CommittedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) ?? string.Empty,
                    commit.Hash.Length > 7 ? commit.Hash[..7] : commit.Hash,
                    commit.Hash,
                    commit.Subject,
                    commit.IsLineCountFiltered ? "是" : "否",
                    commit.FilterReason ?? string.Empty,
                    commit.Additions.ToString(CultureInfo.InvariantCulture),
                    commit.Deletions.ToString(CultureInfo.InvariantCulture),
                    commit.CountedAdditions.ToString(CultureInfo.InvariantCulture),
                    commit.CountedDeletions.ToString(CultureInfo.InvariantCulture),
                    commit.CountedNet.ToString(CultureInfo.InvariantCulture),
                    commit.ChangedFiles.ToString(CultureInfo.InvariantCulture));
            }
        }

        await writer.FlushAsync();
        SetStatus($"CSV 已导出：{Path.GetFileName(path)}", StatusTone.Success);
    }

    private static Task WritePairAsync(TextWriter writer, string label, string value) =>
        writer.WriteLineAsync($"{Csv(label)},{Csv(value)}");

    private static Task WriteRowAsync(TextWriter writer, params string[] values) =>
        writer.WriteLineAsync(string.Join(",", values.Select(Csv)));

    private static string Csv(string? value) =>
        "\"" + (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

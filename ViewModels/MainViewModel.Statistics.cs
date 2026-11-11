using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using GitCount.Models;

namespace GitCount.ViewModels;

public sealed partial class MainViewModel
{
    public async Task CalculateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var selectedProjects = Projects.Where(project => project.IsIncluded).ToList();
        if (selectedProjects.Count == 0)
        {
            SetStatus("请至少选择一个项目后再统计。", StatusTone.Warning);
            return;
        }

        if (!ValidateCustomRange())
        {
            return;
        }

        if (GitStatusTone == StatusTone.Error)
        {
            await DetectGitAsync();
        }

        if (GitStatusTone == StatusTone.Error)
        {
            SetStatus("未检测到 Git，无法统计。请先安装 Git for Windows。", StatusTone.Error);
            return;
        }

        var (since, until) = GetDateBounds();
        var query = new StatisticsQuery(since, until, IncludeAllBranches);
        var results = new List<ProjectStatistics>();
        var cancelled = false;

        _calculationCts = new CancellationTokenSource();
        var cancellationToken = _calculationCts.Token;
        IsBusy = true;
        ProgressText = $"0 / {selectedProjects.Count}";

        try
        {
            for (var index = 0; index < selectedProjects.Count; index++)
            {
                var item = selectedProjects[index];
                ProgressText = $"{index + 1} / {selectedProjects.Count}";
                SetStatus($"正在统计：{item.Name}", StatusTone.Info);

                try
                {
                    var statistics = await _gitService.GetStatisticsAsync(
                        item.ToConfig(),
                        query,
                        cancellationToken);
                    item.LastError = null;
                    results.Add(statistics);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    break;
                }
                catch (Exception exception)
                {
                    item.LastError = exception.Message;
                    results.Add(ProjectStatistics.Failed(item.ToConfig(), exception.Message));
                }
            }

            if (cancelled)
            {
                SetStatus("统计已取消，原有结果保持不变。", StatusTone.Warning);
                return;
            }

            ApplyResults(results, since, until);
            SaveStateSafe();

            var failedCount = results.Count(result => result.HasError);
            if (failedCount == 0)
            {
                SetStatus(
                    _totalCommits == 0
                        ? "统计完成：当前范围内没有提交。"
                        : $"统计完成：{selectedProjects.Count} 个项目，{_authorRows.Count} 位贡献者。",
                    _totalCommits == 0 ? StatusTone.Neutral : StatusTone.Success);
            }
            else
            {
                SetStatus(
                    failedCount == selectedProjects.Count
                        ? "统计失败：所有选中项目均未能读取，请查看「分项目统计」中的原因。"
                        : $"统计完成，其中 {failedCount} 个项目失败，详情见「分项目统计」。",
                    failedCount == selectedProjects.Count ? StatusTone.Error : StatusTone.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("统计已取消。", StatusTone.Warning);
        }
        catch (Exception exception)
        {
            SetStatus($"统计失败：{exception.Message}", StatusTone.Error);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            _calculationCts?.Dispose();
            _calculationCts = null;
        }
    }

    private bool ValidateCustomRange()
    {
        if (SelectedDateRange.Key != DateRangePresets.Custom)
        {
            return true;
        }

        if (StartDate is null || EndDate is null)
        {
            SetStatus("请选择完整的自定义开始日期和结束日期。", StatusTone.Warning);
            return false;
        }

        if (StartDate.Value.Date > EndDate.Value.Date)
        {
            SetStatus("开始日期不能晚于结束日期。", StatusTone.Warning);
            return false;
        }

        return true;
    }

    private (DateTime? Since, DateTime? Until) GetDateBounds()
    {
        var today = DateTime.Today;
        return SelectedDateRange.Key switch
        {
            DateRangePresets.All => (null, null),
            DateRangePresets.Last7Days => (today.AddDays(-6), today),
            DateRangePresets.Last30Days => (today.AddDays(-29), today),
            DateRangePresets.Last90Days => (today.AddDays(-89), today),
            DateRangePresets.ThisYear => (new DateTime(today.Year, 1, 1), today),
            DateRangePresets.Custom => (StartDate?.Date, EndDate?.Date),
            _ => (today.AddDays(-29), today)
        };
    }

    private void ApplyResults(List<ProjectStatistics> results, DateTime? since, DateTime? until)
    {
        _lastProjectStats = results;
        var successful = results.Where(result => !result.HasError).ToList();
        var orderedSuccessful = successful
            .OrderByDescending(result => result.Additions)
            .ThenBy(result => result.Project.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var orderedAll = orderedSuccessful
            .Concat(results.Where(result => result.HasError))
            .ToList();
        var projectIndexes = orderedAll
            .Select((result, index) => new { result.Project.Path, Index = index })
            .ToDictionary(item => item.Path, item => item.Index, StringComparer.OrdinalIgnoreCase);

        AuthorRows = AggregateAuthors(orderedSuccessful);
        SelectedDetailAuthor = AuthorRows.FirstOrDefault();
        _totalCommits = orderedSuccessful.Sum(result => result.CommitCount);
        _totalAdditions = orderedSuccessful.Sum(result => result.Additions);
        _totalDeletions = orderedSuccessful.Sum(result => result.Deletions);

        ProjectSummaryRows = orderedAll.Select(result => new ProjectSummaryRow
        {
            Name = result.Project.Name,
            Path = result.Project.Path,
            CommitCount = result.CommitCount,
            Additions = result.Additions,
            Deletions = result.Deletions,
            AuthorCount = result.Authors.Count,
            HasError = result.HasError,
            ErrorMessage = result.ErrorMessage
        }).ToList();

        var projectRows = new List<ProjectAuthorRow>();
        foreach (var project in orderedSuccessful)
        {
            foreach (var author in project.Authors.OrderByDescending(author => author.Additions))
            {
                projectRows.Add(new ProjectAuthorRow
                {
                    ProjectSortIndex = projectIndexes[project.Project.Path],
                    ProjectName = project.Project.Name,
                    ProjectPath = project.Project.Path,
                    Name = author.Name,
                    Email = author.Email,
                    Commits = author.Commits,
                    Additions = author.Additions,
                    Deletions = author.Deletions,
                    ChangedFiles = author.ChangedFiles
                });
            }
        }

        _projectAuthorRows = projectRows;
        ProjectAuthorView = BuildProjectAuthorView(projectRows);

        ResultTimestampText = $"更新于 {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)}";
        RangeCaption = BuildRangeCaption(since, until);
        HasCalculated = true;
        IsResultStale = false;

        NotifyMetricsChanged();
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(ResultScopeText));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
    }

    private static IReadOnlyList<AuthorRow> AggregateAuthors(IEnumerable<ProjectStatistics> projects)
    {
        var authors = new Dictionary<string, AuthorRow>(StringComparer.Ordinal);

        foreach (var project in projects)
        {
            foreach (var author in project.Authors)
            {
                if (!authors.TryGetValue(author.IdentityKey, out var row))
                {
                    row = new AuthorRow(author.IdentityKey, author.Name, author.Email);
                    authors.Add(author.IdentityKey, row);
                }

                row.Name = author.Name;
                row.Email = author.Email;
                row.CommitDetails.AddRange(author.CommitDetails);
                row.Commits += author.Commits;
                row.Additions += author.Additions;
                row.Deletions += author.Deletions;
                row.ChangedFiles += author.ChangedFiles;
                row.ProjectCount++;
            }
        }

        return authors.Values
            .OrderByDescending(author => author.Additions)
            .ThenByDescending(author => author.Commits)
            .ThenBy(author => author.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static ICollectionView BuildProjectAuthorView(IReadOnlyList<ProjectAuthorRow> rows)
    {
        var view = new ListCollectionView(rows.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProjectAuthorRow.ProjectName)));
        view.SortDescriptions.Add(new SortDescription(
            nameof(ProjectAuthorRow.ProjectSortIndex),
            ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(
            nameof(ProjectAuthorRow.Additions),
            ListSortDirection.Descending));
        return view;
    }

    private void NotifyMetricsChanged()
    {
        OnPropertyChanged(nameof(TotalCommits));
        OnPropertyChanged(nameof(TotalAdditions));
        OnPropertyChanged(nameof(TotalDeletions));
        OnPropertyChanged(nameof(TotalNet));
        OnPropertyChanged(nameof(ProjectSelectionHint));
        OnPropertyChanged(nameof(ResultScopeText));
    }

    private string BuildRangeCaption(DateTime? since, DateTime? until)
    {
        var untilText = until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dateRange = since is null
            ? "全部时间"
            : $"{since.Value:yyyy-MM-dd} 至 {untilText}";
        var branches = IncludeAllBranches ? "所有分支" : "当前分支";
        return $"{dateRange} · {branches}";
    }

    private void UpdateRangeCaption()
    {
        if (!HasCalculated)
        {
            var (since, until) = GetDateBounds();
            RangeCaption = BuildRangeCaption(since, until);
        }
    }

    private void ResetResults()
    {
        _lastProjectStats = [];
        _projectAuthorRows = Array.Empty<ProjectAuthorRow>();
        AuthorRows = Array.Empty<AuthorRow>();
        SelectedDetailAuthor = null;
        ProjectSummaryRows = Array.Empty<ProjectSummaryRow>();
        ProjectAuthorView = null;
        _totalCommits = 0;
        _totalAdditions = 0;
        _totalDeletions = 0;
        HasCalculated = false;
        IsResultStale = false;
        ResultTimestampText = "尚未统计";
        UpdateRangeCaption();
        NotifyMetricsChanged();
        OnPropertyChanged(nameof(CanExport));
    }
}

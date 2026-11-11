using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using GitCount.Infrastructure;
using GitCount.Models;
using GitCount.Services;

namespace GitCount.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly GitService _gitService;
    private readonly ProjectStore _projectStore;
    private CancellationTokenSource? _calculationCts;
    private bool _initialized;
    private bool _isBusy;
    private bool _hasCalculated;
    private bool _isResultStale;
    private bool _bulkUpdatingProjects;
    private DateRangePreset _selectedDateRange;
    private DateTime? _startDate = DateTime.Today.AddDays(-29);
    private DateTime? _endDate = DateTime.Today;
    private bool _includeAllBranches;
    private string _statusText = "准备就绪";
    private StatusTone _statusTone = StatusTone.Neutral;
    private string _progressText = string.Empty;
    private string _gitStatusText = "正在检测 Git...";
    private string _gitStatusTooltip = "Git CLI 用于读取本地提交记录";
    private StatusTone _gitStatusTone = StatusTone.Neutral;
    private string _resultTimestampText = "尚未统计";
    private string _rangeCaption = "最近 30 天 · 当前分支";
    private int _totalCommits;
    private long _totalAdditions;
    private long _totalDeletions;
    private IReadOnlyList<AuthorRow> _authorRows = Array.Empty<AuthorRow>();
    private IReadOnlyList<ProjectSummaryRow> _projectSummaryRows = Array.Empty<ProjectSummaryRow>();
    private IReadOnlyList<ProjectAuthorRow> _projectAuthorRows = Array.Empty<ProjectAuthorRow>();
    private ICollectionView? _projectAuthorView;
    private List<ProjectStatistics> _lastProjectStats = [];

    public MainViewModel() : this(new GitService(), new ProjectStore())
    {
    }

    public MainViewModel(GitService gitService, ProjectStore projectStore)
    {
        _gitService = gitService;
        _projectStore = projectStore;
        DateRanges = DateRangePresets.AllOptions;
        _selectedDateRange = DateRanges.First(range => range.Key == DateRangePresets.Last30Days);
    }

    public ObservableCollection<ProjectItemViewModel> Projects { get; } = new();
    public IReadOnlyList<DateRangePreset> DateRanges { get; }

    public DateRangePreset SelectedDateRange
    {
        get => _selectedDateRange;
        set
        {
            if (value is null || !SetProperty(ref _selectedDateRange, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsCustomRange));
            UpdateRangeCaption();
            MarkResultStale();
        }
    }

    public DateTime? StartDate
    {
        get => _startDate;
        set
        {
            if (SetProperty(ref _startDate, value))
            {
                UpdateRangeCaption();
                MarkResultStale();
            }
        }
    }

    public DateTime? EndDate
    {
        get => _endDate;
        set
        {
            if (SetProperty(ref _endDate, value))
            {
                UpdateRangeCaption();
                MarkResultStale();
            }
        }
    }

    public bool IncludeAllBranches
    {
        get => _includeAllBranches;
        set
        {
            if (SetProperty(ref _includeAllBranches, value))
            {
                UpdateRangeCaption();
                MarkResultStale();
            }
        }
    }

    public bool IsCustomRange => SelectedDateRange.Key == DateRangePresets.Custom;
    public bool IsNotBusy => !_isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public StatusTone StatusTone
    {
        get => _statusTone;
        private set => SetProperty(ref _statusTone, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public string GitStatusText
    {
        get => _gitStatusText;
        private set => SetProperty(ref _gitStatusText, value);
    }

    public string GitStatusTooltip
    {
        get => _gitStatusTooltip;
        private set => SetProperty(ref _gitStatusTooltip, value);
    }

    public StatusTone GitStatusTone
    {
        get => _gitStatusTone;
        private set => SetProperty(ref _gitStatusTone, value);
    }

    public bool HasNoProjects => Projects.Count == 0;
    public int SelectedProjectCount => Projects.Count(project => project.IsIncluded);
    public string ProjectCountText => Projects.Count.ToString(CultureInfo.CurrentCulture);
    public string SelectionSummaryText => $"已选择 {SelectedProjectCount} / {Projects.Count}";
    public string ProjectSelectionHint => $"{SelectedProjectCount} / {Projects.Count} 个项目";

    public bool HasCalculated
    {
        get => _hasCalculated;
        private set
        {
            if (!SetProperty(ref _hasCalculated, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanExport));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(EmptyStateTitle));
            OnPropertyChanged(nameof(EmptyStateDescription));
            OnPropertyChanged(nameof(ResultScopeText));
        }
    }

    public bool IsResultStale
    {
        get => _isResultStale;
        private set => SetProperty(ref _isResultStale, value);
    }

    public bool CanExport => HasCalculated && _lastProjectStats.Count > 0;
    public bool ShowEmptyState => _authorRows.Count == 0;

    public string EmptyStateTitle
    {
        get
        {
            if (!HasCalculated)
            {
                return "还没有统计数据";
            }

            if (_lastProjectStats.Count > 0 && _lastProjectStats.All(project => project.HasError))
            {
                return "统计失败";
            }

            return "没有可统计的提交";
        }
    }

    public string EmptyStateDescription
    {
        get
        {
            if (!HasCalculated)
            {
                return "在左侧添加 Git 项目，选择时间范围后点击「开始统计」。";
            }

            var failedCount = _lastProjectStats.Count(project => project.HasError);
            if (_lastProjectStats.Count > 0 && failedCount == _lastProjectStats.Count)
            {
                return "请检查项目路径、Git 是否可用，以及仓库是否存在提交。";
            }

            if (failedCount > 0)
            {
                return "成功项目在当前范围内没有提交；失败项目请在「分项目统计」中查看原因。";
            }

            return "尝试扩大时间范围，或勾选「所有分支」后重新统计。";
        }
    }

    public string ResultTimestampText
    {
        get => _resultTimestampText;
        private set => SetProperty(ref _resultTimestampText, value);
    }

    public string RangeCaption
    {
        get => _rangeCaption;
        private set => SetProperty(ref _rangeCaption, value);
    }

    public string ResultScopeText => HasCalculated
        ? $"{_lastProjectStats.Count} 个项目 · {_authorRows.Count} 位贡献者"
        : "尚未统计";

    public int TotalCommits => _totalCommits;
    public long TotalAdditions => _totalAdditions;
    public long TotalDeletions => _totalDeletions;
    public long TotalNet => _totalAdditions - _totalDeletions;
    public string ContributorHint => $"{_authorRows.Count} 位贡献者";

    public IReadOnlyList<AuthorRow> AuthorRows
    {
        get => _authorRows;
        private set
        {
            _authorRows = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(EmptyStateTitle));
            OnPropertyChanged(nameof(EmptyStateDescription));
            OnPropertyChanged(nameof(ContributorHint));
            OnPropertyChanged(nameof(ResultScopeText));
        }
    }

    public IReadOnlyList<ProjectSummaryRow> ProjectSummaryRows
    {
        get => _projectSummaryRows;
        private set
        {
            _projectSummaryRows = value;
            OnPropertyChanged();
        }
    }

    public ICollectionView? ProjectAuthorView
    {
        get => _projectAuthorView;
        private set => SetProperty(ref _projectAuthorView, value);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var state = _projectStore.Load();

        foreach (var config in state.Projects)
        {
            if (string.IsNullOrWhiteSpace(config.Path))
            {
                continue;
            }

            var item = new ProjectItemViewModel(config);
            SubscribeProject(item);
            Projects.Add(item);
        }

        var savedRange = DateRanges.FirstOrDefault(range =>
            string.Equals(range.Key, state.DateRangeKey, StringComparison.OrdinalIgnoreCase));
        if (savedRange is not null)
        {
            SelectedDateRange = savedRange;
        }

        StartDate = state.CustomStartDate ?? DateTime.Today.AddDays(-29);
        EndDate = state.CustomEndDate ?? DateTime.Today;
        IncludeAllBranches = state.IncludeAllBranches;
        RefreshProjectStateProperties();
        UpdateRangeCaption();

        await DetectGitAsync();

        if (GitStatusTone != StatusTone.Error)
        {
            SetStatus(
                Projects.Count == 0
                    ? "请添加一个 Git 项目开始统计。"
                    : $"已加载 {Projects.Count} 个项目，点击「开始统计」。",
                StatusTone.Neutral);
        }
    }

    public async Task<string?> AddProjectAsync(string path)
    {
        if (IsBusy)
        {
            return "正在统计，请稍后再添加项目。";
        }

        try
        {
            var root = await _gitService.ResolveRepositoryRootAsync(path);
            var normalizedRoot = NormalizePath(root);

            if (Projects.Any(project => PathEquals(project.Path, normalizedRoot)))
            {
                return $"项目已存在：{normalizedRoot}";
            }

            var name = CreateUniqueProjectName(normalizedRoot);
            var config = new ProjectConfig
            {
                Name = name,
                Path = normalizedRoot,
                IsIncluded = true
            };
            var item = new ProjectItemViewModel(config);
            SubscribeProject(item);
            Projects.Add(item);
            RefreshProjectStateProperties();
            MarkResultStale();
            SaveStateSafe();
            SetStatus($"已添加项目：{name}", StatusTone.Success);
            return null;
        }
        catch (GitNotFoundException exception)
        {
            GitStatusText = "未检测到 Git";
            GitStatusTone = StatusTone.Error;
            GitStatusTooltip = exception.Message;
            SetStatus(exception.Message, StatusTone.Error);
            return exception.Message;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, StatusTone.Error);
            return exception.Message;
        }
    }

    public void RemoveProject(ProjectItemViewModel project)
    {
        if (IsBusy || !Projects.Remove(project))
        {
            return;
        }

        project.InclusionChanged -= Project_InclusionChanged;
        RefreshProjectStateProperties();

        if (Projects.Count == 0)
        {
            ResetResults();
        }
        else
        {
            MarkResultStale();
        }

        SaveStateSafe();
        SetStatus($"已移除项目：{project.Name}", StatusTone.Neutral);
    }

    public void SetAllProjectsIncluded(bool included)
    {
        if (IsBusy || Projects.Count == 0)
        {
            return;
        }

        _bulkUpdatingProjects = true;
        try
        {
            foreach (var project in Projects)
            {
                project.IsIncluded = included;
                project.LastError = null;
            }
        }
        finally
        {
            _bulkUpdatingProjects = false;
        }

        RefreshProjectStateProperties();
        MarkResultStale();
        SaveStateSafe();
        SetStatus(
            included ? "已选中全部项目。" : "已取消选择全部项目。",
            StatusTone.Neutral);
    }

    public void CancelCalculation()
    {
        if (_calculationCts is { IsCancellationRequested: false })
        {
            _calculationCts.Cancel();
            SetStatus("正在取消统计...", StatusTone.Warning);
        }
    }

    public void SaveState()
    {
        SaveStateSafe();
    }

    private void SaveStateSafe()
    {
        try
        {
            var state = new ProjectState
            {
                Projects = Projects.Select(project => project.ToConfig()).ToList(),
                DateRangeKey = SelectedDateRange.Key,
                CustomStartDate = StartDate,
                CustomEndDate = EndDate,
                IncludeAllBranches = IncludeAllBranches
            };
            _projectStore.Save(state);
        }
        catch (Exception exception)
        {
            SetStatus($"保存项目配置失败：{exception.Message}", StatusTone.Warning);
        }
    }

    private async Task DetectGitAsync()
    {
        try
        {
            var version = await _gitService.GetVersionAsync();
            GitStatusText = version.Replace("git version", "Git", StringComparison.OrdinalIgnoreCase);
            GitStatusTooltip = version;
            GitStatusTone = StatusTone.Success;
        }
        catch (Exception exception)
        {
            GitStatusText = "未检测到 Git";
            GitStatusTooltip = exception.Message;
            GitStatusTone = StatusTone.Error;
            SetStatus(exception.Message, StatusTone.Error);
        }
    }

    private void SubscribeProject(ProjectItemViewModel project)
    {
        project.InclusionChanged += Project_InclusionChanged;
    }

    private void Project_InclusionChanged(object? sender, EventArgs e)
    {
        if (_bulkUpdatingProjects)
        {
            return;
        }

        RefreshProjectStateProperties();
        MarkResultStale();
        SaveStateSafe();
    }

    private void RefreshProjectStateProperties()
    {
        OnPropertyChanged(nameof(HasNoProjects));
        OnPropertyChanged(nameof(SelectedProjectCount));
        OnPropertyChanged(nameof(ProjectCountText));
        OnPropertyChanged(nameof(SelectionSummaryText));
        OnPropertyChanged(nameof(ProjectSelectionHint));
    }

    private void MarkResultStale()
    {
        if (HasCalculated)
        {
            IsResultStale = true;
        }
    }

    private void SetStatus(string text, StatusTone tone)
    {
        StatusText = text;
        StatusTone = tone;
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private string CreateUniqueProjectName(string repositoryPath)
    {
        var baseName = Path.GetFileName(NormalizePath(repositoryPath));
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = repositoryPath;
        }

        var candidate = baseName;
        var suffix = 2;
        while (Projects.Any(project => string.Equals(project.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} ({suffix++})";
        }

        return candidate;
    }
}

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
    private readonly GitLabService _gitLabService;
    private readonly ProjectStore _projectStore;
    private readonly ObservableCollection<ProjectItemViewModel> _localProjects = [];
    private readonly ObservableCollection<ProjectItemViewModel> _gitLabProjects = [];
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
    private StatisticsMode _mode = StatisticsMode.Local;
    private string _gitLabBaseUrl = string.Empty;
    private string _gitLabToken = string.Empty;
    private string _gitLabUserName = string.Empty;
    private bool _largeCommitFilterEnabled = true;
    private int _largeCommitFileThreshold = 20;
    private int _largeCommitAdditionThreshold = 500;
    private string _statusText = "准备就绪";
    private StatusTone _statusTone = StatusTone.Neutral;
    private string _progressText = string.Empty;
    private string _gitStatusText = "正在检测 Git...";
    private string _gitStatusTooltip = "Git CLI 用于读取本地提交记录";
    private StatusTone _gitStatusTone = StatusTone.Neutral;
    private string _localGitStatusText = "正在检测 Git...";
    private string _localGitStatusTooltip = "Git CLI 用于读取本地提交记录";
    private StatusTone _localGitStatusTone = StatusTone.Neutral;
    private string _resultTimestampText = "尚未统计";
    private string _rangeCaption = "最近 30 天 · 当前分支";
    private int _totalCommits;
    private long _totalAdditions;
    private long _totalDeletions;
    private int _totalFilteredCommits;
    private IReadOnlyList<AuthorRow> _authorRows = Array.Empty<AuthorRow>();
    private IReadOnlyList<ProjectSummaryRow> _projectSummaryRows = Array.Empty<ProjectSummaryRow>();
    private IReadOnlyList<ProjectAuthorRow> _projectAuthorRows = Array.Empty<ProjectAuthorRow>();
    private ICollectionView? _projectAuthorView;
    private List<ProjectStatistics> _lastProjectStats = [];

    public MainViewModel() : this(new GitService(), new ProjectStore(), new GitLabService())
    {
    }

    public MainViewModel(GitService gitService, ProjectStore projectStore)
        : this(gitService, projectStore, new GitLabService())
    {
    }

    public MainViewModel(GitService gitService, ProjectStore projectStore, GitLabService gitLabService)
    {
        _gitService = gitService;
        _gitLabService = gitLabService;
        _projectStore = projectStore;
        DateRanges = DateRangePresets.AllOptions;
        _selectedDateRange = DateRanges.First(range => range.Key == DateRangePresets.Last30Days);
    }

    public ObservableCollection<ProjectItemViewModel> Projects =>
        _mode == StatisticsMode.Local ? _localProjects : _gitLabProjects;

    public IReadOnlyList<DateRangePreset> DateRanges { get; }

    public StatisticsMode Mode => _mode;
    public bool IsLocalMode => _mode == StatisticsMode.Local;
    public bool IsGitLabMode => _mode == StatisticsMode.GitLab;
    public string HeaderDescription => IsLocalMode
        ? "跨项目汇总本地 Git 仓库每个人的提交、新增与删除行数"
        : "通过 GitLab API 汇总线上项目每个人的提交、新增与删除行数";
    public string ProjectListTitle => IsLocalMode ? "本地项目" : "线上项目";
    public string EmptyProjectListHint => IsLocalMode
        ? "点击上方按钮，或把 Git 仓库文件夹拖入窗口"
        : "点击「GitLab 项目管理」连接服务器并选择项目";
    public string ModeFooterText => IsLocalMode ? "本地统计" : "线上 API 统计";
    public string ModeStorageText => IsLocalMode
        ? "项目配置保存在本机"
        : "GitLab 令牌已使用 Windows 加密保存";
    public string ModeBadgeText => IsLocalMode
        ? "本地运行 · 数据不上传"
        : "线上读取 · GitLab API";
    public string ModeEngineText => IsLocalMode
        ? "Git CLI · 本地统计"
        : "GitLab API · 线上统计";
    public string GitLabBaseUrl => _gitLabBaseUrl;
    public string GitLabUserName => _gitLabUserName;
    public bool HasGitLabConnection => !string.IsNullOrWhiteSpace(_gitLabToken);

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

    public bool LargeCommitFilterEnabled
    {
        get => _largeCommitFilterEnabled;
        set
        {
            if (SetProperty(ref _largeCommitFilterEnabled, value))
            {
                OnPropertyChanged(nameof(LargeCommitFilterDescription));
                OnPropertyChanged(nameof(CanEditLargeCommitFilter));
                UpdateRangeCaption();
                MarkResultStale();
            }
        }
    }

    public int LargeCommitFileThreshold
    {
        get => _largeCommitFileThreshold;
        set
        {
            var normalized = Math.Clamp(value, 1, 100_000);
            if (SetProperty(ref _largeCommitFileThreshold, normalized))
            {
                OnPropertyChanged(nameof(LargeCommitFilterDescription));
                UpdateRangeCaption();
                MarkResultStale();
            }
        }
    }

    public int LargeCommitAdditionThreshold
    {
        get => _largeCommitAdditionThreshold;
        set
        {
            var normalized = Math.Clamp(value, 0, 100_000_000);
            if (SetProperty(ref _largeCommitAdditionThreshold, normalized))
            {
                OnPropertyChanged(nameof(LargeCommitFilterDescription));
                UpdateRangeCaption();
                MarkResultStale();
            }
        }
    }

    public string LargeCommitFilterDescription => _largeCommitFilterEnabled
        ? $"命中后仍计入提交次数，但新增和删除行数不计入统计（文件 ≥ {_largeCommitFileThreshold:N0}，或新增 > {_largeCommitAdditionThreshold:N0}）。"
        : "当前未启用大提交过滤。";

    public bool IsCustomRange => SelectedDateRange.Key == DateRangePresets.Custom;
    public bool IsNotBusy => !_isBusy;
    public bool CanEditLargeCommitFilter => _largeCommitFilterEnabled && !_isBusy;
    public bool CanEditGitLabProjects => !_isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(CanEditLargeCommitFilter));
                OnPropertyChanged(nameof(CanEditGitLabProjects));
            }
        }
    }

    public void SetMode(StatisticsMode mode)
    {
        if (_mode == mode || IsBusy)
        {
            return;
        }

        _mode = mode;
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsLocalMode));
        OnPropertyChanged(nameof(IsGitLabMode));
        OnPropertyChanged(nameof(Projects));
        OnPropertyChanged(nameof(HeaderDescription));
        OnPropertyChanged(nameof(ProjectListTitle));
        OnPropertyChanged(nameof(EmptyProjectListHint));
        OnPropertyChanged(nameof(ModeFooterText));
        OnPropertyChanged(nameof(ModeStorageText));
        OnPropertyChanged(nameof(ModeBadgeText));
        OnPropertyChanged(nameof(ModeEngineText));

        ResetResults();
        RefreshProjectStateProperties();
        ApplyModeConnectionStatus();
        if (mode == StatisticsMode.Local && _localGitStatusTone == StatusTone.Neutral)
        {
            _ = DetectGitAsync();
        }

        SaveStateSafe();
        SetStatus(
            mode == StatisticsMode.Local
                ? "已切换到本地模式，请选择本地 Git 仓库后统计。"
                : "已切换到线上模式，请配置 GitLab 连接并选择线上项目。",
            StatusTone.Neutral);
    }

    public string GetGitLabToken() => _gitLabToken;

    public IReadOnlyCollection<long> GetGitLabProjectIds() =>
        _gitLabProjects.Select(project => project.GitLabProjectId).ToHashSet();

    public void ApplyGitLabConfiguration(
        string baseUrl,
        string token,
        string userName,
        IReadOnlyList<GitLabProjectInfo> projects)
    {
        _gitLabBaseUrl = _gitLabService.NormalizeBaseUrl(baseUrl);
        _gitLabToken = token.Trim();
        _gitLabUserName = userName.Trim();

        var existing = _gitLabProjects
            .GroupBy(project => project.GitLabProjectId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var project in _gitLabProjects)
        {
            project.InclusionChanged -= Project_InclusionChanged;
        }

        _gitLabProjects.Clear();
        var usedNames = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var projectInfo in projects)
        {
            var name = CreateUniqueGitLabProjectName(projectInfo, usedNames);
            usedNames.Add(name);
            existing.TryGetValue(projectInfo.Id, out var previous);
            var config = new ProjectConfig
            {
                Name = name,
                Path = projectInfo.PathWithNamespace,
                IsIncluded = previous?.IsIncluded ?? true,
                SourceType = StatisticsMode.GitLab,
                GitLabProjectId = projectInfo.Id,
                GitLabPathWithNamespace = projectInfo.PathWithNamespace,
                GitLabWebUrl = projectInfo.WebUrl,
                GitLabDefaultBranch = projectInfo.DefaultBranch
            };
            var item = new ProjectItemViewModel(config);
            SubscribeProject(item);
            _gitLabProjects.Add(item);
        }

        OnPropertyChanged(nameof(GitLabBaseUrl));
        OnPropertyChanged(nameof(GitLabUserName));
        OnPropertyChanged(nameof(HasGitLabConnection));
        OnPropertyChanged(nameof(Projects));
        RefreshProjectStateProperties();
        ApplyModeConnectionStatus();
        ResetResults();
        SaveStateSafe();

        if (projects.Count == 0)
        {
            SetStatus("GitLab 连接已保存，但尚未选择项目。", StatusTone.Warning);
        }
        else
        {
            SetStatus($"GitLab 配置已保存：{projects.Count} 个项目。", StatusTone.Success);
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
                return IsLocalMode
                    ? "在左侧添加本地 Git 项目，选择时间范围后点击「开始统计」。"
                    : "在左侧配置 GitLab 连接并选择线上项目，然后点击「开始统计」。";
            }

            var failedCount = _lastProjectStats.Count(project => project.HasError);
            if (_lastProjectStats.Count > 0 && failedCount == _lastProjectStats.Count)
            {
                return IsLocalMode
                    ? "请检查项目路径、Git 是否可用，以及仓库是否存在提交。"
                    : "请检查 GitLab 地址、访问令牌、项目权限以及时间范围。";
            }

            if (failedCount > 0)
            {
                return "成功项目在当前范围内没有提交；失败项目请在「分项目统计」中查看原因。";
            }

            return IsLocalMode
                ? "尝试扩大时间范围，或勾选「所有分支」后重新统计。"
                : "尝试扩大时间范围、勾选「所有分支」，或重新配置 GitLab 令牌。";
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

    public string ResultScopeText
    {
        get
        {
            if (!HasCalculated)
            {
                return "尚未统计";
            }

            var text = $"{_lastProjectStats.Count} 个项目 · {_authorRows.Count} 位贡献者";
            return _totalFilteredCommits > 0
                ? $"{text} · 已过滤 {_totalFilteredCommits:N0} 次大提交"
                : text;
        }
    }

    public int TotalCommits => _totalCommits;
    public long TotalAdditions => _totalAdditions;
    public long TotalDeletions => _totalDeletions;
    public int TotalFilteredCommits => _totalFilteredCommits;
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
            EnsureSelectedDetailAuthor();
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

            config.SourceType = StatisticsMode.Local;
            var item = new ProjectItemViewModel(config);
            SubscribeProject(item);
            _localProjects.Add(item);
        }

        foreach (var config in state.GitLabProjects)
        {
            if (config.GitLabProjectId <= 0 || string.IsNullOrWhiteSpace(config.Path))
            {
                continue;
            }

            config.SourceType = StatisticsMode.GitLab;
            var item = new ProjectItemViewModel(config);
            SubscribeProject(item);
            _gitLabProjects.Add(item);
        }

        _mode = state.Mode;
        _gitLabBaseUrl = state.GitLabBaseUrl;
        _gitLabUserName = state.GitLabUserName;
        _gitLabToken = SecretProtector.Unprotect(state.GitLabTokenProtected);
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsLocalMode));
        OnPropertyChanged(nameof(IsGitLabMode));
        OnPropertyChanged(nameof(Projects));
        OnPropertyChanged(nameof(HeaderDescription));
        OnPropertyChanged(nameof(ProjectListTitle));
        OnPropertyChanged(nameof(EmptyProjectListHint));
        OnPropertyChanged(nameof(ModeFooterText));
        OnPropertyChanged(nameof(ModeStorageText));
        OnPropertyChanged(nameof(ModeBadgeText));
        OnPropertyChanged(nameof(ModeEngineText));
        OnPropertyChanged(nameof(GitLabBaseUrl));
        OnPropertyChanged(nameof(GitLabUserName));
        OnPropertyChanged(nameof(HasGitLabConnection));

        var savedRange = DateRanges.FirstOrDefault(range =>
            string.Equals(range.Key, state.DateRangeKey, StringComparison.OrdinalIgnoreCase));
        if (savedRange is not null)
        {
            SelectedDateRange = savedRange;
        }

        StartDate = state.CustomStartDate ?? DateTime.Today.AddDays(-29);
        EndDate = state.CustomEndDate ?? DateTime.Today;
        IncludeAllBranches = state.IncludeAllBranches;
        LargeCommitFilterEnabled = state.LargeCommitFilterEnabled;
        LargeCommitFileThreshold = state.LargeCommitFileThreshold;
        LargeCommitAdditionThreshold = state.LargeCommitAdditionThreshold;
        RefreshProjectStateProperties();
        UpdateRangeCaption();

        ApplyModeConnectionStatus();
        if (IsLocalMode)
        {
            await DetectGitAsync();
        }

        if (GitStatusTone != StatusTone.Error)
        {
            SetStatus(
                Projects.Count == 0
                    ? IsLocalMode
                        ? "请添加一个本地 Git 项目开始统计。"
                        : "请配置 GitLab 连接并选择线上项目开始统计。"
                    : $"已加载 {Projects.Count} 个项目，点击「开始统计」。",
                StatusTone.Neutral);
        }
    }

    public async Task<string?> AddProjectAsync(string path)
    {
        if (IsGitLabMode)
        {
            return "当前是线上模式，请使用「GitLab 项目管理」添加项目。";
        }

        if (IsBusy)
        {
            return "正在统计，请稍后再添加项目。";
        }

        try
        {
            var root = await _gitService.ResolveRepositoryRootAsync(path);
            var normalizedRoot = NormalizePath(root);

            if (_localProjects.Any(project => PathEquals(project.Path, normalizedRoot)))
            {
                return $"项目已存在：{normalizedRoot}";
            }

            var name = CreateUniqueProjectName(normalizedRoot);
            var config = new ProjectConfig
            {
                Name = name,
                Path = normalizedRoot,
                IsIncluded = true,
                SourceType = StatisticsMode.Local
            };
            var item = new ProjectItemViewModel(config);
            SubscribeProject(item);
            _localProjects.Add(item);
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
            var protectedToken = SecretProtector.Protect(_gitLabToken);
            var state = new ProjectState
            {
                Projects = _localProjects.Select(project => project.ToConfig()).ToList(),
                GitLabProjects = _gitLabProjects.Select(project => project.ToConfig()).ToList(),
                Mode = _mode,
                GitLabBaseUrl = _gitLabBaseUrl,
                GitLabUserName = _gitLabUserName,
                GitLabTokenProtected = protectedToken,
                DateRangeKey = SelectedDateRange.Key,
                CustomStartDate = StartDate,
                CustomEndDate = EndDate,
                IncludeAllBranches = IncludeAllBranches,
                LargeCommitFilterEnabled = LargeCommitFilterEnabled,
                LargeCommitFileThreshold = LargeCommitFileThreshold,
                LargeCommitAdditionThreshold = LargeCommitAdditionThreshold
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
            _localGitStatusText = version.Replace("git version", "Git", StringComparison.OrdinalIgnoreCase);
            _localGitStatusTooltip = version;
            _localGitStatusTone = StatusTone.Success;
        }
        catch (Exception exception)
        {
            _localGitStatusText = "未检测到 Git";
            _localGitStatusTooltip = exception.Message;
            _localGitStatusTone = StatusTone.Error;
            if (IsLocalMode)
            {
                SetStatus(exception.Message, StatusTone.Error);
            }
        }

        ApplyModeConnectionStatus();
    }

    private void ApplyModeConnectionStatus()
    {
        if (IsGitLabMode)
        {
            if (string.IsNullOrWhiteSpace(_gitLabToken))
            {
                GitStatusText = "GitLab 未连接";
                GitStatusTooltip = "点击「GitLab 项目管理」配置服务器地址和访问令牌。";
                GitStatusTone = StatusTone.Neutral;
            }
            else
            {
                GitStatusText = string.IsNullOrWhiteSpace(_gitLabUserName)
                    ? "GitLab Token 已保存"
                    : $"GitLab · {_gitLabUserName}";
                GitStatusTooltip = string.IsNullOrWhiteSpace(_gitLabBaseUrl)
                    ? "GitLab 访问令牌已加密保存。"
                    : $"{_gitLabBaseUrl}{Environment.NewLine}GitLab 访问令牌已加密保存。";
                GitStatusTone = StatusTone.Success;
            }

            return;
        }

        GitStatusText = _localGitStatusText;
        GitStatusTooltip = _localGitStatusTooltip;
        GitStatusTone = _localGitStatusTone;
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
        while (_localProjects.Any(project => string.Equals(project.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} ({suffix++})";
        }

        return candidate;
    }

    private static string CreateUniqueGitLabProjectName(
        GitLabProjectInfo project,
        ISet<string> usedNames)
    {
        var baseName = string.IsNullOrWhiteSpace(project.Name)
            ? project.PathWithNamespace
            : project.Name.Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = project.Id.ToString(CultureInfo.InvariantCulture);
        }

        var candidate = baseName;
        var suffix = 2;
        while (usedNames.Contains(candidate))
        {
            candidate = $"{baseName} ({suffix++})";
        }

        return candidate;
    }
}

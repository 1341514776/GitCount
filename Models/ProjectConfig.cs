namespace GitCount.Models;

public sealed class ProjectConfig
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsIncluded { get; set; } = true;
    public StatisticsMode SourceType { get; set; } = StatisticsMode.Local;
    public long GitLabProjectId { get; set; }
    public string GitLabPathWithNamespace { get; set; } = string.Empty;
    public string GitLabWebUrl { get; set; } = string.Empty;
    public string GitLabDefaultBranch { get; set; } = string.Empty;
}

public sealed class ProjectState
{
    public List<ProjectConfig> Projects { get; set; } = [];
    public List<ProjectConfig> GitLabProjects { get; set; } = [];
    public StatisticsMode Mode { get; set; } = StatisticsMode.Local;
    public string GitLabBaseUrl { get; set; } = string.Empty;
    public string GitLabUserName { get; set; } = string.Empty;
    public string GitLabTokenProtected { get; set; } = string.Empty;
    public string DateRangeKey { get; set; } = DateRangePresets.Last30Days;
    public DateTime? CustomStartDate { get; set; } = DateTime.Today.AddDays(-29);
    public DateTime? CustomEndDate { get; set; } = DateTime.Today;
    public bool IncludeAllBranches { get; set; }
    public bool LargeCommitFilterEnabled { get; set; } = true;
    public int LargeCommitFileThreshold { get; set; } = 20;
    public int LargeCommitAdditionThreshold { get; set; } = 500;
}

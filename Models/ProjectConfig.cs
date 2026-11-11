namespace GitCount.Models;

public sealed class ProjectConfig
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsIncluded { get; set; } = true;
}

public sealed class ProjectState
{
    public List<ProjectConfig> Projects { get; set; } = [];
    public string DateRangeKey { get; set; } = DateRangePresets.Last30Days;
    public DateTime? CustomStartDate { get; set; } = DateTime.Today.AddDays(-29);
    public DateTime? CustomEndDate { get; set; } = DateTime.Today;
    public bool IncludeAllBranches { get; set; }
}

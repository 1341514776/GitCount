using System.Globalization;
using GitCount.Infrastructure;
using GitCount.Models;

namespace GitCount.ViewModels;

public sealed class GitLabProjectSelectionRow : ObservableObject
{
    private bool _isSelected;

    public GitLabProjectSelectionRow(GitLabProjectInfo project, bool isSelected)
    {
        Project = project;
        _isSelected = isSelected;
    }

    public GitLabProjectInfo Project { get; }
    public long Id => Project.Id;
    public string Name => Project.Name;
    public string PathWithNamespace => Project.PathWithNamespace;
    public string DefaultBranch => string.IsNullOrWhiteSpace(Project.DefaultBranch) ? "-" : Project.DefaultBranch;
    public string LastActivityText => Project.LastActivityAt?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) ?? "-";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

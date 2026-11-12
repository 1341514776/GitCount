using GitCount.Infrastructure;
using GitCount.Models;

namespace GitCount.ViewModels;

public sealed class ProjectItemViewModel : ObservableObject
{
    private bool _isIncluded;
    private string? _lastError;

    public ProjectItemViewModel(ProjectConfig config)
    {
        Name = config.Name;
        Path = config.Path;
        SourceType = config.SourceType;
        GitLabProjectId = config.GitLabProjectId;
        GitLabPathWithNamespace = config.GitLabPathWithNamespace;
        GitLabWebUrl = config.GitLabWebUrl;
        GitLabDefaultBranch = config.GitLabDefaultBranch;
        _isIncluded = config.IsIncluded;
    }

    public event EventHandler? InclusionChanged;

    public string Name { get; }
    public string Path { get; }
    public StatisticsMode SourceType { get; }
    public bool IsGitLab => SourceType == StatisticsMode.GitLab;
    public long GitLabProjectId { get; }
    public string GitLabPathWithNamespace { get; }
    public string GitLabWebUrl { get; }
    public string GitLabDefaultBranch { get; }
    public string SourceLabel => IsGitLab ? "GitLab" : "本地";

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (SetProperty(ref _isIncluded, value))
            {
                InclusionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string? LastError
    {
        get => _lastError;
        set
        {
            if (SetProperty(ref _lastError, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ErrorToolTip));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(LastError);
    public string ErrorToolTip => string.IsNullOrWhiteSpace(LastError) ? string.Empty : LastError;

    public ProjectConfig ToConfig() => new()
    {
        Name = Name,
        Path = Path,
        IsIncluded = IsIncluded,
        SourceType = SourceType,
        GitLabProjectId = GitLabProjectId,
        GitLabPathWithNamespace = GitLabPathWithNamespace,
        GitLabWebUrl = GitLabWebUrl,
        GitLabDefaultBranch = GitLabDefaultBranch
    };
}

using System.Globalization;

namespace GitCount.ViewModels;

public sealed partial class MainViewModel
{
    private AuthorRow? _selectedDetailAuthor;
    private IReadOnlyList<CommitRow> _selectedAuthorCommits = Array.Empty<CommitRow>();
    private IReadOnlyList<CommitRow> _commitChartRows = Array.Empty<CommitRow>();
    private double _commitChartMaxValue;

    public AuthorRow? SelectedDetailAuthor
    {
        get => _selectedDetailAuthor;
        set
        {
            if (SetProperty(ref _selectedDetailAuthor, value))
            {
                UpdateSelectedCommitDetails();
            }
        }
    }

    public IReadOnlyList<CommitRow> SelectedAuthorCommits
    {
        get => _selectedAuthorCommits;
        private set
        {
            if (SetProperty(ref _selectedAuthorCommits, value))
            {
                OnPropertyChanged(nameof(HasSelectedCommitDetails));
                OnPropertyChanged(nameof(HasNoSelectedCommitDetails));
            }
        }
    }

    public IReadOnlyList<CommitRow> CommitChartRows
    {
        get => _commitChartRows;
        private set => SetProperty(ref _commitChartRows, value);
    }

    public bool HasSelectedCommitDetails => _selectedAuthorCommits.Count > 0;
    public bool HasNoSelectedCommitDetails => !HasSelectedCommitDetails;

    public int SelectedDetailCommitCount => _selectedAuthorCommits.Count;
    public long SelectedDetailAdditions => _selectedAuthorCommits.Sum(commit => commit.Additions);
    public long SelectedDetailDeletions => _selectedAuthorCommits.Sum(commit => commit.Deletions);
    public long SelectedDetailNet => SelectedDetailAdditions - SelectedDetailDeletions;
    public string CommitChartMaxText => FormatAxisValue(_commitChartMaxValue);
    public string CommitChartMidText => FormatAxisValue(_commitChartMaxValue / 2);
    public string CommitChartHint => _selectedAuthorCommits.Count == 0
        ? "暂无提交数据"
        : $"{_selectedAuthorCommits.Count:N0} 次提交 · 按时间升序";

    private void UpdateSelectedCommitDetails()
    {
        if (_selectedDetailAuthor is null)
        {
            CommitChartRows = Array.Empty<CommitRow>();
            CompleteCommitDetailUpdate(Array.Empty<CommitRow>());
            return;
        }

        var rawMaximum = _selectedDetailAuthor.CommitDetails
            .Select(commit => Math.Max(commit.Additions, commit.Deletions))
            .DefaultIfEmpty(0)
            .Max();
        var chartMaximum = NiceCeiling(rawMaximum);
        var rows = _selectedDetailAuthor.CommitDetails
            .OrderByDescending(commit => commit.CommittedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(commit => commit.Hash, StringComparer.Ordinal)
            .Select(commit => new CommitRow(commit, chartMaximum))
            .ToList();

        _commitChartMaxValue = chartMaximum;
        CommitChartRows = rows.AsEnumerable().Reverse().ToArray();
        CompleteCommitDetailUpdate(rows);
    }

    private void CompleteCommitDetailUpdate(IReadOnlyList<CommitRow> rows)
    {
        SelectedAuthorCommits = rows;
        OnPropertyChanged(nameof(SelectedDetailCommitCount));
        OnPropertyChanged(nameof(SelectedDetailAdditions));
        OnPropertyChanged(nameof(SelectedDetailDeletions));
        OnPropertyChanged(nameof(SelectedDetailNet));
        OnPropertyChanged(nameof(CommitChartMaxText));
        OnPropertyChanged(nameof(CommitChartMidText));
        OnPropertyChanged(nameof(CommitChartHint));
    }

    private static double NiceCeiling(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalized = value / magnitude;
        var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    private static string FormatAxisValue(double value)
    {
        if (value <= 0)
        {
            return "0";
        }

        return value >= 10
            ? value.ToString("N0", CultureInfo.CurrentCulture)
            : value.ToString("0.#", CultureInfo.CurrentCulture);
    }
}

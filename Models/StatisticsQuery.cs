namespace GitCount.Models;

public sealed record CommitFilterOptions(bool Enabled, int FileThreshold, int AdditionThreshold)
{
    public string? GetFilterReason(int changedFiles, long additions)
    {
        if (!Enabled)
        {
            return null;
        }

        var fileMatched = changedFiles >= FileThreshold;
        var additionsMatched = additions > AdditionThreshold;

        return (fileMatched, additionsMatched) switch
        {
            (true, true) => $"文件 {changedFiles} ≥ {FileThreshold}，新增 {additions:N0} > {AdditionThreshold:N0}",
            (true, false) => $"文件 {changedFiles} ≥ {FileThreshold}",
            (false, true) => $"新增 {additions:N0} > {AdditionThreshold:N0}",
            _ => null
        };
    }
}

public sealed record StatisticsQuery(
    DateTime? Since,
    DateTime? Until,
    bool AllBranches,
    CommitFilterOptions LargeCommitFilter);

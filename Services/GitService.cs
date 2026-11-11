using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using GitCount.Models;

namespace GitCount.Services;

public sealed class GitNotFoundException : Exception
{
    public GitNotFoundException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class GitCommandException : Exception
{
    public GitCommandException(string message, string? details = null)
        : base(string.IsNullOrWhiteSpace(details) ? message : $"{message}{Environment.NewLine}{details.Trim()}")
    {
        Details = details;
    }

    public string? Details { get; }
}

public sealed class GitService
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(["--version"], cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new GitNotFoundException("无法调用 Git，请确认 Git for Windows 已安装并已加入 PATH。");
        }

        return result.StandardOutput.Trim();
    }

    public async Task<string> ResolveRepositoryRootAsync(string selectedPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selectedPath) || !Directory.Exists(selectedPath))
        {
            throw new DirectoryNotFoundException("所选文件夹不存在。");
        }

        var result = await RunGitAsync(
            ["-C", selectedPath, "rev-parse", "--show-toplevel"],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new GitCommandException("所选文件夹不是 Git 仓库，或 Git 无法访问该目录。", result.StandardError);
        }

        var root = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new GitCommandException("未能识别 Git 仓库根目录。", result.StandardError);
        }

        return NormalizePath(root);
    }

    public async Task<ProjectStatistics> GetStatisticsAsync(
        ProjectConfig project,
        StatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "-c", "i18n.logOutputEncoding=UTF-8",
            "-c", "core.quotepath=false",
            "-C", project.Path,
            "log",
            "--no-merges",
            "--no-show-signature",
            "--numstat",
            "--date=iso-strict",
            "--format=%x1e%H%x1f%an%x1f%ae%x1f%aI%x1f%s"
        };

        if (query.AllBranches)
        {
            arguments.Add("--all");
        }

        if (query.Since is { } since)
        {
            arguments.Add($"--since={since.ToString("yyyy-MM-dd", InvariantCulture)} 00:00:00");
        }

        if (query.Until is { } until)
        {
            arguments.Add($"--until={until.ToString("yyyy-MM-dd", InvariantCulture)} 23:59:59");
        }

        var result = await RunGitAsync(arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            if (IsEmptyRepositoryError(result.StandardError))
            {
                return new ProjectStatistics { Project = project };
            }

            throw new GitCommandException($"统计项目「{project.Name}」失败。", result.StandardError);
        }

        return new ProjectStatistics
        {
            Project = project,
            Authors = ParseNumstat(result.StandardOutput, project)
        };
    }

    private static bool IsEmptyRepositoryError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return error.Contains("does not have any commits yet", StringComparison.OrdinalIgnoreCase)
            || error.Contains("your current branch", StringComparison.OrdinalIgnoreCase)
               && error.Contains("does not have any commits", StringComparison.OrdinalIgnoreCase)
            || error.Contains("bad default revision 'HEAD'", StringComparison.OrdinalIgnoreCase)
            || error.Contains("ambiguous argument 'HEAD'", StringComparison.OrdinalIgnoreCase);
    }

    private static List<AuthorStatistics> ParseNumstat(string output, ProjectConfig project)
    {
        var authors = new Dictionary<string, AuthorStatistics>(StringComparer.Ordinal);

        foreach (var rawSegment in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\r', '\n');
            var firstLineBreak = segment.IndexOf('\n');
            var headerLine = firstLineBreak < 0 ? segment : segment[..firstLineBreak];
            var body = firstLineBreak < 0 ? string.Empty : segment[(firstLineBreak + 1)..];
            var fields = headerLine.Split('\x1f');

            if (fields.Length < 5)
            {
                continue;
            }

            var name = fields[1].Trim();
            var email = fields[2].Trim();
            var identityKey = string.IsNullOrWhiteSpace(email)
                ? "name:" + name.ToLowerInvariant()
                : "email:" + email.ToLowerInvariant();

            if (!authors.TryGetValue(identityKey, out var author))
            {
                author = new AuthorStatistics
                {
                    IdentityKey = identityKey,
                    Name = name,
                    Email = email
                };
                authors.Add(identityKey, author);
            }

            var committedAt = DateTimeOffset.TryParse(
                fields[3],
                InvariantCulture,
                DateTimeStyles.None,
                out var parsedCommittedAt)
                ? parsedCommittedAt
                : (DateTimeOffset?)null;

            var commit = new CommitStatistics
            {
                Hash = fields[0].Trim(),
                Subject = fields[4].Trim(),
                AuthorIdentityKey = identityKey,
                AuthorName = name,
                AuthorEmail = email,
                ProjectName = project.Name,
                ProjectPath = project.Path,
                CommittedAt = committedAt
            };
            author.CommitDetails.Add(commit);
            author.Commits++;

            foreach (var rawLine in body.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var columns = line.Split('\t', 3);
                if (columns.Length < 3)
                {
                    continue;
                }

                commit.ChangedFiles++;
                author.ChangedFiles++;

                if (columns[0] == "-" || columns[1] == "-")
                {
                    continue;
                }

                if (long.TryParse(columns[0], NumberStyles.Integer, InvariantCulture, out var additions))
                {
                    commit.Additions += additions;
                    author.Additions += additions;
                }

                if (long.TryParse(columns[1], NumberStyles.Integer, InvariantCulture, out var deletions))
                {
                    commit.Deletions += deletions;
                    author.Deletions += deletions;
                }
            }
        }

        return authors.Values
            .OrderByDescending(author => author.Additions)
            .ThenByDescending(author => author.Commits)
            .ThenBy(author => author.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static async Task<GitProcessResult> RunGitAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["LC_ALL"] = "C.UTF-8";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new GitNotFoundException("无法启动 Git 进程。");
        }
        catch (Win32Exception exception)
        {
            throw new GitNotFoundException(
                "未检测到 Git。请先安装 Git for Windows，并确保 git 命令已加入 PATH。",
                exception);
        }

        using (process)
        {
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;

            return new GitProcessResult(process.ExitCode, standardOutput, standardError);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may already have exited.
        }
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(trimmed) ? fullPath : trimmed;
    }

    private sealed record GitProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitCount.Models;

namespace GitCount.Services;

public sealed class GitLabApiException : Exception
{
    public GitLabApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

public sealed class GitLabService
{
    private const int PageSize = 100;
    private const int DiffConcurrency = 4;
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private readonly ConcurrentDictionary<string, int> _fileCountCache = new(StringComparer.Ordinal);

    public string NormalizeBaseUrl(string? input)
    {
        var value = input?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = "http://" + value;
        }

        value = value.TrimEnd('/');
        if (!value.EndsWith("/api/v4", StringComparison.OrdinalIgnoreCase))
        {
            value += "/api/v4";
        }

        return value;
    }

    public async Task<GitLabConnectionInfo> TestConnectionAsync(
        string baseUrl,
        string token,
        CancellationToken cancellationToken = default)
    {
        var apiBase = NormalizeBaseUrl(baseUrl);
        if (string.IsNullOrWhiteSpace(apiBase))
        {
            throw new GitLabApiException("请填写 GitLab 根地址。");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new GitLabApiException("请填写 GitLab 访问令牌。");
        }

        using var userResponse = await SendAsync(new Uri(apiBase + "/user"), token, cancellationToken);
        var user = await userResponse.Content.ReadFromJsonAsync<GitLabUserDto>(cancellationToken)
            ?? throw new GitLabApiException("GitLab 返回的用户信息为空。");

        var version = string.Empty;
        try
        {
            using var versionResponse = await SendAsync(new Uri(apiBase + "/version"), token, cancellationToken);
            var versionDto = await versionResponse.Content.ReadFromJsonAsync<GitLabVersionDto>(cancellationToken);
            version = versionDto?.Version ?? string.Empty;
        }
        catch
        {
            // The version endpoint is not available to every token, but /user already proved connectivity.
        }

        return new GitLabConnectionInfo(version, user.Username ?? string.Empty, user.Name ?? string.Empty);
    }

    public async Task<IReadOnlyList<GitLabProjectInfo>> GetProjectsAsync(
        string baseUrl,
        string token,
        CancellationToken cancellationToken = default)
    {
        var apiBase = NormalizeBaseUrl(baseUrl);
        var projects = new List<GitLabProjectInfo>();
        var page = 1;

        while (page <= 100)
        {
            var uri = new Uri(
                apiBase
                + "/projects?membership=true&simple=true&per_page="
                + PageSize.ToString(InvariantCulture)
                + "&page="
                + page.ToString(InvariantCulture)
                + "&order_by=last_activity_at&sort=desc");

            using var response = await SendAsync(uri, token, cancellationToken);
            var batch = await response.Content.ReadFromJsonAsync<List<GitLabProjectDto>>(cancellationToken)
                ?? [];

            projects.AddRange(batch
                .Where(item => item.Id > 0)
                .Select(item => new GitLabProjectInfo
                {
                    Id = item.Id,
                    Name = item.Name ?? item.PathWithNamespace ?? item.Path ?? string.Empty,
                    PathWithNamespace = item.PathWithNamespace ?? item.Path ?? string.Empty,
                    WebUrl = item.WebUrl ?? string.Empty,
                    DefaultBranch = item.DefaultBranch ?? string.Empty,
                    LastActivityAt = ParseDate(item.LastActivityAt)
                }));

            var nextPage = GetNextPage(response);
            if (nextPage is null || batch.Count == 0)
            {
                break;
            }

            page = nextPage.Value;
        }

        return projects
            .GroupBy(project => project.Id)
            .Select(group => group.First())
            .OrderByDescending(project => project.LastActivityAt ?? DateTimeOffset.MinValue)
            .ThenBy(project => project.PathWithNamespace, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ProjectStatistics> GetStatisticsAsync(
        ProjectConfig project,
        GitLabConnection connection,
        StatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        var apiBase = NormalizeBaseUrl(connection.BaseUrl);
        var commits = await GetCommitsAsync(apiBase, connection.Token, project, query, cancellationToken);
        var nonMergeCommits = commits
            .Where(commit => commit.ParentIds.Count <= 1)
            .Select(commit => CreateCommitStatistics(commit, project))
            .ToList();

        await PopulateChangedFilesAsync(
            apiBase,
            connection.Token,
            project.GitLabProjectId,
            nonMergeCommits,
            cancellationToken);

        var authors = new Dictionary<string, AuthorStatistics>(StringComparer.Ordinal);
        foreach (var commit in nonMergeCommits)
        {
            if (!authors.TryGetValue(commit.AuthorIdentityKey, out var author))
            {
                author = new AuthorStatistics
                {
                    IdentityKey = commit.AuthorIdentityKey,
                    Name = commit.AuthorName,
                    Email = commit.AuthorEmail
                };
                authors.Add(author.IdentityKey, author);
            }

            author.CommitDetails.Add(commit);
            author.Commits++;
            author.ChangedFiles += commit.ChangedFiles;

            var filterReason = query.LargeCommitFilter.GetFilterReason(commit.ChangedFiles, commit.Additions);
            if (filterReason is not null)
            {
                commit.IsLineCountFiltered = true;
                commit.FilterReason = filterReason;
                author.FilteredCommits++;
            }
            else
            {
                author.Additions += commit.Additions;
                author.Deletions += commit.Deletions;
            }
        }

        return new ProjectStatistics
        {
            Project = project,
            Authors = authors.Values
                .OrderByDescending(author => author.Additions)
                .ThenByDescending(author => author.Commits)
                .ThenBy(author => author.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };
    }

    private static async Task<List<GitLabCommitDto>> GetCommitsAsync(
        string apiBase,
        string token,
        ProjectConfig project,
        StatisticsQuery query,
        CancellationToken cancellationToken)
    {
        var commits = new List<GitLabCommitDto>();
        var page = 1;
        while (page <= 10000)
        {
            var queryParts = new List<string>
            {
                "with_stats=true",
                "per_page=" + PageSize.ToString(InvariantCulture),
                "page=" + page.ToString(InvariantCulture)
            };

            if (query.AllBranches)
            {
                queryParts.Add("all=true");
            }

            if (query.Since is { } since)
            {
                queryParts.Add("since=" + Uri.EscapeDataString(FormatApiDate(since.Date, endOfDay: false)));
            }

            if (query.Until is { } until)
            {
                queryParts.Add("until=" + Uri.EscapeDataString(FormatApiDate(until.Date, endOfDay: true)));
            }

            var uri = new Uri(
                apiBase
                + "/projects/"
                + project.GitLabProjectId.ToString(InvariantCulture)
                + "/repository/commits?"
                + string.Join("&", queryParts));

            using var response = await SendAsync(uri, token, cancellationToken);
            var batch = await response.Content.ReadFromJsonAsync<List<GitLabCommitDto>>(cancellationToken)
                ?? [];
            commits.AddRange(batch);

            var nextPage = GetNextPage(response);
            if (nextPage is null || batch.Count == 0)
            {
                break;
            }

            page = nextPage.Value;
        }

        return commits;
    }

    private async Task PopulateChangedFilesAsync(
        string apiBase,
        string token,
        long projectId,
        IReadOnlyList<CommitStatistics> commits,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(DiffConcurrency);
        var tasks = commits.Select(async commit =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                commit.ChangedFiles = await GetChangedFileCountAsync(
                    apiBase,
                    token,
                    projectId,
                    commit.Hash,
                    cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task<int> GetChangedFileCountAsync(
        string apiBase,
        string token,
        long projectId,
        string hash,
        CancellationToken cancellationToken)
    {
        var cacheKey = projectId.ToString(InvariantCulture) + ":" + hash;
        if (_fileCountCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var page = 1;
        var count = 0;
        while (page <= 1000)
        {
            var uri = new Uri(
                apiBase
                + "/projects/"
                + projectId.ToString(InvariantCulture)
                + "/repository/commits/"
                + Uri.EscapeDataString(hash)
                + "/diff?per_page="
                + PageSize.ToString(InvariantCulture)
                + "&page="
                + page.ToString(InvariantCulture));

            using var response = await SendAsync(uri, token, cancellationToken);
            var batch = await response.Content.ReadFromJsonAsync<List<GitLabDiffDto>>(cancellationToken)
                ?? [];
            count += batch.Count;

            var nextPage = GetNextPage(response);
            if (nextPage is null || batch.Count == 0)
            {
                break;
            }

            page = nextPage.Value;
        }

        _fileCountCache[cacheKey] = count;
        return count;
    }

    private static CommitStatistics CreateCommitStatistics(GitLabCommitDto commit, ProjectConfig project)
    {
        var name = commit.AuthorName?.Trim() ?? string.Empty;
        var email = commit.AuthorEmail?.Trim() ?? string.Empty;
        var identityKey = string.IsNullOrWhiteSpace(email)
            ? "name:" + name.ToLowerInvariant()
            : "email:" + email.ToLowerInvariant();

        return new CommitStatistics
        {
            Hash = commit.Id ?? string.Empty,
            Subject = string.IsNullOrWhiteSpace(commit.Title) ? "(无提交信息)" : commit.Title.Trim(),
            AuthorIdentityKey = identityKey,
            AuthorName = name,
            AuthorEmail = email,
            ProjectName = project.Name,
            ProjectPath = project.GitLabPathWithNamespace,
            CommittedAt = ParseDate(commit.AuthoredDate) ?? ParseDate(commit.CommittedDate),
            Additions = commit.Stats?.Additions ?? 0,
            Deletions = commit.Stats?.Deletions ?? 0
        };
    }

    private static string FormatApiDate(DateTime date, bool endOfDay)
    {
        var value = endOfDay
            ? date.Date.AddDays(1).AddSeconds(-1)
            : date.Date;
        var offset = TimeZoneInfo.Local.GetUtcOffset(value);
        return new DateTimeOffset(value, offset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", InvariantCulture);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static int? GetNextPage(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Next-Page", out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        return int.TryParse(value, NumberStyles.Integer, InvariantCulture, out var page) && page > 0
            ? page
            : null;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        Uri uri,
        string token,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
            request.Headers.TryAddWithoutValidation("User-Agent", "GitCount/1.0");

            HttpResponseMessage response;
            try
            {
                response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new GitLabApiException(
                    $"无法连接 GitLab：{exception.Message}",
                    null,
                    exception);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 2)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var details = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = response.StatusCode;
            response.Dispose();

            var message = statusCode switch
            {
                HttpStatusCode.Unauthorized => "GitLab 令牌无效或已过期。",
                HttpStatusCode.Forbidden => "GitLab 令牌没有读取该项目的权限。",
                HttpStatusCode.NotFound => "GitLab 项目或接口不存在。",
                _ => $"GitLab API 请求失败（{(int)statusCode} {statusCode}）。"
            };

            if (!string.IsNullOrWhiteSpace(details))
            {
                message += " " + ExtractErrorMessage(details);
            }

            throw new GitLabApiException(message.Trim(), statusCode);
        }
    }

    private static string ExtractErrorMessage(string details)
    {
        try
        {
            using var document = JsonDocument.Parse(details);
            if (document.RootElement.TryGetProperty("message", out var message))
            {
                return message.ValueKind == JsonValueKind.String
                    ? message.GetString() ?? string.Empty
                    : message.ToString();
            }
        }
        catch
        {
            // Fall through to a bounded raw response.
        }

        return details.Length > 300 ? details[..300] : details;
    }

    private sealed class GitLabUserDto
    {
        [JsonPropertyName("username")]
        public string? Username { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class GitLabVersionDto
    {
        [JsonPropertyName("version")]
        public string? Version { get; init; }
    }

    private sealed class GitLabProjectDto
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("path")]
        public string? Path { get; init; }

        [JsonPropertyName("path_with_namespace")]
        public string? PathWithNamespace { get; init; }

        [JsonPropertyName("web_url")]
        public string? WebUrl { get; init; }

        [JsonPropertyName("default_branch")]
        public string? DefaultBranch { get; init; }

        [JsonPropertyName("last_activity_at")]
        public string? LastActivityAt { get; init; }
    }

    private sealed class GitLabCommitDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("author_name")]
        public string? AuthorName { get; init; }

        [JsonPropertyName("author_email")]
        public string? AuthorEmail { get; init; }

        [JsonPropertyName("authored_date")]
        public string? AuthoredDate { get; init; }

        [JsonPropertyName("committed_date")]
        public string? CommittedDate { get; init; }

        [JsonPropertyName("parent_ids")]
        public List<string> ParentIds { get; init; } = [];

        [JsonPropertyName("stats")]
        public GitLabCommitStatsDto? Stats { get; init; }
    }

    private sealed class GitLabCommitStatsDto
    {
        [JsonPropertyName("additions")]
        public long Additions { get; init; }

        [JsonPropertyName("deletions")]
        public long Deletions { get; init; }
    }

    private sealed class GitLabDiffDto
    {
        [JsonPropertyName("new_path")]
        public string? NewPath { get; init; }
    }
}

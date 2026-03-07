using DevAtlas.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DevAtlas.Services;

/// <summary>
/// Service that fetches Git statistics (additions, deletions, commits) for projects.
/// Windows equivalent of GitStatsService.swift.
/// </summary>
public class GitStatsService
{
    private static readonly Lazy<GitStatsService> _instance = new(() => new GitStatsService());
    private static readonly Regex DateRegex = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);
    public static GitStatsService Shared => _instance.Value;

    private string? _cachedGitPath;

    private GitStatsService() { }

    /// <summary>
    /// Fetch git stats for multiple projects within a date range.
    /// </summary>
    public async Task<List<GitDailyStat>> FetchGitStatsAsync(
        IEnumerable<ProjectInfo> projects,
        DateRangeFilter range,
        CancellationToken ct = default)
    {
        var allStats = new List<GitDailyStat>();
        var gitPath = FindGitExecutable();
        if (gitPath == null)
        {
            Debug.WriteLine("GitStatsService: git executable not found. Git additions/deletions will be empty.");
            return allStats;
        }

        var cutoffDate = range.Days() is int days
            ? DateTime.Now.AddDays(-days)
            : (DateTime?)null;

        var gitProjects = projects
            .Where(p => IsGitRepository(p.Path))
            .ToList();

        if (gitProjects.Count == 0)
        {
            return allStats;
        }

        var maxConcurrency = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        using var throttler = new SemaphoreSlim(maxConcurrency);

        var tasks = gitProjects.Select(async project =>
        {
            await throttler.WaitAsync(ct);
            try
            {
                var stats = await FetchProjectStatsAsync(gitPath, project.Path, project.Name, cutoffDate, ct);
                return stats;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GitStatsService error for {project.Name} ({project.Path}): {ex}");
                return new List<GitDailyStat>();
            }
            finally
            {
                throttler.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        foreach (var batch in results)
        {
            allStats.AddRange(batch);
        }

        return allStats.OrderBy(s => s.Date).ToList();
    }

    private static bool IsGitRepository(string projectPath)
    {
        try
        {
            var gitMarker = Path.Combine(projectPath, ".git");
            return Directory.Exists(gitMarker) || File.Exists(gitMarker);
        }
        catch
        {
            return false;
        }
    }

    private string? FindGitExecutable()
    {
        if (_cachedGitPath != null) return _cachedGitPath;

        // Try common paths
        var candidates = new[]
        {
            "git",
            @"C:\Program Files\Git\bin\git.exe",
            @"C:\Program Files (x86)\Git\bin\git.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\bin\git.exe")
        };

        foreach (var path in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo(path, "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(3000);
                    if (proc.ExitCode == 0)
                    {
                        _cachedGitPath = path;
                        return path;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    private async Task<List<GitDailyStat>> FetchProjectStatsAsync(
        string gitPath,
        string projectPath,
        string projectName,
        DateTime? since,
        CancellationToken ct)
    {
        var args = "log --numstat --date=short --format=%ad";
        // NOTE: Do not filter by author; global user.name often doesn't match commit authors
        // across machines (email vs name), and would yield empty stats.
        if (since.HasValue)
            args += $" --since={since.Value:yyyy-MM-dd}";

        var psi = new ProcessStartInfo(gitPath, args)
        {
            WorkingDirectory = projectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return [];

        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        var error = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            Debug.WriteLine($"GitStatsService git log failed for {projectName} ({projectPath}). ExitCode={proc.ExitCode}. Error={error}");
            return [];
        }

        var projectStats = new Dictionary<string, (int additions, int deletions, int commits)>();
        var currentDate = "";

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (DateRegex.IsMatch(trimmed))
            {
                currentDate = trimmed;
                var current = projectStats.GetValueOrDefault(currentDate);
                projectStats[currentDate] = (current.additions, current.deletions, current.commits + 1);
            }
            else
            {
                var parts = trimmed.Split('\t');
                if (parts.Length >= 2 && parts[0] != "-" && parts[1] != "-")
                {
                    if (int.TryParse(parts[0], out var added) && int.TryParse(parts[1], out var deleted))
                    {
                        if (!string.IsNullOrEmpty(currentDate))
                        {
                            var current = projectStats.GetValueOrDefault(currentDate);
                            projectStats[currentDate] = (current.additions + added, current.deletions + deleted, current.commits);
                        }
                    }
                }
            }
        }

        var result = new List<GitDailyStat>();
        foreach (var (dateStr, stats) in projectStats)
        {
            if (DateTime.TryParse(dateStr, out var date))
            {
                result.Add(new GitDailyStat
                {
                    Date = date,
                    ProjectName = projectName,
                    Additions = stats.additions,
                    Deletions = stats.deletions,
                    Commits = stats.commits
                });
            }
        }

        return result;
    }
}

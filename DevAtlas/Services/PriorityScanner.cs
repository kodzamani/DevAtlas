using DevAtlas.Configuration;

namespace DevAtlas.Services;

public class PriorityScanner
{
    private readonly ScalabilityConfiguration _config;

    public PriorityScanner(ScalabilityConfiguration config)
    {
        _config = config;
    }

    public List<string> GetPriorityPaths(DriveInfo drive)
    {
        var priorityPaths = new List<string>();
        var priorityDirNames = _config.PriorityDirectories.Select(d => d.ToLowerInvariant()).ToHashSet();

        try
        {
            var stack = new Stack<string>();
            stack.Push(drive.RootDirectory.FullName);

            while (stack.Count > 0)
            {
                var currentPath = stack.Pop();
                if (!Directory.Exists(currentPath))
                {
                    continue;
                }

                var dirName = Path.GetFileName(currentPath).ToLowerInvariant();

                if (_config.SkipDirectories.Contains(dirName) || dirName.StartsWith("."))
                {
                    continue;
                }

                if (priorityDirNames.Contains(dirName))
                {
                    priorityPaths.Add(currentPath);
                }

                if (priorityDirNames.Contains(dirName) || priorityPaths.Any(p => currentPath.StartsWith(Path.GetDirectoryName(p) ?? "")))
                {
                    try
                    {
                        var subDirs = Directory.GetDirectories(currentPath);
                        foreach (var subDir in subDirs.Take(50))
                        {
                            stack.Push(subDir);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }
        catch
        {
        }

        return priorityPaths.Distinct().ToList();
    }
}
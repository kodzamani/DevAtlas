using System.Reflection;
using DevAtlas.Models;
using DevAtlas.Services;

namespace DevAtlas.Tests.Services;

public class ProjectScannerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"DevAtlas.ProjectScannerTests.{Guid.NewGuid():N}");

    public ProjectScannerTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void CheckForProject_UsesPackageJsonNameWhenAvailable()
    {
        var projectDir = CreateProjectDirectory("client-app");
        File.WriteAllText(Path.Combine(projectDir, "package.json"), """{"name":"@acme/soundatlas-web"}""");

        var project = DetectProject(projectDir);

        Assert.NotNull(project);
        Assert.Equal("@acme/soundatlas-web", project!.Name);
    }

    [Fact]
    public void CheckForProject_PrefersSolutionOrProjectFileStemOverGenericFolderName()
    {
        var projectDir = CreateProjectDirectory("src");
        File.WriteAllText(Path.Combine(projectDir, "SoundAtlas.csproj"), "<Project />");

        var project = DetectProject(projectDir);

        Assert.NotNull(project);
        Assert.Equal("SoundAtlas", project!.Name);
    }

    private ProjectInfo? DetectProject(string projectDir)
    {
        var scanner = new ProjectScanner();
        var method = typeof(ProjectScanner).GetMethod("CheckForProject", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (ProjectInfo?)method!.Invoke(scanner, [projectDir]);
    }

    private string CreateProjectDirectory(string name)
    {
        var path = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
        }
    }
}

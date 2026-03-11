using DevAtlas.Models;

namespace DevAtlas.Tests.Models;

public class ProjectExplorerGroupingTests
{
    [Fact]
    public void IsWslPath_DetectsUncAndLocalhostWslPaths()
    {
        Assert.True(ProjectInfo.IsWslPath(@"\\wsl$\Ubuntu\home\developer\project"));
        Assert.True(ProjectInfo.IsWslPath(@"\\wsl.localhost\Ubuntu\home\developer\project"));
        Assert.False(ProjectInfo.IsWslPath(@"C:\Users\developer\source\project"));
    }

    [Fact]
    public void GroupForExplorer_PlacesWslProjectsInDedicatedFirstGroup()
    {
        var now = DateTime.Now;
        var groups = ProjectGroup.GroupForExplorer(
        [
            new ProjectInfo
            {
                Name = "linux-api",
                Path = @"\\wsl$\Ubuntu\home\developer\linux-api",
                LastModified = now
            },
            new ProjectInfo
            {
                Name = "desktop-app",
                Path = @"C:\Users\developer\source\desktop-app",
                LastModified = now.AddDays(-1)
            }
        ]);

        Assert.NotEmpty(groups);
        Assert.Equal("linux-api", groups[0].Projects.Single().Name);
        Assert.All(groups.Skip(1).SelectMany(group => group.Projects), project => Assert.False(project.IsWslProject));
    }
}

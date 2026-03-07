namespace DevAtlas.Models;

public abstract class ProjectListEntry;

public sealed class ProjectGroupHeaderEntry : ProjectListEntry
{
    public string GroupName { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed class ProjectGridRowEntry : ProjectListEntry
{
    public IReadOnlyList<ProjectInfo> Projects { get; init; } = Array.Empty<ProjectInfo>();
}

public sealed class ProjectListItemEntry : ProjectListEntry
{
    public ProjectInfo Project { get; init; } = new();
}

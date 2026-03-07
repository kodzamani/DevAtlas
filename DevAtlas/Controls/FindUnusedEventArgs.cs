namespace DevAtlas.Controls;

public class FindUnusedEventArgs : EventArgs
{
    public FindUnusedEventArgs(string projectPath)
    {
        ProjectPath = projectPath;
    }

    public string ProjectPath { get; }
}
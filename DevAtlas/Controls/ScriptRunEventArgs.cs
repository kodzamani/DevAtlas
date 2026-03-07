namespace DevAtlas.Controls;

public class ScriptRunEventArgs : EventArgs
{
    public string ProjectPath { get; set; } = string.Empty;
    public string ScriptName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
}
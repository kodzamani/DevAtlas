using DevAtlas.Models;

namespace DevAtlas.Controls;

public sealed class AIPromptRequestedEventArgs(AIPromptDefinition prompt) : EventArgs
{
    public AIPromptDefinition Prompt { get; } = prompt;
}
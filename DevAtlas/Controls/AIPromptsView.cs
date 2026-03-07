using Avalonia.Controls;
using Avalonia.Interactivity;
using DevAtlas.Models;
using DevAtlas.ViewModels;

namespace DevAtlas.Controls;

/// <summary>
/// Interaction logic for the AI prompt library screen.
/// </summary>
public partial class AIPromptsView : UserControl
{
    public AIPromptsView()
    {
        InitializeComponent();
    }

    public event EventHandler<AIPromptRequestedEventArgs>? PromptExpandRequested;

    private async void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: AIPromptCardViewModel prompt })
        {
            return;
        }

        await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(prompt.Prompt) ?? Task.CompletedTask);
        await prompt.FlashCopiedAsync();
    }

    private void ShowMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: AIPromptCardViewModel prompt })
        {
            return;
        }

        PromptExpandRequested?.Invoke(this, new AIPromptRequestedEventArgs(prompt.Definition));
    }
}

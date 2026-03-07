using Avalonia.Controls;
using Avalonia.Input;
using DevAtlas.Models;
using DevAtlas.Services;

namespace DevAtlas.Controls
{
    /// <summary>
    /// Popup for selecting and running npm scripts from package.json
    /// </summary>
    public partial class RunScriptPopup : UserControl
    {
        private string? _currentProjectPath;

        /// <summary>
        /// Event fired when a user selects a script to run
        /// </summary>
        public event EventHandler<ScriptRunEventArgs>? ScriptSelected;

        /// <summary>
        /// Event fired when the popup is closed without selection
        /// </summary>
        public event EventHandler? Closed;

        public RunScriptPopup()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the popup with available scripts for the project
        /// </summary>
        public async void Show(string projectPath, string projectName)
        {
            _currentProjectPath = projectPath;
            ProjectNameText.Text = projectName;

            NpmInstallWarning.IsVisible = false;

            LoadScripts(projectPath);

            IsVisible = true;
        }

        /// <summary>
        /// Hides the popup
        /// </summary>
        public void Hide()
        {
            IsVisible = false;
            _currentProjectPath = null;
            ScriptsList.Items.Clear();
            LoadingPanel.IsVisible = false;
        }

        /// <summary>
        /// Loads the list of scripts from package.json
        /// </summary>
        private void LoadScripts(string projectPath)
        {
            var scripts = ProjectRunner.GetAllScripts(projectPath);

            ScriptsList.Items.Clear();

            if (scripts.Any())
            {
                foreach (var script in scripts)
                {
                    var isDev = IsDevScript(script.Name);
                    var uiScript = new NpmScriptUi
                    {
                        Name = script.Name,
                        Command = script.Command,
                        IsDev = isDev,
                        IsHighlighted = isDev || IsBuildScript(script.Name),
                        BadgeText = GetBadgeText(script.Name),
                        Description = GetDescription(script.Name),
                        KindGlyph = GetKindGlyph(script.Name)
                    };
                    ScriptsList.Items.Add(uiScript);
                }

                ScriptsList.IsVisible = true;
                NoScriptsMessage.IsVisible = false;
            }
            else
            {
                ScriptsList.IsVisible = false;
                NoScriptsMessage.IsVisible = true;
            }
        }

        /// <summary>
        /// Checks if the script is a development script
        /// </summary>
        private bool IsDevScript(string scriptName)
        {
            string[] devScripts = { "dev", "start", "serve", "develop" };
            return devScripts.Contains(scriptName.ToLowerInvariant());
        }

        private bool IsBuildScript(string scriptName)
        {
            string[] buildScripts = { "build", "dist", "prod", "preview" };
            return buildScripts.Contains(scriptName.ToLowerInvariant());
        }

        private string GetBadgeText(string scriptName)
        {
            var normalizedName = scriptName.ToLowerInvariant();
            return normalizedName switch
            {
                "dev" or "start" or "serve" or "develop" => "Primary",
                "build" or "dist" or "prod" or "preview" => "Build",
                "test" or "testunit" or "unit" => "Test",
                _ => string.Empty
            };
        }

        private string GetDescription(string scriptName)
        {
            var normalizedName = scriptName.ToLowerInvariant();
            return normalizedName switch
            {
                "dev" or "start" or "serve" or "develop" => "Best choice for launching the app locally.",
                "build" or "dist" or "prod" or "preview" => "Creates a production-ready build or preview.",
                "test" or "testunit" or "unit" => "Runs the project's automated test workflow.",
                _ => string.Empty
            };
        }

        private string GetKindGlyph(string scriptName)
        {
            var normalizedName = scriptName.ToLowerInvariant();
            return normalizedName switch
            {
                "dev" or "start" or "serve" or "develop" => "DEV",
                "build" or "dist" or "prod" or "preview" => "BLD",
                "test" or "testunit" or "unit" => "TST",
                _ => "CMD"
            };
        }

        /// <summary>
        /// Handles clicking on a script item
        /// </summary>
        private async void ScriptItem_Click(object sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is NpmScript script && _currentProjectPath != null)
            {
                LoadingPanel.IsVisible = true;
                ScriptsList.IsVisible = false;

                var installSuccess = await ProjectRunner.RunNpmInstallAsync(_currentProjectPath);

                LoadingPanel.IsVisible = false;

                if (!installSuccess)
                {
                    var box = MsBox.Avalonia.MessageBoxManager.GetMessageBoxStandard(
                        "Error", "Failed to run npm install. Please check your Node.js installation.",
                        MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
                    await box.ShowAsync();
                    Hide();
                    return;
                }

                ScriptSelected?.Invoke(this, new ScriptRunEventArgs
                {
                    ProjectPath = _currentProjectPath,
                    ScriptName = script.Name,
                    Command = script.Command
                });

                Hide();
            }
        }

        /// <summary>
        /// Handles clicking the close button
        /// </summary>
        private void CloseButton_Click(object sender, PointerPressedEventArgs e)
        {
            Closed?.Invoke(this, EventArgs.Empty);
            Hide();
        }
    }
}

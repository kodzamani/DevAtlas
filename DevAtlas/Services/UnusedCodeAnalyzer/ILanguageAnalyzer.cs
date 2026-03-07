using DevAtlas.Models;

namespace DevAtlas.Services.UnusedCodeAnalyzer;

public interface ILanguageAnalyzer
{
    string[] SupportedExtensions { get; }
    string LanguageName { get; }
    List<UnusedCodeResult> Analyze(string projectPath);
}
using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace GitHubDemo.Core;

// Syntax highlighting. Each language has a colour file the user can change in
// Settings (in the data folder); the app ships the defaults in Assets\codeStyles.
public static class CodeStyles
{
    public static readonly string[] Languages = { "C++", "C#", "Python", "Java", "JavaScript" };

    private static readonly Dictionary<string, IHighlightingDefinition?> Cache = new();

    public static event Action? Changed;

    public static string? LanguageForFile(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".cpp" or ".cc" or ".cxx" or ".h" or ".hpp" or ".c" => "C++",
        ".cs" => "C#",
        ".py" or ".pyw" => "Python",
        ".java" => "Java",
        ".js" or ".mjs" or ".ts" => "JavaScript",
        _ => null,
    };

    // Language names as used for projects ("cpp", "py"...), with a default file extension.
    public static readonly (string Key, string Label, string Extension)[] ProjectLanguages =
    {
        ("cpp", "C++", ".cpp"), ("cs", "C#", ".cs"), ("py", "Python", ".py"), ("java", "Java", ".java"), ("js", "JavaScript", ".js"), ("other", "Other", ".txt"),
    };

    public static string ProjectLanguageLabel(string key) =>
        ProjectLanguages.FirstOrDefault(l => l.Key == key).Label ?? (string.IsNullOrEmpty(key) ? "Other" : key);

    public static string DefaultExtension(string key) =>
        ProjectLanguages.FirstOrDefault(l => l.Key == key).Extension ?? ".txt";

    // The bundled file names differ from the language names.
    private static string BundledName(string language) => language switch
    {
        "C++" => "CPP-Mode.xshd",
        "C#" => "CS-Mode.xshd",
        "Python" => "PY-Mode.xshd",
        "Java" => "JAVA-Mode.xshd",
        _ => "JavaScript-Mode.xshd",
    };

    // The user's (editable) colour file; created from the bundled one on first use.
    public static string UserFile(string language)
    {
        string path = Path.Combine(AppSettings.CodeStylesDir, $"{language}-Mode.xshd");
        if (!File.Exists(path))
        {
            string bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "codeStyles", BundledName(language));
            if (File.Exists(bundled))
            {
                Directory.CreateDirectory(AppSettings.CodeStylesDir);
                File.Copy(bundled, path);
            }
        }
        return path;
    }

    public static IHighlightingDefinition? ForFile(string fileName)
    {
        string? language = LanguageForFile(fileName);
        if (language == null) return HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(fileName));
        return ForLanguage(language) ?? HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(fileName));
    }

    public static IHighlightingDefinition? ForLanguage(string language)
    {
        if (Cache.TryGetValue(language, out var cached)) return cached;
        IHighlightingDefinition? definition = null;
        try
        {
            string path = UserFile(language);
            if (File.Exists(path))
            {
                using var reader = new XmlTextReader(path);
                definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
        }
        catch (Exception)
        {
            // a broken colour file shouldn't stop files from opening
        }
        Cache[language] = definition;
        return definition;
    }

    // Code blocks in chat: ```cpp, ```python...
    public static IHighlightingDefinition? ForFence(string tag) => tag.ToLowerInvariant() switch
    {
        "cpp" or "c++" or "c" or "h" => ForLanguage("C++"),
        "cs" or "csharp" or "c#" => ForLanguage("C#"),
        "py" or "python" => ForLanguage("Python"),
        "java" => ForLanguage("Java"),
        "js" or "javascript" or "ts" or "typescript" => ForLanguage("JavaScript"),
        _ => null,
    };

    // A colour file was saved: editors reload their highlighting.
    public static void Invalidate()
    {
        Cache.Clear();
        Changed?.Invoke();
    }
}

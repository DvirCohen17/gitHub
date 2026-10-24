using System;
using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace client_side
{
    // A window opened from the home page (create project, messages, settings).
    // The home page keeps the only socket listener and forwards replies here,
    // so closing one of these windows can never leave a reader behind that
    // swallows the next message.
    public interface IServerMessageHandler
    {
        // Return true if the message was for this window.
        bool HandleServerMessage(string update);
    }

    // Syntax highlighting: the user's customised colours (Settings > Editor colours)
    // when present, otherwise AvalonEdit's built-in definition.
    public static class SyntaxStyles
    {
        public static readonly string[] Languages = { "C++", "C#", "Python", "Java", "JavaScript" };

        public static string LanguageForExtension(string extension) => extension.ToLowerInvariant() switch
        {
            ".cpp" or ".cc" or ".h" or ".hpp" or ".c" => "C++",
            ".cs" => "C#",
            ".py" => "Python",
            ".java" => "Java",
            ".js" => "JavaScript",
            _ => null,
        };

        public static string StyleFile(string codeStylesDir, string language) =>
            Path.Combine(codeStylesDir, $"{language}-Mode.xshd");

        public static IHighlightingDefinition ForFile(string fileName, string codeStylesDir)
        {
            string extension = Path.GetExtension(fileName);
            string language = LanguageForExtension(extension);
            if (language != null)
            {
                string path = StyleFile(codeStylesDir, language);
                try
                {
                    if (File.Exists(path))
                    {
                        using var reader = new XmlTextReader(path);
                        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    }
                }
                catch (Exception)
                {
                    // a broken custom file shouldn't stop the file from opening
                }
            }
            return HighlightingManager.Instance.GetDefinitionByExtension(extension);
        }
    }
}

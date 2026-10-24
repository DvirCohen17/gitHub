using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GitHubDemo.Core;

// The user's settings, in %LOCALAPPDATA%\gitHubDemo\settings.json (or the folder
// named by GITHUBDEMO_DATA_DIR). Version 1 kept settings under C:\githubDemo;
// those are read once, never changed.
public sealed class AppSettings
{
    public string ServerHost { get; set; } = "";
    public int ServerPort { get; set; } = 12345;
    public string Theme { get; set; } = "Dark";
    public string RememberedUser { get; set; } = "";
    public string RememberedSecret { get; set; } = ""; // DPAPI-encrypted password hash
    public bool Notifications { get; set; } = true;
    public bool NotifyChat { get; set; } = true;
    public double EditorFontSize { get; set; } = 14;
    public bool EditorWordWrap { get; set; }
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = double.NaN;
    public double WindowHeight { get; set; } = double.NaN;
    public bool WindowMaximized { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string DataDir { get; } = ResolveDataDir();
    public static string FilePath => Path.Combine(DataDir, "settings.json");
    public static string CodeStylesDir => Path.Combine(DataDir, "codeStyles");
    public static string AvatarCacheDir => Path.Combine(DataDir, "avatars");

    private const string LegacyDir = @"C:\githubDemo";

    private static string ResolveDataDir()
    {
        string? overridden = Environment.GetEnvironmentVariable("GITHUBDEMO_DATA_DIR");
        string dir = !string.IsNullOrWhiteSpace(overridden)
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gitHubDemo");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception)
        {
            // unreadable: start over with defaults
        }
        var settings = new AppSettings();
        settings.ImportLegacy();
        settings.Save();
        return settings;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception)
        {
            // settings are a convenience; never crash over them
        }
    }

    // First start: take the server address, theme and editor colours from a
    // version 1 install (read only).
    private void ImportLegacy()
    {
        if (Environment.GetEnvironmentVariable("GITHUBDEMO_NO_LEGACY") == "1") return; // tests
        foreach (string configPath in new[] { Path.Combine(AppContext.BaseDirectory, "config.txt"), Path.Combine(LegacyDir, "data", "config.txt") })
        {
            if (TryReadServerConfig(configPath, out string host, out int port))
            {
                ServerHost = host;
                ServerPort = port;
                break;
            }
        }
        try
        {
            string legacySettings = Path.Combine(LegacyDir, "settings", "appsettings.json");
            if (File.Exists(legacySettings))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(legacySettings));
                if (doc.RootElement.TryGetProperty("Theme", out var theme) && theme.GetString() is { Length: > 0 } name
                    && ThemeManager.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    Theme = ThemeManager.Names.First(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                }
            }
        }
        catch (Exception) { }
        try
        {
            string legacyStyles = Path.Combine(LegacyDir, "data", "codeStyles");
            if (Directory.Exists(legacyStyles) && !Directory.Exists(CodeStylesDir))
            {
                Directory.CreateDirectory(CodeStylesDir);
                foreach (string file in Directory.GetFiles(legacyStyles, "*.xshd"))
                    File.Copy(file, Path.Combine(CodeStylesDir, Path.GetFileName(file)), overwrite: false);
            }
        }
        catch (Exception) { }
    }

    // "host,port" (what the installer writes).
    public static bool TryReadServerConfig(string path, out string host, out int port)
    {
        host = "";
        port = 0;
        try
        {
            if (!File.Exists(path)) return false;
            string[] parts = File.ReadAllText(path).Trim().Split(',');
            if (parts.Length == 2 && parts[0].Trim().Length > 0 && int.TryParse(parts[1].Trim(), out port) && port is > 0 and < 65536)
            {
                host = parts[0].Trim();
                return true;
            }
        }
        catch (Exception) { }
        return false;
    }
}

public static class Secrets
{
    // The client never sends the password itself, only SHA-256 of it.
    public static string PasswordHash(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();

    // "Remember me" keeps the password hash, which is as sensitive as the password:
    // encrypt it so only this Windows user on this machine can read it back.
    public static string Protect(string secret) => string.IsNullOrEmpty(secret)
        ? ""
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception)
        {
            return "";
        }
    }
}

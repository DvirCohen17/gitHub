using System;
using System.IO;

namespace client_side
{
    // Server address stored in config.txt as "IP,PORT".
    // Looked up next to the executable first, then in the installer's data folder.
    public class ServerConfig
    {
        public const string InstalledConfigPath = @"C:\githubDemo\data\config.txt";

        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 12345;

        private static string LocalConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");

        // Returns null when there is no usable config file.
        public static ServerConfig? Load()
        {
            foreach (string path in new[] { LocalConfigPath, InstalledConfigPath })
            {
                try
                {
                    if (File.Exists(path) && TryParse(File.ReadAllText(path), out ServerConfig config))
                    {
                        return config;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return null;
        }

        public static bool TryParse(string text, out ServerConfig config)
        {
            config = new ServerConfig();
            string[] parts = text.Trim().Split(',');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                return false;
            }
            if (!int.TryParse(parts[1].Trim(), out int port) || port < 1 || port > 65535)
            {
                return false;
            }
            config.Host = parts[0].Trim();
            config.Port = port;
            return true;
        }

        public void Save()
        {
            string path = File.Exists(LocalConfigPath) || !Directory.Exists(Path.GetDirectoryName(InstalledConfigPath))
                ? LocalConfigPath
                : InstalledConfigPath;
            File.WriteAllText(path, $"{Host},{Port}");
        }

        public override string ToString() => $"{Host}:{Port}";
    }
}

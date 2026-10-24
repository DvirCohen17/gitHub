using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;

namespace GitHubDemo.Core;

// Downloads the client package the server offers (its "500" download) and starts
// it: an .msix opens the Windows app installer, which updates gitHub Demo in place.
public static class Updater
{
    public static async Task DownloadAndInstallAsync(string host, int port, Action<double> progress)
    {
        string folder = Path.Combine(Path.GetTempPath(), "gitHubDemo-update");
        Directory.CreateDirectory(folder);
        string file;

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(host, port);
            var stream = client.GetStream();
            byte[] request = Encoding.ASCII.GetBytes("#00000003500");
            await stream.WriteAsync(request);

            byte[] header = await ReadExactlyAsync(stream, 9);
            int length = int.Parse(Encoding.ASCII.GetString(header, 1, 8));
            string reply = Encoding.UTF8.GetString(await ReadExactlyAsync(stream, length));
            if (reply.StartsWith("200")) throw new InvalidOperationException(reply.Substring(3));
            if (!reply.StartsWith("500") || !long.TryParse(reply.Substring(3), out long size))
                throw new InvalidOperationException("The server sent an unexpected reply.");

            string tempFile = Path.Combine(folder, "download.tmp");
            await using (var output = File.Create(tempFile))
            {
                byte[] buffer = new byte[81920];
                long received = 0;
                while (received < size)
                {
                    int got = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, size - received)));
                    if (got == 0) throw new IOException("The download was interrupted.");
                    await output.WriteAsync(buffer.AsMemory(0, got));
                    received += got;
                    progress((double)received / size);
                }
            }
            file = Path.Combine(folder, LooksLikeMsix(tempFile) ? "gitHubDemo.msix" : "gitHubDemo.zip");
            File.Move(tempFile, file, overwrite: true);
        }

        if (file.EndsWith(".msix"))
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        else
        {
            // An older-style package: show it so it can be installed by hand.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        }
    }

    // An MSIX package is a zip with AppxManifest.xml at the top.
    private static bool LooksLikeMsix(string file)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(file);
            return zip.GetEntry("AppxManifest.xml") != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
    {
        byte[] buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int got = await stream.ReadAsync(buffer.AsMemory(read, count - read));
            if (got == 0) throw new IOException("The server closed the connection.");
            read += got;
        }
        return buffer;
    }
}

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Voxel.Install;

namespace Voxel;

/// <summary>A newer release on GitHub: its installer, and the size and hash that installer must have.</summary>
public sealed record UpdateInfo(string Version, string Url, string Sha256, long Size);

/// <summary>
/// One-click updates. On start the installed app asks GitHub for the latest release (nothing is
/// sent but the request itself). The Update button downloads that release's installer, checks it
/// against the hash GitHub reports and runs it with --update.
/// </summary>
public static class Updater
{
    private const string LatestRelease = "https://api.github.com/repos/iamjorj/voxel/releases/latest";
    private const string DownloadPrefix = "https://github.com/iamjorj/voxel/releases/download/";
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voxel", "update");
    private static readonly HttpClient Http = CreateClient();

    private static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Only the installed copy updates itself: a build run from anywhere else has nothing to replace.</summary>
    public static bool CanUpdate => string.Equals(
        Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), Installation.InstallDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>The newer release, or null if there is none. Being offline or GitHub failing is not worth bothering anyone about.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        RemoveOldDownloads();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await Http.GetAsync(LatestRelease, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(timeout.Token));
            return Parse(json.RootElement);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException
                                      or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static UpdateInfo? Parse(JsonElement release)
    {
        var tag = release.GetProperty("tag_name").GetString()?.TrimStart('v');
        if (!Version.TryParse(tag, out var latest) || latest <= Trimmed(Current)) return null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            if (name.StartsWith("VoxelSetup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && url.StartsWith(DownloadPrefix, StringComparison.Ordinal) && digest.StartsWith("sha256:", StringComparison.Ordinal))
                return new UpdateInfo(tag!, url, digest["sha256:".Length..], asset.GetProperty("size").GetInt64());
        }
        return null;
    }

    // Assembly versions have four parts (0.1.1.0) and release tags three (0.1.1); 0.1.1.0 would count as newer than 0.1.1.
    private static Version Trimmed(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Downloads the installer, refuses it unless size and hash match, and starts it. The installer closes this app.</summary>
    public static async Task DownloadAndRunAsync(UpdateInfo update, IProgress<double> progress)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, $"VoxelSetup-{update.Version}.exe");
        using var response = await Http.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var source = await response.Content.ReadAsStreamAsync())
        await using (var file = File.Create(path))
        {
            using var stalled = new CancellationTokenSource();
            var buffer = new byte[1 << 20];
            long done = 0;
            while (true)
            {
                stalled.CancelAfter(TimeSpan.FromSeconds(60)); // a dead connection would otherwise hang here for good
                var n = await source.ReadAsync(buffer, stalled.Token);
                if (n == 0) break;
                hash.AppendData(buffer, 0, n);
                await file.WriteAsync(buffer.AsMemory(0, n), stalled.Token);
                done += n;
                progress.Report((double)done / update.Size);
            }
            if (done != update.Size) throw new InvalidDataException("The download was incomplete.");
        }
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidDataException("The download was damaged.");
        }

        // Not the app's own working directory: that's the folder the installer is about to replace.
        Process.Start(new ProcessStartInfo(path, "--update") { UseShellExecute = true, WorkingDirectory = Folder });
    }

    private static void RemoveOldDownloads()
    {
        try { if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // next start, then
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Voxel-updater"); // GitHub's API refuses requests without one
        return client;
    }
}

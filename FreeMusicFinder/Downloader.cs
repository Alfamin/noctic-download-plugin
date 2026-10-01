using System.Text.Json;

namespace FreeMusicFinder;

internal enum DownloadOutcome { Saved, AlreadyThere }

/// <summary>Saves tracks into the download folder.</summary>
internal static class Downloader
{
    // A track only learns its extension while it is downloaded, so all of these are checked for an earlier copy.
    private static readonly string[] AudioExtensions = { "flac", "mp3", "m4a", "ogg", "opus", "wav" };

    /// <summary>
    /// Where tracks go when no download folder is configured: inside the first Noctis library
    /// folder, where the app's folder watcher adds a file to the library as soon as it is
    /// saved. Without a library folder, under the user's Music folder.
    /// </summary>
    public static string DefaultFolder(string pluginDataDirectory)
    {
        var parent = FirstLibraryFolder(pluginDataDirectory);
        if (parent is null)
        {
            parent = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrEmpty(parent)) parent = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        return Path.Combine(parent, "Noctis Free Music");
    }

    /// <summary>
    /// The plugin API does not say where the library folders are, so this reads them from
    /// Noctis's own settings file, which sits two levels above the plugin's data folder.
    /// </summary>
    private static string? FirstLibraryFolder(string pluginDataDirectory)
    {
        try
        {
            var settings = Path.Combine(pluginDataDirectory, "..", "..", "settings.json");
            if (!File.Exists(settings)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(settings));
            if (!document.RootElement.TryGetProperty("musicFolders", out var folders) || folders.ValueKind != JsonValueKind.Array) return null;
            return folders.EnumerateArray()
                .Where(f => f.ValueKind == JsonValueKind.String)
                .Select(f => f.GetString())
                .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The file the track was saved as, or null when it is not in the folder.</summary>
    public static string? FindExisting(string folder, BotTrack track)
        => AudioExtensions.Select(e => Stem(folder, track) + "." + e).FirstOrDefault(File.Exists);

    public static async Task<DownloadOutcome> DownloadAsync(BotTrack track, string folder, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        if (FindExisting(folder, track) is not null) return DownloadOutcome.AlreadyThere;

        var partial = Stem(folder, track) + ".part";
        try
        {
            string extension;
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                extension = await track.Fetch(output, progress, ct).ConfigureAwait(false);
            File.Move(partial, Stem(folder, track) + "." + extension, overwrite: true);
        }
        catch
        {
            try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        return DownloadOutcome.Saved;
    }

    private static string Stem(string folder, BotTrack track)
        => Path.Combine(folder, Sanitize($"{track.Artist} - {track.Title}"));

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        // The Windows set is used everywhere so a library moved between systems keeps working.
        var cleaned = new string(name.Select(c => invalid.Contains(c) || "<>:\"/\\|?*".Contains(c) || char.IsControl(c) ? '_' : c).ToArray())
            .Trim().TrimEnd('.');
        if (cleaned.Length > 150) cleaned = cleaned[..150].TrimEnd();
        return cleaned.Length == 0 ? "track" : cleaned;
    }
}

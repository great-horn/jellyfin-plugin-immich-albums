using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ImmichAlbums.Api;
using Jellyfin.Plugin.ImmichAlbums.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ImmichAlbums.Tasks;

public class SyncAlbumsTask : IScheduledTask
{
    private static readonly HashSet<string> HeicExtensions = new(StringComparer.OrdinalIgnoreCase) { ".heic", ".heif" };

    private readonly ILogger<SyncAlbumsTask> _logger;

    public string Name => "Sync Immich Albums";
    public string Key => "ImmichAlbumsSync";
    public string Description => "Sync Immich albums by creating symlinks (+ HEIC to JPEG conversion with auto-orient)";
    public string Category => "Immich Albums";

    public SyncAlbumsTask(ILogger<SyncAlbumsTask> logger)
    {
        _logger = logger;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        int hours = Plugin.Instance?.Configuration.SyncIntervalHours ?? 6;
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(hours).Ticks
            }
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
        {
            _logger.LogError("Plugin configuration is null");
            return;
        }
        if (string.IsNullOrEmpty(config.ImmichApiToken))
        {
            _logger.LogError("Immich API token not configured");
            return;
        }

        string syncFolder = config.SyncFolderPath;
        if (string.IsNullOrEmpty(syncFolder))
        {
            _logger.LogError("Sync folder path not configured");
            return;
        }

        Directory.CreateDirectory(syncFolder);

        using var client = new ImmichApiClient(config.ImmichApiUrl, config.ImmichApiToken, _logger);
        if (!await client.TestConnectionAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogError("Cannot connect to Immich API at {Url}", config.ImmichApiUrl);
            return;
        }

        progress.Report(5.0);

        var albums = await client.GetAlbumsAsync(config.IncludeSharedAlbums, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Found {Count} albums to sync", albums.Count);
        progress.Report(10.0);

        var syncedAlbumDirs = new HashSet<string>(StringComparer.Ordinal);
        int totalLinks = 0;
        int totalConverted = 0;
        int totalRotated = 0;
        int skippedFiles = 0;
        int errorFiles = 0;

        for (int i = 0; i < albums.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var album = albums[i];

            var detail = await client.GetAlbumDetailAsync(album.Id, cancellationToken).ConfigureAwait(false);
            if (detail == null || detail.Assets.Count == 0)
            {
                _logger.LogDebug("Skipping empty album: {Name}", album.AlbumName);
                continue;
            }

            string folderName = SanitizeFolderName(detail.AlbumName);
            if (string.IsNullOrWhiteSpace(folderName))
                folderName = "album-" + detail.Id[..8];

            string albumDir = Path.Combine(syncFolder, folderName);
            Directory.CreateDirectory(albumDir);
            syncedAlbumDirs.Add(folderName);

            var existingFiles = new HashSet<string>(
                Directory.Exists(albumDir)
                    ? Directory.GetFiles(albumDir).Select(Path.GetFileName).Where(n => n != null).Cast<string>()
                    : Enumerable.Empty<string>(),
                StringComparer.Ordinal);

            var currentFiles = new HashSet<string>(StringComparer.Ordinal);

            foreach (var asset in detail.Assets)
            {
                string hostPath = ConvertToHostPath(asset.OriginalPath, config);
                if (string.IsNullOrEmpty(hostPath))
                {
                    _logger.LogWarning("Cannot map path for asset {Id}: {Path}", asset.Id, asset.OriginalPath);
                    errorFiles++;
                    continue;
                }
                if (!File.Exists(hostPath))
                {
                    _logger.LogWarning("Source file not found: {Path}", hostPath);
                    errorFiles++;
                    continue;
                }

                string fileName = asset.OriginalFileName;
                if (string.IsNullOrEmpty(fileName))
                    fileName = Path.GetFileName(hostPath);

                string extension = Path.GetExtension(fileName);
                bool isHeic = HeicExtensions.Contains(extension);

                if (isHeic)
                    fileName = Path.GetFileNameWithoutExtension(fileName) + ".jpg";

                if (currentFiles.Contains(fileName))
                    fileName = Path.GetFileNameWithoutExtension(fileName) + "_" + asset.Id[..8] + Path.GetExtension(fileName);

                currentFiles.Add(fileName);
                string destPath = Path.Combine(albumDir, fileName);

                if (isHeic)
                {
                    if (File.Exists(destPath))
                    {
                        var destInfo = new FileInfo(destPath);
                        var srcInfo = new FileInfo(hostPath);
                        if (destInfo.LastWriteTimeUtc >= srcInfo.LastWriteTimeUtc)
                        {
                            skippedFiles++;
                            existingFiles.Remove(fileName);
                            continue;
                        }
                    }

                    try
                    {
                        var (converted, rotated) = await ConvertHeicToJpeg(hostPath, destPath, cancellationToken).ConfigureAwait(false);
                        if (converted)
                        {
                            totalConverted++;
                            if (rotated) totalRotated++;
                        }
                        else
                        {
                            _logger.LogWarning("HEIC conversion failed for: {Path}", hostPath);
                            errorFiles++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "HEIC conversion error: {Path}", hostPath);
                        errorFiles++;
                    }
                }
                else
                {
                    if (File.Exists(destPath))
                    {
                        try
                        {
                            if (new FileInfo(destPath).LinkTarget == hostPath)
                            {
                                skippedFiles++;
                                existingFiles.Remove(fileName);
                                continue;
                            }
                            File.Delete(destPath);
                        }
                        catch
                        {
                            try { File.Delete(destPath); } catch { }
                        }
                    }

                    try
                    {
                        File.CreateSymbolicLink(destPath, hostPath);
                        totalLinks++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create symlink: {Link} -> {Target}", destPath, hostPath);
                        errorFiles++;
                    }
                }

                existingFiles.Remove(fileName);
            }

            foreach (string orphan in existingFiles)
            {
                string orphanPath = Path.Combine(albumDir, orphan);
                try
                {
                    File.Delete(orphanPath);
                    _logger.LogDebug("Removed orphan: {Path}", orphanPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove orphan: {Path}", orphanPath);
                }
            }

            double value = 10.0 + 80.0 * (i + 1) / albums.Count;
            progress.Report(value);
        }

        if (Directory.Exists(syncFolder))
        {
            foreach (string dir in Directory.GetDirectories(syncFolder))
            {
                string dirName = Path.GetFileName(dir);
                if (!syncedAlbumDirs.Contains(dirName))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                        _logger.LogInformation("Removed orphan album directory: {Dir}", dirName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to remove orphan directory: {Dir}", dirName);
                    }
                }
            }
        }

        progress.Report(100.0);
        _logger.LogInformation(
            "Sync complete: {Albums} albums, {Links} symlinks, {Converted} HEIC→JPEG ({Rotated} auto-rotated), {Skipped} unchanged, {Errors} errors",
            syncedAlbumDirs.Count, totalLinks, totalConverted, totalRotated, skippedFiles, errorFiles);
    }

    /// <summary>
    /// Convertit HEIC → JPEG avec sips et applique l'orientation EXIF physiquement.
    /// Retourne (converted, rotated) : true si converti, true si rotation appliquée.
    /// </summary>
    private async Task<(bool converted, bool rotated)> ConvertHeicToJpeg(string sourcePath, string destPath, CancellationToken cancellationToken)
    {
        // Étape 1 : Convertir HEIC → JPEG avec sips
        var convertInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/sips",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        convertInfo.ArgumentList.Add("-s");
        convertInfo.ArgumentList.Add("format");
        convertInfo.ArgumentList.Add("jpeg");
        convertInfo.ArgumentList.Add("-s");
        convertInfo.ArgumentList.Add("formatOptions");
        convertInfo.ArgumentList.Add("85");
        convertInfo.ArgumentList.Add(sourcePath);
        convertInfo.ArgumentList.Add("--out");
        convertInfo.ArgumentList.Add(destPath);

        using (var convertProcess = Process.Start(convertInfo))
        {
            if (convertProcess == null) return (false, false);
            try
            {
                await convertProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (convertProcess.ExitCode != 0) return (false, false);
            }
            catch (OperationCanceledException)
            {
                convertProcess.Kill();
                throw;
            }
        }

        // Étape 2 : Lire l'orientation EXIF du JPEG converti
        int orientation = await GetExifOrientation(destPath, cancellationToken).ConfigureAwait(false);

        // Orientation 1 = normal, pas besoin de rotation
        if (orientation <= 1) return (true, false);

        // Étape 3 : Appliquer la rotation physique selon l'orientation EXIF
        int rotationDegrees = orientation switch
        {
            3 => 180,   // Upside down
            6 => 90,    // Rotated 90° CW (portrait, cas le plus courant)
            8 => 270,   // Rotated 90° CCW
            _ => 0      // 2,4,5,7 = flips (rares), on ne gère que les rotations
        };

        if (rotationDegrees > 0)
        {
            // Appliquer la rotation physique
            var rotateInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/sips",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            rotateInfo.ArgumentList.Add("-r");
            rotateInfo.ArgumentList.Add(rotationDegrees.ToString());
            rotateInfo.ArgumentList.Add(destPath);

            using var rotateProcess = Process.Start(rotateInfo);
            if (rotateProcess != null)
            {
                try
                {
                    await rotateProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    rotateProcess.Kill();
                    throw;
                }
            }
        }

        // Étape 4 : Reset l'orientation EXIF à 1 (normal) pour éviter une double rotation
        var resetInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/sips",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        resetInfo.ArgumentList.Add("-s");
        resetInfo.ArgumentList.Add("orientation");
        resetInfo.ArgumentList.Add("1");
        resetInfo.ArgumentList.Add(destPath);

        using (var resetProcess = Process.Start(resetInfo))
        {
            if (resetProcess != null)
            {
                try
                {
                    await resetProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    resetProcess.Kill();
                    throw;
                }
            }
        }

        return (true, rotationDegrees > 0);
    }

    /// <summary>
    /// Lit l'orientation EXIF via sips -g orientation.
    /// Retourne 1 (normal) si pas de tag ou erreur.
    /// </summary>
    private static async Task<int> GetExifOrientation(string filePath, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = "/usr/bin/sips",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("-g");
        info.ArgumentList.Add("orientation");
        info.ArgumentList.Add(filePath);

        using var process = Process.Start(info);
        if (process == null) return 1;

        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            throw;
        }

        // Output format: "  orientation: 6"
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("orientation:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out int orient))
                    return orient;
            }
        }

        return 1;
    }

    private static string ConvertToHostPath(string containerPath, PluginConfiguration config)
    {
        if (string.IsNullOrEmpty(containerPath))
            return string.Empty;

        if (!string.IsNullOrEmpty(config.ContainerUploadPath) &&
            containerPath.StartsWith(config.ContainerUploadPath, StringComparison.Ordinal))
        {
            return config.HostUploadPath + containerPath[config.ContainerUploadPath.Length..];
        }

        if (!string.IsNullOrEmpty(config.ContainerExternalPath) &&
            containerPath.StartsWith(config.ContainerExternalPath, StringComparison.Ordinal))
        {
            return config.HostExternalPath + containerPath[config.ContainerExternalPath.Length..];
        }

        return string.Empty;
    }

    private static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        string result = new(name.Where(c => !invalid.Contains(c)).ToArray());
        result = result.Trim('.', ' ');

        if (result.Length > 200)
            result = result[..200];

        return result;
    }
}

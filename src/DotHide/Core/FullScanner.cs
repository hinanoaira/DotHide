using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using DotHide.Config;
using DotHide.Logging;

namespace DotHide.Core;

public sealed class FullScanStatistics
{
    public int DirectoriesScanned { get; set; }
    public int FilesScanned { get; set; }
    public int HiddenApplied { get; set; }
    public int AlreadyHidden { get; set; }
    public int Errors { get; set; }
    public TimeSpan Elapsed { get; set; }

    public override string ToString()
    {
        return $"Scanned {DirectoriesScanned:N0} dirs and {FilesScanned:N0} files in {Elapsed.TotalSeconds:F2}s. Applied hidden: {HiddenApplied:N0}, already hidden: {AlreadyHidden:N0}, errors: {Errors:N0}";
    }
}

/// <summary>
/// Recursively scans target directories and applies FILE_ATTRIBUTE_HIDDEN to dot items.
/// Skips traversing inside excluded directories (e.g., .git, node_modules) while hiding the directory itself.
/// </summary>
public sealed class FullScanner
{
    private readonly AppConfig _config;
    private readonly Logger _logger;

    public FullScanner(AppConfig config, Logger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FullScanStatistics ScanDirectory(string rootPath, CancellationToken cancellationToken = default)
    {
        var stats = new FullScanStatistics();
        var sw = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            _logger.Warn($"Scan target directory does not exist or is inaccessible: {rootPath}");
            sw.Stop();
            stats.Elapsed = sw.Elapsed;
            return stats;
        }

        var excludeSet = _config.GetExcludeDirectoriesSet();
        var dirQueue = new Queue<string>();
        dirQueue.Enqueue(rootPath);

        _logger.Info($"Starting full scan of: {rootPath}");

        while (dirQueue.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.Info("Scan cancelled by user/shutdown.");
                break;
            }

            var currentDir = dirQueue.Dequeue();
            stats.DirectoriesScanned++;

            DirectoryInfo dirInfo;
            try
            {
                dirInfo = new DirectoryInfo(currentDir);
                if (!dirInfo.Exists) continue;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Could not inspect directory {currentDir}: {ex.Message}");
                stats.Errors++;
                continue;
            }

            // Enumerate items inside currentDir
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = dirInfo.EnumerateFileSystemInfos();
            }
            catch (UnauthorizedAccessException)
            {
                _logger.Debug($"Access denied reading directory: {currentDir}");
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error enumerating {currentDir}: {ex.Message}");
                stats.Errors++;
                continue;
            }

            foreach (var entry in entries)
            {
                if (cancellationToken.IsCancellationRequested) break;

                bool isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                var name = entry.Name;

                if (isDirectory)
                {
                    // Check if directory name starts with dot
                    if (FileHider.IsDotItem(name))
                    {
                        var res = FileHider.ApplyHiddenAttribute(
                            entry.FullName,
                            _config.RetryCount,
                            _config.RetryInitialDelayMs,
                            _config.RetryMaxDelayMs,
                            _logger);

                        UpdateStats(res, stats);
                    }

                    // Check if we should traverse INSIDE this directory
                    if (excludeSet.Contains(name))
                    {
                        // Performance optimization: skip recursing inside .git, node_modules, etc.
                        _logger.Debug($"Skipping traversal inside excluded directory: {entry.FullName}");
                        continue;
                    }

                    // Avoid infinite recursion on reparse points (symlinks/junctions)
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        _logger.Debug($"Skipping reparse point / junction: {entry.FullName}");
                        continue;
                    }

                    dirQueue.Enqueue(entry.FullName);
                }
                else
                {
                    stats.FilesScanned++;

                    if (FileHider.IsDotItem(name))
                    {
                        var res = FileHider.ApplyHiddenAttribute(
                            entry.FullName,
                            _config.RetryCount,
                            _config.RetryInitialDelayMs,
                            _config.RetryMaxDelayMs,
                            _logger);

                        UpdateStats(res, stats);
                    }
                }
            }
        }

        sw.Stop();
        stats.Elapsed = sw.Elapsed;
        _logger.Info($"Full scan finished for {rootPath}. {stats}");
        return stats;
    }

    private static void UpdateStats(HideResult res, FullScanStatistics stats)
    {
        switch (res)
        {
            case HideResult.Success:
                stats.HiddenApplied++;
                break;
            case HideResult.AlreadyHidden:
                stats.AlreadyHidden++;
                break;
            case HideResult.Failed:
                stats.Errors++;
                break;
        }
    }
}


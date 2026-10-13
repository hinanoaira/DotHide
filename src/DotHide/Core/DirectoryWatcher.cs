using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DotHide.Config;
using DotHide.Logging;

namespace DotHide.Core;

public enum ChangeEventType
{
    Created,
    Renamed
}

public sealed record FileChangeEvent(string Path, ChangeEventType Type, DateTime Timestamp, string RootPath);

/// <summary>
/// Real-time file system monitoring using FileSystemWatcher and an asynchronous event processing pipeline.
/// </summary>
public sealed class DirectoryWatcher : IDisposable
{
    private readonly AppConfig _config;
    private readonly Logger _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Channel<FileChangeEvent> _eventChannel;
    private readonly ConcurrentDictionary<string, long> _recentProcessed = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private Task? _consumerTask;
    private bool _isPaused;
    private readonly object _lock = new();

    public event Action<string, HideResult>? OnAttributeApplied;

    public DirectoryWatcher(AppConfig config, Logger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Unbounded channel with single reader for maximum throughput and zero watcher blocking
        _eventChannel = Channel.CreateUnbounded<FileChangeEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Start()
    {
        lock (_lock)
        {
            _consumerTask = Task.Run(ProcessQueueAsync);

            var watchPaths = _config.GetExpandedWatchPaths();
            foreach (var watchPath in watchPaths)
            {
                if (!Directory.Exists(watchPath))
                {
                    _logger.Warn($"Watch directory not found, skipping: {watchPath}");
                    continue;
                }

                try
                {
                    var watcher = new FileSystemWatcher(watchPath)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName |
                                      NotifyFilters.DirectoryName |
                                      NotifyFilters.CreationTime |
                                      NotifyFilters.LastWrite,
                        InternalBufferSize = 65536 // 64 KB buffer (maximum recommended for Windows)
                    };

                    var currentRoot = watchPath;
                    watcher.Created += (s, e) => EnqueueEvent(e.FullPath, ChangeEventType.Created, currentRoot);
                    watcher.Renamed += (s, e) => EnqueueEvent(e.FullPath, ChangeEventType.Renamed, currentRoot);
                    watcher.Error += (s, e) => OnWatcherError(e, currentRoot);

                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);

                    _logger.Info($"Real-time watcher active on: {watchPath}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to initialize FileSystemWatcher for {watchPath}", ex);
                }
            }
        }
    }

    private void EnqueueEvent(string fullPath, ChangeEventType type, string rootPath)
    {
        if (_isPaused) return;
        if (string.IsNullOrWhiteSpace(fullPath)) return;

        _eventChannel.Writer.TryWrite(new FileChangeEvent(fullPath, type, DateTime.UtcNow, rootPath));
    }

    private async Task ProcessQueueAsync()
    {
        var reader = _eventChannel.Reader;
        var excludeSet = _config.GetExcludeDirectoriesSet();

        try
        {
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                while (reader.TryRead(out var evt))
                {
                    try
                    {
                        ProcessSingleEvent(evt, excludeSet);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error processing event for {evt.Path}", ex);
                    }
                }

                // Periodically clean up debounce dictionary
                CleanupRecentDebounceCache();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private void ProcessSingleEvent(FileChangeEvent evt, HashSet<string> excludeSet)
    {
        var path = evt.Path;

        // Skip if inside an excluded directory (e.g. inside .git or node_modules)
        if (FileHider.IsInsideExcludedDirectory(path, excludeSet, evt.RootPath))
        {
            return;
        }

        // Debounce: ignore if we already handled this path within 300ms
        var nowTicks = Environment.TickCount64;
        if (_recentProcessed.TryGetValue(path, out var lastTick))
        {
            if (nowTicks - lastTick < 300)
            {
                return;
            }
        }
        _recentProcessed[path] = nowTicks;

        // Check if target itself starts with dot
        if (FileHider.IsDotItem(path))
        {
            var result = FileHider.ApplyHiddenAttribute(
                path,
                _config.RetryCount,
                _config.RetryInitialDelayMs,
                _config.RetryMaxDelayMs,
                _logger);

            OnAttributeApplied?.Invoke(path, result);
        }

        // If a new directory was created, and it is NOT excluded, check if it contains any dot items
        try
        {
            if (Directory.Exists(path))
            {
                var dirName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!excludeSet.Contains(dirName))
                {
                    // Fast scan of immediate directory entries
                    ScanNewDirectory(path, excludeSet);
                }
            }
        }
        catch
        {
            // Ignore non-directory or deleted path
        }
    }

    private void ScanNewDirectory(string dirPath, HashSet<string> excludeSet)
    {
        try
        {
            var entries = Directory.EnumerateFileSystemEntries(dirPath, ".*", SearchOption.TopDirectoryOnly);
            foreach (var entry in entries)
            {
                if (FileHider.IsDotItem(entry))
                {
                    FileHider.ApplyHiddenAttribute(
                        entry,
                        _config.RetryCount,
                        _config.RetryInitialDelayMs,
                        _config.RetryMaxDelayMs,
                        _logger);
                }
            }
        }
        catch
        {
            // Suppress errors on directory access
        }
    }

    private void CleanupRecentDebounceCache()
    {
        if (_recentProcessed.Count > 1000)
        {
            var nowTicks = Environment.TickCount64;
            foreach (var kvp in _recentProcessed)
            {
                if (nowTicks - kvp.Value > 5000)
                {
                    _recentProcessed.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    private void OnWatcherError(ErrorEventArgs e, string rootPath)
    {
        _logger.Warn($"FileSystemWatcher reported error for {rootPath}: {e.GetException()?.Message}. Scheduling resync...");

        // Run resync in background
        Task.Run(() =>
        {
            try
            {
                var scanner = new FullScanner(_config, _logger);
                scanner.ScanDirectory(rootPath, _cts.Token);
            }
            catch (Exception ex)
            {
                _logger.Error($"Resync failed for {rootPath}", ex);
            }
        });
    }

    public void Pause()
    {
        _isPaused = true;
        foreach (var watcher in _watchers)
        {
            try { watcher.EnableRaisingEvents = false; } catch { }
        }
        _logger.Info("Monitoring paused.");
    }

    public void Resume()
    {
        _isPaused = false;
        foreach (var watcher in _watchers)
        {
            try { watcher.EnableRaisingEvents = true; } catch { }
        }
        _logger.Info("Monitoring resumed.");
    }

    public bool IsPaused => _isPaused;

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts.Cancel();
        }
        catch { }

        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch { }
        }
        _watchers.Clear();

        try
        {
            _cts.Dispose();
        }
        catch { }
    }
}

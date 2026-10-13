using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DotHide.Config;

/// <summary>
/// Configuration model for DotHide.
/// Supports JSON serialization and environment variable expansion.
/// </summary>
public sealed class AppConfig
{
    [JsonPropertyName("watch_paths")]
    public List<string> WatchPaths { get; set; } = new() { "%USERPROFILE%" };

    [JsonPropertyName("exclude_directories")]
    public List<string> ExcludeDirectories { get; set; } = new()
    {
        ".git",
        "node_modules",
        ".vs",
        ".idea",
        "target",
        "bin",
        "obj",
        "$RECYCLE.BIN",
        "AppData"
    };

    [JsonPropertyName("scan_on_startup")]
    public bool ScanOnStartup { get; set; } = true;

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; set; } = 5;

    [JsonPropertyName("retry_initial_delay_ms")]
    public int RetryInitialDelayMs { get; set; } = 50;

    [JsonPropertyName("retry_max_delay_ms")]
    public int RetryMaxDelayMs { get; set; } = 1000;

    [JsonPropertyName("enable_tray_icon")]
    public bool EnableTrayIcon { get; set; } = true;

    [JsonPropertyName("log_level")]
    public string LogLevel { get; set; } = "Information";

    [JsonPropertyName("log_to_file")]
    public bool LogToFile { get; set; } = true;

    [JsonPropertyName("log_file_path")]
    public string LogFilePath { get; set; } = "%APPDATA%\\DotHide\\dothide.log";

    /// <summary>
    /// Expands environment variables in all WatchPaths and filters out empty entries.
    /// </summary>
    public List<string> GetExpandedWatchPaths()
    {
        var result = new List<string>();
        foreach (var path in WatchPaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            if (!string.IsNullOrWhiteSpace(expanded))
            {
                result.Add(System.IO.Path.GetFullPath(expanded));
            }
        }
        return result;
    }

    /// <summary>
    /// Returns the expanded absolute path to the log file.
    /// </summary>
    public string GetExpandedLogFilePath()
    {
        var expanded = Environment.ExpandEnvironmentVariables(LogFilePath);
        return System.IO.Path.GetFullPath(expanded);
    }

    /// <summary>
    /// Returns case-insensitive set of directory names to skip recursion inside.
    /// </summary>
    public HashSet<string> GetExcludeDirectoriesSet()
    {
        return new HashSet<string>(ExcludeDirectories, StringComparer.OrdinalIgnoreCase);
    }
}


using System;
using System.IO;

namespace DotHide.Logging;

public enum LogLevel
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3
}

/// <summary>
/// Thread-safe, resilient file and console logger.
/// Handles exceptions gracefully and limits file size to avoid disk fill.
/// </summary>
public sealed class Logger
{
    private readonly object _lock = new();
    private readonly string? _logFilePath;
    private readonly bool _logToFile;
    private readonly bool _logToConsole;
    private readonly LogLevel _minLevel;
    private const long MaxLogFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public event Action<LogLevel, string>? OnMessageLogged;

    public Logger(LogLevel minLevel = LogLevel.Information, string? logFilePath = null, bool logToFile = true, bool logToConsole = false)
    {
        _minLevel = minLevel;
        _logFilePath = logFilePath;
        _logToFile = logToFile && !string.IsNullOrWhiteSpace(logFilePath);
        _logToConsole = logToConsole;

        if (_logToFile && !string.IsNullOrWhiteSpace(_logFilePath))
        {
            try
            {
                var dir = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch
            {
                // Ignore directory creation failure here; will handle during write
            }
        }
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Information, message);
    public void Warn(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message} (Exception: {ex.GetType().Name}: {ex.Message})" : message;
        Log(LogLevel.Error, msg);
    }

    public void Log(LogLevel level, string message)
    {
        if (level < _minLevel) return;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelStr = level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "INFO "
        };

        var line = $"[{timestamp}] [{levelStr}] {message}";

        lock (_lock)
        {
            if (_logToConsole)
            {
                try
                {
                    var prevColor = Console.ForegroundColor;
                    Console.ForegroundColor = level switch
                    {
                        LogLevel.Debug => ConsoleColor.DarkGray,
                        LogLevel.Information => ConsoleColor.Gray,
                        LogLevel.Warning => ConsoleColor.Yellow,
                        LogLevel.Error => ConsoleColor.Red,
                        _ => ConsoleColor.White
                    };
                    Console.WriteLine(line);
                    Console.ForegroundColor = prevColor;
                }
                catch
                {
                    // Ignore console write errors (e.g. redirected or detached)
                }
            }

            if (_logToFile && !string.IsNullOrWhiteSpace(_logFilePath))
            {
                try
                {
                    RotateLogFileIfNeeded();
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                }
                catch
                {
                    // Avoid crashing on log file write failure
                }
            }
        }

        try
        {
            OnMessageLogged?.Invoke(level, message);
        }
        catch
        {
            // Ignore subscriber errors
        }
    }

    private void RotateLogFileIfNeeded()
    {
        if (string.IsNullOrWhiteSpace(_logFilePath)) return;

        var fi = new FileInfo(_logFilePath);
        if (fi.Exists && fi.Length > MaxLogFileSizeBytes)
        {
            var oldPath = _logFilePath + ".old";
            try
            {
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
                File.Move(_logFilePath, oldPath);
            }
            catch
            {
                // If move fails, try truncating
                try
                {
                    File.WriteAllText(_logFilePath, string.Empty);
                }
                catch
                {
                    // Ignore
                }
            }
        }
    }

    public static LogLevel ParseLogLevel(string? levelStr)
    {
        return levelStr?.Trim().ToLowerInvariant() switch
        {
            "debug" => LogLevel.Debug,
            "warn" or "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            _ => LogLevel.Information
        };
    }
}


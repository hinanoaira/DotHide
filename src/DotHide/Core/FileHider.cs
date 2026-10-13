using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using DotHide.Logging;
using DotHide.Native;

namespace DotHide.Core;

public enum HideResult
{
    Success,
    AlreadyHidden,
    SkippedNotDotItem,
    SkippedInsideExcludedDirectory,
    NotFound,
    Failed
}

/// <summary>
/// Handles checking and setting the Windows hidden attribute (FILE_ATTRIBUTE_HIDDEN)
/// with exponential backoff retry and robust Win32 error handling.
/// </summary>
public static class FileHider
{
    /// <summary>
    /// Checks if a file or directory name starts with a dot ('.') and is not '.' or '..'.
    /// </summary>
    public static bool IsDotItem(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var cleanPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(cleanPath);

        if (string.IsNullOrEmpty(name)) return false;
        if (name == "." || name == "..") return false;

        return name.StartsWith('.');
    }

    /// <summary>
    /// Checks whether the given path resides INSIDE any excluded directory (e.g. inside .git or node_modules).
    /// Note: The directory itself (e.g. ".git") is NOT inside itself, so this returns false for ".git" itself.
    /// </summary>
    public static bool IsInsideExcludedDirectory(string path, HashSet<string> excludeSet, string? rootWatchPath = null)
    {
        if (string.IsNullOrWhiteSpace(path) || excludeSet.Count == 0) return false;

        var cleanPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentDir = Path.GetDirectoryName(cleanPath);

        while (!string.IsNullOrEmpty(parentDir))
        {
            var dirName = Path.GetFileName(parentDir);
            if (excludeSet.Contains(dirName))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(rootWatchPath) &&
                string.Equals(parentDir, rootWatchPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var nextParent = Path.GetDirectoryName(parentDir);
            if (nextParent == parentDir) break; // Root drive reached
            parentDir = nextParent;
        }

        return false;
    }

    /// <summary>
    /// Applies the FILE_ATTRIBUTE_HIDDEN attribute to the target path with exponential backoff retry.
    /// </summary>
    public static HideResult ApplyHiddenAttribute(
        string path,
        int maxRetries = 5,
        int initialDelayMs = 50,
        int maxDelayMs = 1000,
        Logger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return HideResult.Failed;
        }

        int currentDelay = Math.Max(10, initialDelayMs);
        int attempts = Math.Max(1, maxRetries);

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            uint attrs = Win32Methods.GetFileAttributesW(path);

            if (attrs == Win32Methods.INVALID_FILE_ATTRIBUTES)
            {
                int err = Marshal.GetLastWin32Error();

                // If file was deleted or path disappeared, stop immediately
                if (err == Win32Methods.ERROR_FILE_NOT_FOUND || err == Win32Methods.ERROR_PATH_NOT_FOUND)
                {
                    logger?.Debug($"File/Path disappeared (ERROR_{err}): {path}");
                    return HideResult.NotFound;
                }

                // If locked or sharing violation, retry with backoff
                if (attempt < attempts && IsTransientWin32Error(err))
                {
                    logger?.Debug($"GetFileAttributes transient error {err} on attempt {attempt}/{attempts} for {path}. Retrying in {currentDelay}ms...");
                    Thread.Sleep(currentDelay);
                    currentDelay = Math.Min(currentDelay * 2, maxDelayMs);
                    continue;
                }

                logger?.Warn($"Failed to get attributes for {path} (Win32 error {err}: {Win32Methods.GetErrorMessage(err)})");
                return HideResult.Failed;
            }

            // Already hidden? No need to change
            if ((attrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0)
            {
                return HideResult.AlreadyHidden;
            }

            // Set hidden attribute
            uint newAttrs = attrs | Win32Methods.FILE_ATTRIBUTE_HIDDEN;
            if (Win32Methods.SetFileAttributesW(path, newAttrs))
            {
                logger?.Info($"[HIDDEN APPLIED] {path}");
                return HideResult.Success;
            }

            int setErr = Marshal.GetLastWin32Error();

            if (setErr == Win32Methods.ERROR_FILE_NOT_FOUND || setErr == Win32Methods.ERROR_PATH_NOT_FOUND)
            {
                logger?.Debug($"File disappeared during SetFileAttributes: {path}");
                return HideResult.NotFound;
            }

            if (attempt < attempts && IsTransientWin32Error(setErr))
            {
                logger?.Debug($"SetFileAttributes transient error {setErr} on attempt {attempt}/{attempts} for {path}. Retrying in {currentDelay}ms...");
                Thread.Sleep(currentDelay);
                currentDelay = Math.Min(currentDelay * 2, maxDelayMs);
                continue;
            }

            logger?.Warn($"Failed to set hidden attribute on {path} (Win32 error {setErr}: {Win32Methods.GetErrorMessage(setErr)})");
            return HideResult.Failed;
        }

        logger?.Warn($"Exhausted all {attempts} retries for {path}");
        return HideResult.Failed;
    }

    private static bool IsTransientWin32Error(int errorCode)
    {
        return errorCode == Win32Methods.ERROR_SHARING_VIOLATION ||
               errorCode == Win32Methods.ERROR_LOCK_VIOLATION ||
               errorCode == Win32Methods.ERROR_ACCESS_DENIED;
    }
}


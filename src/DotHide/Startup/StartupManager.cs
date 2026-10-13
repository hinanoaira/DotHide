using System;
using System.IO;
using Microsoft.Win32;

namespace DotHide.Startup;

public static class StartupManager
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "DotHide";

    /// <summary>
    /// Checks whether DotHide is registered in the Current User Startup registry key.
    /// </summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
            if (key == null) return false;

            var value = key.GetValue(AppName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers the application in the Current User Startup registry key.
    /// Does not require administrative privileges.
    /// </summary>
    public static bool EnableStartup(string? executablePath = null)
    {
        try
        {
            var exe = executablePath ?? Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DotHide.exe");
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (key == null) return false;

            key.SetValue(AppName, $"\"{Path.GetFullPath(exe)}\"");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Removes DotHide from the Current User Startup registry key.
    /// </summary>
    public static bool DisableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (key == null) return false;

            key.DeleteValue(AppName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets current startup command value if registered, or null.
    /// </summary>
    public static string? GetStartupCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
            return key?.GetValue(AppName) as string;
        }
        catch
        {
            return null;
        }
    }
}


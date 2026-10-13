using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotHide.Logging;

namespace DotHide.Config;

public static class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string GetDefaultAppDataConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "DotHide", "config.json");
    }

    public static string GetAppDirectoryConfigPath()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(appDir, "config.json");
    }

    /// <summary>
    /// Determines the config file path to use.
    /// Priority: explicit path > config.json in app directory > config.json in %APPDATA%\DotHide.
    /// </summary>
    public static string ResolveConfigPath(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(customPath));
        }

        var appDirConfig = GetAppDirectoryConfigPath();
        if (File.Exists(appDirConfig))
        {
            return appDirConfig;
        }

        return GetDefaultAppDataConfigPath();
    }

    /// <summary>
    /// Loads the configuration, or creates and returns the default configuration if not found.
    /// </summary>
    public static AppConfig LoadOrCreate(string? customPath = null, Logger? logger = null)
    {
        var configPath = ResolveConfigPath(customPath);

        try
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config != null)
                {
                    logger?.Info($"Loaded configuration from {configPath}");
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Warn($"Failed to load configuration from {configPath}: {ex.Message}. Falling back to default.");
        }

        // Create default configuration file
        var defaultConfig = new AppConfig();
        try
        {
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(defaultConfig, JsonOptions);
            File.WriteAllText(configPath, json);
            logger?.Info($"Created default configuration at {configPath}");
        }
        catch (Exception ex)
        {
            logger?.Warn($"Could not save default configuration to {configPath}: {ex.Message}");
        }

        return defaultConfig;
    }

    /// <summary>
    /// Saves the configuration to the specified path.
    /// </summary>
    public static void Save(AppConfig config, string? customPath = null)
    {
        var configPath = ResolveConfigPath(customPath);
        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(configPath, json);
    }
}


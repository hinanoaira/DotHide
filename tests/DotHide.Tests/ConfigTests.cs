using System;
using System.IO;
using DotHide.Config;
using Xunit;

namespace DotHide.Tests;

public class ConfigTests
{
    [Fact]
    public void AppConfig_ExpandsEnvironmentVariables()
    {
        var config = new AppConfig
        {
            WatchPaths = new() { "%USERPROFILE%" },
            LogFilePath = "%TEMP%\\dothide_test.log"
        };

        var expandedPaths = config.GetExpandedWatchPaths();
        Assert.Single(expandedPaths);
        Assert.DoesNotContain("%USERPROFILE%", expandedPaths[0]);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), expandedPaths[0], ignoreCase: true);

        var expandedLog = config.GetExpandedLogFilePath();
        Assert.DoesNotContain("%TEMP%", expandedLog);
    }

    [Fact]
    public void ConfigManager_LoadOrCreate_CreatesDefaultFileWhenMissing()
    {
        var tempConfigFile = Path.Combine(Path.GetTempPath(), "DotHide_ConfigTest_" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            Assert.False(File.Exists(tempConfigFile));

            var config = ConfigManager.LoadOrCreate(tempConfigFile);
            Assert.NotNull(config);
            Assert.True(File.Exists(tempConfigFile));

            // Verify content can be reloaded
            var reloaded = ConfigManager.LoadOrCreate(tempConfigFile);
            Assert.NotNull(reloaded);
            Assert.Equal(config.WatchPaths, reloaded.WatchPaths);
        }
        finally
        {
            if (File.Exists(tempConfigFile))
            {
                File.Delete(tempConfigFile);
            }
        }
    }
}


using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotHide.Config;
using DotHide.Core;
using DotHide.Logging;
using DotHide.Native;
using Xunit;

namespace DotHide.Tests;

public class DirectoryWatcherTests
{
    [Fact]
    public async Task Watcher_AutomaticallyHidesCreatedAndRenamedDotFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DotHide_WatcherTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var config = new AppConfig
        {
            WatchPaths = new() { tempDir },
            ExcludeDirectories = new() { ".git", "node_modules" },
            ScanOnStartup = false,
            RetryCount = 3,
            RetryInitialDelayMs = 20
        };

        var logger = new Logger(LogLevel.Debug, logToConsole: false);
        using var watcher = new DirectoryWatcher(config, logger);

        watcher.Start();

        try
        {
            // Give FileSystemWatcher a moment to initialize
            await Task.Delay(100);

            // Test 1: Create a dot file (.env)
            var createdDotFile = Path.Combine(tempDir, ".env");
            File.WriteAllText(createdDotFile, "DATABASE_URL=sqlite://test.db");

            // Wait for watcher to process
            bool isDotFileHidden = false;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(100);
                uint attrs = Win32Methods.GetFileAttributesW(createdDotFile);
                if (attrs != Win32Methods.INVALID_FILE_ATTRIBUTES && (attrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0)
                {
                    isDotFileHidden = true;
                    break;
                }
            }
            Assert.True(isDotFileHidden, "Newly created .env should have FILE_ATTRIBUTE_HIDDEN applied");

            // Test 2: Rename normal file (config.txt -> .config)
            var normalFile = Path.Combine(tempDir, "config.txt");
            var renamedDotFile = Path.Combine(tempDir, ".config");
            File.WriteAllText(normalFile, "settings");

            // Verify normal file is not hidden initially
            uint normalAttrs = Win32Methods.GetFileAttributesW(normalFile);
            Assert.False((normalAttrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0);

            // Rename it
            File.Move(normalFile, renamedDotFile);

            // Wait for watcher to process rename
            bool isRenamedHidden = false;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(100);
                uint attrs = Win32Methods.GetFileAttributesW(renamedDotFile);
                if (attrs != Win32Methods.INVALID_FILE_ATTRIBUTES && (attrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0)
                {
                    isRenamedHidden = true;
                    break;
                }
            }
            Assert.True(isRenamedHidden, "Renamed file .config should have FILE_ATTRIBUTE_HIDDEN applied");

            // Test 3: Normal file creation should NOT be hidden
            var untouchedFile = Path.Combine(tempDir, "hello.txt");
            File.WriteAllText(untouchedFile, "world");
            await Task.Delay(300);
            uint helloAttrs = Win32Methods.GetFileAttributesW(untouchedFile);
            Assert.False((helloAttrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0, "Normal file hello.txt should NOT be hidden");
        }
        finally
        {
            watcher.Dispose();
            await Task.Delay(100);
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}


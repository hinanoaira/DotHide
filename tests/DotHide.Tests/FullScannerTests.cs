using System;
using System.IO;
using DotHide.Config;
using DotHide.Core;
using DotHide.Logging;
using DotHide.Native;
using Xunit;

namespace DotHide.Tests;

public class FullScannerTests
{
    [Fact]
    public void FullScanner_HidesDotFilesAndFolders_AndSkipsInsideExcludedDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DotHide_ScanTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. Root dot file -> Should be hidden
            var rootDotFile = Path.Combine(tempDir, ".env");
            File.WriteAllText(rootDotFile, "SECRET=123");

            // 2. Root normal file -> Should NOT be hidden
            var rootNormalFile = Path.Combine(tempDir, "readme.md");
            File.WriteAllText(rootNormalFile, "hello");

            // 3. .git directory -> Directory itself should be hidden
            var gitDir = Path.Combine(tempDir, ".git");
            Directory.CreateDirectory(gitDir);

            // 4. File inside .git -> Recursion inside should be skipped, so this file is NOT hidden
            var gitHeadFile = Path.Combine(gitDir, "HEAD");
            File.WriteAllText(gitHeadFile, "ref: refs/heads/main");

            // 5. node_modules directory -> Not a dot directory, should NOT be hidden
            var nodeModulesDir = Path.Combine(tempDir, "node_modules");
            Directory.CreateDirectory(nodeModulesDir);

            // 6. Dot file inside node_modules -> Recursion inside should be skipped, so NOT hidden
            var nodeModulesDotFile = Path.Combine(nodeModulesDir, ".package-lock.json");
            File.WriteAllText(nodeModulesDotFile, "{}");

            // 7. Subfolder with dot file -> Subfolder normal, child dotfile hidden
            var subDir = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(subDir);
            var subDotFile = Path.Combine(subDir, ".prettierrc");
            File.WriteAllText(subDotFile, "{}");

            // Run scanner
            var config = new AppConfig
            {
                WatchPaths = new() { tempDir },
                ExcludeDirectories = new() { ".git", "node_modules" },
                RetryCount = 2,
                RetryInitialDelayMs = 20
            };
            var logger = new Logger(LogLevel.Debug, logToConsole: false);
            var scanner = new FullScanner(config, logger);

            var stats = scanner.ScanDirectory(tempDir);

            // Assertions
            Assert.True(stats.HiddenApplied >= 3); // .env, .git, .prettierrc

            // Check attributes
            uint envAttrs = Win32Methods.GetFileAttributesW(rootDotFile);
            Assert.True((envAttrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0, ".env should be hidden");

            uint gitAttrs = Win32Methods.GetFileAttributesW(gitDir);
            Assert.True((gitAttrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0, ".git folder itself should be hidden");

            uint prettierAttrs = Win32Methods.GetFileAttributesW(subDotFile);
            Assert.True((prettierAttrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0, "src/.prettierrc should be hidden");

            uint readmeAttrs = Win32Methods.GetFileAttributesW(rootNormalFile);
            Assert.False((readmeAttrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0, "readme.md should NOT be hidden");

            uint nodeModulesAttrs = Win32Methods.GetFileAttributesW(nodeModulesDir);
            Assert.False((nodeModulesAttrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0, "node_modules folder itself should NOT be hidden");

            uint gitHeadAttrs = Win32Methods.GetFileAttributesW(gitHeadFile);
            Assert.False((gitHeadAttrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0, "File inside .git should NOT be hidden (recursion skipped)");

            uint nodeDotAttrs = Win32Methods.GetFileAttributesW(nodeModulesDotFile);
            Assert.False((nodeDotAttrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0, "File inside node_modules should NOT be hidden (recursion skipped)");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}


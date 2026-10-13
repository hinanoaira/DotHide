using System;
using System.Collections.Generic;
using System.IO;
using DotHide.Core;
using DotHide.Native;
using Xunit;

namespace DotHide.Tests;

public class FileHiderTests
{
    [Theory]
    [InlineData(".env", true)]
    [InlineData(".git", true)]
    [InlineData(".gitignore", true)]
    [InlineData(".vscode", true)]
    [InlineData("file.txt", false)]
    [InlineData("normal_dir", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(@"C:\Users\test\.config", true)]
    [InlineData(@"C:\Users\test\normal.txt", false)]
    [InlineData(@"C:\Users\test\.hidden_dir\", true)]
    public void IsDotItem_IdentifiesCorrectly(string path, bool expected)
    {
        bool actual = FileHider.IsDotItem(path);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsInsideExcludedDirectory_CorrectlyDetectsAncestors()
    {
        var excludeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            "node_modules",
            ".vs"
        };

        // The directory itself is not INSIDE an excluded directory
        Assert.False(FileHider.IsInsideExcludedDirectory(@"C:\repo\.git", excludeSet));
        Assert.False(FileHider.IsInsideExcludedDirectory(@"C:\repo\node_modules", excludeSet));

        // Files/folders inside excluded directories ARE inside
        Assert.True(FileHider.IsInsideExcludedDirectory(@"C:\repo\.git\HEAD", excludeSet));
        Assert.True(FileHider.IsInsideExcludedDirectory(@"C:\repo\.git\objects\abc", excludeSet));
        Assert.True(FileHider.IsInsideExcludedDirectory(@"C:\repo\node_modules\express\index.js", excludeSet));
        Assert.True(FileHider.IsInsideExcludedDirectory(@"C:\repo\.vs\project\v17\.suo", excludeSet));

        // Normal files in normal directories are NOT inside
        Assert.False(FileHider.IsInsideExcludedDirectory(@"C:\repo\src\.env", excludeSet));
        Assert.False(FileHider.IsInsideExcludedDirectory(@"C:\repo\src\test.cs", excludeSet));
    }

    [Fact]
    public void ApplyHiddenAttribute_AppliesWin32HiddenAttribute()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DotHide_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var testFilePath = Path.Combine(tempDir, ".env");
            File.WriteAllText(testFilePath, "KEY=VALUE");

            // Verify initial state
            uint initialAttrs = Win32Methods.GetFileAttributesW(testFilePath);
            Assert.NotEqual(Win32Methods.INVALID_FILE_ATTRIBUTES, initialAttrs);

            // First call: should successfully apply
            var result1 = FileHider.ApplyHiddenAttribute(testFilePath);
            Assert.Equal(HideResult.Success, result1);

            // Verify file is hidden on disk via Win32 API
            uint updatedAttrs = Win32Methods.GetFileAttributesW(testFilePath);
            Assert.True((updatedAttrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0);

            // Second call: should report AlreadyHidden
            var result2 = FileHider.ApplyHiddenAttribute(testFilePath);
            Assert.Equal(HideResult.AlreadyHidden, result2);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void ApplyHiddenAttribute_AppliesToDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DotHide_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var dotFolder = Path.Combine(tempDir, ".config");
            Directory.CreateDirectory(dotFolder);

            var res = FileHider.ApplyHiddenAttribute(dotFolder);
            Assert.Equal(HideResult.Success, res);

            uint attrs = Win32Methods.GetFileAttributesW(dotFolder);
            Assert.True((attrs & Win32Methods.FILE_ATTRIBUTE_HIDDEN) != 0);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void ApplyHiddenAttribute_NonExistentFile_ReturnsNotFound()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "non_existent_" + Guid.NewGuid().ToString("N"));
        var res = FileHider.ApplyHiddenAttribute(nonExistent, maxRetries: 1);
        Assert.Equal(HideResult.NotFound, res);
    }
}


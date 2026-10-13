using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using DotHide.Config;
using DotHide.Core;
using DotHide.Logging;
using DotHide.Native;
using DotHide.Startup;
using DotHide.Tray;

namespace DotHide;

public static class Program
{
    private const string MutexName = @"Local\DotHide_SingleInstance_Mutex";

    [STAThread]
    public static int Main(string[] args)
    {
        // Check for CLI flags
        bool hasConsoleFlag = HasFlag(args, "--console");
        bool hasScanOnlyFlag = HasFlag(args, "--scan-only");
        bool hasInstallStartupFlag = HasFlag(args, "--install-startup");
        bool hasUninstallStartupFlag = HasFlag(args, "--uninstall-startup");
        bool hasStatusFlag = HasFlag(args, "--status");
        bool hasHelpFlag = HasFlag(args, "--help", "-h", "/?");
        string? customConfigPath = GetOptionValue(args, "--config", "-c");

        // If any CLI command was specified or --console requested, attach to the parent console
        bool needsConsole = hasConsoleFlag || hasScanOnlyFlag || hasInstallStartupFlag ||
                            hasUninstallStartupFlag || hasStatusFlag || hasHelpFlag;

        SetupConsoleOutput(needsConsole);

        if (hasHelpFlag)
        {
            PrintHelp();
            return 0;
        }

        if (hasInstallStartupFlag)
        {
            bool ok = StartupManager.EnableStartup();
            Console.WriteLine(ok
                ? "[DotHide] Successfully registered to Windows Startup (HKCU Run key)."
                : "[DotHide] Failed to register to Windows Startup.");
            return ok ? 0 : 1;
        }

        if (hasUninstallStartupFlag)
        {
            bool ok = StartupManager.DisableStartup();
            Console.WriteLine(ok
                ? "[DotHide] Successfully removed from Windows Startup."
                : "[DotHide] Failed to remove from Windows Startup.");
            return ok ? 0 : 1;
        }

        if (hasStatusFlag)
        {
            PrintStatus(customConfigPath);
            return 0;
        }

        // Initialize configuration and logger
        var config = ConfigManager.LoadOrCreate(customConfigPath);
        var minLogLevel = Logger.ParseLogLevel(config.LogLevel);
        var logFilePath = config.GetExpandedLogFilePath();
        var logger = new Logger(minLogLevel, logFilePath, config.LogToFile, logToConsole: needsConsole);

        logger.Info("=== DotHide starting ===");

        if (hasScanOnlyFlag)
        {
            logger.Info("Running in one-time scan mode (--scan-only)...");
            var scanner = new FullScanner(config, logger);
            var watchPaths = config.GetExpandedWatchPaths();

            int totalApplied = 0;
            foreach (var path in watchPaths)
            {
                var stats = scanner.ScanDirectory(path);
                totalApplied += stats.HiddenApplied;
            }

            logger.Info($"Scan complete. Total hidden applied: {totalApplied:N0}");
            return 0;
        }

        // Enforce single instance for the resident background service
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            logger.Warn("Another instance of DotHide is already running. Exiting.");
            if (needsConsole)
            {
                Console.WriteLine("[DotHide] Another instance is already running.");
            }
            return 0;
        }

        // Run resident application
        ApplicationConfiguration.Initialize();
        var watcher = new DirectoryWatcher(config, logger);
        var appCtx = new TrayApplicationContext(config, logger, watcher);

        Application.Run(appCtx);

        logger.Info("=== DotHide stopped ===");
        return 0;
    }

    private static bool HasFlag(string[] args, params string[] flagNames)
    {
        foreach (var arg in args)
        {
            foreach (var flag in flagNames)
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static string? GetOptionValue(string[] args, params string[] optionNames)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            foreach (var opt in optionNames)
            {
                if (string.Equals(args[i], opt, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
        }
        return null;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"
DotHide - Windows Dotfile Auto-Hider Background Tool
======================================================
Usage:
  DotHide.exe [options]

Options:
  --scan-only           Run initial full scan on target directories once and exit.
  --console             Run with console logging attached to current shell.
  --install-startup     Register DotHide into Windows Startup (HKCU Run key).
  --uninstall-startup   Remove DotHide from Windows Startup.
  --status              Show configuration and Windows Startup registration status.
  --config <path>       Specify custom path to config.json.
  --help, -h            Show this help message.

If launched without options, DotHide runs silently as a background process with a system tray icon.
");
    }

    private static void PrintStatus(string? customConfigPath)
    {
        Console.WriteLine("\n=== DotHide Status ===");
        bool isStartup = StartupManager.IsStartupEnabled();
        string? startupCmd = StartupManager.GetStartupCommand();
        Console.WriteLine($"Windows Startup: {(isStartup ? "ENABLED" : "DISABLED")}");
        if (isStartup && startupCmd != null)
        {
            Console.WriteLine($"Startup Command: {startupCmd}");
        }

        var configPath = ConfigManager.ResolveConfigPath(customConfigPath);
        Console.WriteLine($"Config File Path: {configPath} (Exists: {File.Exists(configPath)})");

        var config = ConfigManager.LoadOrCreate(customConfigPath);
        Console.WriteLine($"Log File: {config.GetExpandedLogFilePath()}");
        Console.WriteLine("Watch Paths:");
        foreach (var p in config.GetExpandedWatchPaths())
        {
            Console.WriteLine($"  - {p} (Exists: {Directory.Exists(p)})");
        }
        Console.WriteLine("Excluded Directories (recursion skipped inside):");
        Console.WriteLine($"  [{string.Join(", ", config.ExcludeDirectories)}]");
        Console.WriteLine();
    }

    private static void SetupConsoleOutput(bool needsConsole)
    {
        if (!needsConsole) return;

        var stdOutHandle = Win32Methods.GetStdHandle(Win32Methods.STD_OUTPUT_HANDLE);
        bool hasRedirectedStdOut = stdOutHandle != IntPtr.Zero && stdOutHandle != new IntPtr(-1);

        if (!hasRedirectedStdOut)
        {
            if (!Win32Methods.AttachConsole(Win32Methods.ATTACH_PARENT_PROCESS))
            {
                Win32Methods.AllocConsole();
            }
            stdOutHandle = Win32Methods.GetStdHandle(Win32Methods.STD_OUTPUT_HANDLE);
        }

        try
        {
            if (stdOutHandle != IntPtr.Zero && stdOutHandle != new IntPtr(-1))
            {
                var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdOutHandle, ownsHandle: false);
                var fs = new FileStream(safeHandle, FileAccess.Write);
                var stdOut = new StreamWriter(fs, Console.OutputEncoding) { AutoFlush = true };
                Console.SetOut(stdOut);
            }
        }
        catch { }
    }
}
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DotHide.Config;
using DotHide.Core;
using DotHide.Logging;
using DotHide.Startup;

namespace DotHide.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppConfig _config;
    private readonly Logger _logger;
    private readonly DirectoryWatcher _watcher;
    private readonly NotifyIcon? _notifyIcon;
    private readonly ToolStripMenuItem? _pauseResumeItem;
    private readonly ToolStripMenuItem? _startupItem;
    private readonly ToolStripMenuItem? _statusItem;
    private int _hiddenCount;

    public TrayApplicationContext(AppConfig config, Logger logger, DirectoryWatcher watcher)
    {
        _config = config;
        _logger = logger;
        _watcher = watcher;

        _watcher.OnAttributeApplied += (path, result) =>
        {
            if (result == HideResult.Success)
            {
                Interlocked.Increment(ref _hiddenCount);
                UpdateTooltip();
            }
        };

        if (_config.EnableTrayIcon)
        {
            var menu = new ContextMenuStrip();

            var titleItem = new ToolStripMenuItem("DotHide - ドットファイル自動隠し常駐")
            {
                Enabled = false,
                Font = new Font(Control.DefaultFont, FontStyle.Bold)
            };
            menu.Items.Add(titleItem);

            _statusItem = new ToolStripMenuItem("状態: リアルタイム監視中") { Enabled = false };
            menu.Items.Add(_statusItem);

            menu.Items.Add(new ToolStripSeparator());

            var scanNowItem = new ToolStripMenuItem("今すぐフルスキャン実行 (&S)", null, (s, e) => TriggerManualScan());
            menu.Items.Add(scanNowItem);

            _pauseResumeItem = new ToolStripMenuItem("監視を一時停止 (&P)", null, (s, e) => TogglePause());
            menu.Items.Add(_pauseResumeItem);

            menu.Items.Add(new ToolStripSeparator());

            var openConfigItem = new ToolStripMenuItem("設定ファイルを開く (&C)", null, (s, e) => OpenConfigFile());
            menu.Items.Add(openConfigItem);

            var openLogItem = new ToolStripMenuItem("ログファイルを開く (&L)", null, (s, e) => OpenLogFile());
            menu.Items.Add(openLogItem);

            var openFolderItem = new ToolStripMenuItem("監視対象フォルダを開く (&F)", null, (s, e) => OpenFirstWatchFolder());
            menu.Items.Add(openFolderItem);

            menu.Items.Add(new ToolStripSeparator());

            _startupItem = new ToolStripMenuItem("Windows起動時に実行 (&W)")
            {
                Checked = StartupManager.IsStartupEnabled(),
                CheckOnClick = true
            };
            _startupItem.CheckedChanged += (s, e) =>
            {
                if (_startupItem.Checked)
                {
                    StartupManager.EnableStartup();
                    _logger.Info("Enabled startup via tray menu.");
                }
                else
                {
                    StartupManager.DisableStartup();
                    _logger.Info("Disabled startup via tray menu.");
                }
            };
            menu.Items.Add(_startupItem);

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("終了 (&X)", null, (s, e) => ExitApplication());
            menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                Icon = CreateApplicationIcon(),
                ContextMenuStrip = menu,
                Text = "DotHide - 監視中",
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => OpenConfigFile();
        }

        // Start watcher
        _watcher.Start();

        // Perform initial full scan in background if configured
        if (_config.ScanOnStartup)
        {
            Task.Run(() =>
            {
                PerformFullScan();
            });
        }
    }

    private void UpdateTooltip()
    {
        if (_notifyIcon == null) return;

        try
        {
            var text = _watcher.IsPaused
                ? "DotHide - 一時停止中"
                : $"DotHide - 監視中 (隠し適用数: {_hiddenCount:N0})";

            // NotifyIcon text limit is 63 characters
            if (text.Length > 63) text = text.Substring(0, 60) + "...";

            _notifyIcon.Text = text;
        }
        catch { }
    }

    private void TogglePause()
    {
        if (_watcher.IsPaused)
        {
            _watcher.Resume();
            if (_pauseResumeItem != null) _pauseResumeItem.Text = "監視を一時停止 (&P)";
            if (_statusItem != null) _statusItem.Text = "状態: リアルタイム監視中";
        }
        else
        {
            _watcher.Pause();
            if (_pauseResumeItem != null) _pauseResumeItem.Text = "監視を再開 (&R)";
            if (_statusItem != null) _statusItem.Text = "状態: 一時停止中";
        }
        UpdateTooltip();
    }

    private void TriggerManualScan()
    {
        Task.Run(() =>
        {
            ShowBalloon("フルスキャン開始", "監視対象フォルダの走査を開始しました。");
            var stats = PerformFullScan();
            ShowBalloon("フルスキャン完了", $"新規適用: {stats.HiddenApplied} 件\n既存の隠し: {stats.AlreadyHidden} 件\nエラー: {stats.Errors} 件");
        });
    }

    private FullScanStatistics PerformFullScan()
    {
        var totalStats = new FullScanStatistics();
        var scanner = new FullScanner(_config, _logger);
        var watchPaths = _config.GetExpandedWatchPaths();

        foreach (var path in watchPaths)
        {
            var stats = scanner.ScanDirectory(path);
            totalStats.DirectoriesScanned += stats.DirectoriesScanned;
            totalStats.FilesScanned += stats.FilesScanned;
            totalStats.HiddenApplied += stats.HiddenApplied;
            totalStats.AlreadyHidden += stats.AlreadyHidden;
            totalStats.Errors += stats.Errors;
        }

        Interlocked.Add(ref _hiddenCount, totalStats.HiddenApplied);
        UpdateTooltip();
        return totalStats;
    }

    private void ShowBalloon(string title, string text)
    {
        if (_notifyIcon != null && _notifyIcon.Visible)
        {
            try
            {
                _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
            }
            catch { }
        }
    }

    private void OpenConfigFile()
    {
        var configPath = ConfigManager.ResolveConfigPath();
        try
        {
            Process.Start(new ProcessStartInfo(configPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not open config file: {configPath}", ex);
        }
    }

    private void OpenLogFile()
    {
        var logPath = _config.GetExpandedLogFilePath();
        try
        {
            if (File.Exists(logPath))
            {
                Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
            }
            else
            {
                ShowBalloon("ログファイル", "ログファイルがまだ作成されていません。");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not open log file: {logPath}", ex);
        }
    }

    private void OpenFirstWatchFolder()
    {
        var watchPaths = _config.GetExpandedWatchPaths();
        if (watchPaths.Count > 0 && Directory.Exists(watchPaths[0]))
        {
            try
            {
                Process.Start(new ProcessStartInfo(watchPaths[0]) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.Error($"Could not open watch folder: {watchPaths[0]}", ex);
            }
        }
    }

    private static Icon CreateApplicationIcon()
    {
        var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DotHide.ico");
        if (File.Exists(icoPath))
        {
            try
            {
                return new Icon(icoPath);
            }
            catch { }
        }

        // Dynamically generate a clean 32x32 icon with dark blue circle and a white dot
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Draw outer dark blue / indigo badge
            using var bgBrush = new SolidBrush(Color.FromArgb(41, 98, 255));
            g.FillEllipse(bgBrush, 2, 2, 28, 28);

            // Draw inner dot
            using var dotBrush = new SolidBrush(Color.White);
            g.FillEllipse(dotBrush, 11, 11, 10, 10);
        }

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    private void ExitApplication()
    {
        _logger.Info("Shutting down DotHide...");
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        _watcher.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon?.Dispose();
            _watcher.Dispose();
        }
        base.Dispose(disposing);
    }
}


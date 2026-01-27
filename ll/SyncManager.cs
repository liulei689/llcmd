using System;
using System.IO;
using System.Threading;
using System.Collections.Concurrent;
using LL;

namespace LL
{
    internal static class SyncManager
    {
        private static FileSystemWatcher? _watcher;
        private static string? _sourcePath;
        private static string? _targetPath;
        private static readonly ConcurrentQueue<(string source, string target, WatcherChangeTypes changeType)> _syncQueue = new();
        private static Timer? _batchTimer;
        private static bool _isRunning = false;
        private static int _totalFilesSynced = 0;
        private static int _currentBatchSize = 0;

        public static void HandleSync(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: sync <source> <target> 或 sync stop");
                return;
            }

            if (args[0].ToLower() == "stop")
            {
                StopSync();
            }
            else if (args.Length >= 2)
            {
                string source = args[0];
                string target = args[1];
                StartSync(source, target);
            }
            else
            {
                UI.PrintError("用法: sync <source> <target> 或 sync stop");
            }
        }

        public static void StartSync(string sourcePath, string targetPath)
        {
            if (_isRunning)
            {
                UI.PrintError("同步已在运行中。");
                return;
            }

            if (!Directory.Exists(sourcePath))
            {
                UI.PrintError($"源文件夹不存在: {sourcePath}");
                return;
            }

            if (!Directory.Exists(targetPath))
            {
                try
                {
                    Directory.CreateDirectory(targetPath);
                }
                catch (Exception ex)
                {
                    UI.PrintError($"无法创建目标文件夹: {ex.Message}");
                    return;
                }
            }

            _sourcePath = sourcePath;
            _targetPath = targetPath;
            _isRunning = true;
            _totalFilesSynced = 0;

            // 初始同步整个文件夹
            UI.PrintInfo("正在进行初始同步...");
            InitialSync(sourcePath, targetPath);
            UI.PrintSuccess($"初始同步完成，共 {_totalFilesSynced} 个文件。");

            _watcher = new FileSystemWatcher(sourcePath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileChanged;
            _watcher.Changed += OnFileChanged;
            _watcher.Deleted += OnFileDeleted;
            _watcher.Renamed += OnFileRenamed;

            // 批量处理定时器，每 2 秒处理一次队列
            _batchTimer = new Timer(ProcessBatch, null, 2000, 2000);

            UI.PrintSuccess($"开始监听同步: {sourcePath} -> {targetPath}");
        }

        public static void StopSync()
        {
            if (!_isRunning)
            {
                UI.PrintError("同步未运行。");
                return;
            }

            _watcher?.Dispose();
            _batchTimer?.Dispose();
            _syncQueue.Clear();
            _isRunning = false;
            _sourcePath = null;
            _targetPath = null;

            Console.WriteLine(); // 换行
            UI.PrintSuccess($"同步已停止。总共同步 {_totalFilesSynced} 个文件。");
        }

        private static void InitialSync(string sourceDir, string targetDir)
        {
            int totalFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories).Count(f => !f.Contains("\\.git\\") && !f.Contains("/.git/"));
            int synced = 0;

            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                if (dirPath.Contains("\\.git\\") || dirPath.Contains("/.git/")) continue; // 跳过 .git
                string relativePath = GetRelativePath(dirPath, sourceDir);
                string targetDirPath = Path.Combine(targetDir, relativePath);
                Directory.CreateDirectory(targetDirPath);
            }

            foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                if (filePath.Contains("\\.git\\") || filePath.Contains("/.git/")) continue; // 跳过 .git
                string relativePath = GetRelativePath(filePath, sourceDir);
                string targetFilePath = Path.Combine(targetDir, relativePath);
                try
                {
                    File.Copy(filePath, targetFilePath, true);
                    synced++;
                    _totalFilesSynced++;
                    // 显示进度
                    if (synced % 10 == 0 || synced == totalFiles)
                    {
                        int percentage = (synced * 100) / totalFiles;
                        string progressBar = GetProgressBar(percentage);
                        Console.Write($"\r初始同步: {progressBar} {percentage}% ({synced}/{totalFiles})");
                    }
                }
                catch (Exception ex)
                {
                    UI.PrintError($"初始同步失败 {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }
            Console.WriteLine(); // 换行
        }

        private static void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (e.FullPath.Contains("\\.git\\") || e.FullPath.Contains("/.git/")) return; // 跳过 .git
            if (e.ChangeType == WatcherChangeTypes.Created || e.ChangeType == WatcherChangeTypes.Changed)
            {
                string relativePath = GetRelativePath(e.FullPath, _sourcePath);
                string targetFile = Path.Combine(_targetPath, relativePath);
                _syncQueue.Enqueue((e.FullPath, targetFile, e.ChangeType));
            }
        }

        private static void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            if (e.FullPath.Contains("\\.git\\") || e.FullPath.Contains("/.git/")) return; // 跳过 .git
            string relativePath = GetRelativePath(e.FullPath, _sourcePath);
            string targetFile = Path.Combine(_targetPath, relativePath);
            if (File.Exists(targetFile))
            {
                try
                {
                    File.Delete(targetFile);
                }
                catch { }
            }
            else if (Directory.Exists(targetFile))
            {
                try
                {
                    Directory.Delete(targetFile, true);
                }
                catch { }
            }
        }

        private static void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (e.FullPath.Contains("\\.git\\") || e.FullPath.Contains("/.git/")) return; // 跳过 .git
            // 处理重命名：删除旧文件，复制新文件
            string oldRelative = GetRelativePath(e.OldFullPath, _sourcePath);
            string oldTarget = Path.Combine(_targetPath, oldRelative);
            if (File.Exists(oldTarget))
            {
                try
                {
                    File.Delete(oldTarget);
                }
                catch { }
            }
            else if (Directory.Exists(oldTarget))
            {
                try
                {
                    Directory.Delete(oldTarget, true);
                }
                catch { }
            }

            string newRelative = GetRelativePath(e.FullPath, _sourcePath);
            string newTarget = Path.Combine(_targetPath, newRelative);
            _syncQueue.Enqueue((e.FullPath, newTarget, WatcherChangeTypes.Created));
        }

        private static void ProcessBatch(object state)
        {
            if (_syncQueue.IsEmpty) return;

            var batch = new List<(string source, string target, WatcherChangeTypes changeType)>();
            while (_syncQueue.TryDequeue(out var item))
            {
                batch.Add(item);
            }

            _currentBatchSize = batch.Count;
            int synced = 0;
            var processedTargets = new HashSet<string>();

            foreach (var (source, target, changeType) in batch)
            {
                if (processedTargets.Contains(target)) continue; // 避免重复处理同一目标文件
                try
                {
                    if (File.Exists(source))
                    {
                        string targetDir = Path.GetDirectoryName(target);
                        if (!Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }
                        File.Copy(source, target, true); // 覆盖
                        synced++;
                        _totalFilesSynced++;
                        processedTargets.Add(target);
                    }
                }
                catch (Exception ex)
                {
                    UI.PrintError($"同步失败 {Path.GetFileName(source)}: {ex.Message}");
                }
            }

            // 显示同步信息，使用 \r 覆盖
            if (synced > 0)
            {
                Console.Write($"\r实时同步: {synced} 个文件已同步");
            }
        }

        private static string GetProgressBar(int percentage)
        {
            int barLength = 20;
            int filled = (percentage * barLength) / 100;
            string bar = new string('#', filled) + new string(' ', barLength - filled);
            return $"[{bar}]";
        }

        private static string GetRelativePath(string fullPath, string basePath)
        {
            return Path.GetRelativePath(basePath, fullPath);
        }

        public static bool IsRunning => _isRunning;
    }
}
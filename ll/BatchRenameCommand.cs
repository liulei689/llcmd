using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LL;

namespace LL;

public static class BatchRenameCommand
{
    public static void Handle(string[] args)
    {
        if (args.Length == 0 || args[0] == "help")
        {
            UI.PrintInfo("用法:");
            UI.PrintInfo("  batch-rename rename <directory> [--prefix <prefix>] [--replace <old> <new>] [--ext <ext>] [--dry-run] [--rs]");
            UI.PrintInfo("  batch-rename collect <directory> <level> <newfolder> [--dry-run]");
            return;
        }

        string subcommand = args[0];
        if (subcommand == "rename")
        {
            HandleRename(args.AsSpan(1).ToArray());
        }
        else if (subcommand == "collect")
        {
            HandleCollect(args.AsSpan(1).ToArray());
        }
        else
        {
            UI.PrintError($"未知子命令: {subcommand}");
            UI.PrintInfo("使用 batch-rename help 查看用法。");
        }
    }

    private static void HandleRename(string[] args)
    {
        if (args.Length < 1)
        {
            UI.PrintError("用法: batch-rename rename <path> [options]");
            UI.PrintInfo("  <path> 可以是文件或目录。");
            return;
        }

        string path = args[0];

        // 解析选项
        bool dryRun = false;
        bool renameFolder = false;
        bool removeSpaces = false;
        string prefix = null;
        string replaceOld = null;
        string replaceNew = null;
        string ext = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--folder":
                    renameFolder = true;
                    break;
                case "--prefix":
                    if (i + 1 < args.Length)
                    {
                        prefix = args[++i];
                    }
                    else
                    {
                        UI.PrintError("--prefix 需要一个参数。");
                        return;
                    }
                    break;
                case "--replace":
                    if (i + 2 < args.Length)
                    {
                        replaceOld = args[++i];
                        replaceNew = args[++i];
                    }
                    else
                    {
                        UI.PrintError("--replace 需要两个参数。");
                        return;
                    }
                    break;
                case "--ext":
                    if (i + 1 < args.Length)
                    {
                        ext = args[++i].TrimStart('.');
                    }
                    else
                    {
                        UI.PrintError("--ext 需要一个参数。");
                        return;
                    }
                    break;
                case "--rs":
                    removeSpaces = true;
                    break;
                default:
                    UI.PrintError($"未知选项: {args[i]}");
                    return;
            }
        }

        if (File.Exists(path))
        {
            // 单个文件重命名
            PerformSingleRename(path, prefix, replaceOld, replaceNew, dryRun, removeSpaces);
        }
        else if (Directory.Exists(path))
        {
            if (renameFolder)
            {
                // 重命名文件夹
                PerformFolderRename(path, prefix, replaceOld, replaceNew, dryRun, removeSpaces);
            }
            else
            {
                // 批量重命名文件
                PerformRename(path, prefix, replaceOld, replaceNew, ext, dryRun, removeSpaces);
            }
        }
        else
        {
            UI.PrintError("指定的路径不存在。");
        }
    }

    private static void PerformSingleRename(string filePath, string prefix, string replaceOld, string replaceNew, bool dryRun, bool removeSpaces)
    {
        string name = Path.GetFileName(filePath);
        string newName = name;

        if (prefix != null)
        {
            newName = prefix + newName;
        }

        if (replaceOld != null && replaceNew != null)
        {
            newName = newName.Replace(replaceOld, replaceNew);
        }

        if (removeSpaces)
        {
            newName = newName.Replace(" ", "_");
        }

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
        {
            UI.PrintError($"新文件名包含非法字符，跳过: {name}");
            return;
        }

        if (newName == name)
        {
            UI.PrintInfo("文件不需要重命名。");
            return;
        }

        string newPath = Path.Combine(Path.GetDirectoryName(filePath), newName);

        if (dryRun)
        {
            UI.PrintResult(name, $"-> {newName}");
        }
        else
        {
            try
            {
                if (File.Exists(newPath))
                {
                    UI.PrintError($"目标文件已存在: {newPath}");
                    return;
                }
                File.Move(filePath, newPath);
                UI.PrintSuccess($"重命名: {name} -> {newName}");
            }
            catch (Exception ex)
            {
                UI.PrintError($"重命名失败: {ex.Message}");
            }
        }
    }

    private static void HandleCollect(string[] args)
    {
        if (args.Length < 3)
        {
            UI.PrintError("用法: batch-rename collect <directory> <level> <newfolder> [--dry-run]");
            return;
        }

        string dir = args[0];
        if (!Directory.Exists(dir))
        {
            UI.PrintError("指定的目录不存在。");
            return;
        }

        if (!int.TryParse(args[1], out int level) || level <= 0)
        {
            UI.PrintError("level 必须是正整数。");
            return;
        }

        string newfolder = args[2];
        bool dryRun = args.Length > 3 && args[3] == "--dry-run";

        PerformCollect(dir, level, newfolder, dryRun);
    }

    private static void PerformRename(string dir, string prefix, string replaceOld, string replaceNew, string ext, bool dryRun, bool removeSpaces)
    {
        // 获取文件列表
        string searchPattern = ext != null ? $"*.{ext}" : "*.*";
        string[] files;
        try
        {
            files = Directory.GetFiles(dir, searchPattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            UI.PrintError($"获取文件列表失败: {ex.Message}");
            return;
        }

        if (files.Length == 0)
        {
            UI.PrintInfo("没有找到匹配的文件。");
            return;
        }

        // 生成重命名列表
        var renames = new List<(string oldPath, string newPath)>();
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            string newName = name;

            if (prefix != null)
            {
                newName = prefix + newName;
            }

            if (replaceOld != null && replaceNew != null)
            {
                newName = newName.Replace(replaceOld, replaceNew);
            }

            if (removeSpaces)
            {
                newName = newName.Replace(" ", "_");
            }

            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
            {
                UI.PrintError($"新文件名包含非法字符，跳过: {name}");
                continue;
            }

            if (newName != name)
            {
                string newPath = Path.Combine(Path.GetDirectoryName(file), newName);
                renames.Add((file, newPath));
            }
        }

        if (renames.Count == 0)
        {
            UI.PrintInfo("没有文件需要重命名。");
            return;
        }

        // 执行或预览
        if (dryRun)
        {
            UI.PrintInfo("预览模式 - 以下文件将被重命名:");
            foreach (var (oldPath, newPath) in renames)
            {
                string oldName = Path.GetFileName(oldPath);
                string newName = Path.GetFileName(newPath);
                UI.PrintResult(oldName, $"-> {newName}");
            }
            UI.PrintInfo($"总计: {renames.Count} 个文件。");
        }
        else
        {
            UI.PrintInfo("开始重命名...");
            int successCount = 0;
            foreach (var (oldPath, newPath) in renames)
            {
                if (File.Exists(newPath))
                {
                    UI.PrintError($"目标文件已存在，跳过: {Path.GetFileName(newPath)}");
                    continue;
                }
                try
                {
                    File.Move(oldPath, newPath);
                    string oldName = Path.GetFileName(oldPath);
                    string newName = Path.GetFileName(newPath);
                    UI.PrintSuccess($"重命名: {oldName} -> {newName}");
                    successCount++;
                }
                catch (Exception ex)
                {
                    string oldName = Path.GetFileName(oldPath);
                    UI.PrintError($"重命名失败 {oldName}: {ex.Message}");
                }
            }
            UI.PrintInfo($"完成: {successCount}/{renames.Count} 个文件重命名成功。");
        }
    }

    private static void PerformCollect(string dir, int level, string newFolder, bool dryRun)
    {
        string newFolderPath = Path.Combine(dir, newFolder);
        if (!dryRun && !Directory.Exists(newFolderPath))
        {
            Directory.CreateDirectory(newFolderPath);
        }

        var filesToMove = new List<(string oldPath, string newPath)>();
        CollectFiles(dir, level, newFolderPath, filesToMove, 0);

        if (filesToMove.Count == 0)
        {
            UI.PrintInfo("没有找到符合级别的文件。");
            return;
        }

        if (dryRun)
        {
            UI.PrintInfo("预览模式 - 以下文件将被移动:");
            foreach (var (oldPath, newPath) in filesToMove)
            {
                UI.PrintResult(oldPath, $"-> {newPath}");
            }
        }
        else
        {
            UI.PrintInfo("开始移动...");
            int successCount = 0;
            foreach (var (oldPath, newPath) in filesToMove)
            {
                try
                {
                    string targetPath = newPath;
                    int counter = 1;
                    while (File.Exists(targetPath))
                    {
                        string dirName = Path.GetDirectoryName(targetPath);
                        string fileName = Path.GetFileNameWithoutExtension(targetPath);
                        string ext = Path.GetExtension(targetPath);
                        targetPath = Path.Combine(dirName, $"{fileName}_{counter}{ext}");
                        counter++;
                    }
                    File.Move(oldPath, targetPath);
                    UI.PrintSuccess($"移动: {oldPath} -> {targetPath}");
                    successCount++;
                }
                catch (Exception ex)
                {
                    UI.PrintError($"移动失败 {oldPath}: {ex.Message}");
                }
            }
            UI.PrintInfo($"完成: {successCount}/{filesToMove.Count} 个文件移动成功。");
        }
    }

    private static void CollectFiles(string currentDir, int targetLevel, string newFolderPath, List<(string, string)> files, int currentLevel)
    {
        if (currentLevel == targetLevel)
        {
            string[] dirFiles = Directory.GetFiles(currentDir);
            foreach (string file in dirFiles)
            {
                string newPath = Path.Combine(newFolderPath, Path.GetFileName(file));
                files.Add((file, newPath));
            }
            return;
        }

        string[] subDirs = Directory.GetDirectories(currentDir);
        foreach (string subDir in subDirs)
        {
            CollectFiles(subDir, targetLevel, newFolderPath, files, currentLevel + 1);
        }
    }

    private static void PerformFolderRename(string folderPath, string prefix, string replaceOld, string replaceNew, bool dryRun, bool removeSpaces)
    {
        string name = Path.GetFileName(folderPath);
        string newName = name;

        if (prefix != null)
        {
            newName = prefix + newName;
        }

        if (replaceOld != null && replaceNew != null)
        {
            newName = newName.Replace(replaceOld, replaceNew);
        }

        if (removeSpaces)
        {
            newName = newName.Replace(" ", "_");
        }

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
        {
            UI.PrintError($"新文件夹名包含非法字符，跳过: {name}");
            return;
        }

        if (newName == name)
        {
            UI.PrintInfo("文件夹不需要重命名。");
            return;
        }

        string newPath = Path.Combine(Path.GetDirectoryName(folderPath), newName);

        if (dryRun)
        {
            UI.PrintResult(name, $"-> {newName}");
        }
        else
        {
            try
            {
                if (Directory.Exists(newPath))
                {
                    UI.PrintError($"目标文件夹已存在: {newPath}");
                    return;
                }
                Directory.Move(folderPath, newPath);
                UI.PrintSuccess($"重命名: {name} -> {newName}");
            }
            catch (Exception ex)
            {
                UI.PrintError($"重命名失败: {ex.Message}");
            }
        }
    }
}
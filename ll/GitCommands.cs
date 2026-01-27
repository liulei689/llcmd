using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LL;

namespace LL
{
    internal static class GitCommandHandler
    {
        // 精简的 Git 命令处理器：仅保留查看历史和回滚功能
        // 设置默认项目路径（全局，通过 Program.CurrentProjectPath 共享）
        internal static void SetDefaultProject(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(Program.CurrentProjectPath))
                {
                    UI.PrintInfo("未设置默认项目目录。使用 cd <path> 设置。");
                }
                else
                {
                    UI.PrintInfo($"当前默认项目目录: {Program.CurrentProjectPath}");
                }
                return;
            }

            string path = string.Join(" ", args).Trim('"');
            if (!Directory.Exists(path))
            {
                UI.PrintError("目录不存在。");
                return;
            }

            var full = Path.GetFullPath(path);
            Program.CurrentProjectPath = full;
            UI.PrintInfo($"已设置默认项目目录: {full}");
            try { Console.Title = $"LL - project: {full}"; } catch { }
            try { Program.UpdateConsoleTitle(); } catch { }
        }

        // 返回当前设置的默认项目目录（可能为 null）
        internal static string? GetCurrentProjectPath()
        {
            return Program.CurrentProjectPath;
        }

        private static string FindGitPath()
        {
            // 尝试常见路径
            string[] possiblePaths = new[]
            {
                @"C:\Program Files\Git\bin\git.exe",
                @"C:\Program Files (x86)\Git\bin\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2023\Professional\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2023\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2023\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2024\Professional\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2024\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2024\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2025\Professional\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2025\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2025\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2026\Professional\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2026\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe",
                @"C:\Program Files\Microsoft Visual Studio\2026\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"
            };

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            // 尝试使用 where git
            try
            {
                ProcessStartInfo wherePsi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "git",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process whereProcess = Process.Start(wherePsi))
                {
                    string output = whereProcess.StandardOutput.ReadToEnd();
                    whereProcess.WaitForExit();
                    if (whereProcess.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        string gitPath = output.Split('\n')[0].Trim();
                        if (File.Exists(gitPath))
                        {
                            return gitPath;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static string RunGitCommandWithOutput(string arguments, string workingDirectory = null)
        {
            try
            {
                string gitExePath = FindGitPath();
                if (gitExePath == null)
                {
                    return "ERROR: 未找到Git安装路径。请确保Git已安装。";
                }

                // 如果未指定 workingDirectory，且已设置全局 CurrentProjectPath，则使用之
                string cwd = workingDirectory;
                if (string.IsNullOrWhiteSpace(cwd))
                {
                    cwd = !string.IsNullOrWhiteSpace(Program.CurrentProjectPath) ? Program.CurrentProjectPath : Directory.GetCurrentDirectory();
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = gitExePath,
                    Arguments = arguments,
                    WorkingDirectory = cwd,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        return output ?? string.Empty;
                    }
                    else
                    {
                        return $"ERROR: Git命令失败: {error}";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"ERROR: 执行失败: {ex.Message}";
            }
        }

        private static void RunGitCommand(string arguments, string workingDirectory = null)
        {
            string outp = RunGitCommandWithOutput(arguments, workingDirectory);
            if (outp.StartsWith("ERROR:")) UI.PrintError(outp.Substring(6).Trim());
            else if (!string.IsNullOrWhiteSpace(outp)) Console.WriteLine(outp);
        }

        public static void Handle(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: git <子命令> [参数]");
                UI.PrintInfo("可用子命令: history, info, rollback, help — 用法: git <子命令> [参数]");
                return;
            }

            // 简化：只支持 history 和 rollback 两个子命令
            string subCommand = args[0].ToLower();
            string[] subArgs = args.Skip(1).ToArray();

            switch (subCommand)
            {
                case "history":
                case "his":
                    {
                        int n = 10;
                        if (subArgs.Length > 0 && int.TryParse(subArgs[0], out int parsed)) n = parsed;
                        // show hash, author, relative time, subject
                        string outp = RunGitCommandWithOutput($"log --pretty=format:\"%h %an %ar %s\" -{n}");
                        if (outp.StartsWith("ERROR:")) { UI.PrintError(outp.Substring(6).Trim()); break; }
                        var lines = outp.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        Console.WriteLine($"Last {lines.Length} commits:");
                        foreach (var line in lines)
                        {
                            // format: <hash> <author> <reltime> <subject>
                            Console.WriteLine(line.Trim());
                        }
                        break;
                    }
                case "info":
                    {
                        // repo root
                        string root = RunGitCommandWithOutput("rev-parse --show-toplevel").Trim();
                        if (root.StartsWith("ERROR:")) { UI.PrintError(root.Substring(6).Trim()); break; }
                        // remote url
                        string remote = RunGitCommandWithOutput("config --get remote.origin.url").Trim();
                        if (remote.StartsWith("ERROR:")) remote = "";
                        // branch
                        string branch = RunGitCommandWithOutput("rev-parse --abbrev-ref HEAD").Trim();
                        if (branch.StartsWith("ERROR:")) branch = "";
                        // last commit
                        string last = RunGitCommandWithOutput("log -1 --pretty=format:\"%h %an %ar %s\"").Trim();
                        if (last.StartsWith("ERROR:")) last = "";

                        Console.WriteLine($"Repository Root: {root}");
                        Console.WriteLine($"Remote (origin): {(!string.IsNullOrWhiteSpace(remote) ? remote : "<none>")}");
                        Console.WriteLine($"Current Branch: {(!string.IsNullOrWhiteSpace(branch) ? branch : "<unknown>")}");
                        Console.WriteLine($"Latest Commit: {(!string.IsNullOrWhiteSpace(last) ? last : "<none>")}");
                        break;
                    }
                case "rollback":
                case "rb":
                    {
                        if (subArgs.Length == 0)
                        {
                            UI.PrintError("用法: git rollback <commit> [--hard]");
                            break;
                        }
                        string commit = subArgs[0];
                        string info = RunGitCommandWithOutput($"show --oneline -s {commit}");
                        if (info.StartsWith("ERROR:")) { UI.PrintError(info.Substring(6).Trim()); break; }
                        Console.WriteLine("Target commit:");
                        Console.WriteLine(info.Trim());
                        bool hard = subArgs.Skip(1).Any(a => a == "--hard");
                        if (!hard)
                        {
                            Console.WriteLine("执行回退（安全模式）：将使用 checkout 到该提交（detached HEAD）。");
                            // perform checkout and report result
                            string coOut = RunGitCommandWithOutput($"checkout {commit}");
                            if (coOut.StartsWith("ERROR:"))
                            {
                                UI.PrintError(coOut.Substring(6).Trim());
                            }
                            else
                            {
                                string headName = RunGitCommandWithOutput("rev-parse --abbrev-ref HEAD").Trim();
                                string headShort = RunGitCommandWithOutput("rev-parse --short HEAD").Trim();
                                if (headName == "HEAD")
                                    UI.PrintSuccess($"已切换到 detached HEAD @ {headShort}");
                                else
                                    UI.PrintSuccess($"已切换到分支 {headName} @ {headShort}");
                            }
                        }
                        else
                        {
                            Console.Write($"确认进行硬回退到 {commit} ? 这将丢失本地未提交更改。输入 y 确认: ");
                            var key = Console.ReadKey(true);
                            Console.WriteLine();
                            if (key.KeyChar == 'y' || key.KeyChar == 'Y')
                            {
                                string rstOut = RunGitCommandWithOutput($"reset --hard {commit}");
                                if (rstOut.StartsWith("ERROR:"))
                                {
                                    UI.PrintError(rstOut.Substring(6).Trim());
                                }
                                else
                                {
                                    string headShort = RunGitCommandWithOutput("rev-parse --short HEAD").Trim();
                                    UI.PrintSuccess($"已硬回退到 {commit} @ {headShort}");
                                }
                            }
                            else
                            {
                                UI.PrintInfo("已取消硬回退。");
                            }
                        }
                        break;
                    }
                case "help":
                    UI.PrintInfo("用法: git history [n]  - 查看最近 n 条提交（默认10）\n       git rollback <commit> [--hard]  - 回退到指定提交（--hard 需确认）");
                    break;
                case "version":
                case "ver":
                    {
                        string gitPath = FindGitPath();
                        if (gitPath == null)
                        {
                            UI.PrintError("未找到 Git 安装路径。");
                            break;
                        }
                        string version = RunGitCommandWithOutput("--version").Trim();
                        if (version.StartsWith("ERROR:"))
                        {
                            UI.PrintError(version.Substring(6).Trim());
                        }
                        else
                        {
                            Console.WriteLine($"Git 路径: {gitPath}");
                            Console.WriteLine($"Git 版本: {version}");
                        }
                        break;
                    }
                default:
                    UI.PrintError($"未知子命令: {subCommand}");
                    UI.PrintInfo("可用子命令: history, info, rollback, help — 用法: git <子命令> [参数]");
                    break;
            }

            // no working-dir restore needed in simplified handler
        }

        private static void Status(string[] args)
        {
            RunGitCommand("status");
        }

        private static void Add(string[] args)
        {
            string files = args.Length > 0 ? string.Join(" ", args) : ".";
            RunGitCommand($"add {files}");
        }

        private static void Commit(string[] args)
        {
            string message = args.Length > 0 ? string.Join(" ", args) : "Auto commit";
            RunGitCommand($"commit -m \"{message}\"");
        }

        private static void Push(string[] args)
        {
            string branch = args.Length > 0 ? args[0] : "origin main";
            RunGitCommand($"push {branch}");
        }

        private static void Pull(string[] args)
        {
            string branch = args.Length > 0 ? args[0] : "origin main";
            RunGitCommand($"pull {branch}");
        }

        private static void Branch(string[] args)
        {
            if (args.Length > 0)
            {
                RunGitCommand($"branch {string.Join(" ", args)}");
            }
            else
            {
                RunGitCommand("branch");
            }
        }

        private static void Checkout(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: git checkout <分支或提交>");
                return;
            }
            RunGitCommand($"checkout {string.Join(" ", args)}");
        }

        private static void Merge(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: git merge <分支>");
                return;
            }
            RunGitCommand($"merge {string.Join(" ", args)}");
        }

        private static void Rebase(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: git rebase <分支>");
                return;
            }
            RunGitCommand($"rebase {string.Join(" ", args)}");
        }

        private static void Reset(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: git reset [--hard] <提交>");
                return;
            }
            RunGitCommand($"reset {string.Join(" ", args)}");
        }

        private static void Revert(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: git revert <提交>");
                return;
            }
            RunGitCommand($"revert {string.Join(" ", args)}");
        }

        private static void Diff(string[] args)
        {
            RunGitCommand($"diff {string.Join(" ", args)}");
        }

        private static void Log(string[] args)
        {
            string options = args.Length > 0 ? string.Join(" ", args) : "--oneline -10";
            RunGitCommand($"log {options}");
        }

        private static void Clone(string[] args)
        {
            if (args.Length < 2)
            {
                UI.PrintError("用法: git clone <仓库URL> <目录>");
                return;
            }
            RunGitCommand($"clone {args[0]} {args[1]}");
        }

        private static void Init(string[] args)
        {
            RunGitCommand("init");
        }

        private static void Remote(string[] args)
        {
            RunGitCommand($"remote {string.Join(" ", args)}");
        }

        private static void Fetch(string[] args)
        {
            string remote = args.Length > 0 ? args[0] : "origin";
            RunGitCommand($"fetch {remote}");
        }

        private static void Stash(string[] args)
        {
            RunGitCommand($"stash {string.Join(" ", args)}");
        }

        private static void Tag(string[] args)
        {
            RunGitCommand($"tag {string.Join(" ", args)}");
        }

        private static void Config(string[] args)
        {
            RunGitCommand($"config {string.Join(" ", args)}");
        }

        private static void Clean(string[] args)
        {
            string options = args.Length > 0 ? string.Join(" ", args) : "-fd";
            RunGitCommand($"clean {options}");
        }

        private static void Rm(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: git rm <文件>");
                return;
            }
            RunGitCommand($"rm {string.Join(" ", args)}");
        }

        private static void Mv(string[] args)
        {
            if (args.Length < 2)
            {
                UI.PrintError("用法: git mv <源> <目标>");
                return;
            }
            RunGitCommand($"mv {args[0]} {args[1]}");
        }

        private static void Show(string[] args)
        {
            string commit = args.Length > 0 ? args[0] : "HEAD";
            RunGitCommand($"show {commit}");
        }

        private static void Blame(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: git blame <文件>");
                return;
            }
            RunGitCommand($"blame {string.Join(" ", args)}");
        }
    }
}
using System;
using System.Diagnostics;
using LL;
using Renci.SshNet;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace LL
{
    internal static class RemoteCommands
    {
        private static SshClient? _sharedClient;

        public static void ExecuteRemote(string[] args)
        {
            string host, user, password;
            string command = null;

            if (args.Length == 0)
            {
                // 使用默认 SSH 配置
                string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
                if (!File.Exists(configPath))
                {
                    UI.PrintError("config.json 文件不存在。");
                    return;
                }
                var json = File.ReadAllText(configPath);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("SSH", out var ssh) || !ssh.TryGetProperty("Host", out var hostProp))
                {
                    UI.PrintError("SSH 配置未找到。");
                    return;
                }
                host = hostProp.GetString() ?? "";
                user = ssh.TryGetProperty("Username", out var userProp) ? userProp.GetString() ?? "root" : "root";
                password = ssh.TryGetProperty("Password", out var passProp) ? passProp.GetString() ?? "" : "";
                // 解密密码
                try { password = LL.SM4Helper.Decrypt(password); } catch { }
            }
            else
            {
                host = args[0];
                command = args.Length > 1 ? args[1] : null;
                user = args.Length > 2 ? args[2] : "root";
                password = args.Length > 3 ? args[3] : "";
            }

            try
            {
                SshClient client;

                // 重用共享客户端，如果数据库 SSH 已连接
                if (Program.SharedSSHConn != null && Program.SharedSSHConn.Client != null && Program.SharedSSHConn.Client.IsConnected)
                {
                    client = Program.SharedSSHConn.Client;
                }
                else
                {
                    // 重用或创建新的共享客户端
                    if (_sharedClient == null || !_sharedClient.IsConnected || _sharedClient.ConnectionInfo.Host != host || _sharedClient.ConnectionInfo.Username != user)
                    {
                        _sharedClient?.Disconnect();
                        _sharedClient = new SshClient(host, user, password);
                        _sharedClient.Connect();
                        if (!_sharedClient.IsConnected)
                        {
                            UI.PrintError("SSH 连接失败。");
                            return;
                        }
                    }
                    client = _sharedClient;
                }

                if (!string.IsNullOrEmpty(command))
                {
                    // 执行单个命令
                    using (var cmd = client.CreateCommand(command))
                    {
                        var result = cmd.Execute();
                        string output = cmd.Result;
                        string error = cmd.Error;

                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            UI.PrintSuccess("远程输出:");
                            Console.WriteLine(output);
                        }
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            UI.PrintError("远程错误:");
                            Console.WriteLine(error);
                        }
                        if (string.IsNullOrWhiteSpace(output) && string.IsNullOrWhiteSpace(error))
                        {
                            UI.PrintInfo("命令执行完成，无输出。");
                        }
                    }
                }
                else
                {
                    // 启动交互式 shell
                    UI.PrintInfo("启动远程交互式 shell。输入 'exit' 退出。");
                    using (var shell = client.CreateShellStream("xterm", 80, 24, 800, 600, 1024))
                    {
                        var reader = new StreamReader(shell);
                        var writer = new StreamWriter(shell) { AutoFlush = true };

                        // 读取初始输出
                        Task.Run(() =>
                        {
                            try
                            {
                                char[] buffer = new char[1024];
                                int read;
                                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    Console.Write(new string(buffer, 0, read));
                                }
                            }
                            catch { }
                        });

                        while (true)
                        {
                            Console.Write($"{user}@{host}$ ");
                            string input = Console.ReadLine();
                            if (input.ToLower() == "exit") break;

                            writer.WriteLine(input);

                            // 短暂等待输出
                            Thread.Sleep(200);
                        }
                    }
                    UI.PrintInfo("远程 shell 已退出。");
                }

                // 不断开连接，保持重用
            }
            catch (Exception ex)
            {
                UI.PrintError($"远程执行失败: {ex.Message}");
                _sharedClient?.Disconnect();
                _sharedClient = null;
            }
        }

        public static void HandleSSH(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: ssh <子命令> [参数]");
                UI.PrintInfo("子命令: exec <command>, shell, upload <local> <remote>, download <remote> <local>, status");
                return;
            }

            string subCommand = args[0].ToLower();
            string[] subArgs = args.Skip(1).ToArray();

            switch (subCommand)
            {
                case "exec":
                    if (subArgs.Length == 0)
                    {
                        UI.PrintError("用法: ssh exec <command>");
                        return;
                    }
                    ExecuteRemote(new[] { subArgs[0] }); // 使用默认配置执行命令
                    break;
                case "shell":
                    ExecuteRemote(new string[0]); // 启动 shell
                    break;
                case "upload":
                    if (subArgs.Length < 2)
                    {
                        UI.PrintError("用法: ssh upload <local_file> <remote_path>");
                        return;
                    }
                    UploadFile(subArgs[0], subArgs[1]);
                    break;
                case "download":
                    if (subArgs.Length < 2)
                    {
                        UI.PrintError("用法: ssh download <remote_file> <local_path>");
                        return;
                    }
                    DownloadFile(subArgs[0], subArgs[1]);
                    break;
                case "status":
                    CheckSSHStatus();
                    break;
                default:
                    UI.PrintError($"未知子命令: {subCommand}");
                    break;
            }
        }

        private static void UploadFile(string localPath, string remotePath)
        {
            try
            {
                if (_sharedClient == null || !_sharedClient.IsConnected)
                {
                    UI.PrintError("SSH 未连接。请先运行 ssh exec 或 ssh shell。");
                    return;
                }

                using (var sftp = new SftpClient(_sharedClient.ConnectionInfo))
                {
                    sftp.Connect();
                    using (var fileStream = new FileStream(localPath, FileMode.Open))
                    {
                        sftp.UploadFile(fileStream, remotePath);
                    }
                    sftp.Disconnect();
                }
                UI.PrintSuccess($"文件上传成功: {localPath} -> {remotePath}");
            }
            catch (Exception ex)
            {
                UI.PrintError($"上传失败: {ex.Message}");
            }
        }

        private static void DownloadFile(string remotePath, string localPath)
        {
            try
            {
                if (_sharedClient == null || !_sharedClient.IsConnected)
                {
                    UI.PrintError("SSH 未连接。请先运行 ssh exec 或 ssh shell。");
                    return;
                }

                using (var sftp = new SftpClient(_sharedClient.ConnectionInfo))
                {
                    sftp.Connect();
                    using (var fileStream = new FileStream(localPath, FileMode.Create))
                    {
                        sftp.DownloadFile(remotePath, fileStream);
                    }
                    sftp.Disconnect();
                }
                UI.PrintSuccess($"文件下载成功: {remotePath} -> {localPath}");
            }
            catch (Exception ex)
            {
                UI.PrintError($"下载失败: {ex.Message}");
            }
        }

        private static void CheckSSHStatus()
        {
            if (_sharedClient != null && _sharedClient.IsConnected)
            {
                UI.PrintSuccess($"SSH 已连接到 {_sharedClient.ConnectionInfo.Host} 用户 {_sharedClient.ConnectionInfo.Username}");
            }
            else
            {
                UI.PrintInfo("SSH 未连接。");
            }
        }
    }
}
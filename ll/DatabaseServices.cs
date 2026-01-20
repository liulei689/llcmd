using System.Threading.Channels;
using SqlSugar;
using Npgsql;
using System.IO;
using System.Threading;

namespace LL;

public static class LogManager
{
    private static readonly Channel<LogEntry> LogChannel = Channel.CreateUnbounded<LogEntry>();
    private static SqlSugarClient? DbClient;
    private static Task? ProcessorTask;

    public static void Initialize(string connString)
    {
        DbClient = new SqlSugarClient(new ConnectionConfig()
        {
            ConnectionString = connString,
            DbType = DbType.PostgreSQL,
            IsAutoCloseConnection = true,
            ConfigureExternalServices = new ConfigureExternalServices()
            {
                EntityService = (property, column) =>
                {
                    if (property.Name == "Id" && property.PropertyType == typeof(long))
                    {
                        column.IsPrimarykey = true;
                        column.IsIdentity = true;
                    }
                }
            },
            MoreSettings = new ConnMoreSettings()
            {
                IsAutoToUpper = false,
                DisableNvarchar = true,
                PgSqlIsAutoToLower = false
            }
        });

//        // 手动创建日志表，避免 SqlSugar CodeFirst 的动态代码生成问题
//        string createTableSql = @"
//CREATE TABLE IF NOT EXISTS ll_logs (
//    id BIGSERIAL PRIMARY KEY,
//    timestamp TIMESTAMP NOT NULL,
//    level VARCHAR(50) NOT NULL,
//    category VARCHAR(100) NOT NULL,
//    message TEXT NOT NULL,
//    user_name VARCHAR(100) NOT NULL,
//    machine VARCHAR(100) NOT NULL,
//    command VARCHAR(100)
//);
//";
//        try
//        {
//            DbClient.Ado.ExecuteCommand(createTableSql);
//            Console.WriteLine("日志表创建成功。");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"表创建失败: {ex.Message}");
//        }

        ProcessorTask = Task.Run(ProcessLogs);
    }

    public static void Log(string level, string category, string message, string? command = null)
    {
        if (DbClient == null) return; // 如果数据库未初始化，不记录

        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Category = category,
            Message = message,
            User = Environment.UserName,
            Machine = Environment.MachineName,
            Command = command
        };

        LogChannel.Writer.TryWrite(entry);
    }

    private static async Task ProcessLogs()
    {
        var batch = new List<LogEntry>();
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5)); // Batch every 5 seconds

        while (await LogChannel.Reader.WaitToReadAsync())
        {
            while (LogChannel.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
            }

            // Check if timer elapsed or batch is large
            if (await timer.WaitForNextTickAsync())
            {
                if (batch.Count > 0)
                {
                    try
                    {
                        await DbClient!.Insertable(batch).ExecuteCommandAsync();
                        batch.Clear();
                    }
                    catch (Exception ex)
                    {
                        // 数据库写入失败，写入本地文件
                        foreach (var logEntry in batch)
                        {
                            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "log.txt"), $"{logEntry.Timestamp}: {logEntry.Level} {logEntry.Category} {logEntry.Message}\r\n");
                        }
                        batch.Clear();
                        LogManager.Log("Error", "System", $"数据库日志写入失败，已写入本地文件: {ex.Message}");
                    }
                }
            }
        }

        // 处理剩余
        if (batch.Count > 0)
        {
            try
            {
                await DbClient!.Insertable(batch).ExecuteCommandAsync();
            }
            catch (Exception ex)
            {
                foreach (var logEntry in batch)
                {
                    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "log.txt"), $"{logEntry.Timestamp}: {logEntry.Level} {logEntry.Category} {logEntry.Message}\r\n");
                }
                LogManager.Log("Error", "System", $"数据库日志写入失败，已写入本地文件: {ex.Message}");
            }
        }
    }
}

public static class ListenManager
{
    public static bool IsListening { get; private set; } = false;
    public static string CurrentChannel { get; private set; } = "";
    public static Task? ListenTask { get; private set; }
    public static CancellationTokenSource? Cts { get; private set; }

    public static void StartListen(string channel, string connString)
    {
        if (IsListening)
        {
            UI.PrintInfo("监听已在运行。");
            return;
        }

        Cts = new CancellationTokenSource();
        ListenTask = Task.Run(() => ListenLoop(channel, connString, Cts));
        IsListening = true;
        CurrentChannel = channel;
        // 不在命令行直接输出启动信息，改为记录日志并由顶部状态显示
        LogManager.Log("Info", "Database", $"尝试启动监听频道 '{channel}'");
        Program.IsDBListening = true;
        Program.UpdateConsoleTitle();
    }

    public static void StopListen()
    {
        if (!IsListening)
        {
            UI.PrintInfo("当前没有正在监听。");
            return;
        }

        Cts?.Cancel();
        IsListening = false;
        string channel = CurrentChannel;
        CurrentChannel = "";
        // 不在命令行输出，记录日志并更新状态
        LogManager.Log("Info", "Database", $"停止监听频道 '{channel}'");
        Program.IsDBListening = false;
        Program.UpdateConsoleTitle();
    }

    private static async Task ListenLoop(string channel, string connString, CancellationTokenSource cts)
    {
        try
        {
            using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand($"LISTEN {channel}", conn);
            await cmd.ExecuteNonQueryAsync();

            // 监听成功，不在命令行输出，更新状态并记录日志
            Program.IsDBListening = true;
            Program.UpdateConsoleTitle();
            LogManager.Log("Info", "Database", $"监听频道 '{channel}' 成功");

            string machineName = Environment.MachineName;
            string osVersion = Environment.OSVersion.ToString();
            int processorCount = Environment.ProcessorCount;
            string startTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string message = $"程序已在 {machineName} 上启动。启动时间: {startTime}。系统: {osVersion}，CPU核心: {processorCount}";
            string escapedMessage = message.Replace("'", "''");
            using var notifyCmd = new NpgsqlCommand($"SELECT pg_notify('{channel}', '{escapedMessage}')", conn);
            await notifyCmd.ExecuteNonQueryAsync();

            conn.Notification += (o, e) => {
                UI.PrintInfo($"收到通知: {e.Payload}");
            };

            while (!cts.Token.IsCancellationRequested)
            {
                await conn.WaitAsync(cts.Token);
            }
        }
        catch (Exception ex)
        {
            // 不在命令行输出错误，记录日志并更新状态
            LogManager.Log("Error", "Database", $"监听失败: {ex.Message}");
            IsListening = false;
            CurrentChannel = "";
            Program.IsDBListening = false;
            Program.UpdateConsoleTitle();
        }
        finally
        {
            ListenTask = null;
            Cts = null;
        }
    }
}
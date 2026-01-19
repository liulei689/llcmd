using System.Threading.Channels;
using SqlSugar;
using Npgsql;
using System.IO;

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
        const int batchSize = 10; // 批量插入大小
        while (await LogChannel.Reader.WaitToReadAsync())
        {
            while (LogChannel.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
                if (batch.Count >= batchSize)
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
        UI.PrintSuccess($"已启动监听频道 '{channel}'。");
        LogManager.Log("Info", "Database", $"启动监听频道 '{channel}'");
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
        UI.PrintSuccess("已停止监听。");
        LogManager.Log("Info", "Database", $"停止监听频道 '{channel}'");
    }

    private static async Task ListenLoop(string channel, string connString, CancellationTokenSource cts)
    {
        try
        {
            using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand($"LISTEN {channel}", conn);
            await cmd.ExecuteNonQueryAsync();

            UI.PrintSuccess($"监听频道 '{channel}' 成功。");

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
            UI.PrintError($"监听失败: {ex.Message}");
            LogManager.Log("Error", "Database", $"监听失败: {ex.Message}");
            IsListening = false;
            CurrentChannel = "";
        }
        finally
        {
            ListenTask = null;
            Cts = null;
        }
    }
}
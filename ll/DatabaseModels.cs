using SqlSugar;

namespace LL;

[SugarTable("ll_logs")]
public class LogEntry
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "timestamp")]
    public DateTime Timestamp { get; set; }

    [SugarColumn(ColumnName = "level")]
    public string Level { get; set; } // Info, Error, Warning

    [SugarColumn(ColumnName = "category")]
    public string Category { get; set; } // Command, System, Database

    [SugarColumn(ColumnName = "message")]
    public string Message { get; set; }

    [SugarColumn(ColumnName = "user_name")]
    public string User { get; set; }

    [SugarColumn(ColumnName = "machine")]
    public string Machine { get; set; }

    [SugarColumn(ColumnName = "command")]
    public string? Command { get; set; } // Optional command name
}
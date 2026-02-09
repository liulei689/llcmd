using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniExcelLibs.Attributes;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace CheckInProject.PersonDataCore.Models
{
    public class RawPersonDataBase
    {
        public string? Name { get; set; }
        public required double[] FaceEncoding { get; set; }
        public uint? ClassID { get; set; }
        public Guid StudentID { get; set; }
        /// <summary>人脸图片（Base64存储）</summary>
        public string? ProfilePicture { get; set; }
        public StringPersonDataBase ConvertToStringPersonDataBase()
        {
            var encodingResult = string.Join(";", FaceEncoding.Select(p => p.ToString()).ToArray());
            return new StringPersonDataBase { FaceEncodingString = encodingResult, Name = Name, ClassID = ClassID, StudentID = StudentID, ProfilePicture = ProfilePicture };
        }
    }
    public class StringPersonDataBase
    {
        [ExcelColumnName("用户名")]
        public string? Name { get; set; }
        [ExcelIgnore]
        public required string FaceEncodingString { get; set; }
        [ExcelColumnName("编号")]
        public uint? ClassID { get; set; }
        [ExcelIgnore]
        [Key]
        public Guid StudentID { get; set; }
        /// <summary>人脸图片（Base64存储）</summary>
        [ExcelIgnore]
        public string? ProfilePicture { get; set; }
        public RawPersonDataBase ConvertToRawPersonDataBase()
        {
            var encodingResult = Array.ConvertAll(FaceEncodingString.Split(';'), double.Parse);
            return new RawPersonDataBase { FaceEncoding = encodingResult, Name = Name, ClassID = ClassID, StudentID = StudentID, ProfilePicture = ProfilePicture };
        }
    }

    public class StringPersonDataBaseContext : DbContext
    {
        public DbSet<StringPersonDataBase> PersonData { get; set; }

        public string DbPath { get; }

        public StringPersonDataBaseContext()
        {
            var path = Environment.CurrentDirectory;
            var targetPath = Path.Join(path, "PersonData.db");
            DbPath = targetPath;
            // 自动迁移数据库（添加新字段）
            EnsureMigrated();
        }

        private void EnsureMigrated()
        {
            try
            {
                // 如果数据库不存在，直接创建
                if (!File.Exists(DbPath))
                {
                    Database.EnsureCreated();
                    return;
                }

                // 检查是否需要添加 ProfilePicture 列
                using var connection = Database.GetDbConnection();
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('PersonData') WHERE name='ProfilePicture';";
                var result = command.ExecuteScalar();
                if (result is long count && count == 0)
                {
                    // 列不存在，添加新列
                    using var alterCmd = connection.CreateCommand();
                    alterCmd.CommandText = "ALTER TABLE PersonData ADD COLUMN ProfilePicture TEXT;";
                    alterCmd.ExecuteNonQuery();
                }
            }
            catch
            {
                // 如果迁移失败，尝试重新创建数据库
                try
                {
                    Database.EnsureCreated();
                }
                catch { }
            }
        }

        // The following configures EF to create a Sqlite database file in the
        // special "local" folder for your platform.
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={DbPath};Password=AccessDenied");
    }
}
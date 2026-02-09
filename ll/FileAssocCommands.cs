using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace LL
{
    /// <summary>
    /// 文件关联命令 - 注册.llv文件双击打开
    /// </summary>
    internal static class FileAssocCommands
    {
        private const string FileExtension = ".llv";
        private const string ProgId = "LL.VideoFile";

        public static void Handle(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("用法: assoc register    - 注册.llv文件关联");
                Console.WriteLine("       assoc unregister - 取消.llv文件关联");
                Console.WriteLine("       assoc status     - 查看关联状态");
                return;
            }

            string action = args[0].ToLower();

            // register/unregister 需要管理员权限
            if ((action == "register" || action == "r" || action == "unregister" || action == "u" || action == "remove") 
                && !ElevationCommands.IsAdministrator())
            {
                // 动态获取管理员权限
                if (ElevationCommands.RunElevatedCommand("assoc", args))
                {
                    UI.PrintInfo("已请求管理员权限(UAC)，已在新窗口执行 assoc");
                    return;
                }

                UI.PrintError("需要管理员权限");
                UI.PrintInfo("已取消操作");
                return;
            }

            try
            {
                switch (action)
                {
                    case "register":
                    case "r":
                        RegisterAssociation();
                        break;
                    case "unregister":
                    case "u":
                    case "remove":
                        UnregisterAssociation();
                        break;
                    case "status":
                    case "s":
                        ShowStatus();
                        break;
                    default:
                        Console.WriteLine($"[x] 未知操作: {action}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[x] 操作失败: {ex.Message}");
                UI.PrintInfo("请确保以管理员权限运行");
            }
        }

        private static void RegisterAssociation()
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            if (exePath.EndsWith(".dll"))
            {
                exePath = exePath.Replace(".dll", ".exe");
            }

            if (!File.Exists(exePath))
            {
                Console.WriteLine($"[x] 找不到程序文件: {exePath}");
                return;
            }

            // 创建 ProgId
            using (RegistryKey progKey = Registry.ClassesRoot.CreateSubKey(ProgId))
            {
                progKey.SetValue(null, "LL加密视频文件");
                progKey.SetValue("FriendlyTypeName", "LL加密视频文件");

                using (RegistryKey iconKey = progKey.CreateSubKey("DefaultIcon"))
                {
                    iconKey.SetValue(null, $"\"{exePath}\",0");
                }

                using (RegistryKey cmdKey = progKey.CreateSubKey("shell\\open\\command"))
                {
                    cmdKey.SetValue(null, $"\"{exePath}\" \"%1\"");
                }
            }

            // 关联扩展名
            using (RegistryKey extKey = Registry.ClassesRoot.CreateSubKey(FileExtension))
            {
                extKey.SetValue(null, ProgId);
                extKey.SetValue("Content Type", "application/octet-stream");
                extKey.SetValue("PerceivedType", "video");
            }

            // 刷新图标缓存
            FileAssocNativeMethods.SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);

            Console.WriteLine($"[√] 已注册 {FileExtension} 文件关联");
            Console.WriteLine($"    程序: {exePath}");
            Console.WriteLine($"    操作: 双击.llv文件将自动播放");
        }

        private static void UnregisterAssociation()
        {
            Registry.ClassesRoot.DeleteSubKeyTree(ProgId, false);
            Registry.ClassesRoot.DeleteSubKeyTree(FileExtension, false);

            FileAssocNativeMethods.SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);

            Console.WriteLine($"[√] 已取消 {FileExtension} 文件关联");
        }

        private static void ShowStatus()
        {
            using (RegistryKey extKey = Registry.ClassesRoot.OpenSubKey(FileExtension))
            {
                if (extKey == null)
                {
                    Console.WriteLine($"[!] {FileExtension} 未注册关联");
                    return;
                }

                string? progId = extKey.GetValue(null) as string;
                Console.WriteLine($"[i] {FileExtension} 关联到: {progId}");
            }

            using (RegistryKey progKey = Registry.ClassesRoot.OpenSubKey(ProgId))
            {
                if (progKey == null)
                {
                    Console.WriteLine($"[!] {ProgId} 不存在");
                    return;
                }

                string? desc = progKey.GetValue(null) as string;
                Console.WriteLine($"[i] 文件类型描述: {desc}");
            }

            using (RegistryKey? cmdKey = Registry.ClassesRoot.OpenSubKey($"{ProgId}\\shell\\open\\command"))
            {
                if (cmdKey != null)
                {
                    string? command = cmdKey.GetValue(null) as string;
                    Console.WriteLine($"[i] 打开命令: {command}");
                }
            }

            Console.WriteLine("[√] 关联状态正常");
        }
    }

    internal static class FileAssocNativeMethods
    {
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}

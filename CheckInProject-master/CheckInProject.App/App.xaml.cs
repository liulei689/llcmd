using CheckInProject.App.Models;
using CheckInProject.App.Pages;
using CheckInProject.CheckInCore.Implementation;
using CheckInProject.CheckInCore.Interfaces;
using CheckInProject.CheckInCore.Models;
using CheckInProject.PersonDataCore.Implementation;
using CheckInProject.PersonDataCore.Interfaces;
using CheckInProject.PersonDataCore.Models;
using FaceRecognitionDotNet;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CheckInProject.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public readonly IServiceProvider ServiceProvider;

        public static Frame? RootFrame = null;

        public static bool DisableDatabaseProtection = false;
        
        #region 控制台输出支持
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);
        
        private const int STD_OUTPUT_HANDLE = -11;
        private const int STD_ERROR_HANDLE = -12;
        private const int ATTACH_PARENT_PROCESS = -1;
        
        private static bool _consoleAllocated = false;
        
        /// <summary>
        /// 初始化控制台输出（用于权限验证模式）
        /// </summary>
        private static void InitConsoleOutput()
        {
            if (_consoleAllocated) return;
            
            // 尝试附加到父进程的控制台
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                // 如果失败，分配一个新的控制台
                AllocConsole();
            }
            
            // 重定向标准输出
            var stdout = GetStdHandle(STD_OUTPUT_HANDLE);
            if (stdout != IntPtr.Zero)
            {
                var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
            }
            
            _consoleAllocated = true;
        }
        
        /// <summary>
        /// 输出验证结果
        /// </summary>
        private static void WriteAuthOutput(AuthResult result)
        {
            if (IsAuthMode)
            {
                InitConsoleOutput();
            }
            Console.WriteLine(result.ToJson());
        }
        
        #endregion
        
        /// <summary>
        /// 是否为权限验证模式
        /// </summary>
        public static bool IsAuthMode { get; private set; } = false;
        
        /// <summary>
        /// 权限验证目标用户ID
        /// </summary>
        public static uint? AuthTargetUserId { get; private set; } = null;

        public App()
        {
            var service = new ServiceCollection();
            //For Databases
            service.AddDbContext<StringPersonDataBaseContext>();
            service.AddDbContext<CheckInDataModelContext>();
            //For services
            var faceRecognitionService = FaceRecognition.Create("FaceRecognitionModel");
            service.AddSingleton(faceRecognitionService);
            CascadeClassifier cascadeService = new CascadeClassifier("haarcascade_frontalface_alt.xml");
            service.AddSingleton(cascadeService);
            var applicationSettings = Settings.CreateSettings();
            service.AddSingleton(applicationSettings);
            service.AddSingleton<IPersonDatabaseManager, PersonDatabaseManager>();
            service.AddSingleton<ICheckInManager, CheckInManager>();
            service.AddSingleton<IFaceDataManager, FaceDataManager>();
            service.AddSingleton<List<RawPersonDataBase>>();
            // For UI
            service.AddSingleton<MainWindow>();
            service.AddSingleton<WelcomePage>();
            service.AddSingleton<ScanStaticPicturePage>();
            service.AddTransient<CheckInRecordsPage>();
            service.AddSingleton<ScanDynamicPicturePage>();
            service.AddSingleton<FaceDataManagementPage>();
            service.AddTransient<MultipleResultsPage>();
            service.AddTransient<UncheckedPeoplePage>();
            service.AddTransient<DatabaseManagementPage>();
            service.AddTransient<SetDatabasePasswordPage>();
            // For Auth Mode
            service.AddTransient<AuthVerifyPage>();
            service.AddTransient<AuthWindow>();
            ServiceProvider = service.BuildServiceProvider();
            App.Current.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // 解析命令行参数
            ParseCommandLineArgs(e.Args);
            
            // 异步初始化数据库，避免阻塞UI线程
            await Task.Run(() =>
            {
                ServiceProvider.GetRequiredService<StringPersonDataBaseContext>().Database.EnsureCreated();
                ServiceProvider.GetRequiredService<CheckInDataModelContext>().Database.EnsureCreated();
            });
            
            if (IsAuthMode)
            {
                // 权限验证模式：全屏窗口，验证通过后输出结果并退出
                RunAuthMode();
            }
            else
            {
                // 正常签到系统模式
                RunNormalMode();
            }
        }
        
        /// <summary>
        /// 解析命令行参数
        /// </summary>
        private void ParseCommandLineArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                
                if (arg == "--DisableDatabaseProtection")
                {
                    DisableDatabaseProtection = true;
                }
                else if (arg == "--auth" || arg == "-a")
                {
                    IsAuthMode = true;
                    // 检查下一个参数是否为目标用户ID
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        if (uint.TryParse(args[i + 1], out uint userId))
                        {
                            AuthTargetUserId = userId;
                        }
                        i++; // 跳过用户ID参数
                    }
                }
                else if (!arg.StartsWith("-"))
                {
                    // 直接传递用户ID作为参数（简化用法）
                    if (uint.TryParse(arg, out uint userId))
                    {
                        AuthTargetUserId = userId;
                        IsAuthMode = true;
                    }
                }
            }
        }
        
        /// <summary>
        /// 运行正常签到系统模式
        /// </summary>
        private void RunNormalMode()
        {
            ServiceProvider.GetRequiredService<MainWindow>()?.Show();
            var settings = ServiceProvider.GetRequiredService<Settings>();
            if (settings.IsFirstRun && string.IsNullOrEmpty(settings.PasswordMD5))
            {
                RootFrame?.Navigate(ServiceProvider.GetRequiredService<SetDatabasePasswordPage>());
            }
            else
            {
                // 启动后进入欢迎页
                RootFrame?.Navigate(ServiceProvider.GetRequiredService<WelcomePage>());
            }
        }
        
        /// <summary>
        /// 运行权限验证模式
        /// </summary>
        private void RunAuthMode()
        {
            // 检查人脸数据库是否为空
            var personDb = ServiceProvider.GetRequiredService<IPersonDatabaseManager>();
            if (personDb.GetFaceData().Count == 0)
            {
                // 输出错误结果到stdout
                var errorResult = new AuthResult
                {
                    Success = false,
                    ErrorMessage = "人脸数据库为空",
                    AuthTime = DateTime.Now
                };
                WriteAuthOutput(errorResult);
                
                // 写入文件供调用方读取
                WriteAuthResultToFile(errorResult);
                
                Shutdown(1);
                return;
            }
            
            // 创建并显示全屏权限验证窗口（使用ShowDialog阻塞等待）
            var authWindow = ServiceProvider.GetRequiredService<AuthWindow>();
            authWindow.TargetUserId = AuthTargetUserId;
            authWindow.StartAuth();
            
            // 窗口关闭后处理结果
            authWindow.Closed += (s, e) =>
            {
                var result = authWindow.AuthResult;
                if (result != null)
                {
                    // 输出JSON结果到stdout
                    WriteAuthOutput(result);
                    
                    // 写入文件供调用方读取
                    WriteAuthResultToFile(result);
                    
                    // 根据验证结果设置退出码
                    Shutdown(result.Success ? 0 : 1);
                }
                else
                {
                    // 用户取消或其他原因
                    var cancelResult = new AuthResult
                    {
                        Success = false,
                        ErrorMessage = "验证被取消",
                        AuthTime = DateTime.Now
                    };
                    WriteAuthOutput(cancelResult);
                    WriteAuthResultToFile(cancelResult);
                    Shutdown(1);
                }
            };
            
            // 使用ShowDialog显示为模态对话框
            authWindow.ShowDialog();
        }
        
        /// <summary>
        /// 将验证结果写入临时文件，方便其他程序读取
        /// </summary>
        private void WriteAuthResultToFile(AuthResult result)
        {
            try
            {
                var tempFile = Path.Combine(Path.GetTempPath(), $"CheckInAuth_{Environment.ProcessId}.json");
                File.WriteAllText(tempFile, result.ToJson());
            }
            catch
            {
                // 忽略文件写入错误
            }
        }
        private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"发生了未经处理的异常:\n{e.Exception.Message}", "未经处理的异常", MessageBoxButton.OK,
                             MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}

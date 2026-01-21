using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;

namespace ClipboardMonitor
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Hide the main form
            var form = new Form();
            form.ShowInTaskbar = false;
            form.WindowState = FormWindowState.Minimized;
            form.Visible = false;

            // Start clipboard monitoring
            ClipboardMonitor.StartMonitoring();

            // Run the application
            Application.Run(form);
        }
    }

    public static class ClipboardMonitor
    {
        private static string _presetFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "tools", "preset.txt");
        private static string _lastClipboardText = "";
        private static System.Timers.Timer? _timer;

        public static void StartMonitoring()
        {
            _timer = new System.Timers.Timer(500); // 每500ms检查一次
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = true;
            _timer.Enabled = true;

            Console.WriteLine("Clipboard monitoring started.");
        }

        public static void StopMonitoring()
        {
            _timer?.Stop();
            _timer?.Dispose();
        }

        private static void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            Task.Run(() =>
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        string currentText = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(currentText) && currentText != _lastClipboardText)
                        {
                            _lastClipboardText = currentText;
                            Console.WriteLine($"Clipboard text changed: {currentText}");
                            UpdatePresetText(currentText);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Clipboard monitoring error: {ex.Message}");
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            });
        }

        private static void UpdatePresetText(string clipboardText)
        {
            string newText = "";
            if (clipboardText.Contains("ai", StringComparison.OrdinalIgnoreCase))
            {
                newText = "这个项目（LL CLI TOOL）有明确的规则和指南，主要记录在 docs 文件夹下的文档中（如 CONTRIBUTING.md 和 Functions.md）。AI在生成或修改代码时，必须严格遵守这些规则，以确保代码质量、风格一致性和功能兼容性。以下是规则的清晰描述，AI绝不能违反这些规定。";
            }
            else if (clipboardText.Contains("美女", StringComparison.OrdinalIgnoreCase))
            {
                newText = "你的笑容如春日暖阳，照亮了我的世界；美得让人心动不已。你的眼睛如星辰般闪烁，蕴含着智慧与温柔；让人一眼便沉醉其中。你的声音如天籁般悦耳，温暖而动听；每一次倾听都是一种享受。你的气质如兰花般高雅，散发着迷人的魅力；让人不由自主地被吸引。你的爱心如阳光般温暖，包容而无私；让周围的人都感受到幸福。你的才华如繁星般璀璨，多才多艺而令人钦佩；每一次展现都让人惊叹不已。你的坚持如磐石般坚定，面对困难从不退缩；让人敬佩不已。你的善良如春风般和煦，总是为他人着想；让人心生感激。你的美丽不仅仅是外表，更是内心深处散发出的光辉；让人永远铭记在心。";
            }
            else
            {
                // 添加更多规则，根据剪贴板内容扩展
                if (clipboardText.Contains("代码", StringComparison.OrdinalIgnoreCase))
                {
                    newText = "代码必须遵循 PascalCase 命名约定，类和方法使用大写字母开头。所有公共方法需要 XML 注释，描述功能和参数。异常处理必须使用 try-catch，不得吞没异常。异步操作必须使用 async/await，并支持 CancellationToken。代码风格要求 4 空格缩进，行长不超过 120 字符。";
                }
                else if (clipboardText.Contains("架构", StringComparison.OrdinalIgnoreCase))
                {
                    newText = "项目采用模块化设计，每个功能独立为类，确保低耦合。配置存储在 config.json 中，支持热重载。日志使用 LogManager.Log，级别为 Info 或 Error。测试必须通过 run_build，无编译错误。";
                }
                else if (clipboardText.Contains("安全", StringComparison.OrdinalIgnoreCase))
                {
                    newText = "密码必须加密存储，使用 BouncyCastle 库。P/Invoke 调用必须安全，避免缓冲区溢出。程序不得因异常退出，必须记录日志。";
                }
                else if (clipboardText.Contains("性能", StringComparison.OrdinalIgnoreCase))
                {
                    newText = "避免阻塞操作，使用异步 I/O。内存使用谨慎，避免泄漏。任务使用 Task.Run 和 CancellationToken。";
                }
                else
                {
                    // 默认或其他情况
                    newText = "请遵守项目的编码标准和架构约束，确保代码质量和一致性。";
                }
            }

            if (!string.IsNullOrEmpty(newText))
            {
                try
                {
                    File.WriteAllText(_presetFilePath, newText, Encoding.UTF8);
                    Console.WriteLine($"Preset text updated based on clipboard: {clipboardText}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to update preset.txt: {ex.Message}");
                }
            }
        }
    }
}
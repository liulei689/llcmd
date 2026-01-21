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
            
            // AI 项目规则
            if (clipboardText.Contains("ai", StringComparison.OrdinalIgnoreCase))
            {
                newText = "这个项目（LL CLI TOOL）有明确的规则和指南，主要记录在 docs 文件夹下的文档中（如 CONTRIBUTING.md 和 Functions.md）。AI在生成或修改代码时，必须严格遵守这些规则，以确保代码质量、风格一致性和功能兼容性。以下是规则的清晰描述，AI绝不能违反这些规定。";
            }
            // 赞美语
            else if (clipboardText.Contains("美女", StringComparison.OrdinalIgnoreCase))
            {
                newText = "你的笑容如春日暖阳，照亮了我的世界；美得让人心动不已。你的眼睛如星辰般闪烁，蕴含着智慧与温柔；让人一眼便沉醉其中。你的声音如天籁般悦耳，温暖而动听；每一次倾听都是一种享受。你的气质如兰花般高雅，散发着迷人的魅力；让人不由自主地被吸引。你的爱心如阳光般温暖，包容而无私；让周围的人都感受到幸福。你的才华如繁星般璀璨，多才多艺而令人钦佩；每一次展现都让人惊叹不已。你的坚持如磐石般坚定，面对困难从不退缩；让人敬佩不已。你的善良如春风般和煦，总是为他人着想；让人心生感激。你的美丽不仅仅是外表，更是内心深处散发出的光辉；让人永远铭记在心。";
            }
            // 开发相关
            else if (clipboardText.Contains("bug", StringComparison.OrdinalIgnoreCase))
            {
                newText = "## Bug 报告\n\n**问题描述：**\n[简要描述问题]\n\n**复现步骤：**\n1. \n2. \n3. \n\n**预期行为：**\n[描述预期结果]\n\n**实际行为：**\n[描述实际结果]\n\n**环境信息：**\n- 操作系统：\n- 版本号：\n- 其他相关信息：\n\n**截图/日志：**\n[如有请附上]";
            }
            else if (clipboardText.Contains("commit", StringComparison.OrdinalIgnoreCase))
            {
                newText = "feat: 添加新功能\nfix: 修复bug\nrefactor: 重构代码\ndocs: 更新文档\nstyle: 代码格式调整\ntest: 添加测试\nchore: 构建/工具链更新";
            }
            else if (clipboardText.Contains("review", StringComparison.OrdinalIgnoreCase))
            {
                newText = "## 代码审查\n\n**优点：**\n- \n\n**建议改进：**\n- \n\n**潜在问题：**\n- \n\n**总体评价：**\n[LGTM / 需要修改]";
            }
            else if (clipboardText.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                newText = "## 测试用例\n\n**测试场景：**\n[描述测试场景]\n\n**前置条件：**\n- \n\n**测试步骤：**\n1. \n2. \n3. \n\n**预期结果：**\n- \n\n**实际结果：**\n- \n\n**测试状态：**\n[通过/失败]";
            }
            else if (clipboardText.Contains("api", StringComparison.OrdinalIgnoreCase))
            {
                newText = "## API 文档\n\n**接口名称：**\n[接口名]\n\n**请求方法：**\nGET/POST/PUT/DELETE\n\n**请求路径：**\n/api/v1/\n\n**请求参数：**\n```json\n{\n  \"param1\": \"value1\",\n  \"param2\": \"value2\"\n}\n```\n\n**响应示例：**\n```json\n{\n  \"code\": 200,\n  \"message\": \"success\",\n  \"data\": {}\n}\n```\n\n**错误码：**\n- 200: 成功\n- 400: 参数错误\n- 500: 服务器错误";
            }
            else if (clipboardText.Contains("代码", StringComparison.OrdinalIgnoreCase))
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
            // 日常工作
            else if (clipboardText.Contains("会议", StringComparison.OrdinalIgnoreCase))
            {
                newText = "## 会议纪要\n\n**会议主题：**\n[主题]\n\n**时间：**\n[日期 时间]\n\n**参会人员：**\n- \n\n**会议内容：**\n1. \n2. \n3. \n\n**决议事项：**\n- \n\n**待办事项：**\n- [ ] 任务1 - 负责人 - 截止日期\n- [ ] 任务2 - 负责人 - 截止日期\n\n**下次会议：**\n[日期 时间]";
            }
            else if (clipboardText.Contains("邮件", StringComparison.OrdinalIgnoreCase))
            {
                newText = "尊敬的 [收件人]：\n\n您好！\n\n[正文内容]\n\n如有任何问题，请随时与我联系。\n\n此致\n敬礼！\n\n[发件人]\n[日期]";
            }
            else if (clipboardText.Contains("报告", StringComparison.OrdinalIgnoreCase))
            {
                newText = "## 工作报告\n\n**报告周期：**\n[开始日期] - [结束日期]\n\n**本周完成工作：**\n1. \n2. \n3. \n\n**工作进展：**\n- 项目A：进度 X%\n- 项目B：进度 Y%\n\n**遇到的问题：**\n- \n\n**下周计划：**\n1. \n2. \n3. \n\n**需要支持：**\n- ";
            }
            else if (clipboardText.Contains("计划", StringComparison.OrdinalIgnoreCase))
            {
                newText = "## 工作计划\n\n**计划周期：**\n[开始日期] - [结束日期]\n\n**目标：**\n- \n\n**主要任务：**\n1. [ ] 任务1 - 优先级：高/中/低 - 预计完成时间：\n2. [ ] 任务2 - 优先级：高/中/低 - 预计完成时间：\n3. [ ] 任务3 - 优先级：高/中/低 - 预计完成时间：\n\n**资源需求：**\n- \n\n**风险评估：**\n- ";
            }
            // 技术文档
            else if (clipboardText.Contains("readme", StringComparison.OrdinalIgnoreCase))
            {
                newText = "# 项目名称\n\n## 简介\n[项目简介]\n\n## 功能特性\n- 功能1\n- 功能2\n- 功能3\n\n## 安装\n```bash\n# 安装命令\n```\n\n## 使用方法\n```bash\n# 使用示例\n```\n\n## 配置\n[配置说明]\n\n## 贡献\n欢迎提交 Issue 和 Pull Request\n\n## 许可证\n[许可证类型]";
            }
            else if (clipboardText.Contains("changelog", StringComparison.OrdinalIgnoreCase))
            {
                newText = "# 更新日志\n\n## [版本号] - YYYY-MM-DD\n\n### 新增\n- \n\n### 修复\n- \n\n### 改进\n- \n\n### 移除\n- ";
            }
            else if (clipboardText.Contains("todo", StringComparison.OrdinalIgnoreCase))
            {
                newText = "## 待办事项\n\n### 紧急重要\n- [ ] \n\n### 重要不紧急\n- [ ] \n\n### 紧急不重要\n- [ ] \n\n### 不紧急不重要\n- [ ] ";
            }
            // 礼貌用语
            else if (clipboardText.Contains("感谢", StringComparison.OrdinalIgnoreCase))
            {
                newText = "非常感谢您的帮助和支持！您的付出让我们的工作更加顺利。再次表示衷心的感谢！";
            }
            else if (clipboardText.Contains("抱歉", StringComparison.OrdinalIgnoreCase))
            {
                newText = "非常抱歉给您带来不便。我们会尽快解决这个问题，感谢您的理解和耐心。";
            }
            else if (clipboardText.Contains("问候", StringComparison.OrdinalIgnoreCase))
            {
                newText = "您好！希望您一切顺利。如有任何需要帮助的地方，请随时告诉我。祝您工作愉快！";
            }
            // SQL 相关
            else if (clipboardText.Contains("sql", StringComparison.OrdinalIgnoreCase))
            {
                newText = "-- 查询模板\nSELECT * FROM table_name WHERE condition;\n\n-- 插入模板\nINSERT INTO table_name (column1, column2) VALUES (value1, value2);\n\n-- 更新模板\nUPDATE table_name SET column1 = value1 WHERE condition;\n\n-- 删除模板\nDELETE FROM table_name WHERE condition;";
            }
            // 注释模板
            else if (clipboardText.Contains("注释", StringComparison.OrdinalIgnoreCase))
            {
                newText = "/// <summary>\n/// [方法描述]\n/// </summary>\n/// <param name=\"param1\">[参数1描述]</param>\n/// <param name=\"param2\">[参数2描述]</param>\n/// <returns>[返回值描述]</returns>";
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

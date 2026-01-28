using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LL;

public static class TemplateCommands
{
    public static void Run(string[] args)
    {
        if (args.Length < 1)
        {
            UI.PrintError("用法: template <type> [projectName]");
            UI.PrintInfo("支持类型: c/console, w/webapi, lib/classlib, blazorwasm, blazorserver, mstest/ms, nunit/nu, xunit/xu");
            return;
        }

        var type = args[0].ToLower();
        var projectName = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : GenerateProjectName(type);
        var targetDir = Directory.GetCurrentDirectory(); // 默认当前目录

        // 自动重命名如果目录存在
        var projectDir = Path.Combine(targetDir, projectName);
        int counter = 1;
        while (Directory.Exists(projectDir))
        {
            projectName = $"{GenerateProjectName(type)}{counter}";
            projectDir = Path.Combine(targetDir, projectName);
            counter++;
        }

        try
        {
            // 调用 dotnet new 创建项目
            var templateName = GetTemplateName(type);
            if (string.IsNullOrEmpty(templateName))
            {
                UI.PrintError($"不支持的类型: {type}");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"new {templateName} -n {projectName} -o \"{projectDir}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                UI.PrintSuccess($"项目创建成功: {projectDir}");

                // 稳定打开项目或文件夹
                OpenProjectOrFolder(projectDir);
            }
            else
            {
                var error = process.StandardError.ReadToEnd();
                UI.PrintError($"创建失败: {error}");
            }
        }
        catch (Exception ex)
        {
            UI.PrintError($"创建失败: {ex.Message}");
        }
    }

    private static string GenerateProjectName(string type)
    {
        var normalizedType = NormalizeType(type);
        return normalizedType switch
        {
            "console" => "ConsoleApp",
            "webapi" => "WebApi",
            "classlib" => "ClassLibrary",
            "blazorwasm" => "BlazorWasm",
            "blazorserver" => "BlazorServer",
            "mstest" => "TestProject",
            "nunit" => "TestProject",
            "xunit" => "TestProject",
            _ => "MyProject"
        };
    }

    private static string GetTemplateName(string type)
    {
        var normalizedType = NormalizeType(type);
        return normalizedType switch
        {
            "console" => "console",
            "webapi" => "webapi",
            "classlib" => "classlib",
            "blazorwasm" => "blazorwasm",
            "blazorserver" => "blazor",
            "mstest" => "mstest",
            "nunit" => "nunit",
            "xunit" => "xunit",
            _ => null
        };
    }

    private static string NormalizeType(string type)
    {
        return type switch
        {
            "c" => "console",
            "w" => "webapi",
            "lib" => "classlib",
            "blazor" => "blazorwasm",
            "server" => "blazorserver",
            "mstest" or "ms" => "mstest",
            "nunit" or "nu" => "nunit",
            "xunit" or "xu" => "xunit",
            _ => type
        };
    }

    private static void OpenProjectOrFolder(string projectDir)
    {
        var vsPath = @"C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe"; // VS 2022 Insiders/Preview
        if (!File.Exists(vsPath))
        {
            vsPath = @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe";
        }
        if (!File.Exists(vsPath))
        {
            vsPath = @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe";
        }
        if (!File.Exists(vsPath))
        {
            vsPath = @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe";
        }

        if (File.Exists(vsPath))
        {
            var slnFile = Directory.EnumerateFiles(projectDir, "*.csproj").FirstOrDefault();
            if (slnFile != null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = vsPath,
                    Arguments = $"\"{slnFile}\"",
                    UseShellExecute = true
                });
                UI.PrintInfo("已打开项目");
                return;
            }
        }

        // 打开文件夹
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{projectDir}\"",
            UseShellExecute = true
        });
        UI.PrintInfo("已打开项目文件夹");
    }
}
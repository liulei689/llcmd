using System;
using System.Diagnostics;
using Microsoft.Win32;
using LL;

namespace LL
{
    internal static class ProxyCommands
    {
        public static void CheckProxy(string[] args)
        {
            try
            {
                UI.PrintHeader("系统代理检测");

                // 重点检查 WinINet (系统代理设置)
                bool systemProxyEnabled = false;
                string proxyDetails = "";

                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                    {
                        if (key != null)
                        {
                            object proxyEnable = key.GetValue("ProxyEnable");
                            object proxyServer = key.GetValue("ProxyServer");
                            object autoConfig = key.GetValue("AutoConfigURL");

                            if (proxyEnable is int pe && pe == 1)
                            {
                                systemProxyEnabled = true;
                                proxyDetails = $"ProxyServer: {proxyServer?.ToString() ?? "未设置"}";
                            }
                            else if (autoConfig != null && !string.IsNullOrWhiteSpace(autoConfig.ToString()))
                            {
                                systemProxyEnabled = true;
                                proxyDetails = $"AutoConfigURL: {autoConfig.ToString()}";
                            }
                        }
                    }
                }
                catch { }

                if (systemProxyEnabled)
                {
                    UI.PrintSuccess($"系统代理已开启 ({proxyDetails})");
                }
                else
                {
                    UI.PrintInfo("系统代理未开启");
                }

                // 可选：简要检查其他代理源
                string httpEnv = Environment.GetEnvironmentVariable("HTTP_PROXY") ?? Environment.GetEnvironmentVariable("http_proxy");
                if (!string.IsNullOrWhiteSpace(httpEnv))
                {
                    UI.PrintInfo($"环境变量代理: HTTP_PROXY={httpEnv}");
                }

                try
                {
                    var psi = new ProcessStartInfo("netsh", "winhttp show proxy")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        if (!string.IsNullOrWhiteSpace(output) && !output.Contains("Direct access (no proxy server)", StringComparison.OrdinalIgnoreCase))
                        {
                            UI.PrintInfo("WinHTTP 代理设置存在");
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                UI.PrintError($"检测失败: {ex.Message}");
            }
        }
    }
}

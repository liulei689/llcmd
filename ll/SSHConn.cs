using Renci.SshNet;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace LL;

public class SSHConn
{
    private SshClient? _sshClient;
    private ForwardedPortLocal? _forwardedPort;
    public int LocalPort { get; private set; } = 5433;

    public bool OpenDbPort()
    {
        try
        {
            // 从配置获取SSH信息，假设在config.json中添加SSH部分
            var builder = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("config.json", optional: true, reloadOnChange: true);
            var config = builder.Build();

            string sshHost = config["SSH:Host"] ?? "47.108.141.51"; // 默认使用数据库主机
            int sshPort = int.Parse(config["SSH:Port"] ?? "22");
            string sshUser = config["SSH:Username"] ?? "root"; // 需要配置
            string sshPassword = config["SSH:Password"] ?? ""; // 需要配置

            string dbHost = config["SSH:Host"] ?? "47.108.141.51";
            int dbPort = 5432; // 数据库端口固定为5432

            // 自动分配本地端口，从5433开始
            LocalPort = FindAvailablePort(5433);

            _sshClient = new SshClient(sshHost, sshPort, sshUser, sshPassword);
            _sshClient.Connect();

            // 本地端口转发，例如本地localPort -> 远程dbHost:dbPort
            _forwardedPort = new ForwardedPortLocal("127.0.0.1", (uint)LocalPort, dbHost, (uint)dbPort);
            _sshClient.AddForwardedPort(_forwardedPort);
            _forwardedPort.Start();

            UI.PrintSuccess($"SSH隧道已建立: 本地 127.0.0.1:{LocalPort} -> 远程 {dbHost}:{dbPort}");
            return true;
        }
        catch (Exception ex)
        {
            UI.PrintError($"SSH隧道建立失败: {ex.Message}");
            return false;
        }
    }

    private static int FindAvailablePort(int startPort)
    {
        for (int port = startPort; port < 65535; port++)
        {
            try
            {
                using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch
            {
                // 端口占用，继续
            }
        }
        throw new Exception("未找到可用端口");
    }

    public void CloseDbPort()
    {
        if (_forwardedPort != null)
        {
            _forwardedPort.Stop();
            _forwardedPort = null;
        }
        if (_sshClient != null && _sshClient.IsConnected)
        {
            _sshClient.Disconnect();
            _sshClient = null;
        }
        UI.PrintInfo("SSH隧道已关闭。");
    }
}
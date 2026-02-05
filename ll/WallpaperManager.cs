using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace LL;

/// <summary>
/// 壁纸管理器 - 支持本地壁纸切换、Bing每日美图、在线随机美女壁纸
/// </summary>
public static class WallpaperManager
{
    private static readonly HttpClient _httpClient = new();
    private static readonly string _wallpaperFolder;
    private static readonly string _historyFile;
    private static readonly string _configFile;
    private static readonly string _configKey = "WallpaperFolder";
    
    // 自动切换相关
    private static Timer? _autoSwitchTimer;
    private static CancellationTokenSource? _autoSwitchCts;
    private static string _currentSource = "btstu";
    private static int _intervalMinutes = 5;
    private static bool _isRunning = false;
    private static readonly object _lockObj = new();

    static WallpaperManager()
    {
        _wallpaperFolder = ConfigManager.GetValue(_configKey, 
            Path.Combine(AppContext.BaseDirectory, "wallpapers"));
        _historyFile = Path.Combine(AppContext.BaseDirectory, "wallpaper_history.json");
        _configFile = Path.Combine(AppContext.BaseDirectory, "wallpaper_config.json");
        
        // 加载配置
        LoadConfig();
    }

    /// <summary>
    /// 壁纸源配置 - 已验证可用 (2025-02-05)
    /// </summary>
    private static readonly Dictionary<string, (string Name, string Url, string Desc, bool IsJsonApi)> _sources = new()
    {
        // ========== 直接返回图片的API ==========
        ["paugram"] = ("保罗", "https://api.paugram.com/wallpaper/", "高清壁纸聚合", false),
        ["paugram-gh"] = ("保罗-GH", "https://api.paugram.com/wallpaper/?source=gh", "高清壁纸-Github源", false),
        ["paugram-sm"] = ("保罗-SM", "https://api.paugram.com/wallpaper/?source=sm", "高清壁纸-第三方源", false),
        ["paugram-mg"] = ("保罗-MG", "https://api.paugram.com/wallpaper/?source=mg", "高清壁纸-美图源", false),
        ["paugram-wh"] = ("保罗-WH", "https://api.paugram.com/wallpaper/?source=wallhaven", "高清壁纸-Wallhaven源", false),
        ["paugram-bing"] = ("保罗-Bing", "https://api.paugram.com/wallpaper/?source=bing", "Bing每日壁纸", false),
        ["paugram-us"] = ("保罗-US", "https://api.paugram.com/wallpaper/?source=unsplash", "Unsplash高清壁纸", false),
        ["paugram-dm"] = ("保罗-DM", "https://api.paugram.com/wallpaper/?source=dm", "动漫壁纸", false),
        ["mtyqx"] = ("墨天逸", "https://api.mtyqx.cn/api/random.php", "高清动漫/美女", false),
        ["picre"] = ("PicRe", "https://pic.re/images", "随机高清壁纸", false),
        
        // ========== 返回JSON的API (需要解析) ==========
        ["lolicon"] = ("Lolicon", "https://api.lolicon.app/setu/v2?r18=0&num=1&size=regular", "二次元图库 (R18=0)", true),
        ["waifu"] = ("Waifu", "https://api.waifu.im/search?included_tags=waifu&is_nsfw=false", "二次元萌图", true),
        ["waifu-maid"] = ("Waifu-女仆", "https://api.waifu.im/search?included_tags=maid&is_nsfw=false", "二次元女仆", true),
        ["waifu-uniform"] = ("Waifu-制服", "https://api.waifu.im/search?included_tags=uniform&is_nsfw=false", "二次元制服", true),
        ["nekos"] = ("Nekos-猫娘", "https://nekos.best/api/v2/neko", "二次元猫娘", true),
        ["nekos-waifu"] = ("Nekos-二次元", "https://nekos.best/api/v2/waifu", "二次元萌图", true),
        ["nekos-cuddle"] = ("Nekos-萌系", "https://nekos.best/api/v2/cuddle", "二次元萌系", true),
        
        // ========== 已失效/不稳定的API (保留记录) ==========
        // ["btstu"] = ("搏天", "https://api.btstu.cn/sjbz/api.php?lx=dongman&format=images", "二次元萌图", false), // 已超时
        // ["btstu2"] = ("搏天2", "https://api.btstu.cn/sjbz/api.php?lx=meizi&format=images", "美女壁纸", false), // 已超时
        // ["dmoe"] = ("萌化", "https://www.dmoe.cc/random.php", "二次元随机", false), // 404
        // ["zj"] = ("只因", "https://api.zxki.cn/api/jitang?type=image", "美女壁纸", false), // 不稳定
    };

    /// <summary>
    /// 处理壁纸命令
    /// </summary>
    public static void Handle(string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return;
        }

        var subCommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();

        try
        {
            switch (subCommand)
            {
                case "set":
                case "s":
                    SetWallpaper(subArgs);
                    break;
                case "random":
                case "r":
                    SetRandomWallpaper();
                    break;
                case "online":
                case "o":
                    SetOnlineWallpaper(subArgs);
                    break;
                case "bing":
                case "b":
                    DownloadAndSetBingWallpaper(subArgs);
                    break;
                case "folder":
                case "f":
                    SetWallpaperFolder(subArgs);
                    break;
                case "list":
                case "l":
                    ListWallpapers();
                    break;
                case "history":
                case "h":
                    ShowHistory();
                    break;
                case "prev":
                case "p":
                    SetPreviousWallpaper();
                    break;
                case "mode":
                case "m":
                    SetWallpaperStyle(subArgs);
                    break;
                case "auto":
                case "a":
                    HandleAutoSwitch(subArgs);
                    break;
                case "source":
                    HandleSource(subArgs);
                    break;
                case "sources":
                    ListSources();
                    break;
                default:
                    UI.PrintError($"未知子命令: {subCommand}");
                    ShowUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            UI.PrintError($"壁纸操作失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 显示用法
    /// </summary>
    private static void ShowUsage()
    {
        UI.PrintHeader("壁纸管理器");
        UI.PrintItem("wallpaper set <path>", "设置指定图片为壁纸");
        UI.PrintItem("wallpaper random", "随机切换本地壁纸");
        UI.PrintItem("wallpaper online [source]", "设置在线随机壁纸 (默认搏天)");
        UI.PrintItem("wallpaper bing [save]", "下载 Bing 每日美图");
        UI.PrintItem("wallpaper auto <start|stop|status>", "自动切换壁纸");
        UI.PrintItem("wallpaper source [name]", "查看/设置壁纸源");
        UI.PrintItem("wallpaper sources", "列出所有可用壁纸源");
        UI.PrintItem("wallpaper folder [path]", "查看/设置壁纸文件夹");
        UI.PrintItem("wallpaper list", "列出本地壁纸");
        UI.PrintItem("wallpaper history", "查看切换历史");
        UI.PrintItem("wallpaper prev", "切换到上一张壁纸");
        UI.PrintItem("wallpaper mode <fill|fit|stretch|tile|center|span>", "设置显示模式");
        UI.PrintInfo("当前壁纸源: " + _sources[_currentSource].Name + " - " + _sources[_currentSource].Desc);
        UI.PrintInfo("自动切换间隔: " + _intervalMinutes + " 分钟");
        UI.PrintInfo("自动切换状态: " + (_isRunning ? "运行中" : "已停止"));
    }

    /// <summary>
    /// 列出所有壁纸源
    /// </summary>
    private static void ListSources()
    {
        UI.PrintHeader("可用壁纸源");
        foreach (var kv in _sources)
        {
            var marker = kv.Key == _currentSource ? "[当前] " : "";
            UI.PrintItem($"{kv.Key,-10}", $"{marker}{kv.Value.Name} - {kv.Value.Desc}");
        }
    }

    /// <summary>
    /// 处理壁纸源设置
    /// </summary>
    private static void HandleSource(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintResult("当前壁纸源", $"{_currentSource} ({_sources[_currentSource].Name} - {_sources[_currentSource].Desc})");
            ListSources();
            return;
        }

        var source = args[0].ToLowerInvariant();
        if (!_sources.ContainsKey(source))
        {
            UI.PrintError($"未知壁纸源: {source}");
            UI.PrintInfo("可用源: " + string.Join(", ", _sources.Keys));
            return;
        }

        _currentSource = source;
        SaveConfig();
        UI.PrintSuccess($"已设置壁纸源: {_sources[source].Name} ({_sources[source].Desc})");
    }

    /// <summary>
    /// 设置在线随机壁纸
    /// </summary>
    public static async void SetOnlineWallpaper(string[] args)
    {
        var source = args.Length > 0 ? args[0].ToLowerInvariant() : _currentSource;
        
        if (!_sources.ContainsKey(source))
        {
            UI.PrintError($"未知壁纸源: {source}");
            return;
        }

        try
        {
            UI.PrintInfo($"正在从 {_sources[source].Name} 获取壁纸...");

            // 创建临时文件路径
            var tempFile = Path.Combine(Path.GetTempPath(), $"wallpaper_{Guid.NewGuid()}.jpg");

            // 添加随机参数避免缓存
            var url = _sources[source].Url;
            url = url + (url.Contains("?") ? "&" : "?") + $"t={Guid.NewGuid():N}";
            
            byte[] imageData;
            
            // 判断是否为JSON API
            if (_sources[source].IsJsonApi)
            {
                // JSON API: 先获取JSON，再解析图片URL
                var jsonResponse = await _httpClient.GetStringAsync(url);
                var imageUrl = ParseImageUrlFromJson(jsonResponse, source);
                
                if (string.IsNullOrEmpty(imageUrl))
                {
                    UI.PrintError("无法从API响应中解析图片URL");
                    return;
                }
                
                UI.PrintInfo($"解析到图片URL: {imageUrl.Substring(0, Math.Min(60, imageUrl.Length))}...");
                imageData = await _httpClient.GetByteArrayAsync(imageUrl);
            }
            else
            {
                // 直接返回图片的API
                imageData = await _httpClient.GetByteArrayAsync(url);
            }
            
            await File.WriteAllBytesAsync(tempFile, imageData);

            // 设置壁纸
            ApplyWallpaper(tempFile);
            AddToHistory(tempFile);

            // 复制到壁纸文件夹保留
            EnsureWallpaperFolderExists();
            var savedFile = Path.Combine(_wallpaperFolder, $"online_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
            File.Copy(tempFile, savedFile, true);

            UI.PrintSuccess($"已设置在线壁纸 [{_sources[source].Name}]");
            UI.PrintResult("文件大小", Utils.FormatSize(imageData.Length));
            UI.PrintResult("保存路径", savedFile);
        }
        catch (Exception ex)
        {
            UI.PrintError($"获取在线壁纸失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从JSON响应中解析图片URL
    /// </summary>
    private static string? ParseImageUrlFromJson(string json, string source)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            
            if (source.StartsWith("lolicon"))
            {
                // Lolicon API: data[0].urls.regular
                var data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() > 0)
                {
                    return data[0].GetProperty("urls").GetProperty("regular").GetString();
                }
            }
            else if (source.StartsWith("waifu"))
            {
                // Waifu.im API: images[0].url
                var images = doc.RootElement.GetProperty("images");
                if (images.GetArrayLength() > 0)
                {
                    return images[0].GetProperty("url").GetString();
                }
            }
            else if (source.StartsWith("nekos"))
            {
                // Nekos.best API: results[0].url
                var results = doc.RootElement.GetProperty("results");
                if (results.GetArrayLength() > 0)
                {
                    return results[0].GetProperty("url").GetString();
                }
            }
        }
        catch (Exception ex)
        {
            UI.PrintError($"解析JSON失败: {ex.Message}");
        }
        
        return null;
    }

    /// <summary>
    /// 处理自动切换
    /// </summary>
    private static void HandleAutoSwitch(string[] args)
    {
        if (args.Length == 0)
        {
            ShowAutoStatus();
            return;
        }

        var action = args[0].ToLowerInvariant();

        switch (action)
        {
            case "start":
            case "s":
                // 解析间隔时间
                if (args.Length > 1 && int.TryParse(args[1], out int minutes))
                {
                    if (minutes < 1) minutes = 1;
                    if (minutes > 1440) minutes = 1440; // 最大24小时
                    _intervalMinutes = minutes;
                    SaveConfig();
                }
                StartAutoSwitch();
                break;

            case "stop":
            case "x":
                StopAutoSwitch();
                break;

            case "status":
            case "st":
                ShowAutoStatus();
                break;

            case "interval":
            case "i":
                if (args.Length > 1 && int.TryParse(args[1], out int interval))
                {
                    _intervalMinutes = Math.Max(1, Math.Min(1440, interval));
                    SaveConfig();
                    UI.PrintSuccess($"已设置切换间隔为 {_intervalMinutes} 分钟");
                    
                    // 如果正在运行，重启定时器
                    if (_isRunning)
                    {
                        StopAutoSwitch();
                        StartAutoSwitch();
                    }
                }
                else
                {
                    UI.PrintResult("当前间隔", $"{_intervalMinutes} 分钟");
                }
                break;

            default:
                UI.PrintError($"未知操作: {action}");
                UI.PrintInfo("用法: wallpaper auto <start|stop|status|interval> [分钟]");
                break;
        }
    }

    /// <summary>
    /// 显示自动切换状态
    /// </summary>
    private static void ShowAutoStatus()
    {
        UI.PrintHeader("自动切换状态");
        UI.PrintResult("状态", _isRunning ? "运行中" : "已停止");
        UI.PrintResult("壁纸源", $"{_sources[_currentSource].Name} ({_sources[_currentSource].Desc})");
        UI.PrintResult("切换间隔", $"{_intervalMinutes} 分钟");
        
        if (_isRunning && _autoSwitchTimer != null)
        {
            var nextSwitch = DateTime.Now.AddMilliseconds(_autoSwitchTimer.Interval).ToString("HH:mm:ss");
            UI.PrintResult("下次切换", nextSwitch);
        }
    }

    /// <summary>
    /// 启动自动切换
    /// </summary>
    private static void StartAutoSwitch()
    {
        lock (_lockObj)
        {
            if (_isRunning)
            {
                UI.PrintInfo("自动切换已在运行中");
                return;
            }

            StopAutoSwitchInternal();

            _autoSwitchCts = new CancellationTokenSource();
            _autoSwitchTimer = new Timer(_intervalMinutes * 60 * 1000);
            _autoSwitchTimer.Elapsed += (s, e) =>
            {
                try
                {
                    Task.Run(() => SetOnlineWallpaper(new[] { _currentSource }));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[自动切换] 失败: {ex.Message}");
                }
            };
            _autoSwitchTimer.AutoReset = true;
            _autoSwitchTimer.Start();

            // 立即切换一张
            SetOnlineWallpaper(new[] { _currentSource });

            _isRunning = true;
            SaveConfig();
            UI.PrintSuccess($"自动切换已启动，间隔 {_intervalMinutes} 分钟");
        }
    }

    /// <summary>
    /// 停止自动切换
    /// </summary>
    private static void StopAutoSwitch()
    {
        lock (_lockObj)
        {
            StopAutoSwitchInternal();
            _isRunning = false;
            SaveConfig();
            UI.PrintSuccess("自动切换已停止");
        }
    }

    private static void StopAutoSwitchInternal()
    {
        _autoSwitchTimer?.Stop();
        _autoSwitchTimer?.Dispose();
        _autoSwitchTimer = null;
        
        _autoSwitchCts?.Cancel();
        _autoSwitchCts?.Dispose();
        _autoSwitchCts = null;
    }

    /// <summary>
    /// 保存配置
    /// </summary>
    private static void SaveConfig()
    {
        try
        {
            var config = new WallpaperConfig
            {
                CurrentSource = _currentSource,
                IntervalMinutes = _intervalMinutes,
                IsAutoRunning = _isRunning
            };
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFile, json);
        }
        catch { }
    }

    /// <summary>
    /// 加载配置
    /// </summary>
    private static void LoadConfig()
    {
        try
        {
            if (File.Exists(_configFile))
            {
                var json = File.ReadAllText(_configFile);
                var config = JsonSerializer.Deserialize<WallpaperConfig>(json);
                if (config != null)
                {
                    _currentSource = config.CurrentSource ?? "btstu";
                    _intervalMinutes = Math.Max(1, Math.Min(1440, config.IntervalMinutes));
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// 设置指定图片为壁纸
    /// </summary>
    private static void SetWallpaper(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintError("请提供图片路径");
            UI.PrintInfo("用法: wallpaper set <图片路径>");
            return;
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            UI.PrintError($"文件不存在: {path}");
            return;
        }

        if (!IsImageFile(path))
        {
            UI.PrintError("不支持的图片格式，仅支持: .jpg, .jpeg, .png, .bmp, .gif");
            return;
        }

        ApplyWallpaper(path);
        AddToHistory(path);
        UI.PrintSuccess($"已设置壁纸: {Path.GetFileName(path)}");
    }

    /// <summary>
    /// 随机设置本地壁纸
    /// </summary>
    private static void SetRandomWallpaper()
    {
        EnsureWallpaperFolderExists();

        var images = GetWallpaperImages();
        if (images.Length == 0)
        {
            UI.PrintError($"壁纸文件夹中没有图片: {_wallpaperFolder}");
            UI.PrintInfo("请添加图片到该文件夹，或使用 'wallpaper online' 下载在线壁纸");
            return;
        }

        var random = new Random();
        var selected = images[random.Next(images.Length)];

        ApplyWallpaper(selected);
        AddToHistory(selected);
        UI.PrintSuccess($"已随机切换壁纸: {Path.GetFileName(selected)}");
    }

    /// <summary>
    /// 下载并设置 Bing 每日壁纸
    /// </summary>
    private static async void DownloadAndSetBingWallpaper(string[] args)
    {
        try
        {
            UI.PrintInfo("正在获取 Bing 每日美图...");

            var bingApiUrl = "https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=zh-CN";
            var response = await _httpClient.GetStringAsync(bingApiUrl);
            
            using var doc = JsonDocument.Parse(response);
            var images = doc.RootElement.GetProperty("images");
            var firstImage = images[0];
            
            var imageUrl = "https://www.bing.com" + firstImage.GetProperty("url").GetString();
            var imageTitle = firstImage.GetProperty("title").GetString();
            var imageCopyright = firstImage.GetProperty("copyright").GetString();

            var imageData = await _httpClient.GetByteArrayAsync(imageUrl);
            
            EnsureWallpaperFolderExists();
            var fileName = $"bing_{DateTime.Now:yyyyMMdd}.jpg";
            var filePath = Path.Combine(_wallpaperFolder, fileName);
            await File.WriteAllBytesAsync(filePath, imageData);

            ApplyWallpaper(filePath);
            AddToHistory(filePath);

            UI.PrintSuccess("已设置 Bing 每日美图");
            UI.PrintResult("标题", imageTitle ?? "未知");
            UI.PrintResult("版权", imageCopyright ?? "未知");
            UI.PrintResult("保存路径", filePath);
        }
        catch (Exception ex)
        {
            UI.PrintError($"下载 Bing 壁纸失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 设置壁纸文件夹
    /// </summary>
    private static void SetWallpaperFolder(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintResult("当前壁纸文件夹", _wallpaperFolder);
            if (!Directory.Exists(_wallpaperFolder))
            {
                UI.PrintInfo("文件夹不存在，将自动创建");
            }
            else
            {
                var count = GetWallpaperImages().Length;
                UI.PrintResult("图片数量", count.ToString());
            }
            return;
        }

        var newPath = args[0];
        if (!Directory.Exists(newPath))
        {
            try
            {
                Directory.CreateDirectory(newPath);
                UI.PrintInfo($"创建文件夹: {newPath}");
            }
            catch (Exception ex)
            {
                UI.PrintError($"无法创建文件夹: {ex.Message}");
                return;
            }
        }

        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        ConfigManager.SetValue(_configKey, newPath, configPath);
        UI.PrintSuccess($"已设置壁纸文件夹: {newPath}");
    }

    /// <summary>
    /// 列出所有壁纸
    /// </summary>
    private static void ListWallpapers()
    {
        EnsureWallpaperFolderExists();

        var images = GetWallpaperImages();
        if (images.Length == 0)
        {
            UI.PrintInfo($"壁纸文件夹为空: {_wallpaperFolder}");
            return;
        }

        UI.PrintHeader($"壁纸列表 (共 {images.Length} 张)");
        for (int i = 0; i < images.Length && i < 20; i++)
        {
            var fileName = Path.GetFileName(images[i]);
            var fileInfo = new FileInfo(images[i]);
            UI.PrintItem($"{i + 1,2}. {fileName}", Utils.FormatSize(fileInfo.Length));
        }

        if (images.Length > 20)
        {
            UI.PrintInfo($"... 还有 {images.Length - 20} 张图片");
        }
    }

    /// <summary>
    /// 显示历史记录
    /// </summary>
    private static void ShowHistory()
    {
        var history = LoadHistory();
        if (history.Length == 0)
        {
            UI.PrintInfo("暂无壁纸历史记录");
            return;
        }

        UI.PrintHeader("壁纸历史记录");
        for (int i = 0; i < history.Length && i < 10; i++)
        {
            var fileName = Path.GetFileName(history[i]);
            var exists = File.Exists(history[i]) ? "" : " [已删除]";
            UI.PrintItem($"{i + 1,2}. {fileName}{exists}", "");
        }
    }

    /// <summary>
    /// 切换到上一个壁纸
    /// </summary>
    private static void SetPreviousWallpaper()
    {
        var history = LoadHistory();
        if (history.Length < 2)
        {
            UI.PrintError("历史记录不足，无法切换到上一个壁纸");
            return;
        }

        var previous = history[1];
        if (!File.Exists(previous))
        {
            UI.PrintError($"上一个壁纸文件已不存在: {previous}");
            return;
        }

        ApplyWallpaper(previous);
        AddToHistory(previous);
        UI.PrintSuccess($"已切换到上一个壁纸: {Path.GetFileName(previous)}");
    }

    /// <summary>
    /// 设置壁纸显示模式
    /// </summary>
    private static void SetWallpaperStyle(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintError("请提供显示模式");
            UI.PrintInfo("可用模式: fill(填充), fit(适应), stretch(拉伸), tile(平铺), center(居中), span(跨显示器)");
            return;
        }

        var mode = args[0].ToLowerInvariant();
        var (style, tile) = mode switch
        {
            "fill" => (10, 0),
            "fit" => (6, 0),
            "stretch" => (2, 0),
            "tile" => (0, 1),
            "center" => (0, 0),
            "span" => (22, 0),
            _ => (-1, -1)
        };

        if (style == -1)
        {
            UI.PrintError($"未知的显示模式: {mode}");
            return;
        }

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Control Panel\Desktop", true);
        if (key != null)
        {
            key.SetValue("WallpaperStyle", style.ToString());
            key.SetValue("TileWallpaper", tile.ToString());
            
            var currentWallpaper = GetCurrentWallpaperPath();
            if (!string.IsNullOrEmpty(currentWallpaper) && File.Exists(currentWallpaper))
            {
                ApplyWallpaper(currentWallpaper);
            }
            
            UI.PrintSuccess($"已设置壁纸显示模式: {mode}");
        }
    }

    /// <summary>
    /// 应用壁纸到系统
    /// </summary>
    public static void ApplyWallpaper(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            UI.PrintError("仅支持 Windows 系统");
            return;
        }

        NativeMethodsWallpaper.SystemParametersInfo(
            NativeMethodsWallpaper.SPI_SETDESKWALLPAPER, 
            0, 
            path, 
            NativeMethodsWallpaper.SPIF_UPDATEINIFILE | NativeMethodsWallpaper.SPIF_SENDCHANGE);
    }

    /// <summary>
    /// 获取当前壁纸路径
    /// </summary>
    private static string GetCurrentWallpaperPath()
    {
        IntPtr ptr = Marshal.AllocHGlobal(512 * 2);
        try
        {
            NativeMethodsWallpaper.SystemParametersInfo(
                NativeMethodsWallpaper.SPI_GETDESKWALLPAPER, 
                512, 
                ptr, 
                0);
            return Marshal.PtrToStringUni(ptr) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// 添加到历史记录
    /// </summary>
    public static void AddToHistory(string path)
    {
        var history = LoadHistory().ToList();
        history.RemoveAll(h => h.Equals(path, StringComparison.OrdinalIgnoreCase));
        history.Insert(0, path);
        if (history.Count > 20)
            history = history.Take(20).ToList();

        try
        {
            var json = JsonSerializer.Serialize(history);
            File.WriteAllText(_historyFile, json);
        }
        catch { }
    }

    /// <summary>
    /// 加载历史记录
    /// </summary>
    private static string[] LoadHistory()
    {
        try
        {
            if (File.Exists(_historyFile))
            {
                var json = File.ReadAllText(_historyFile);
                return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
            }
        }
        catch { }
        return Array.Empty<string>();
    }

    /// <summary>
    /// 获取壁纸文件夹中的所有图片
    /// </summary>
    private static string[] GetWallpaperImages()
    {
        if (!Directory.Exists(_wallpaperFolder))
            return Array.Empty<string>();

        var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
        return Directory.GetFiles(_wallpaperFolder)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToArray();
    }

    /// <summary>
    /// 确保壁纸文件夹存在
    /// </summary>
    private static void EnsureWallpaperFolderExists()
    {
        if (!Directory.Exists(_wallpaperFolder))
        {
            Directory.CreateDirectory(_wallpaperFolder);
        }
    }

    /// <summary>
    /// 检查是否为图片文件
    /// </summary>
    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" }.Contains(ext);
    }

    /// <summary>
    /// 壁纸配置类
    /// </summary>
    private class WallpaperConfig
    {
        public string? CurrentSource { get; set; }
        public int IntervalMinutes { get; set; } = 5;
        public bool IsAutoRunning { get; set; }
    }
}

/// <summary>
/// NativeMethods 扩展 - 壁纸相关 API
/// </summary>
internal static class NativeMethodsWallpaper
{
    public const uint SPI_SETDESKWALLPAPER = 0x0014;
    public const uint SPI_GETDESKWALLPAPER = 0x0073;
    public const uint SPIF_UPDATEINIFILE = 0x01;
    public const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, 
        [MarshalAs(UnmanagedType.LPWStr)] string pvParam, uint fWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, 
        IntPtr pvParam, uint fWinIni);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;

namespace LL;

/// <summary>
/// 壁纸抓取器 - 从网络抓取美女/风景壁纸
/// </summary>
public static class WallpaperCrawler
{
    private static readonly HttpClient _httpClient = new();
    
    // 用户代理
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.0.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.0";
    
    static WallpaperCrawler()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// 处理抓取命令
    /// </summary>
    public static async Task Handle(string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return;
        }

        var command = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();

        switch (command)
        {
            case "girl":
            case "g":
            case "美女":
                await CrawlAndSetGirlWallpaper(subArgs);
                break;
            case "anime":
            case "a":
            case "动漫":
                await CrawlAndSetAnimeWallpaper(subArgs);
                break;
            case "landscape":
            case "l":
            case "风景":
                await CrawlAndSetLandscapeWallpaper(subArgs);
                break;
            case "download":
            case "d":
            case "下载":
                await DownloadImages(subArgs);
                break;
            case "test":
            case "t":
            case "测试":
                await TestSources();
                break;
            default:
                UI.PrintError($"未知命令: {command}");
                ShowUsage();
                break;
        }
    }

    /// <summary>
    /// 显示用法
    /// </summary>
    private static void ShowUsage()
    {
        UI.PrintHeader("壁纸抓取器");
        Console.WriteLine("从网络抓取壁纸图片并设置为桌面壁纸");
        Console.WriteLine();
        UI.PrintItem("用法:", "ll crawl <命令> [选项]");
        Console.WriteLine();
        UI.PrintHeader("可用命令");
        UI.PrintItem("girl/g/美女", "抓取美女壁纸");
        UI.PrintItem("anime/a/动漫", "抓取动漫壁纸");
        UI.PrintItem("landscape/l/风景", "抓取风景壁纸");
        UI.PrintItem("download/d/下载", "批量下载壁纸到指定文件夹");
        UI.PrintItem("test/t/测试", "测试所有可用的抓取源");
        Console.WriteLine();
        UI.PrintHeader("示例");
        UI.PrintItem("ll crawl girl", "随机抓取一张美女壁纸并设置");
        UI.PrintItem("ll crawl anime", "随机抓取一张动漫壁纸并设置");
        UI.PrintItem("ll crawl download 美女 10", "下载10张美女壁纸到默认文件夹");
    }

    /// <summary>
    /// 抓取美女壁纸
    /// </summary>
    private static async Task CrawlAndSetGirlWallpaper(string[] args)
    {
        UI.PrintHeader("抓取美女壁纸");
        
        var sources = new List<(string Name, Func<Task<string?>> Fetcher)>
        {
            ("图片爬虫1", FetchGirlImageFromSource1),
            ("图片爬虫2", FetchGirlImageFromSource2),
            ("图片爬虫3", FetchGirlImageFromSource3),
        };

        // 随机打乱源顺序
        var random = new Random();
        sources = sources.OrderBy(x => random.Next()).ToList();

        foreach (var source in sources)
        {
            try
            {
                UI.PrintInfo($"尝试从 [{source.Name}] 获取...");
                var imageUrl = await source.Fetcher();
                
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    UI.PrintSuccess($"获取到图片URL: {imageUrl.Substring(0, Math.Min(60, imageUrl.Length))}...");
                    await DownloadAndSetWallpaper(imageUrl, "girl");
                    return;
                }
            }
            catch (Exception ex)
            {
                UI.PrintInfo($"{source.Name} 失败: {ex.Message}");
            }
        }

        UI.PrintError("所有源均不可用，尝试备用方案...");
        // 使用paugram-mg作为备用
        WallpaperManager.SetOnlineWallpaper(new[] { "paugram-mg" });
    }

    /// <summary>
    /// 抓取动漫壁纸
    /// </summary>
    private static async Task CrawlAndSetAnimeWallpaper(string[] args)
    {
        UI.PrintHeader("抓取动漫壁纸");
        
        var sources = new List<(string Name, Func<Task<string?>> Fetcher)>
        {
            ("Lolicon API", () => FetchFromJsonApi("https://api.lolicon.app/setu/v2?r18=0&num=1&size=regular", "data", "urls", "regular")),
            ("Waifu.im", () => FetchFromJsonApi("https://api.waifu.im/search?included_tags=waifu&is_nsfw=false", "images", null, "url")),
            ("Nekos.best", () => FetchFromJsonApi("https://nekos.best/api/v2/neko", "results", null, "url")),
        };

        var random = new Random();
        sources = sources.OrderBy(x => random.Next()).ToList();

        foreach (var source in sources)
        {
            try
            {
                UI.PrintInfo($"尝试从 [{source.Name}] 获取...");
                var imageUrl = await source.Fetcher();
                
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    UI.PrintSuccess($"获取到图片URL: {imageUrl.Substring(0, Math.Min(60, imageUrl.Length))}...");
                    await DownloadAndSetWallpaper(imageUrl, "anime");
                    return;
                }
            }
            catch (Exception ex)
            {
                UI.PrintInfo($"{source.Name} 失败: {ex.Message}");
            }
        }

        UI.PrintError("所有源均不可用");
    }

    /// <summary>
    /// 抓取风景壁纸
    /// </summary>
    private static async Task CrawlAndSetLandscapeWallpaper(string[] args)
    {
        UI.PrintHeader("抓取风景壁纸");
        
        // 直接使用paugram的各个源
        var sources = new[] { "paugram-bing", "paugram-us", "paugram-wh", "paugram" };
        var random = new Random();
        var source = sources[random.Next(sources.Length)];
        
        UI.PrintInfo($"使用源: {source}");
        WallpaperManager.SetOnlineWallpaper(new[] { source });
    }

    /// <summary>
    /// 批量下载壁纸
    /// </summary>
    private static async Task DownloadImages(string[] args)
    {
        if (args.Length < 2)
        {
            UI.PrintError("用法: ll crawl download <类型> <数量>");
            UI.PrintInfo("示例: ll crawl download 美女 10");
            return;
        }

        var type = args[0];
        if (!int.TryParse(args[1], out int count) || count < 1 || count > 50)
        {
            UI.PrintError("数量必须在 1-50 之间");
            return;
        }

        var folder = Path.Combine(AppContext.BaseDirectory, "wallpapers", type);
        Directory.CreateDirectory(folder);

        UI.PrintHeader($"下载{type}壁纸");
        UI.PrintInfo($"目标文件夹: {folder}");
        UI.PrintInfo($"下载数量: {count}");
        Console.WriteLine();

        int success = 0;
        int failed = 0;

        for (int i = 0; i < count; i++)
        {
            try
            {
                UI.PrintInfo($"[{i + 1}/{count}] 正在下载...");
                
                string? imageUrl = type switch
                {
                    "美女" or "girl" => await FetchGirlImageFromSource1(),
                    "动漫" or "anime" => await FetchFromJsonApi("https://api.lolicon.app/setu/v2?r18=0&num=1&size=regular", "data", "urls", "regular"),
                    _ => await FetchGirlImageFromSource1()
                };

                if (!string.IsNullOrEmpty(imageUrl))
                {
                    var ext = Path.GetExtension(imageUrl).Split('?')[0];
                    if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".jpg";
                    
                    var filename = $"{type}_{DateTime.Now:yyyyMMdd_HHmmss}_{i}{ext}";
                    var filepath = Path.Combine(folder, filename);
                    
                    await DownloadImage(imageUrl, filepath);
                    success++;
                    UI.PrintSuccess($"已保存: {filename}");
                }
                else
                {
                    failed++;
                }

                await Task.Delay(1000); // 避免请求过快
            }
            catch (Exception ex)
            {
                failed++;
                UI.PrintInfo($"下载失败: {ex.Message}");
            }
        }

        Console.WriteLine();
        UI.PrintResult("下载完成", $"成功: {success}, 失败: {failed}");
        UI.PrintResult("保存路径", folder);
    }

    /// <summary>
    /// 测试所有源
    /// </summary>
    private static async Task TestSources()
    {
        UI.PrintHeader("测试壁纸抓取源");
        
        var sources = new (string Name, Func<Task<string?>> Fetcher)[]
        {
            ("Source1-美女", FetchGirlImageFromSource1),
            ("Source2-美女", FetchGirlImageFromSource2),
            ("Source3-美女", FetchGirlImageFromSource3),
            ("Lolicon-动漫", () => FetchFromJsonApi("https://api.lolicon.app/setu/v2?r18=0&num=1&size=regular", "data", "urls", "regular")),
            ("Waifu.im-动漫", () => FetchFromJsonApi("https://api.waifu.im/search?included_tags=waifu&is_nsfw=false", "images", null, "url")),
            ("Nekos.best-动漫", () => FetchFromJsonApi("https://nekos.best/api/v2/neko", "results", null, "url")),
        };

        foreach (var source in sources)
        {
            try
            {
                UI.PrintInfo($"测试 [{source.Name}]...");
                var url = await source.Fetcher();
                if (!string.IsNullOrEmpty(url))
                {
                    UI.PrintSuccess($"✅ {source.Name}: 可用");
                    UI.PrintItem("URL", url.Substring(0, Math.Min(50, url.Length)) + "...");
                }
                else
                {
                    UI.PrintError($"❌ {source.Name}: 未获取到图片");
                }
            }
            catch (Exception ex)
            {
                UI.PrintError($"❌ {source.Name}: {ex.Message}");
            }
            Console.WriteLine();
            await Task.Delay(500);
        }
    }

    #region 图片源抓取方法

    /// <summary>
    /// 从源1抓取美女图片 - 使用 picsum + 搜索参数模拟
    /// </summary>
    private static async Task<string?> FetchGirlImageFromSource1()
    {
        // 使用 picsum 配合特定seed来获取美女相关图片
        var seeds = new[] { "girl", "woman", "beauty", "model", "portrait", "fashion" };
        var random = new Random();
        var seed = seeds[random.Next(seeds.Length)] + random.Next(1000);
        var width = 1920 + random.Next(100);
        var height = 1080 + random.Next(100);
        
        // picsum 返回的是重定向到真实图片地址
        var url = $"https://picsum.photos/seed/{seed}/{width}/{height}";
        
        // 发送HEAD请求获取重定向后的URL
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        
        if (response.StatusCode == System.Net.HttpStatusCode.Redirect || 
            response.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
        {
            return response.Headers.Location?.ToString();
        }
        
        // 如果HEAD不行，直接返回原URL让后续请求处理重定向
        return url;
    }

    /// <summary>
    /// 从源2抓取美女图片 - 使用wallhaven随机
    /// </summary>
    private static async Task<string?> FetchGirlImageFromSource2()
    {
        // 使用wallhaven的随机图片API
        var categories = new[] { "women", "people", "model" };
        var random = new Random();
        
        // wallhaven 的搜索链接
        var searchUrl = $"https://wallhaven.cc/api/v1/search?q=portrait&sorting=random&page={random.Next(1, 10)}";
        
        var json = await _httpClient.GetStringAsync(searchUrl);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        
        if (data.GetArrayLength() > 0)
        {
            var idx = random.Next(data.GetArrayLength());
            return data[idx].GetProperty("path").GetString();
        }
        
        return null;
    }

    /// <summary>
    /// 从源3抓取美女图片
    /// </summary>
    private static async Task<string?> FetchGirlImageFromSource3()
    {
        // 使用 placeholder 图片服务，添加美女相关关键词
        var random = new Random();
        var width = 1920;
        var height = 1080;
        
        // 尝试使用 imagecdn 服务
        var keywords = new[] { "woman", "girl", "portrait", "fashion", "beauty" };
        var keyword = keywords[random.Next(keywords.Length)];
        
        return $"https://source.unsplash.com/{width}x{height}/?{keyword}&sig={random.Next(1000)}";
    }

    /// <summary>
    /// 从JSON API获取图片URL
    /// </summary>
    private static async Task<string?> FetchFromJsonApi(string apiUrl, string arrayProperty, string? nestedProperty, string urlProperty)
    {
        var json = await _httpClient.GetStringAsync(apiUrl);
        using var doc = JsonDocument.Parse(json);
        
        var array = doc.RootElement.GetProperty(arrayProperty);
        if (array.GetArrayLength() == 0) return null;
        
        var item = array[0];
        
        if (!string.IsNullOrEmpty(nestedProperty))
        {
            item = item.GetProperty(nestedProperty);
        }
        
        return item.GetProperty(urlProperty).GetString();
    }

    #endregion

    #region 下载和设置壁纸

    /// <summary>
    /// 下载图片并设置为壁纸
    /// </summary>
    private static async Task DownloadAndSetWallpaper(string imageUrl, string category)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"crawl_wallpaper_{Guid.NewGuid()}.jpg");
        
        await DownloadImage(imageUrl, tempFile);
        
        // 设置为壁纸
        WallpaperManager.ApplyWallpaper(tempFile);
        WallpaperManager.AddToHistory(tempFile);
        
        // 保存一份到壁纸文件夹
        var folder = Path.Combine(AppContext.BaseDirectory, "wallpapers", "crawled");
        Directory.CreateDirectory(folder);
        var savedFile = Path.Combine(folder, $"{category}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
        File.Copy(tempFile, savedFile, true);
        
        UI.PrintSuccess("壁纸设置成功!");
        UI.PrintResult("保存路径", savedFile);
        
        var fileInfo = new FileInfo(savedFile);
        UI.PrintResult("文件大小", Utils.FormatSize(fileInfo.Length));
    }

    /// <summary>
    /// 下载图片
    /// </summary>
    private static async Task DownloadImage(string imageUrl, string filepath)
    {
        // 允许自动重定向
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        client.Timeout = TimeSpan.FromSeconds(30);
        
        var data = await client.GetByteArrayAsync(imageUrl);
        await File.WriteAllBytesAsync(filepath, data);
    }

    #endregion
}

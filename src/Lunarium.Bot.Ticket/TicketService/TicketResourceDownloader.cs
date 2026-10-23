using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Lunarium.Logging;

namespace Lunarium.Bot.Ticket.TicketService;

/// <summary>一次下载的结果。</summary>
/// <param name="Bytes">文件内容。</param>
/// <param name="MediaType">响应头里的 MIME 类型。</param>
public sealed record DownloadedResource(byte[] Bytes, string MediaType);

/// <summary>
/// 资源下载器（Discord 附件 / 头像 / 正文外链图片）。
/// <para>
/// 外链来自用户输入，必须防 SSRF：只允许 http(s)、解析后拒绝回环/私有/链路本地地址、
/// 手动跟随重定向并逐跳校验、限制体积与超时。
/// </para>
/// </summary>
public sealed partial class TicketResourceDownloader
{
    private const int MaxRedirects = 3;

    /// <summary>Discord CDN 与常见图片站的下载客户端；关闭自动重定向以便逐跳校验。</summary>
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10),
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly ILogger _logger;

    public TicketResourceDownloader(ILogger logger)
    {
        _logger = logger.ForContext("TicketService.TicketResourceDownloader");
    }

    /// <summary>抓取图片 URL 的正则（仅 http/https，排除常见尾随字符）。</summary>
    [GeneratedRegex(@"https?://[^\s<>""'）)】\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    /// <summary>
    /// 下载一个资源。任何失败（不安全、非 2xx、超限、超时）都返回 <see langword="null"/>，不抛业务异常。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="maxBytes">允许的最大字节数。</param>
    /// <param name="requireImage">是否要求响应 Content-Type 为 image/*。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<DownloadedResource?> DownloadAsync(
        Uri uri,
        long maxBytes,
        bool requireImage,
        CancellationToken cancellationToken = default)
    {
        var current = uri;

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!await IsSafeUriAsync(current, cancellationToken))
            {
                _logger.Warning("拒绝下载不安全的地址: {Url}", current);
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var response = await Client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                if (location is null)
                {
                    return null;
                }

                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("下载失败: {Url} HTTP {StatusCode}", current, (int)response.StatusCode);
                return null;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            if (requireImage && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning("跳过非图片资源: {Url} ContentType={MediaType}", current, mediaType);
                return null;
            }

            if (response.Content.Headers.ContentLength is { } declared && declared > maxBytes)
            {
                _logger.Warning("跳过超限资源: {Url} Size={Size} Limit={Limit}", current, declared, maxBytes);
                return null;
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;

            while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    _logger.Warning("下载中途超限，已丢弃: {Url}", current);
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return new DownloadedResource(buffer.ToArray(), mediaType);
        }

        _logger.Warning("重定向次数过多: {Url}", uri);
        return null;
    }

    /// <summary>从文本中提取候选图片外链。</summary>
    public static IEnumerable<Uri> ExtractImageUrls(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            yield break;
        }

        foreach (Match match in UrlRegex().Matches(content))
        {
            if (!Uri.TryCreate(match.Value.TrimEnd('.', ',', ';', ':', '!', '?'), UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (IsImageExtension(Path.GetExtension(uri.AbsolutePath)))
            {
                yield return uri;
            }
        }
    }

    /// <summary>按 MIME 推断扩展名；未知返回空串。</summary>
    public static string ExtensionFor(string mediaType)
    {
        return mediaType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/svg+xml" => ".svg",
            _ => string.Empty,
        };
    }

    /// <summary>URL 是否看起来指向图片（按扩展名）。</summary>
    public static bool IsImageExtension(string extension)
    {
        return extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    }

    /// <summary>为外链生成稳定的短文件名，天然去重。</summary>
    public static string ShortHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private static async Task<bool> IsSafeUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme is not ("http" or "https") || uri.IsLoopback || string.IsNullOrWhiteSpace(uri.DnsSafeHost))
        {
            return false;
        }

        if (IPAddress.TryParse(uri.DnsSafeHost, out var literal))
        {
            return IsPublicAddress(literal);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
            return addresses.Length > 0 && addresses.All(IsPublicAddress);
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal && !address.IsIPv6UniqueLocal;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            0 or 10 or 127 => false,
            169 => bytes[1] != 254,          // 169.254.0.0/16 链路本地
            172 => bytes[1] < 16 || bytes[1] > 31,
            192 => bytes[1] != 168,          // 192.168.0.0/16
            100 => bytes[1] < 64 || bytes[1] > 127, // 100.64.0.0/10 CGNAT
            _ => true,
        };
    }
}

using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

using Lunarium.Bot.Ticket.Model;
using Lunarium.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Lunarium.Bot.Ticket.TicketService;

/// <summary>
/// 归档打包器：把 <c>ticket.json</c> + <c>attachment/</c> 原件 + 生成的 <c>index.html</c>
/// 打成一个 zip，并缓存复用。
/// <para>
/// HTML 通过相对路径引用 <c>attachment/...</c>，解压后可直接用浏览器打开；
/// PNG 只在写入 zip 时转成高质量 JPEG（内存中完成，磁盘上的原件保持不变），
/// HTML 引用的路径会同步替换成 <c>.jpg</c>。
/// </para>
/// </summary>
public sealed partial class TicketArchiveService
{
    private const string HtmlFileName = "index.html";
    private const string AttachmentRelativePrefix = "attachment/";

    /// <summary>JPEG 质量：95 对截图/照片基本视觉无损。</summary>
    private const int JpegQuality = 95;

    private static readonly TimeZoneInfo DisplayTimeZone = ResolveDisplayTimeZone();

    private readonly ILogger _logger;
    private readonly TicketLocalData _localData;

    public TicketArchiveService(ILogger logger, TicketLocalData localData)
    {
        _logger = logger.ForContext("TicketArchiveService");
        _localData = localData;
    }

    /// <summary>zip 是否已生成。</summary>
    public bool HasArchiveZip(int ticketId)
    {
        var path = _localData.GetArchiveZipPath(ticketId);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    /// <summary>
    /// 确保归档 zip 存在并返回其路径。已存在时直接复用（同一工单归档后不再变化）。
    /// </summary>
    public async Task<string> EnsureArchiveZipAsync(TicketData ticket, CancellationToken cancellationToken = default)
    {
        var zipPath = _localData.GetArchiveZipPath(ticket.Id);
        if (File.Exists(zipPath) && new FileInfo(zipPath).Length > 0)
        {
            return zipPath;
        }

        var ticketDirectory = _localData.GetTicketDirectory(ticket.Id);
        var dataPath = Path.Combine(ticketDirectory, TicketLocalData.TicketFileName);
        if (!File.Exists(dataPath))
        {
            throw new FileNotFoundException($"工单数据文件不存在: {dataPath}");
        }

        Directory.CreateDirectory(ticketDirectory);
        // zip 落在归档目录：即使工单仍处于活跃区（异常调用）也要保证父目录存在
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        var tempPath = zipPath + ".tmp";

        try
        {
            var entryNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                var converted = 0;

                foreach (var file in Directory.EnumerateFiles(ticketDirectory, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relative = Relative(ticketDirectory, file);
                    if (relative.Equals(TicketLocalData.TicketFileName, StringComparison.OrdinalIgnoreCase)
                        || relative.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        || relative.EndsWith(".zip.tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (relative.StartsWith(AttachmentRelativePrefix, StringComparison.OrdinalIgnoreCase)
                        && Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        var jpeg = await TryConvertToJpegAsync(file, cancellationToken);
                        if (jpeg is not null)
                        {
                            var jpegRelative = Path.ChangeExtension(relative, ".jpg").Replace('\\', '/');
                            AddEntry(zip, jpegRelative, jpeg);
                            entryNames[relative] = jpegRelative;
                            converted++;
                            continue;
                        }
                    }

                    zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                    entryNames[relative] = relative;
                }

                var dataBytes = await File.ReadAllBytesAsync(dataPath, cancellationToken);
                AddEntry(zip, TicketLocalData.TicketFileName, dataBytes);

                var html = BuildHtml(ticket, entryNames);
                AddEntry(zip, HtmlFileName, Encoding.UTF8.GetBytes(html));

                _logger.Info("归档 zip 已生成: {Ticket} 条目={Entries} PNG转JPG={Converted}",
                    ticket, entryNames.Count + 2, converted);
            }

            File.Move(tempPath, zipPath, overwrite: true);
            return zipPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// 确保工单目录原样打包（数据库文件原件），排除归档包/临时文件。
    /// </summary>
    public async Task<string> EnsureDatabaseZipAsync(TicketData ticket, CancellationToken cancellationToken = default)
    {
        var ticketDirectory = _localData.GetTicketDirectory(ticket.Id);
        if (!Directory.Exists(ticketDirectory))
        {
            throw new DirectoryNotFoundException($"工单目录不存在: {ticketDirectory}");
        }

        var zipPath = Path.Combine(ticketDirectory, "database.zip");
        if (File.Exists(zipPath) && new FileInfo(zipPath).Length > 0)
        {
            return zipPath;
        }

        var tempPath = zipPath + ".tmp";

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var file in Directory.EnumerateFiles(ticketDirectory, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relative = Relative(ticketDirectory, file);
                    if (relative.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        || relative.EndsWith(".zip.tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                }
            }

            File.Move(tempPath, zipPath, overwrite: true);
            _logger.Info("数据库原件包已生成: {Ticket} Size={Size}", ticket, new FileInfo(zipPath).Length);
            return zipPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void AddEntry(ZipArchive zip, string entryName, byte[] content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(content);
    }

    /// <summary>把 PNG 在内存里转成白底 JPEG；失败返回 <see langword="null"/>（打包时回退原件）。</summary>
    private async Task<byte[]?> TryConvertToJpegAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);

            using var image = Image.Load(bytes);
            // JPEG 不支持透明，先铺白底，避免透明区域变黑
            image.Mutate(x => x.BackgroundColor(Color.White));

            using var output = new MemoryStream();
            image.SaveAsJpeg(output, new JpegEncoder { Quality = JpegQuality });
            var jpeg = output.ToArray();

            // 小图标转 JPEG 反而更大时保留原 PNG
            return jpeg.Length < bytes.Length ? jpeg : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "PNG 转换 JPEG 失败，保留原件: {Path}", path);
            return null;
        }
    }

    private static string BuildHtml(TicketData ticket, IReadOnlyDictionary<string, string> entryNames)
    {
        var builder = new StringBuilder(64 * 1024);

        builder.Append("""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            """);
        builder.Append($"<title>{Html(ticket.ToString())} | 工单归档</title>");
        builder.Append("""
            <style>
            :root { color-scheme: dark; }
            * { box-sizing: border-box; }
            body { margin: 0; background: #313338; color: #dbdee1;
                   font-family: "Segoe UI", "PingFang SC", "Microsoft YaHei", system-ui, sans-serif;
                   line-height: 1.5; }
            header { background: #1e1f22; border-bottom: 1px solid #3f4147; padding: 20px 24px; }
            header h1 { margin: 0 0 8px; font-size: 22px; color: #f2f3f5; }
            header dl { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px;
                        margin: 0; font-size: 14px; color: #b5bac1; }
            header dt { color: #949ba4; }
            main { max-width: 1100px; margin: 0 auto; padding: 16px 24px 64px; }
            .message { display: flex; gap: 14px; padding: 10px 12px; border-radius: 6px; }
            .message:hover { background: rgba(4, 4, 5, .07); }
            .avatar { width: 40px; height: 40px; border-radius: 50%; flex: none; object-fit: cover;
                      background: #5865f2; }
            .avatar.placeholder { display: block; }
            .body { min-width: 0; flex: 1; }
            .meta { display: flex; align-items: baseline; gap: 10px; }
            .author { color: #f2f3f5; font-weight: 600; }
            .meta time { color: #949ba4; font-size: 12px; }
            .tag { font-size: 10px; font-weight: 700; background: #5865f2; color: #fff;
                   border-radius: 3px; padding: 1px 4px; }
            .reply { color: #b5bac1; font-size: 13px; border-left: 3px solid #4e5058;
                     padding-left: 8px; margin: 4px 0; }
            .content { white-space: normal; word-break: break-word; }
            .content a { color: #00a8fc; }
            .attachments { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 8px; }
            .attachments .image img { max-width: 340px; max-height: 260px; border-radius: 6px;
                                      display: block; cursor: zoom-in; }
            .attachments .file { display: inline-flex; gap: 8px; align-items: center;
                                 background: #2b2d31; border: 1px solid #3f4147; border-radius: 6px;
                                 padding: 8px 12px; color: #00a8fc; text-decoration: none; }
            .attachments .file span { color: #949ba4; font-size: 12px; }
            .attachments .missing { color: #f23f43; }
            .empty { color: #949ba4; padding: 32px 12px; }
            #lightbox { position: fixed; inset: 0; background: rgba(0,0,0,.85); display: none;
                        align-items: center; justify-content: center; z-index: 99; cursor: zoom-out; }
            #lightbox.open { display: flex; }
            #lightbox img { max-width: 92vw; max-height: 92vh; border-radius: 6px; }
            @media (max-width: 640px) {
                main { padding: 12px; }
                .attachments .image img { max-width: 100%; }
            }
            </style>
            </head>
            <body>
            """);

        builder.Append("<header>");
        builder.Append($"<h1>{Html(ticket.ToString())}</h1>");
        builder.Append("<dl>");
        AppendDefinition(builder, "类型", ticket.TicketType.ToString());
        AppendDefinition(builder, "分类", ticket.TicketCategory.ToString());
        AppendDefinition(builder, "提交人", $"{ticket.TicketCreator.DisplayName} ({ticket.TicketCreator.UserId})");
        AppendDefinition(builder, "提交时间", FormatTime(ticket.CreatedAt));
        AppendDefinition(builder, "状态", ticket.Status.ToString());
        AppendDefinition(builder, "审核", DescribeApprove(ticket));
        AppendDefinition(builder, "关闭", DescribeActor(ticket.CloseInfo?.UserId, ticket.CloseInfo?.DisplayName, ticket.CloseInfo?.ClosedAt));
        AppendDefinition(builder, "归档", DescribeActor(ticket.ArchiveInfo?.UserId, ticket.ArchiveInfo?.DisplayName, ticket.ArchiveInfo?.ArchivedAt));
        AppendDefinition(builder, "原始数据", $"<a href=\"{TicketLocalData.TicketFileName}\">{TicketLocalData.TicketFileName}</a>");
        builder.Append("</dl></header><main>");

        var messages = ticket.TicketMessages;
        if (messages is null || messages.Count == 0)
        {
            builder.Append("<p class=\"empty\">没有抓取到任何消息。</p>");
        }
        else
        {
            foreach (var message in messages)
            {
                AppendMessage(builder, message, entryNames);
            }
        }

        builder.Append("""
            </main>
            <div id="lightbox"><img alt=""></div>
            <script>
            (() => {
              const box = document.getElementById('lightbox');
              const img = box.querySelector('img');
              document.querySelectorAll('.attachments .image img').forEach(el => {
                el.addEventListener('click', e => {
                  e.preventDefault();
                  img.src = el.src;
                  img.alt = el.alt;
                  box.classList.add('open');
                });
              });
              box.addEventListener('click', () => box.classList.remove('open'));
            })();
            </script>
            </body>
            </html>
            """);

        return builder.ToString();
    }

    private static void AppendMessage(
        StringBuilder builder,
        TicketMessage message,
        IReadOnlyDictionary<string, string> entryNames)
    {
        builder.Append("<article class=\"message\">");

        var avatar = MapPath(message.AvatarPath, entryNames);
        builder.Append(avatar is null
            ? "<div class=\"avatar placeholder\"></div>"
            : $"<img class=\"avatar\" src=\"{Attr(avatar)}\" alt=\"\" loading=\"lazy\">");

        builder.Append("<div class=\"body\"><div class=\"meta\">");
        builder.Append($"<span class=\"author\">{Html(message.AuthorDisplayName)}</span>");
        if (message.AuthorId == 0)
        {
            builder.Append("<span class=\"tag\">SYSTEM</span>");
        }
        builder.Append($"<time datetime=\"{message.SentAt:O}\">{FormatTime(message.SentAt)}</time>");
        builder.Append("</div>");

        if (message.ReplyToMessageId is { } replyTo)
        {
            builder.Append($"<div class=\"reply\">回复消息 {replyTo}</div>");
        }

        builder.Append($"<div class=\"content\">{FormatContent(message.Content)}</div>");

        if (message.Attachments is { Count: > 0 } attachments)
        {
            builder.Append("<div class=\"attachments\">");
            foreach (var attachment in attachments)
            {
                AppendAttachment(builder, attachment, entryNames);
            }
            builder.Append("</div>");
        }

        builder.Append("</div></article>");
    }

    private static void AppendAttachment(
        StringBuilder builder,
        TicketAttachment attachment,
        IReadOnlyDictionary<string, string> entryNames)
    {
        var path = MapPath(attachment.LocalPath, entryNames);

        if (path is null)
        {
            // 原件没下载成功：至少保留来源链接
            builder.Append($"<a class=\"file missing\" href=\"{Attr(attachment.Url)}\" target=\"_blank\" rel=\"noreferrer noopener\">");
            builder.Append($"{Html(attachment.FileName)}（原件未下载）</a>");
            return;
        }

        if (attachment.IsImage)
        {
            builder.Append($"<a class=\"image\" href=\"{Attr(path)}\" target=\"_blank\">");
            builder.Append($"<img src=\"{Attr(path)}\" alt=\"{Attr(attachment.FileName)}\" loading=\"lazy\"></a>");
            return;
        }

        builder.Append($"<a class=\"file\" href=\"{Attr(path)}\" download>");
        builder.Append($"<span>{Html(attachment.FileName)}</span><span>{FormatSize(attachment.Size)}</span></a>");
    }

    private static string? MapPath(string? relativePath, IReadOnlyDictionary<string, string> entryNames)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        return entryNames.TryGetValue(relativePath, out var mapped) ? mapped : relativePath;
    }

    private static void AppendDefinition(StringBuilder builder, string name, string value)
    {
        builder.Append($"<dt>{Html(name)}</dt><dd>{value}</dd>");
    }

    private static string DescribeApprove(TicketData ticket)
    {
        if (ticket.ApproveInfo is null)
        {
            return "未审核";
        }

        return ticket.IsAutoApprove
            ? $"系统自动通过（{FormatTime(ticket.ApproveInfo.ApprovedAt)}）"
            : $"{Html(ticket.ApproveInfo.DisplayName)} ({ticket.ApproveInfo.UserId}) · {FormatTime(ticket.ApproveInfo.ApprovedAt)}";
    }

    private static string DescribeActor(ulong? userId, string? displayName, DateTimeOffset? at)
    {
        return userId is null or 0
            ? "—"
            : $"{Html(displayName ?? string.Empty)} ({userId}) · {FormatTime(at ?? default)}";
    }

    /// <summary>转义 + 提及还原 + 链接 + 换行。</summary>
    private static string FormatContent(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(content.Length + 32);
        var last = 0;

        foreach (Match match in UrlRegex().Matches(content))
        {
            builder.Append(FormatPlain(content[last..match.Index]));

            var raw = match.Value;
            var trimmed = raw.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}');
            var suffix = raw[trimmed.Length..];

            builder.Append($"<a href=\"{Attr(trimmed)}\" target=\"_blank\" rel=\"noreferrer noopener\">{Html(trimmed)}</a>");
            builder.Append(FormatPlain(suffix));

            last = match.Index + match.Length;
        }

        builder.Append(FormatPlain(content[last..]));
        return builder.ToString();
    }

    private static string FormatPlain(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var encoded = WebUtility.HtmlEncode(text);
        encoded = UserMentionRegex().Replace(encoded, "@用户");
        encoded = ChannelMentionRegex().Replace(encoded, "#频道");
        encoded = RoleMentionRegex().Replace(encoded, "@身份组");

        return encoded.Replace("\r\n", "\n").Replace("\n", "<br>");
    }

    private static string Html(string text) => WebUtility.HtmlEncode(text);

    private static string Attr(string text) => WebUtility.HtmlEncode(text);

    private static string FormatTime(DateTimeOffset time)
    {
        if (time == default)
        {
            return "—";
        }

        var local = TimeZoneInfo.ConvertTime(time, DisplayTimeZone);
        return local.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "未知大小";
        }

        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static TimeZoneInfo ResolveDisplayTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("清理临时 zip 失败: {Path} {Message}", path, ex.Message);
        }
    }

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"&lt;@!?\d+&gt;")]
    private static partial Regex UserMentionRegex();

    [GeneratedRegex(@"&lt;#\d+&gt;")]
    private static partial Regex ChannelMentionRegex();

    [GeneratedRegex(@"&lt;@&amp;\d+&gt;")]
    private static partial Regex RoleMentionRegex();
}

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

    /// <summary>贴纸目录：PNG→JPG 转换会丢掉透明度，必须跳过。</summary>
    private const string StickerRelativePrefix = "attachment/sticker/";

    /// <summary>JPEG 质量：95 对截图/照片基本视觉无损。</summary>
    private const int JpegQuality = 95;

    private static readonly TimeZoneInfo DisplayTimeZone = ResolveDisplayTimeZone();

    private readonly ILogger _logger;
    private readonly TicketLocalData _localData;

    public TicketArchiveService(ILogger logger, TicketLocalData localData)
    {
        _logger = logger.ForContext("TicketService.TicketArchiveService");
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

                    // 只排除工单目录根下的归档产物；attachment/ 里用户上传的 .zip 必须保留
                    var isRootFile = !relative.Contains('/');
                    if (relative.Equals(TicketLocalData.TicketFileName, StringComparison.OrdinalIgnoreCase)
                        || (isRootFile && relative.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        || (isRootFile && relative.EndsWith(".zip.tmp", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (relative.StartsWith(AttachmentRelativePrefix, StringComparison.OrdinalIgnoreCase)
                        && !relative.StartsWith(StickerRelativePrefix, StringComparison.OrdinalIgnoreCase)
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

                    // 只排除根目录下的归档产物，attachment/ 里用户上传的 .zip 必须保留
                    var isRootFile = !relative.Contains('/');
                    if (isRootFile
                        && (relative.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                            || relative.EndsWith(".zip.tmp", StringComparison.OrdinalIgnoreCase)))
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
            header { background: #1e1f22; border-bottom: 1px solid #3f4147; padding: 20px 0; }
            header .header-inner { max-width: 1100px; margin: 0 auto; padding: 0 24px; }
            header h1 { margin: 0 0 8px; font-size: 22px; color: #f2f3f5; text-align: center; }
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
            .cv2 { display: flex; flex-direction: column; gap: 8px; margin-top: 6px; }
            .cv2-container { background: #2b2d31; border: 1px solid #3f4147; border-radius: 8px;
                             padding: 12px; display: flex; flex-direction: column; gap: 8px; }
            .cv2-section { display: flex; gap: 12px; align-items: flex-start; }
            .cv2-row { display: flex; flex-wrap: wrap; gap: 8px; }
            .chip { display: inline-flex; align-items: center; gap: 6px; background: #4e5058;
                    color: #dbdee1; border-radius: 4px; padding: 5px 12px; font-size: 14px;
                    text-decoration: none; }
            .chip.select { background: #2b2d31; border: 1px solid #3f4147; }
            .chip-hint { color: #949ba4; font-size: 12px; }
            hr.divider { border: 0; border-top: 1px solid #3f4147; margin: 4px 0; width: 100%; }
            .embed { border-left: 4px solid #4f545c; background: #2b2d31; border-radius: 4px;
                     padding: 10px 14px; margin-top: 8px; display: grid; gap: 6px; }
            .embed-title { font-weight: 600; color: #f2f3f5; }
            .embed-title a { color: #00a8fc; text-decoration: none; }
            .embed-author { font-size: 13px; color: #f2f3f5; font-weight: 600; }
            .embed-fields { display: flex; flex-wrap: wrap; gap: 10px; }
            .embed-field { flex: 1 1 100%; }
            .embed-field.inline { flex: 1 1 180px; }
            .embed-field-name { font-weight: 600; color: #f2f3f5; font-size: 13px; }
            .embed-footer { color: #949ba4; font-size: 12px; }
            .embed-thumb { max-width: 120px; max-height: 120px; border-radius: 4px; }
            .forward { border: 1px solid #3f4147; border-radius: 8px; padding: 10px 12px;
                       margin-top: 8px; background: rgba(43, 45, 49, .5); }
            .forward-head { display: flex; gap: 10px; align-items: baseline; color: #949ba4; font-size: 12px; }
            .forward-author { color: #b5bac1; font-size: 13px; margin-top: 4px; }
            .stickers { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 6px; }
            .sticker-item { display: flex; flex-direction: column; align-items: center; gap: 2px;
                            font-size: 11px; color: #949ba4; max-width: 96px; text-align: center; }
            .sticker-item img { width: 96px; height: 96px; object-fit: contain; }
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

        builder.Append("<header><div class=\"header-inner\">");
        builder.Append($"<h1>{Html(ticket.ToString())}</h1>");
        builder.Append("<dl>");
        AppendDefinition(builder, "类型", ticket.TicketType.Describe());
        AppendDefinition(builder, "分类", ticket.TicketCategory.Describe());
        AppendDefinition(builder, "提交人", $"{ticket.TicketCreator.DisplayName} ({ticket.TicketCreator.UserId})");
        AppendDefinition(builder, "提交时间", FormatTime(ticket.CreatedAt));
        AppendDefinition(builder, "状态", ticket.Status.ToString());
        AppendDefinition(builder, "审核", DescribeApprove(ticket));
        AppendDefinition(builder, "关闭", DescribeActor(ticket.CloseInfo?.UserId, ticket.CloseInfo?.DisplayName, ticket.CloseInfo?.ClosedAt));
        AppendDefinition(builder, "归档", DescribeActor(ticket.ArchiveInfo?.UserId, ticket.ArchiveInfo?.DisplayName, ticket.ArchiveInfo?.ArchivedAt));
        AppendDefinition(builder, "原始数据", $"<a href=\"{TicketLocalData.TicketFileName}\">{TicketLocalData.TicketFileName}</a>");
        builder.Append("</dl></div></header><main>");

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
              document.querySelectorAll('.image img').forEach(el => {
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

        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            builder.Append($"<div class=\"content\">{FormatContent(message.Content)}</div>");
        }

        AppendEmbeds(builder, message.Embeds, entryNames);
        AppendStickers(builder, message.Stickers, entryNames);
        AppendComponents(builder, message.Components, entryNames);
        AppendForwards(builder, message.Forwards, entryNames);

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

    /// <summary>渲染 embed 卡片：标题 / 描述 / 字段 / 图片 / 页脚。</summary>
    private static void AppendEmbeds(
        StringBuilder builder,
        List<TicketEmbed>? embeds,
        IReadOnlyDictionary<string, string> entryNames)
    {
        if (embeds is not { Count: > 0 })
        {
            return;
        }

        foreach (var embed in embeds)
        {
            var color = embed.Color is { } raw ? $"#{raw & 0xFFFFFF:X6}" : "#4f545c";
            builder.Append($"<div class=\"embed\" style=\"border-left-color:{color}\">");

            if (!string.IsNullOrWhiteSpace(embed.AuthorName))
            {
                builder.Append($"<div class=\"embed-author\">{Html(embed.AuthorName)}</div>");
            }

            if (!string.IsNullOrWhiteSpace(embed.Title))
            {
                builder.Append(string.IsNullOrWhiteSpace(embed.Url)
                    ? $"<div class=\"embed-title\">{Html(embed.Title)}</div>"
                    : $"<div class=\"embed-title\"><a href=\"{Attr(embed.Url)}\" target=\"_blank\" rel=\"noreferrer noopener\">{Html(embed.Title)}</a></div>");
            }

            if (!string.IsNullOrWhiteSpace(embed.Description))
            {
                builder.Append($"<div class=\"content\">{FormatContent(embed.Description)}</div>");
            }

            if (embed.Fields is { Count: > 0 } fields)
            {
                builder.Append("<div class=\"embed-fields\">");
                foreach (var field in fields)
                {
                    builder.Append(field.Inline ? "<div class=\"embed-field inline\">" : "<div class=\"embed-field\">");
                    builder.Append($"<div class=\"embed-field-name\">{Html(field.Name)}</div>");
                    builder.Append($"<div class=\"content\">{FormatContent(field.Value)}</div></div>");
                }
                builder.Append("</div>");
            }

            var image = MapPath(embed.ImagePath, entryNames);
            if (image is not null)
            {
                builder.Append($"<a class=\"image\" href=\"{Attr(image)}\" target=\"_blank\">");
                builder.Append($"<img src=\"{Attr(image)}\" alt=\"\" loading=\"lazy\"></a>");
            }
            else if (!string.IsNullOrWhiteSpace(embed.ImageUrl))
            {
                builder.Append($"<a class=\"file missing\" href=\"{Attr(embed.ImageUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">图片（未下载）</a>");
            }

            var thumbnail = MapPath(embed.ThumbnailPath, entryNames);
            if (thumbnail is not null)
            {
                builder.Append($"<img class=\"embed-thumb\" src=\"{Attr(thumbnail)}\" alt=\"\" loading=\"lazy\">");
            }

            if (!string.IsNullOrWhiteSpace(embed.FooterText))
            {
                builder.Append($"<div class=\"embed-footer\">{Html(embed.FooterText)}</div>");
            }

            builder.Append("</div>");
        }
    }

    /// <summary>渲染贴纸：能预览的显示小图，Lottie 只给原件链接。</summary>
    private static void AppendStickers(
        StringBuilder builder,
        List<TicketSticker>? stickers,
        IReadOnlyDictionary<string, string> entryNames)
    {
        if (stickers is not { Count: > 0 })
        {
            return;
        }

        builder.Append("<div class=\"stickers\">");
        foreach (var sticker in stickers)
        {
            var path = MapPath(sticker.LocalPath, entryNames);

            builder.Append("<div class=\"sticker-item\">");
            if (path is not null && TicketResourceDownloader.IsImageExtension(Path.GetExtension(path)))
            {
                builder.Append($"<img src=\"{Attr(path)}\" alt=\"{Attr(sticker.Name)}\" loading=\"lazy\">");
            }
            else if (!string.IsNullOrWhiteSpace(sticker.Url))
            {
                builder.Append($"<a class=\"file missing\" href=\"{Attr(sticker.Url)}\" target=\"_blank\" rel=\"noreferrer noopener\">原件</a>");
            }

            builder.Append($"<span>{Html(sticker.Name)}</span></div>");
        }
        builder.Append("</div>");
    }

    /// <summary>渲染 Components V2 组件树（Container / Section / 文本 / 按钮 / 媒体）。</summary>
    private static void AppendComponents(
        StringBuilder builder,
        List<TicketComponent>? components,
        IReadOnlyDictionary<string, string> entryNames)
    {
        if (components is not { Count: > 0 })
        {
            return;
        }

        builder.Append("<div class=\"cv2\">");
        foreach (var component in components)
        {
            AppendComponent(builder, component, entryNames);
        }
        builder.Append("</div>");
    }

    private static void AppendComponent(
        StringBuilder builder,
        TicketComponent component,
        IReadOnlyDictionary<string, string> entryNames)
    {
        switch (component.Kind)
        {
            case "text":
                if (!string.IsNullOrWhiteSpace(component.Text))
                {
                    builder.Append($"<div class=\"content\">{FormatContent(component.Text)}</div>");
                }
                return;

            case "button":
            case "link":
            {
                var label = Html(component.Text ?? "按钮");
                builder.Append(string.IsNullOrWhiteSpace(component.Url)
                    ? $"<span class=\"chip\">{label}</span>"
                    : $"<a class=\"chip\" href=\"{Attr(component.Url)}\" target=\"_blank\" rel=\"noreferrer noopener\">{label}</a>");
                return;
            }

            case "select":
                builder.Append($"<div class=\"chip select\">{Html(component.Text ?? "下拉菜单")}");
                if (!string.IsNullOrWhiteSpace(component.Description))
                {
                    builder.Append($"<span class=\"chip-hint\">{Html(component.Description)}</span>");
                }
                builder.Append("</div>");
                return;

            case "media":
            case "file":
            {
                var path = MapPath(component.Path, entryNames);
                if (path is not null && TicketResourceDownloader.IsImageExtension(Path.GetExtension(path)))
                {
                    builder.Append($"<a class=\"image\" href=\"{Attr(path)}\" target=\"_blank\">");
                    builder.Append($"<img src=\"{Attr(path)}\" alt=\"{Attr(component.Text ?? string.Empty)}\" loading=\"lazy\"></a>");
                }
                else if (path is not null)
                {
                    builder.Append($"<a class=\"file\" href=\"{Attr(path)}\" download>");
                    builder.Append($"{Html(component.Text ?? Path.GetFileName(path))}</a>");
                }
                else if (!string.IsNullOrWhiteSpace(component.Url))
                {
                    builder.Append($"<a class=\"file missing\" href=\"{Attr(component.Url)}\" target=\"_blank\" rel=\"noreferrer noopener\">");
                    builder.Append($"{Html(component.Text ?? component.Url)}（未下载）</a>");
                }
                return;
            }

            case "divider":
                builder.Append("<hr class=\"divider\">");
                return;

            default:
                if (component.Children is { Count: > 0 } children)
                {
                    builder.Append($"<div class=\"cv2-{Attr(component.Kind)}\">");
                    foreach (var child in children)
                    {
                        AppendComponent(builder, child, entryNames);
                    }
                    builder.Append("</div>");
                }
                return;
        }
    }

    /// <summary>渲染转发的消息快照。</summary>
    private static void AppendForwards(
        StringBuilder builder,
        List<TicketForward>? forwards,
        IReadOnlyDictionary<string, string> entryNames)
    {
        if (forwards is not { Count: > 0 })
        {
            return;
        }

        foreach (var forward in forwards)
        {
            builder.Append("<div class=\"forward\"><div class=\"forward-head\">↪ 转发的消息");
            if (forward.SentAt is { } sentAt)
            {
                builder.Append($"<time datetime=\"{sentAt:O}\">{FormatTime(sentAt)}</time>");
            }
            builder.Append("</div>");

            if (!string.IsNullOrWhiteSpace(forward.AuthorDisplayName))
            {
                builder.Append($"<div class=\"forward-author\">原作者：{Html(forward.AuthorDisplayName)}</div>");
            }

            if (!string.IsNullOrWhiteSpace(forward.Content))
            {
                builder.Append($"<div class=\"content\">{FormatContent(forward.Content)}</div>");
            }

            AppendEmbeds(builder, forward.Embeds, entryNames);
            AppendStickers(builder, forward.Stickers, entryNames);
            AppendComponents(builder, forward.Components, entryNames);

            if (forward.Attachments is { Count: > 0 } attachments)
            {
                builder.Append("<div class=\"attachments\">");
                foreach (var attachment in attachments)
                {
                    AppendAttachment(builder, attachment, entryNames);
                }
                builder.Append("</div>");
            }

            builder.Append("</div>");
        }
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

    /// <summary>转义 + 链接 + 换行；提及保持原始 <c>&lt;@id&gt;</c> / <c>&lt;#id&gt;</c> / <c>&lt;@&amp;id&gt;</c> 形式。</summary>
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

        // 昵称提及 <@!id> 归一化为 <@id>；用户/频道/身份组提及全部原样保留
        var normalized = NicknameMentionRegex().Replace(text, "<@$1>");
        var encoded = WebUtility.HtmlEncode(normalized);
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

    [GeneratedRegex(@"<@!(\d+)>")]
    private static partial Regex NicknameMentionRegex();
}

using System.Text;

using Discord;
using Discord.WebSocket;

using Lunarium.Bot.Ticket.Model;
using Lunarium.Logging;

namespace Lunarium.Bot.Ticket.TicketService;

/// <summary>
/// 归档抓取器：把工单频道的全部消息写入 <see cref="TicketData.TicketMessages"/>，
/// 并把附件 / 头像 / 正文外链图片以**原件**保存到工单目录的 <c>attachment/</c> 下。
/// <para>
/// 只负责下载与落盘，不修改原件、不生成 HTML（打包时才做 PNG→JPG 转换）。
/// </para>
/// </summary>
public sealed class TicketCrawler
{
    /// <summary>单张工单最多抓取的消息数。</summary>
    private const int MaxMessages = 5000;

    /// <summary>单个 Discord 附件的体积上限。</summary>
    private const long MaxDiscordFileBytes = 25L * 1024 * 1024;

    /// <summary>单张外链图片的体积上限。</summary>
    private const long MaxExternalImageBytes = 8L * 1024 * 1024;

    /// <summary>单张工单最多下载的外链图片数量。</summary>
    private const int MaxExternalPerTicket = 20;

    /// <summary>单张工单下载资源总量上限，防止磁盘被打满。</summary>
    private const long MaxTotalBytes = 256L * 1024 * 1024;

    private readonly ILogger _logger;
    private readonly TicketLocalData _localData;
    private readonly TicketResourceDownloader _downloader;

    public TicketCrawler(ILogger logger, TicketLocalData localData, TicketResourceDownloader downloader)
    {
        _logger = logger.ForContext("TicketCrawler");
        _localData = localData;
        _downloader = downloader;
    }

    /// <summary>
    /// 抓取频道并填充 <paramref name="ticket"/> 的 <see cref="TicketData.TicketMessages"/>。
    /// 调用方负责随后 <c>SaveAsync</c>；单个资源失败只记录，不影响整体归档。
    /// </summary>
    public async Task CrawlAsync(
        ITextChannel channel,
        TicketData ticket,
        CancellationToken cancellationToken = default)
    {
        var ticketDirectory = _localData.GetTicketDirectory(ticket.Id);
        var attachmentRoot = _localData.GetAttachmentDirectory(ticket.Id);
        var avatarDirectory = Path.Combine(attachmentRoot, "avatar");
        var externalDirectory = Path.Combine(attachmentRoot, "external");

        Directory.CreateDirectory(attachmentRoot);

        var raw = new List<IMessage>();
        await foreach (var batch in channel.GetMessagesAsync(limit: MaxMessages))
        {
            cancellationToken.ThrowIfCancellationRequested();
            raw.AddRange(batch);
            if (raw.Count >= MaxMessages)
            {
                break;
            }
        }

        // GetMessagesAsync 返回"新→旧"，归档按时间正序渲染
        raw.Reverse();

        _logger.Info("开始抓取工单对话: {Ticket} 消息数={Count}", ticket, raw.Count);

        var avatars = new Dictionary<ulong, string?>();
        var messages = new List<TicketMessage>(raw.Count);
        var externalCount = 0;
        var totalBytes = 0L;

        for (var i = 0; i < raw.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var message = raw[i];
            var index = i + 1;

            if (!avatars.TryGetValue(message.Author.Id, out var avatarPath))
            {
                avatarPath = await DownloadAvatarAsync(message.Author, avatarDirectory, ticketDirectory, cancellationToken);
                avatars[message.Author.Id] = avatarPath;
            }

            var attachments = new List<TicketAttachment>();

            foreach (var attachment in message.Attachments)
            {
                var saved = await SaveDiscordAttachmentAsync(
                    attachment, attachmentRoot, ticketDirectory, index, cancellationToken);
                attachments.Add(saved);
                totalBytes += saved.Size;
            }

            foreach (var embed in message.Embeds)
            {
                foreach (var url in EnumerateEmbedImageUrls(embed))
                {
                    if (externalCount >= MaxExternalPerTicket || totalBytes >= MaxTotalBytes)
                    {
                        break;
                    }

                    var saved = await SaveExternalAsync(url, externalDirectory, ticketDirectory, cancellationToken);
                    if (saved is not null)
                    {
                        attachments.Add(saved);
                        totalBytes += saved.Size;
                        externalCount++;
                    }
                }
            }

            foreach (var url in TicketResourceDownloader.ExtractImageUrls(message.Content))
            {
                if (externalCount >= MaxExternalPerTicket || totalBytes >= MaxTotalBytes)
                {
                    break;
                }

                var saved = await SaveExternalAsync(url, externalDirectory, ticketDirectory, cancellationToken);
                if (saved is not null)
                {
                    attachments.Add(saved);
                    totalBytes += saved.Size;
                    externalCount++;
                }
            }

            ulong? replyToMessageId = null;
            if (message.Reference is { } reference && reference.MessageId.IsSpecified)
            {
                replyToMessageId = reference.MessageId.Value;
            }

            messages.Add(new TicketMessage
            {
                MessageId = message.Id,
                AuthorId = message.Author.Id,
                AuthorDisplayName = message.Author.GlobalName ?? message.Author.Username,
                AvatarPath = avatarPath,
                Content = message.Content,
                SentAt = message.CreatedAt,
                ReplyToMessageId = replyToMessageId,
                Attachments = attachments.Count > 0 ? attachments : null,
            });

            if (totalBytes >= MaxTotalBytes)
            {
                _logger.Warning("工单 {Ticket} 下载资源达到上限，剩余消息只记录元数据", ticket);
                break;
            }
        }

        ticket.TicketMessages = messages;
        _logger.Info("工单对话抓取完成: {Ticket} 消息={Messages} 外链={External} 下载总量={Bytes}",
            ticket, messages.Count, externalCount, totalBytes);
    }

    private async Task<string?> DownloadAvatarAsync(
        IUser author,
        string avatarDirectory,
        string ticketDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = author.GetDisplayAvatarUrl(size: 128);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var resource = await _downloader.DownloadAsync(
                uri, MaxExternalImageBytes, requireImage: true, cancellationToken);
            if (resource is null)
            {
                return null;
            }

            var extension = TicketResourceDownloader.ExtensionFor(resource.MediaType);
            if (extension.Length == 0)
            {
                extension = ".png";
            }

            Directory.CreateDirectory(avatarDirectory);
            var fileName = $"{author.Id}{extension}";
            var path = Path.Combine(avatarDirectory, fileName);

            if (!File.Exists(path))
            {
                await File.WriteAllBytesAsync(path, resource.Bytes, cancellationToken);
            }

            return ToRelativePath(ticketDirectory, path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "下载头像失败: UserId={UserId}", author.Id);
            return null;
        }
    }

    private async Task<TicketAttachment> SaveDiscordAttachmentAsync(
        IAttachment attachment,
        string attachmentRoot,
        string ticketDirectory,
        int index,
        CancellationToken cancellationToken)
    {
        var isImage = IsImage(attachment.ContentType, attachment.Filename);

        try
        {
            if (!Uri.TryCreate(attachment.Url, UriKind.Absolute, out var uri))
            {
                return Failed(attachment, isImage);
            }

            var resource = await _downloader.DownloadAsync(
                uri, MaxDiscordFileBytes, requireImage: false, cancellationToken);
            if (resource is null)
            {
                return Failed(attachment, isImage);
            }

            Directory.CreateDirectory(attachmentRoot);
            var fileName = $"{index:D4}-{SanitizeFileName(attachment.Filename)}";
            var path = MakeUnique(Path.Combine(attachmentRoot, fileName));
            await File.WriteAllBytesAsync(path, resource.Bytes, cancellationToken);

            return new TicketAttachment
            {
                FileName = attachment.Filename,
                Url = attachment.Url,
                Size = resource.Bytes.LongLength,
                IsImage = isImage,
                LocalPath = ToRelativePath(ticketDirectory, path),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "下载附件失败: {FileName} {Url}", attachment.Filename, attachment.Url);
            return Failed(attachment, isImage);
        }
    }

    private async Task<TicketAttachment?> SaveExternalAsync(
        Uri uri,
        string externalDirectory,
        string ticketDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var hash = TicketResourceDownloader.ShortHash(uri.AbsoluteUri);

            // 同一外链在一张工单里只下载一次
            if (Directory.Exists(externalDirectory))
            {
                var cached = Directory.EnumerateFiles(externalDirectory, $"{hash}.*").FirstOrDefault();
                if (cached is not null)
                {
                    return new TicketAttachment
                    {
                        FileName = Path.GetFileName(uri.AbsolutePath),
                        Url = uri.AbsoluteUri,
                        Size = new FileInfo(cached).Length,
                        IsImage = true,
                        IsExternal = true,
                        LocalPath = ToRelativePath(ticketDirectory, cached),
                    };
                }
            }

            var resource = await _downloader.DownloadAsync(
                uri, MaxExternalImageBytes, requireImage: true, cancellationToken);
            if (resource is null)
            {
                return null;
            }

            var extension = TicketResourceDownloader.ExtensionFor(resource.MediaType);
            if (extension.Length == 0)
            {
                extension = Path.GetExtension(uri.AbsolutePath);
                if (extension.Length == 0)
                {
                    extension = ".img";
                }
            }

            Directory.CreateDirectory(externalDirectory);
            var path = Path.Combine(externalDirectory, hash + extension);
            await File.WriteAllBytesAsync(path, resource.Bytes, cancellationToken);

            return new TicketAttachment
            {
                FileName = Path.GetFileName(uri.AbsolutePath) is { Length: > 0 } name ? name : hash + extension,
                Url = uri.AbsoluteUri,
                Size = resource.Bytes.LongLength,
                IsImage = true,
                IsExternal = true,
                LocalPath = ToRelativePath(ticketDirectory, path),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "下载外链图片失败: {Url}", uri);
            return null;
        }
    }

    private static TicketAttachment Failed(IAttachment attachment, bool isImage) => new()
    {
        FileName = attachment.Filename,
        Url = attachment.Url,
        Size = attachment.Size,
        IsImage = isImage,
        LocalPath = null,
    };

    private static IEnumerable<Uri> EnumerateEmbedImageUrls(IEmbed embed)
    {
        foreach (var url in new[] { embed.Image?.Url, embed.Thumbnail?.Url })
        {
            if (!string.IsNullOrWhiteSpace(url)
                && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && TicketResourceDownloader.IsImageExtension(Path.GetExtension(uri.AbsolutePath)))
            {
                yield return uri;
            }
        }
    }

    private static bool IsImage(string? contentType, string fileName)
    {
        return (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false)
            || TicketResourceDownloader.IsImageExtension(Path.GetExtension(fileName));
    }

    private static string ToRelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static string MakeUnique(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name}-{i}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }

        var result = builder.ToString().Trim();
        if (result.Length == 0)
        {
            result = "file";
        }

        if (result.Length > 80)
        {
            var extension = Path.GetExtension(result);
            result = string.Concat(result.AsSpan(0, Math.Max(1, 80 - extension.Length)), extension);
        }

        return result;
    }
}

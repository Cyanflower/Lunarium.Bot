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

    /// <summary>下载并发上限：同时拉多个大文件会争带宽，过高的并发反而拉长总耗时。</summary>
    private readonly SemaphoreSlim _downloadGate = new(4);

    public TicketCrawler(ILogger logger, TicketLocalData localData, TicketResourceDownloader downloader)
    {
        _logger = logger.ForContext("TicketService.TicketCrawler");
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
        var stickerDirectory = Path.Combine(attachmentRoot, "sticker");

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
        var budget = new CrawlBudget();

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
            var attachmentsByName = new Dictionary<string, TicketAttachment>(StringComparer.OrdinalIgnoreCase);

            // 消息内附件并行下载（WhenAll 返回值顺序与输入一致）
            var attachmentTasks = message.Attachments
                .Select(attachment => SaveDiscordAttachmentAsync(
                    attachment, attachmentRoot, ticketDirectory, $"{index:D4}-", cancellationToken))
                .ToArray();
            var savedAttachments = await Task.WhenAll(attachmentTasks);
            foreach (var (attachment, saved) in message.Attachments.Zip(savedAttachments))
            {
                attachments.Add(saved);
                attachmentsByName[attachment.Filename] = saved;
                budget.AddBytes(saved.Size);
            }

            var embedTasks = message.Embeds
                .Select(embed => SaveEmbedAsync(embed, externalDirectory, ticketDirectory, budget, cancellationToken))
                .ToArray();
            var embeds = (await Task.WhenAll(embedTasks)).ToList();

            var stickers = await SaveStickersAsync(
                message.Stickers, stickerDirectory, ticketDirectory, budget, cancellationToken);

            var components = await SaveComponentsAsync(
                message.Components, externalDirectory, ticketDirectory, attachmentsByName, budget, cancellationToken);

            var forwards = await SaveForwardsAsync(
                message, attachmentRoot, externalDirectory, stickerDirectory, ticketDirectory, index, budget, cancellationToken);

            foreach (var url in TicketResourceDownloader.ExtractImageUrls(message.Content))
            {
                if (budget.Exhausted)
                {
                    break;
                }

                var saved = await SaveExternalAsync(url.AbsoluteUri, externalDirectory, ticketDirectory, cancellationToken);
                if (saved is not null)
                {
                    attachments.Add(saved);
                    budget.CountExternal(saved.Size);
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
                Embeds = embeds.Count > 0 ? embeds : null,
                Stickers = stickers,
                Components = components,
                Forwards = forwards,
            });

            if (budget.TotalBytes >= MaxTotalBytes)
            {
                _logger.Warning("工单 {Ticket} 下载资源达到上限，剩余消息只记录元数据", ticket);
                break;
            }
        }

        ticket.TicketMessages = messages;
        _logger.Info("工单对话抓取完成: {Ticket} 消息={Messages} 外链={External} 下载总量={Bytes}",
            ticket, messages.Count, budget.ExternalCount, budget.TotalBytes);
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

            await _downloadGate.WaitAsync(cancellationToken);
            try
            {
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
            finally
            {
                _downloadGate.Release();
            }
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
        string prefix,
        CancellationToken cancellationToken)
    {
        var isImage = IsImage(attachment.ContentType, attachment.Filename);

        try
        {
            if (!Uri.TryCreate(attachment.Url, UriKind.Absolute, out var uri))
            {
                return Failed(attachment, isImage);
            }

            await _downloadGate.WaitAsync(cancellationToken);
            try
            {
                var resource = await _downloader.DownloadAsync(
                    uri, MaxDiscordFileBytes, requireImage: false, cancellationToken);
                if (resource is null)
                {
                    return Failed(attachment, isImage);
                }

                Directory.CreateDirectory(attachmentRoot);
                var fileName = $"{prefix}{SanitizeFileName(attachment.Filename)}";
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
            finally
            {
                _downloadGate.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "下载附件失败: {FileName} {Url}", attachment.Filename, attachment.Url);
            return Failed(attachment, isImage);
        }
    }

    /// <summary>抓取消息里的贴纸；返回 <see langword="null"/> 表示没有贴纸。</summary>
    private async Task<List<TicketSticker>?> SaveStickersAsync(
        IReadOnlyCollection<IStickerItem> stickers,
        string stickerDirectory,
        string ticketDirectory,
        CrawlBudget budget,
        CancellationToken cancellationToken)
    {
        if (stickers.Count == 0)
        {
            return null;
        }

        var result = new List<TicketSticker>(stickers.Count);
        foreach (var sticker in stickers)
        {
            result.Add(await SaveStickerAsync(sticker, stickerDirectory, ticketDirectory, budget, cancellationToken));
        }

        return result;
    }

    /// <summary>
    /// 下载一张贴纸。Lottie 只能保存 <c>.json</c> 原件（归档 HTML 无法渲染），其余保存图片。
    /// </summary>
    private async Task<TicketSticker> SaveStickerAsync(
        IStickerItem sticker,
        string stickerDirectory,
        string ticketDirectory,
        CrawlBudget budget,
        CancellationToken cancellationToken)
    {
        var format = sticker.Format switch
        {
            StickerFormatType.Gif => "gif",
            StickerFormatType.Lottie => "json",
            _ => "png",
        };

        // 静态图统一走 media 端点：APNG 也能拿到可直接展示的 PNG
        var url = format == "json"
            ? $"https://cdn.discordapp.com/stickers/{sticker.Id}.json"
            : $"https://media.discordapp.net/stickers/{sticker.Id}.{format}?size=320";

        var fallback = new TicketSticker
        {
            Id = sticker.Id,
            Name = sticker.Name,
            Format = format,
            Url = url,
        };

        try
        {
            if (budget.Exhausted || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return fallback;
            }

            var resource = await _downloader.DownloadAsync(
                uri, MaxExternalImageBytes, requireImage: format != "json", cancellationToken);
            if (resource is null)
            {
                return fallback;
            }

            Directory.CreateDirectory(stickerDirectory);
            var path = Path.Combine(stickerDirectory, $"{sticker.Id}.{format}");
            await File.WriteAllBytesAsync(path, resource.Bytes, cancellationToken);
            budget.AddBytes(resource.Bytes.LongLength);

            return new TicketSticker
            {
                Id = sticker.Id,
                Name = sticker.Name,
                Format = format,
                Url = url,
                LocalPath = ToRelativePath(ticketDirectory, path),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "下载贴纸失败: StickerId={StickerId}", sticker.Id);
            return fallback;
        }
    }

    /// <summary>抓取一条 embed，并把其中的大图/缩略图下载到 <c>attachment/external/</c>。</summary>
    private async Task<TicketEmbed> SaveEmbedAsync(
        IEmbed embed,
        string externalDirectory,
        string ticketDirectory,
        CrawlBudget budget,
        CancellationToken cancellationToken)
    {
        var imageUrl = embed.Image?.Url;
        var thumbnailUrl = embed.Thumbnail?.Url;

        var imageTask = TrySaveEmbedImageAsync(
            imageUrl, externalDirectory, ticketDirectory, budget, cancellationToken);
        var thumbnailTask = TrySaveEmbedImageAsync(
            thumbnailUrl, externalDirectory, ticketDirectory, budget, cancellationToken);

        await Task.WhenAll(imageTask, thumbnailTask);
        var imagePath = imageTask.Result;
        var thumbnailPath = thumbnailTask.Result;

        return new TicketEmbed
        {
            Title = NullIfEmpty(embed.Title),
            Description = NullIfEmpty(embed.Description),
            Url = NullIfEmpty(embed.Url),
            Color = embed.Color?.RawValue,
            AuthorName = NullIfEmpty(embed.Author?.Name),
            FooterText = NullIfEmpty(embed.Footer?.Text),
            Timestamp = embed.Timestamp,
            Fields = embed.Fields.Length > 0
                ? embed.Fields
                    .Select(field => new TicketEmbedField
                    {
                        Name = field.Name,
                        Value = field.Value,
                        Inline = field.Inline,
                    })
                    .ToList()
                : null,
            ImageUrl = NullIfEmpty(imageUrl),
            ImagePath = imagePath,
            ThumbnailUrl = NullIfEmpty(thumbnailUrl),
            ThumbnailPath = thumbnailPath,
        };
    }

    private async Task<string?> TrySaveEmbedImageAsync(
        string? url,
        string externalDirectory,
        string ticketDirectory,
        CrawlBudget budget,
        CancellationToken cancellationToken)
    {
        if (budget.Exhausted || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var saved = await SaveExternalAsync(url, externalDirectory, ticketDirectory, cancellationToken);
        if (saved is null)
        {
            return null;
        }

        budget.CountExternal(saved.Size);
        return saved.LocalPath;
    }

    /// <summary>抓取转发的消息快照（正文 / 附件 / embed / 组件）。</summary>
    private async Task<List<TicketForward>?> SaveForwardsAsync(
        IMessage message,
        string attachmentRoot,
        string externalDirectory,
        string stickerDirectory,
        string ticketDirectory,
        int index,
        CrawlBudget budget,
        CancellationToken cancellationToken)
    {
        if (message is not IUserMessage userMessage || userMessage.ForwardedMessages.Count == 0)
        {
            return null;
        }

        var forwards = new List<TicketForward>(userMessage.ForwardedMessages.Count);
        var ordinal = 0;

        foreach (var snapshot in userMessage.ForwardedMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ordinal++;

            var inner = snapshot.Message;
            var attachments = new List<TicketAttachment>();
            var attachmentsByName = new Dictionary<string, TicketAttachment>(StringComparer.OrdinalIgnoreCase);

            foreach (var attachment in inner.Attachments)
            {
                var saved = await SaveDiscordAttachmentAsync(
                    attachment, attachmentRoot, ticketDirectory, $"{index:D4}-fwd{ordinal}-", cancellationToken);
                attachments.Add(saved);
                attachmentsByName[attachment.Filename] = saved;
                budget.AddBytes(saved.Size);
            }

            var embeds = new List<TicketEmbed>();
            foreach (var embed in inner.Embeds)
            {
                embeds.Add(await SaveEmbedAsync(embed, externalDirectory, ticketDirectory, budget, cancellationToken));
            }

            var stickers = await SaveStickersAsync(
                inner.Stickers, stickerDirectory, ticketDirectory, budget, cancellationToken);

            var components = await SaveComponentsAsync(
                inner.Components, externalDirectory, ticketDirectory, attachmentsByName, budget, cancellationToken);

            // 注意：Discord.Net 构造快照时没有传 author（RestMessage.Create 收到 null），
            // 因此这里通常拿不到原作者；保留判空以便官方补上后自动生效。
            var author = inner.Author;

            forwards.Add(new TicketForward
            {
                AuthorId = author?.Id ?? 0,
                AuthorDisplayName = author?.GlobalName ?? author?.Username,
                Content = inner.Content,
                SentAt = inner.CreatedAt,
                Attachments = attachments.Count > 0 ? attachments : null,
                Embeds = embeds.Count > 0 ? embeds : null,
                Stickers = stickers,
                Components = components,
            });
        }

        return forwards.Count > 0 ? forwards : null;
    }

    /// <summary>递归抓取组件树；返回 <see langword="null"/> 表示整条消息没有可用组件。</summary>
    private async Task<List<TicketComponent>?> SaveComponentsAsync(
        IReadOnlyCollection<IMessageComponent> components,
        string externalDirectory,
        string ticketDirectory,
        IReadOnlyDictionary<string, TicketAttachment> attachmentsByName,
        CrawlBudget budget,
        CancellationToken cancellationToken)
    {
        if (components.Count == 0)
        {
            return null;
        }

        var result = new List<TicketComponent>(components.Count);
        foreach (var component in components)
        {
            var saved = await SaveComponentAsync(
                component, externalDirectory, ticketDirectory, attachmentsByName, budget, cancellationToken);
            if (saved is not null)
            {
                result.Add(saved);
            }
        }

        return result.Count > 0 ? result : null;
    }

    private async Task<TicketComponent?> SaveComponentAsync(
        IMessageComponent component,
        string externalDirectory,
        string ticketDirectory,
        IReadOnlyDictionary<string, TicketAttachment> attachmentsByName,
        CrawlBudget budget,
        CancellationToken cancellationToken)
    {
        switch (component)
        {
            case TextDisplayComponent text:
                return string.IsNullOrWhiteSpace(text.Content)
                    ? null
                    : new TicketComponent { Kind = "text", Text = text.Content };

            case ButtonComponent button:
            {
                var label = !string.IsNullOrWhiteSpace(button.Label)
                    ? button.Label
                    : button.Emote?.Name;
                return new TicketComponent
                {
                    Kind = string.IsNullOrWhiteSpace(button.Url) ? "button" : "link",
                    Text = label,
                    Url = NullIfEmpty(button.Url),
                };
            }

            case SelectMenuComponent select:
                return new TicketComponent
                {
                    Kind = "select",
                    Text = NullIfEmpty(select.Placeholder) ?? "（下拉菜单）",
                    Description = select.Options.Count > 0
                        ? string.Join(" / ", select.Options.Select(option => option.Label))
                        : null,
                };

            case ContainerComponent container:
                return new TicketComponent
                {
                    Kind = "container",
                    Children = await SaveComponentsAsync(
                        container.Components, externalDirectory, ticketDirectory, attachmentsByName, budget, cancellationToken),
                };

            case SectionComponent section:
            {
                var children = await SaveComponentsAsync(
                    section.Components, externalDirectory, ticketDirectory, attachmentsByName, budget, cancellationToken)
                    ?? [];

                if (section.Accessory is { } accessory)
                {
                    var saved = await SaveComponentAsync(
                        accessory, externalDirectory, ticketDirectory, attachmentsByName, budget, cancellationToken);
                    if (saved is not null)
                    {
                        children.Add(saved);
                    }
                }

                return new TicketComponent
                {
                    Kind = "section",
                    Children = children.Count > 0 ? children : null,
                };
            }

            case ActionRowComponent row:
                return new TicketComponent
                {
                    Kind = "row",
                    Children = await SaveComponentsAsync(
                        row.Components, externalDirectory, ticketDirectory, attachmentsByName, budget, cancellationToken),
                };

            case ThumbnailComponent thumbnail:
                return await SaveMediaComponentAsync(
                    "media", thumbnail.Media.Url, thumbnail.Description,
                    externalDirectory, ticketDirectory, attachmentsByName, budget, cancellationToken);

            case MediaGalleryComponent gallery:
            {
                var children = new List<TicketComponent>();
                foreach (var item in gallery.Items)
                {
                    var saved = await SaveMediaComponentAsync(
                        "media", item.Media.Url, item.Description,
                        externalDirectory, ticketDirectory, attachmentsByName, budget, cancellationToken);
                    if (saved is not null)
                    {
                        children.Add(saved);
                    }
                }

                return children.Count > 0 ? new TicketComponent { Kind = "gallery", Children = children } : null;
            }

            case FileComponent file:
                return await SaveMediaComponentAsync(
                    "file", file.File.Url, file.Name,
                    externalDirectory, ticketDirectory, attachmentsByName, budget, cancellationToken);

            case SeparatorComponent:
                return new TicketComponent { Kind = "divider" };

            default:
                return null;
        }
    }

    /// <summary>
    /// 保存组件里的媒体。<c>attachment://name</c> 指向本条消息自身的附件（已下载），
    /// 其余 http(s) 地址按外链下载。
    /// </summary>
    private async Task<TicketComponent?> SaveMediaComponentAsync(
        string kind,
        string? url,
        string? description,
        string externalDirectory,
        string ticketDirectory,
        IReadOnlyDictionary<string, TicketAttachment> attachmentsByName,
        CrawlBudget budget,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (url.StartsWith("attachment://", StringComparison.OrdinalIgnoreCase))
        {
            var name = url["attachment://".Length..];
            return attachmentsByName.TryGetValue(name, out var attachment)
                ? new TicketComponent
                {
                    Kind = kind,
                    Text = NullIfEmpty(description),
                    Url = attachment.Url,
                    Path = attachment.LocalPath,
                }
                : new TicketComponent { Kind = kind, Text = NullIfEmpty(description), Url = url };
        }

        string? path = null;
        if (!budget.Exhausted)
        {
            // 组件里的"文件"可能不是图片，只有缩略图/画廊才强制 image/*
            var requireImage = kind is "media";
            var saved = await SaveExternalAsync(
                url, externalDirectory, ticketDirectory, cancellationToken, requireImage);
            if (saved is not null)
            {
                budget.CountExternal(saved.Size);
                path = saved.LocalPath;
            }
        }

        return new TicketComponent { Kind = kind, Text = NullIfEmpty(description), Url = url, Path = path };
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>单张工单的下载预算：限制外链数量与总字节（多线程并发访问）。</summary>
    private sealed class CrawlBudget
    {
        private int _externalCount;
        private long _totalBytes;

        public int ExternalCount => Volatile.Read(ref _externalCount);

        public long TotalBytes => Volatile.Read(ref _totalBytes);

        public bool Exhausted => ExternalCount >= MaxExternalPerTicket || TotalBytes >= MaxTotalBytes;

        public void AddBytes(long bytes)
        {
            if (bytes > 0)
            {
                Interlocked.Add(ref _totalBytes, bytes);
            }
        }

        public void CountExternal(long bytes)
        {
            Interlocked.Increment(ref _externalCount);
            AddBytes(bytes);
        }
    }

    private async Task<TicketAttachment?> SaveExternalAsync(
        string url,
        string externalDirectory,
        string ticketDirectory,
        CancellationToken cancellationToken,
        bool requireImage = true)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var hash = TicketResourceDownloader.ShortHash(uri.AbsoluteUri);

            await _downloadGate.WaitAsync(cancellationToken);
            try
            {
                // 同一外链在一张工单里只下载一次（并发下也要二次确认缓存）
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
                    uri, MaxExternalImageBytes, requireImage, cancellationToken);
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
            finally
            {
                _downloadGate.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "下载外链图片失败: {Url}", url);
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

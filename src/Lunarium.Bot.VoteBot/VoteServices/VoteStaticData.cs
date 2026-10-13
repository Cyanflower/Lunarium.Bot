
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Lunarium.Bot.Logger;
using Lunarium.Bot.VoteBot.Utils;
using Polly.Retry;
using Discord.Rest;
using Discord;

namespace Lunarium.Bot.VoteBot.VoteServices;

public static class VoteStaticData
{
    public static List<SubmitterData> ResourcesAdmin { get; set; } = new();
    public static List<SubmitterData> ChatsAdmin { get; set; } = new();
    public static List<SubmitterData> Press { get; set; } = new();
    public static List<SubmitterData> CompetitionAdmin { get; set; } = new();
    // private static string _basePath = AppContext.BaseDirectory;
    private static string _basePath = Directory.GetCurrentDirectory();
    private static string _submittersDataPath = Path.Combine("Data", "SubmittersData.json");
    public const int PageSize = 25;

    public static async Task ConvertData(Dictionary<ulong, SubmitData> datas, RestGuild guild, ILogger logger)
    {
        try
        {
            int num = 0;

            AsyncRetryPolicy httpRetryPolicy = Util.HttpRetryPolicyConfig(logger);
            
            foreach (var data in datas)
            {
                num++;

                var submitterData = new SubmitterData();
           
                var user =  await httpRetryPolicy.ExecuteAsync(async () =>
                {
                    return await guild.GetUserAsync(data.Value.SubmitterId);
                });
                
                string username = "Unknown User";
                if (!string.IsNullOrWhiteSpace(user.Nickname))
                {
                    username = user.Nickname;
                }
                else if (!string.IsNullOrWhiteSpace(user.GlobalName))
                {
                    username = user.GlobalName;
                }
                else if (!string.IsNullOrWhiteSpace(user.Username))
                {
                    username = user.Username;
                }
                username = username ?? "Unknown User";

                username = $"{username} (编号: {num})";

                submitterData.Name = username;
                submitterData.SubmitterId = data.Value.SubmitterId;
                submitterData.Number = num;
                submitterData.FirstChoice = data.Value.FirstChoice;
                submitterData.SecondChoice = data.Value.SecondChoice;

                switch (data.Value.FirstChoice)
                {
                    case Choice.ResourcesAdmin:
                        ResourcesAdmin.Add(submitterData);
                        break;
                    case Choice.ChatsAdmin:
                        ChatsAdmin.Add(submitterData);
                        break;
                    case Choice.Press:
                        Press.Add(submitterData);
                        break;
                    case Choice.CompetitionAdmin:
                        CompetitionAdmin.Add(submitterData);
                        break;
                    case Choice.None:
                    default:
                        break;
                }
    
                if (data.Value.FirstChoice == data.Value.SecondChoice) return;
    
                switch (data.Value.SecondChoice)
                {
                    case Choice.ResourcesAdmin:
                        ResourcesAdmin.Add(submitterData);
                        break;
                    case Choice.ChatsAdmin:
                        ChatsAdmin.Add(submitterData);
                        break;
                    case Choice.Press:
                        Press.Add(submitterData);
                        break;
                    case Choice.CompetitionAdmin:
                        CompetitionAdmin.Add(submitterData);
                        break;
                    case Choice.None:
                    default:
                        break;
                }
            }
            SaveSubmitterData(logger);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "VoteStaticData.ConvertData Exception");
        }
    }

    public static void LoadSumbitterData(ILogger logger)
    {
        try
        {
            var submittersDataPath = Path.Combine(_basePath, _submittersDataPath);
            if (File.Exists(submittersDataPath))
            {
                var json = File.ReadAllText(submittersDataPath);
                // 使用特定的 DTO 解析
                var submittersDatas = JsonSerializer.Deserialize<AllSubmitterDatas>(json, JsonSerializerConfig.Options);

                if (submittersDatas != null)
                {
                    // ResourcesAdmin
                    ResourcesAdmin.Clear();
                    if (submittersDatas.ResourcesAdmin != null)
                        ResourcesAdmin.AddRange(submittersDatas.ResourcesAdmin);

                    // ChatsAdmin
                    ChatsAdmin.Clear();
                    if (submittersDatas.ChatsAdmin != null)
                        ChatsAdmin.AddRange(submittersDatas.ChatsAdmin);

                    // Press
                    Press.Clear();
                    if (submittersDatas.Press != null)
                        Press.AddRange(submittersDatas.Press);

                    // CompetitionAdmin
                    CompetitionAdmin.Clear();
                    if (submittersDatas.CompetitionAdmin != null)
                        CompetitionAdmin.AddRange(submittersDatas.CompetitionAdmin);

                    logger.Info($"已加载报名名单 -> 资源: {ResourcesAdmin.Count}, 闲聊: {ChatsAdmin.Count}, 记者: {Press.Count}, 赛事: {CompetitionAdmin.Count}");

                }
            }
            else
            {
                logger.Warning($"用户报名数据文件不存在: {_submittersDataPath}, 将跳过加载。");
            }
        }
        catch
        {
            throw;
        }
    }
    
    public static void SaveSubmitterData(ILogger logger)
    {
        try
        {
            AllSubmitterDatas dataToSave;

            dataToSave = new AllSubmitterDatas
            {
                // 创建新列表作为快照，切断引用关系
                ResourcesAdmin = new List<SubmitterData>(ResourcesAdmin),
                ChatsAdmin = new List<SubmitterData>(ChatsAdmin),
                Press = new List<SubmitterData>(Press),
                CompetitionAdmin = new List<SubmitterData>(CompetitionAdmin)
            };
            var json = JsonSerializer.Serialize(dataToSave, JsonSerializerConfig.Options);
            var fullPath = Path.Combine(_basePath, _submittersDataPath);
            var directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, json);
            
            logger.Info($"保存成功！已将报名数据写入: {fullPath}");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "VoteStaticData.SaveSubmitterData Exception");
            throw;
        }
    }

    public static IReadOnlyList<SubmitterData> GetPageData(Choice choice, int page)
    {
        if (page < 0) page = 0;

        List<SubmitterData>? targetList = choice switch
        {
            Choice.ResourcesAdmin => ResourcesAdmin,
            Choice.ChatsAdmin => ChatsAdmin,
            Choice.Press => Press,
            Choice.CompetitionAdmin => CompetitionAdmin,
            _ => null
        };

        if (targetList == null) return Array.Empty<SubmitterData>();

        // 同样的分页逻辑
        return targetList
            .Skip(page * PageSize)
            .Take(PageSize)
            .ToList(); // 返回的是对象引用，性能开销很小
    }

    public static int GetTotalPages(Choice choice)
    {
        int totalItems = choice switch
        {
            Choice.ResourcesAdmin    => ResourcesAdmin.Count,
            Choice.ChatsAdmin        => ChatsAdmin.Count,
            Choice.Press             => Press.Count,
            Choice.CompetitionAdmin  => CompetitionAdmin.Count,
            _                        => 0
        };

        if (totalItems == 0) return 0;

        return (int)Math.Ceiling((double)totalItems / PageSize);
    }
}
// using Discord;
// using Discord.WebSocket;
// using Discord.Interactions;
// using LunariumBot.Net.SearchBot.Scheduler;
// using LunariumBot.Net.SearchBot.Database;
// using LunariumBot.Net.SearchBot.Utils;
// using LunariumBot.Net.SearchBot.Models;
// using LunariumBot.Net.Extensions;
// using LunariumBot.Net.Logger;

// namespace LunariumBot.Net.SearchBot.AppInteractionService;

// public partial class InteractiveButtons : InteractionModuleBase<SocketInteractionContext>
// {
//     private readonly PaginatorManager _paginatorManager;
//     private readonly DatabaseService _databaseService;
//     private readonly InteractionUtils _interactionUtils;
//     private readonly ILogger _logger;

//     public InteractiveButtons(ILogger logger, PaginatorManager paginatorManager, DatabaseService databaseService, InteractionUtils interactionUtils)
//     {
//         _logger = logger;
//         _paginatorManager = paginatorManager;
//         _databaseService = databaseService;
//         _interactionUtils = interactionUtils;
//     }

//     // --- 新的分页按钮，用 "IP" (Interactive Paginator) 作为前缀 ---
//     [ComponentInteraction("IP_first_*_*", true)]
//     public async Task FirstPageButton(int currentPage, string uuid) => await UpdateInteractivePaginatorAsync(uuid, 1);

//     [ComponentInteraction("IP_prev_*_*", true)]
//     public async Task PreviousPageButton(int currentPage, string uuid) => await UpdateInteractivePaginatorAsync(uuid, currentPage - 1);

//     [ComponentInteraction("IP_jump_*", true)]
//     public async Task JumpPageButton(string uuid)
//     {
//         try
//         {
//             var state = _paginatorManager.GetInteractiveState(uuid);
//             if (state == null)
//             {
//                 await RespondAsync("这个分页视图已经过期啦，请重新使用命令。", ephemeral: true);
//                 return;
//             }
    
//             var totalPages = state.GetTotalPages();
            
//             var modal = new ModalBuilder()
//                 .WithTitle("跳转到页面")
//                 // CustomId 格式: IP_modal_jump_{totalPages}_{uuid}
//                 .WithCustomId($"IP_modal_jump_{totalPages}_{uuid}")
//                 .AddTextInput(
//                     label: "页码",
//                     customId: "page_number_input",
//                     placeholder: $"请输入 1 - {totalPages} 之间的页码",
//                     required: true,
//                     style: TextInputStyle.Short,
//                     maxLength: totalPages.ToString().Length
//                 );
    
//             await Context.Interaction.RespondWithModalAsync(modal.Build());
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex);
//         }
//     }

//     [ComponentInteraction("IP_next_*_*", true)]
//     public async Task NextPageButton(int currentPage, string uuid) => await UpdateInteractivePaginatorAsync(uuid, currentPage + 1);

//     [ComponentInteraction("IP_last_*_*", true)]
//     public async Task LastPageButton(int currentPage, string uuid)
//     {
//         var state = _paginatorManager.GetInteractiveState(uuid);
//         if (state != null)
//         {
//             await UpdateInteractivePaginatorAsync(uuid, state.GetTotalPages());
//         }
//     }

//     [ModalInteraction("IP_modal_jump_*_*", true)]
//     public async Task HandleJumpPageModal(int totalPages, string uuid, InteractivePageJumpModal modal)
//     {
//         if (Context.Interaction is not SocketModal modalInteraction) return;

//         if (!int.TryParse(modal.PageNumber, out int pageNum) || pageNum < 1 || pageNum > totalPages)
//         {
//             await modalInteraction.RespondAsync($"页码超出范围啦，请输入 1 到 {totalPages} 之间的数字。", ephemeral: true);
//             return;
//         }
//         // 这里的逻辑可以直接调用核心更新方法
//         await UpdateInteractivePaginatorAsync(uuid, pageNum);
//     }

//     // --- 核心更新方法 ---
//     private async Task UpdateInteractivePaginatorAsync(string uuid, int newPageNum, bool needDefer = true)
//     {
//         if (Context.Interaction is not SocketMessageComponent interaction) return;
//         if (needDefer)
//         {
//             await DeferAsync();
//         }
        
//         var state = _paginatorManager.GetInteractiveState(uuid);
//         if (state == null)
//         {
//             await interaction.FollowupAsync("这个分页视图已经过期啦，请重新使用命令。", ephemeral: true);
//             await interaction.ModifyOriginalResponseAsync(p => { p.Content = "已过期"; p.Embeds = null; p.Components = null; });
//             return;
//         }

//         var totalPages = state.GetTotalPages();
//         // 如果删完了所有东西
//         if (totalPages == 0)
//         {
//             var emptyEmbed = new EmbedBuilder().WithDescription("这里已经空空如也啦！").WithColor(Color.Blue).Build();
//             await interaction.ModifyOriginalResponseAsync(p =>
//             {
//                 p.Embed = emptyEmbed;
//                 p.Components = new ComponentBuilder().Build();
//             });
//             return;
//         }

//         // 确保页码在有效范围内
//         var pageToShow = Math.Clamp(newPageNum, 1, totalPages);

//         var (postsOnPage, embedsOnPage) = state.GetPage(pageToShow);

//         var components = _interactionUtils.BuildInteractiveComponents(uuid, pageToShow, totalPages, postsOnPage, state.Type, Context);

//         await interaction.ModifyOriginalResponseAsync(props =>
//         {
//             props.Embeds = embedsOnPage.ToArray();
//             props.Components = components;
//         });
//     }

//     // --- 删除按钮回调 ---
//     [ComponentInteraction("rm_F_inter_*_*", true)]
//     public async Task HandleInteractiveRemoveFavorite(ulong threadId, string uuid)
//     {
//         await DeferAsync();
//         // 鉴权
//         var userId = Context.User.Id;
//         // 确保操作者就是发起者
//         if (!uuid.StartsWith(userId.ToString()))
//         {
//             await FollowupAsync("这是别人的按钮哦！", ephemeral: true);
//             return;
//         }

//         await _databaseService.RemoveFavoriteAsync(userId, threadId);

//         var state = _paginatorManager.GetInteractiveState(uuid);
//         if (state != null)
//         {
//             state.RemoveItem(threadId);
//         }

//         // 获取当前页码并刷新
//         var interaction = Context.Interaction as SocketMessageComponent;
//         var paginatorButton = interaction!.Message.Components.SelectMany(r => r.Components).FirstOrDefault(c => c.CustomId.StartsWith("IP_prev_")) as ButtonComponent;
//         var currentPage = int.Parse(paginatorButton!.CustomId.Split('_')[2]);

//         await UpdateInteractivePaginatorAsync(uuid, currentPage, false);
//     }

//     [ComponentInteraction("rm_ST_inter_*_*", true)]
//     public async Task HandleInteractiveRemoveSubscription(ulong threadId, string uuid)
//     {
//         await DeferAsync();
//         // 鉴权
//         var userId = Context.User.Id;
//         if (!uuid.StartsWith(userId.ToString()))
//         {
//             await RespondAsync("这是别人的按钮哦！", ephemeral: true);
//             return;
//         }

//         await _databaseService.RemoveSubscriptionAsync(userId, threadId);

//         var state = _paginatorManager.GetInteractiveState(uuid);
//         if (state != null)
//         {
//             state.RemoveItem(threadId);
//         }

//         var interaction = Context.Interaction as SocketMessageComponent;
//         var paginatorButton = interaction!.Message.Components.SelectMany(r => r.Components).FirstOrDefault(c => c.CustomId.StartsWith("IP_prev_")) as ButtonComponent;
//         var currentPage = int.Parse(paginatorButton!.CustomId.Split('_')[2]);

//         await UpdateInteractivePaginatorAsync(uuid, currentPage,false);
//     }
// }

// public class InteractivePageJumpModal : IModal
// {
//     public string Title => "跳转到页面";

//     [InputLabel("输入页码")]
//     [ModalTextInput("page_num_input", TextInputStyle.Short, "请输入想跳转到的页码", 1, 5)]
//     public string PageNumber { get; set; } = "";
// }

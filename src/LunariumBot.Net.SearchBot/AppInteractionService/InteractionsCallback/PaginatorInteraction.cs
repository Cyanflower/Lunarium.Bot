
// // NuGet 包依赖
// using Discord;
// using Discord.WebSocket;
// using Discord.Interactions;

// // 解决方案内引用项目
// using LunariumBot.Net.Logger;

// // 项目内命名空间
// using LunariumBot.Net.SearchBot.Scheduler;
// using LunariumBot.Net.SearchBot.Models;

// namespace LunariumBot.Net.SearchBot.AppInteractionService;

// public partial class Buttons : InteractionModuleBase<SocketInteractionContext>
// {
//     // --- 首页 ---
//     // CustomId 格式: P_first_{currentPage}_{totalPages}_{uuid}
//     [ComponentInteraction("P_first_*_*_*", true)]
//     public async Task FirstPageButton(int currentPage, int totalPages, string uuid)
//     {
//         if (Context.Interaction is SocketMessageComponent interaction)
//         {
//             await UpdatePaginatorAsync(interaction, uuid, 1);
//         }
//     }

//     // --- 上一页 ---
//     // CustomId 格式: P_prev_{currentPage}_{totalPages}_{uuid}
//     [ComponentInteraction("P_prev_*_*_*", true)]
//     public async Task PreviousPageButton(int currentPage, int totalPages, string uuid)
//     {
//         if (Context.Interaction is SocketMessageComponent interaction)
//         {
//             await UpdatePaginatorAsync(interaction, uuid, currentPage - 1);
//         }
//     }

//     // --- 跳转页面 (弹出模态框) ---
//     // CustomId 格式: P_jump_{totalPages}_{uuid}
//     [ComponentInteraction("P_jump_*_*", true)]
//     public async Task JumpPageButton(int totalPages, string uuid)
//     {
//         try
//         {
//             var modal = new ModalBuilder()
//                 .WithTitle("跳转到页面")
//                 // CustomId 格式: P_modal_jump_{totalPages}_{uuid}
//                 .WithCustomId($"P_modal_jump_{totalPages}_{uuid}")
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
//             _logger.Error("JumpPageButton", ex);
//         }
//     }

//     // --- 下一页 ---
//     // CustomId 格式: P_next_{currentPage}_{totalPages}_{uuid}
//     [ComponentInteraction("P_next_*_*_*", true)]
//     public async Task NextPageButton(int currentPage, int totalPages, string uuid)
//     {
//         if (Context.Interaction is SocketMessageComponent interaction)
//         {
//             await UpdatePaginatorAsync(interaction, uuid, currentPage + 1);
//         }
//     }

//     // --- 尾页 ---
//     // CustomId 格式: P_last_{currentPage}_{totalPages}_{uuid}
//     [ComponentInteraction("P_last_*_*_*", true)]
//     public async Task LastPageButton(int currentPage, int totalPages, string uuid)
//     {
//         if (Context.Interaction is SocketMessageComponent interaction)
//         {
//             await UpdatePaginatorAsync(interaction, uuid, totalPages);
//         }
//     }

//     /// <summary>
//     /// 更新分页消息的核心辅助方法
//     /// </summary>
//     private async Task UpdatePaginatorAsync(SocketMessageComponent interaction, string uuid, int newPageNum)
//     {
//         try
//         {
//             var state = _paginatorManager.GetState(uuid);
//             if (state == null)
//             {
//                 await interaction.UpdateAsync(props =>
//                 {
//                     props.Content = "这个分页视图已经过期啦，请重新使用命令。";
//                     props.Embeds = null;
//                     props.Components = new ComponentBuilder().Build();
//                 });
//                 return;
//             }
    
//             var totalPages = state.Pages.Count;
//             var embeds = state.Pages[newPageNum - 1]; // 页码是1-based, 索引是0-based
    
//             bool isFirstPage = newPageNum == 1;
//             bool isLastPage = newPageNum == totalPages;
    
//             var components = new ComponentBuilder()
//                 .WithButton("首页", $"P_first_{newPageNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isFirstPage)
//                 .WithButton("上一页", $"P_prev_{newPageNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isFirstPage)
//                 .WithButton($"{newPageNum}/{totalPages}", $"P_jump_{totalPages}_{uuid}", style: ButtonStyle.Secondary)
//                 .WithButton("下一页", $"P_next_{newPageNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage)
//                 .WithButton("尾页", $"P_last_{newPageNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage)
//                 .Build();
    
//             await interaction.UpdateAsync(props =>
//             {
//                 props.Embeds = embeds.ToArray();
//                 props.Components = components;
//             });
//         }
//         catch (Exception ex)
//         {
//             _logger.Error("UpdatePaginatorAsync", ex);
//         }
//     }
// }

// // === --- === 模态框处理模块 === --- ===
// public class PaginatorModals : InteractionModuleBase<SocketInteractionContext>
// {
//     private readonly PaginatorManager _paginatorManager;
//     private readonly ILogger _logger;

//     public PaginatorModals(PaginatorManager paginatorManager, ILogger logger)
//     {
//         _paginatorManager = paginatorManager;
//         _logger = logger;
//     }

//     // CustomId 格式: P_modal_jump_{totalPages}_{uuid}
//     [ModalInteraction("P_modal_jump_*_*", true)]
//     public async Task HandleJumpPageModal(int totalPages, string uuid, JumpPageModal modal)
//     {
//         try
//         {
//             // 获取 modal 交互对象
//             if (Context.Interaction is not SocketModal modalInteraction)
//             {
//                 // 加个保险，虽然理论上不会发生
//                 return;
//             }
    
//             if (!int.TryParse(modal.PageNumber, out int pageNum) || pageNum < 1 || pageNum > totalPages)
//             {
//                 // 对于无效输入，用 RespondAsync 发一个只有他自己能看到的提示消息
//                 await modalInteraction.RespondAsync($"页码超出范围啦，请输入 1 到 {totalPages} 之间的数字。", ephemeral: true);
//                 return;
//             }
    
//             var state = _paginatorManager.GetState(uuid);
//             if (state == null)
//             {
//                 // 如果分页过期了，直接更新原消息提示过期，并清空组件
//                 await modalInteraction.UpdateAsync(props =>
//                 {
//                     props.Content = "这个分页视图已经过期啦，请重新使用命令。";
//                     props.Embeds = null;
//                     props.Components = new ComponentBuilder().Build();
//                 });
//                 return;
//             }
    
//             // --- 核心逻辑 ---
//             var embeds = state.Pages[pageNum - 1];
//             bool isFirstPage = pageNum == 1;
//             bool isLastPage = pageNum == totalPages;
    
//             var components = new ComponentBuilder()
//                 .WithButton("首页", $"P_first_{pageNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isFirstPage)
//                 .WithButton("上一页", $"P_prev_{pageNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isFirstPage)
//                 .WithButton($"{pageNum}/{totalPages}", $"P_jump_{totalPages}_{uuid}", style: ButtonStyle.Secondary)
//                 .WithButton("下一页", $"P_next_{pageNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage)
//                 .WithButton("尾页", $"P_last_{pageNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage)
//                 .Build();
    
//             // 2. 用 UpdateAsync 作为对模态框提交的直接响应
//             await modalInteraction.UpdateAsync(props =>
//             {
//                 props.Embeds = embeds.ToArray();
//                 props.Components = components;
//             });
//         }
//         catch (Exception ex)
//         {
//             _logger.Error("HandleJumpPageModal", ex);
//         }
//     }
// }
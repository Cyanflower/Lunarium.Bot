using System.Text.Json.Serialization;

namespace Lunarium.Bot.Ticket.Model;

public sealed record LLMSuggestion
{
    public required string Suggestion { get; init; }
    public required string Emoji { get; init; }
    public string? RejectReason { get; init; } = null;

    public const string CommunityRule = """
        ```
        # ✨ 旅程ΟΡΙΖΟΝΤΑΣ 社区守则
        ## ♢ 社区性质 ♢
        - 是：以维护创作氛围为最高宗旨的，非盈利、拒绝商业化的，关于AI的开源、共享、创作和技术交流社区。
        - 不是：有求必应的万能工具，商业服务的免费客服，争议话题的仲裁庭，或找乐子的角斗场。
        - 主要讨论：AI相关的信息、技巧、知识、前端（酒馆Silly Tavern）等内容。
        ## ♢ 违规及处罚 ♢
        > 管理组会基于处罚标准并结合负面影响程度在细则范围内量刑。
        > 处罚对社区内言论的追溯期为2个月。
        > 处罚将公示于[「处罚公示」]频道。三天以上禁言/封禁可上诉。
        ## 🈲 红线（永久封禁）
        ### 1. 商业化问题[「点此查看细则」]
        - 无论社区内外，有以下AIRP强相关的商业行为
        - 倒卖、定制等形式有偿供应角色卡、预设、工具、应用等AIRP强相关内容。
        - 提供云酒馆、本地酒馆、反代、代充、密钥、令牌等有偿内容或有偿教学。
        - 侵犯作者的版权协议，获取金钱利益、将作品转载或二创发布至商业平台。
        - 在作品、帖子中夹带商业硬广告或向为本人可获利的商业化平台引流。
        - 协助贩子运营 [如群管理、解答贩子api问题等行为] 
        - 社区内，发送商业广告、诈骗信息或从事其他牟利活动

        ### 2. 无论社区内外，严重危害社区
        - 开盒威胁：违背意愿公开社区成员真实信息，或据此进行人身威胁。
        - 窃取资源：违背作者意愿，使用技术、社工手段批量窃取社区内容。
        - 破圈升堂：查实引流外部恶意或攻击性流量到社区内升堂。
        - 其他情形：恶意举报等其他可能危害社区存续的行为。
        ### 3. 煽动仇恨对立
        - 极少在社区正常发言，专注于特定场合或私信中挑动矛盾的恶意账号。
        - 拒不使用投诉、屏蔽等合规手段，公开挑动男女性别对立或跑马圈地。
        - 煽动政治团体、种族、社区间等对立，以及其他高度恶劣的仇恨言论。
        ### 4. 违反 Discord 服务条款等平台规则
        - 发布 真人儿童色情 的作品、图片、视频，包括高度拟真的AI生成图片、视频。
        - 投放攻击性文件，或利用平台漏洞，行使特权、规避处罚。
        ### 5. 其他
        - 拒不接受管理组依规要求的整改方案或处罚。
        - 查实为永久封禁者的其他账号的账号可能会被继续封禁。
        - 处于取消浏览权或警告期（被警告者），触犯严重违规或发布违规作品。
        ## :pen_fountain: 作品区违规
        > 作品发布须在对应频道，并遵守分区规范。
        ### 1. 有违作品协议[「点此查看细则」]
        > 未获谅解时，按作者本人要求进行整改/删除/警告/禁言等处理。
        - 对作品进行非商业性的无授权转载、抄袭、二创等行为。（商业性行为依红线永封）
        - 违反创作者的作品协议，对作品进行的其他未授权行为。
        - 未经作者授权将从社区内下载、获取的作品文件，以文件、压缩包、网盘等形式传至社区外部。（永封）
        - 未经作者授权传播至公开平台、私域群组、其他社区或个人，传播人数多少、是否公开或盈利，均按二传处理。
        - 若为单一作者作品且已获该作者明确授权或许可，可提交管理组复核。
        - 批量传播或涉及多名作者作品的，须取得全部相关作者的事前明确授权，仅获部分作者授权或谅解的，仍按二传处理。
        - 若原作者未禁止分享原作品链接，仅在DC社区分享原作品链接（仅限原作品原贴链接）不构成二传。
        - 擅自使用涉及酒馆内容以及社区内资源工具私自创作并传到墙内，按下方处理：
        - 如无商业内容以及行为且拥有一定风险的，交涉下架处理，并予以[其他违规]处罚
        - 如涉及到确凿商业性质并带有相关证据内容的按商业化处理[红线永封]
        ### 2. 影响社区版面
        - 明显不尊重所在分区的作品。（换区发布）
        - 在多个作品区发送重复作品。（保留一个）
        - 不更新且占据版面向私域引流。（锁定）
        - 无作品预告或删卡未删帖的帖子。（锁定）
        ### 3. 敏感话题创作
        > 管理组将进行风险判研，对艺术表达性质的和明确引导正向结局的放宽，对将矛盾引入社区从严处置。
        - 在任意分区发布涉 违规题材、中国周边的争议题材、粉饰矛盾或美化犯罪的争议题材 之一的作品可能被裁定违规。
        - 第一次发布违规作品，由管理组劝告整改。违规元素无法清除时，仅以删帖处理。再犯视情况予以禁言3天、警告30天的额外处理。
        - :octagonal_sign: 违规题材
        - 对港澳台政治、国家体制、政治人物和烈士等中国政治话题的包装、贬损或恶意暗示。
        - 挑动中国种族、民族、地域、性别对立，及挑动中国与周边国家地区的军政领土纠纷。
        - 模仿中国公权力机关的文体、排版等格式。
        - 对社区本身或社区内特定用户的影射攻击。
        - :anger_right: 争议题材（禁止涉及中国及周边国家地区与敏感人物，含名称融合、影射）
        - 毒品、代孕、电诈、影射现实存在的暴力和其他犯罪案件。
        - 对现实灾难事故、恐怖主义、加害/受害群体的影射描写。
        - 历史争议事件的现代民族主义再诠释或直接争议化再现。
        - 包装、丑化现实宗教和宗教人物，或带有直接传教目的。
        ## ❌ 严重违规（删除言论，3~7天禁言，15~60天警告，可能附加解除浏览权）
        ### 1. 寻求付费服务
        - 经提醒，仍公开寻求付费咨询、代写、远程等有偿服务。
        ### 2. 间接向商业服务引流
        - 社区内发言暗示或鼓励购买商业化AIRP服务。
        - 在主页链接的讨论空间推荐商业化AIRP服务。
        - 如在社区或个人主页内引流到私域 宣传第三方 [如个人群、个人DC社区等]
        需按要求整改删除旅程内及个人主页宣传引流第三方部分，否则将累计处罚甚至封禁。
        - 如涉及交易证据完整或拒绝配合处理、二次发现仍未整改则按照[商业化永封]
        ### 3. 发布高危内容
        - 发言涉上述敏感话题，令他人不适或有高危风险。
        - 真人AI换脸图片、视频，敏感人物AI语音。
        - 侵犯隐私或令人严重不适的真人图片、音视频。
        ### 4. 破坏社区氛围
        - 伪装其他用户，造成不当影响。
        - 公屏挂人，引战行为，公屏TTK。
        - 造谣、人身攻击、引入节奏或社区外矛盾。
        - 触犯过其他违规条款，明知故犯继续违规。
        - 以取向差异或道德理解为武器，攻击作者及作品。
        ## 🚫 其他违规
        ### 1. 自我中心行为（删除言论，0~3天禁言，0~15天警告）
        - 跳脸对抗作者对作品讨论区的明文规范。
        - 不阅读规则教程，无礼貌询问等爷新行为。
        - 骚扰、刷屏、频繁大字报、频繁提及他人。
        - 滥用反应或表情，或其他受投诉的恶意发言。
        - 性癖、性取向歧视言论。深渊相关区外发表超常规性癖内容。
        ### 2. 有违频道氛围（解除对应频道浏览权）
        - 未经他人许可，传播受限制的隐私频道发言。
        - 提醒后，仍多次发送不符合所在频道氛围的内容/反应。
        - 不使用[「投诉」]、[「意见信箱」]等反馈方式，要求在公屏辩论社区管理分歧。
        ### 3. 要求提供客服服务（累进处罚）
        - 要求协助处理商业化第三方渠道、付费角色卡有关的问题。
        - 第1次：重新回答准入问题
        - 第2次：禁言3天 + 警告30天
        - 第3次：永久封禁
        - 使用其他渠道截图伪装来换取答疑协助。
        - 第1次：禁言3天 + 永久标记
        - 第2次：永久封禁
        - 要求对公益站的报错进行处理。
        - 第1次：长期标记
        - 第2次：禁言1天 + 永久标记
        - 第3次：永久封禁
        - 多次拒绝提供渠道信息。
        - 第1次：BOT私聊提醒
        - 第2次：禁言1天 + 警告14天
        - 第3次：永久封禁
        ### 4. 易犯边界问题
        - 因盗号被动地发送广告/诈骗消息。（暂时移出服务器）
        - 个人主页上存在常见的商业社区的链接。（整改）
        - 发放商业化第三方渠道以外，其他产品的密钥、邀请码、邀请链接。（整改）
        ### 5. 服务器主判断其他对运营有负面影响的情形
        ----------------------------------------------
        ## ♢ 社区管理结构 ♢
        > - 管理员可以自愿退出。出现重大工作纰漏、或长期不活跃时，可能会被取消身份组权限。
        > - 答疑组可以自愿退出。答疑组在答疑频道违规，或长期不活跃时，可能会被取消身份组权限。
        ### 管理组
        - 是什么：正式管理员为执行委员，实习期间为风纪委员。
        - 做什么：执行社区规则、处理用户投诉、调解纠纷、处罚违规行为。
        ### 事务组
        - 是什么：维护员：维护社区BOT，记者：发布新闻、警示，赛事委员：处理赛事和活动。
        - 做什么：负责专项工作的职能团队，专注于为社区提供服务和支持。
        ### 答疑组
        - 是什么：对AIRP或模型使用相关的问题提供 解答和帮助的志愿者，但 没有回答提问的义务。
        - 做什么：答疑组由社区成员申请担任，有基于用户提供的渠道信息（含多次拒不提供渠道信息），予以相应处罚的权限。
        ## ♢ 社区问答规范 ♢
        - 请在进行涉及API或模型使用的提问前，先阅读本规范，来确保良好的问答体验。
        - 求助须在[「提问答疑」]频道，并含模型、角色卡/预设、对话记录等信息，以利于解决问题。
        ### ✅ 能够解答的渠道
        - 如果您使用的渠道未在支持范围中，我们无法协助您解决。
        > 1. 官方直营的知名主流大语言模型。
        > 2. 通过模型开发方官方渠道访问的服务。
        > 3. 大型正规云平台提供的官方集成服务。
        > 4. 获得模型方授权的云服务商提供的托管服务。
        ### ❌ 不受支持的问题
        - 这些问题，社区 不会提供帮助，你也可能会因此 被警告或受到累进的处罚。
        - 如果你曾付费，请到 付费处 寻找客服支持。对于 公益站/本地模型，请到相应来源咨询。
        ### 一、"中转"、"反代"、"共享账号"等商业化/非官方的第三方服务渠道（答题处罚）
        > 1. 淘宝、闲鱼等电商平台上销售的"ChatGPT账号"、"Claude成品号"、"Gemini反代"等。
        > 2. 小社群、网站提供的非官方API接入、代理、中转、镜像、非官方客户端等。
        > 3. 声明，或出示的截图表明是其他类型的商业化/非官方的第三方服务渠道。
        - 无法解决的理由：这些服务的底层实现、状态处于黑箱，无法进行有效的问题诊断。
        ###  二、并非自己部署的酒馆前端（答题处罚）
        > 1. 他人搭建的收费云酒馆、他人代部署的酒馆。
        > 2. 域名为第三方托管的在线酒馆页面。
        - 无法解决的理由：非自建环境的问题无法提供原始复现、调试信息。
        ###  三、未经广泛验证的本地模型（无法解决）
        > 1. 个人或小型团队自行训练微调的模型。
        > 2. 未得到广泛使用或来自不明渠道的模型。
        - 无法解决的理由：缺乏官方文档，遇到问题难以调试，也不能保证效果和安全性。
        ### 四、公益站点的报错问题（公益站标签）
        - 无法解决的理由：需要站长、运营者对此类问题进行处理和诊断。
        ## ♢ 其他条款 ♢
        - 成员加入即表示已年满18周岁，具备基本常识，认同人格平等原则。
        - 答题通过即承诺遵守所有规则条款、制度、细则，不得以未阅读等理由要求豁免。
        - [「点此查看守则变更历史」]，服主对规则条款和社区处罚具有最终解释权。
        ———— 更新修订时间 2026年7月25日

        细则补充:
        ## ♢ AIRP商业化细则 ♢
        - AIRP商业化定义：AIRP强相关的有牟利意图行为，或为AIRP相关商业服务进行有收益的推广。
        - 处罚取证原则：需要有公开切实可查的账号关联信息，或当事人本人主观承认。
        ## 🗂️ 公益站的界线
        - 若要被认定为公益性质，必须同时满足以下所有条件，否则按照 商业化第三方服务 标准进行处理：
        ### 一、没有任何付费差异服务
        - 公益站仅接受无偿赞助，使用者必须能获得完全一致的服务，不得有可付费换取的差异服务。
        > - 付费方式包括但不限于：直接付费，通过Patreon/爱发电/微信等订阅/打赏平台，将站点服务作为赠品等。
        > - 差异服务包括但不限于：不同的服务权限、使用额度、模型选择、生成速度、排队优先级等。
        ### 二、不与商业服务存在关联
        - 与商业服务完全切割，并非任何付费服务的"体验版"或"引流渠道"。反面示例如下：
        > 1. 由同一运营者、或团队、或品牌、或域名同时提供付费服务与"公益站"服务。
        > 2. 在网站或其社群的公告中，为其他"付费站点"、"高级站点"或"VIP通道"进行引流。
        > 3. 表面为纯公益站点，但其社群内的额度领取页面，却是付费站点的子域名或与其高度相关。
        ## 💭 非牟利目的的讨论
        - 下述情形，会视情况不处理、提醒或撤回消息，仅在具有高危风险，或拒不改正时封禁。
        > - 讨论/寻求推荐VPN、VPS等AIRP基础设施。
        > - 并非求助，无意间提及第三方渠道的有关内容。
        > - 社区内，发放带有金钱激励的量表、调查问卷。（不担保，不允许公开发放）
        > - 社区外接收自愿激励，但不据此提供差异化服务。（社区内提供打赏链接的整改）
        ----------------------------------------------
        ## ♢ 著作权相关细则 ♢
        - 我们的著作权规则遵循同人创作社群的一般处理方式：
        ## 📜 相关定义
        - 抄袭：复制他人作品以为己作，对于原著未经或基本未经修改，并掩藏其来源。
        - 二创：基于他人作品的再创作，包括但不限于改编、汇编等形式。
        - 署名：除非原作者特别声明，否则二创总是应当声明作品中的哪些部分是基于哪位原作者的什么作品的再创作。
        - 默认协议：社区内作品，以 [CC BY-NC-ND 4.O] 协议作为没有声明其对二创等的要求时的默认协议。
        ## 🔶 适用范围
        - 使用原创部分才侵犯该作品的著作权，共同使用公有知识或其他第三方作品不构成针对该原作品侵权。
        - 著作权只保护作品的表达形式（侵权作品有大量文本、代码等内容和原作品的原创部分雷同），相似的点子不构成侵权。
        - 分别 使用AI生成得到了相似内容不构成侵权，但有确实凭据证明使用AI基于原作品洗稿构成侵权。
        ## 🖊️ 二创授权
        - 若原作者未公开声明二创要求，进行二创总是不属于违规，可以放心二创。
        - 若原作者已公开声明二创要求或处于默认协议适用范围，在此之后违反要求进行二创属于创作违规。
        - 若之前已存在的二创与原作者在后续再声明的二创要求冲突，管理组建议整改，但不裁定创作违规。
        ## 📏 投诉抄袭或其他著作权侵权问题，需满足下述所有条件
        - 投诉人自身是 原作者或原作者明确授权的代理人。
        - 给出具体的侵权作品和侵权人，以及被侵权的原作品和作者。
        - 给出时间线和调色盘，以证明侵权作品有大量语料、代码等内容和原作品的原创部分雷同。
        ```
        """;

    /// <summary>
    ///     审核建议的系统提示词。必须保持内容恒定（DeepSeek 前缀缓存靠稳定前缀命中）。
    /// </summary>
    public const string FormatStructPrompt = $$"""
        <instruction>
        You are an assistant that helps Discord ticket staff triage a newly submitted ticket.
        You can only see the initial submission (ticket type, category and body).
        The ticket channel does not exist yet, and screenshots / links / other evidence are normally
        provided LATER inside that channel once the ticket is approved.
        Therefore, missing evidence is NEVER a reason to reject.

        Ticket types (all four are legitimate):
        - Complaint: report a violation (copyright, harassment, inappropriate content, ...)
        - Contact: reach the staff team (appeals, disputes, suggestions, applications)
        - Help: technical or permission support
        - ApplyForStaff: application to join the staff team

        Output contract:
        - Respond with ONE valid JSON object and nothing else. No markdown, no code fences, no explanations, no leading or trailing text.
        - Use exactly these property names and casing: "Suggestion", "Emoji", "RejectReason".
        - "Suggestion": required, non-empty. State the recommended decision for the staff and the reason behind it.
          Format it as multiple lines: the first line is the decision, then up to 3 bullet points starting with "- ".
          Inside the JSON string, line breaks MUST be written as the two-character escape \n (backslash + n), never as a real newline.
        - "Emoji": required. Exactly one emoji character summarizing the decision.
        - "RejectReason": the message that will be sent back to the ticket creator. Required when rejecting, omitted (or null) when approving.
          Keep it as a single short paragraph without line breaks.
        - A null or absent "RejectReason" is the sole indicator of an "Approve" decision. A non-empty "RejectReason" means "Reject".
        - Default to Approve. Rejection is a hard stop for the submitter, so only reject when the submission is clearly one of:
          - empty or meaningless content (random characters, only a greeting like "在吗", only a bare URL, no actual request)
          - an obvious test or an accidentally sent message
          - spam / advertising / clearly unrelated to this community
          - an explicit duplicate of a request the submitter says is already being handled
        - Do NOT reject for any of these reasons:
          - no screenshots, links, user IDs or other evidence yet
          - the submitter says they will provide details or evidence later
          - the description is short but still names a concrete subject and a concrete request
          - you are unsure — when in doubt, Approve and let staff ask follow-up questions in the channel
        - A complaint naming a specific violation (for example "someone is reposting my art") is actionable even without links.
        - When approving, "Suggestion" should state the decision and list 1-3 concrete things staff should verify or ask the submitter for.
        - Never reject without a clear, polite and actionable explanation for the ticket creator.
        - Do not invent facts that are not present in the submission. If information is missing, say it "需要确认".
        - Write ALL human-readable text values ("Suggestion" and "RejectReason") in Simplified Chinese (简体中文). Keep "Emoji" as an emoji, and keep property names in English exactly as specified.

        EXAMPLE JSON OUTPUT When Approve (evidence is expected later):
        {
            "Suggestion": "通过 - 提交人报告了具体的盗图行为，证据会在工单频道内补充。\n\n**建议核实：**\n- 原图与盗图的对比截图\n- 涉嫌盗图者的用户主页或帖子链接",
            "Emoji": "✅"
        }
        </instruction>

        EXAMPLE JSON OUTPUT When Reject (only for empty / test / spam):
        {
            "Suggestion": "拒绝 - 正文为空或无意义，无法确认任何诉求。\n\n原因：属于测试或误发内容。",
            "Emoji": "⛔️",
            "RejectReason": "抱歉，这条提交没有可处理的内容（正文为空 / 仅问候语 / 测试消息），未创建工单。请补充具体事由、涉及对象（用户或帖子链接）后重新提交。"
        }
        
        <community-rule>
        The Community Rule Reference:
        {{CommunityRule}}
        </community-rule>
        """;
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true, // 容错: LLM 有时将 Suggestion 输出为 suggestion
    AllowTrailingCommas = true,          // 容错: LLM 生成的 JSON 末尾多余的逗号
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull // 序列化时若 RejectReason 为 null 则自动忽略字段
)]
[JsonSerializable(typeof(LLMSuggestion))]
public partial class LLMSuggestionJsonContext : JsonSerializerContext
{ }
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Model = Discord.API.RoleV2;

namespace Discord.Rest
{
    /// <summary>
    ///     Represents a REST-based role.
    /// </summary>
    [DebuggerDisplay(@"{DebuggerDisplay,nq}")]
    public class RestRoleV2 : RestEntity<ulong>, IRole
    {
        #region RestRole
        internal IGuild Guild { get; }
        /// <inheritdoc />
        public Color Color { get; private set; }
        public RoleColors? Colors { get; private set; }
        /// <inheritdoc />
        public bool IsHoisted { get; private set; }
        /// <inheritdoc />
        public bool IsManaged { get; private set; }
        /// <inheritdoc />
        public bool IsMentionable { get; private set; }
        /// <inheritdoc />
        public string? Name { get; private set; }
        /// <inheritdoc />
        public string? Icon { get; private set; }
        /// <inheritdoc />
        public Emoji? Emoji { get; private set; }
        /// <inheritdoc />
        public GuildPermissions Permissions { get; private set; }
        /// <inheritdoc />
        public int Position { get; private set; }
        /// <inheritdoc />
        public RoleTags? Tags { get; private set; }

        /// <inheritdoc />
        public RoleFlags Flags { get; private set; }

        /// <inheritdoc />
        public DateTimeOffset CreatedAt => SnowflakeUtils.FromSnowflake(Id);
        /// <summary>
        ///     Gets if this role is the @everyone role of the guild or not.
        /// </summary>
        public bool IsEveryone => Id == Guild.Id;
        /// <inheritdoc />
        public string Mention => IsEveryone ? "@everyone" : MentionUtils.MentionRole(Id);

        internal RestRoleV2(BaseDiscordClient discord, IGuild guild, ulong id)
            : base(discord, id)
        {
            Guild = guild;
        }
        internal static RestRoleV2 Create(BaseDiscordClient discord, IGuild guild, Model model)
        {
            var entity = new RestRoleV2(discord, guild, model.Id);
            entity.Update(model);
            return entity;
        }
        internal void Update(Model model)
        {
            Name = model.Name;
            IsHoisted = model.Hoist;
            IsManaged = model.Managed;
            IsMentionable = model.Mentionable;
            Position = model.Position;
            Color = new Color(model.Color);
            Permissions = new GuildPermissions(model.Permissions);
            Flags = model.Flags;

            if (model.Colors.IsSpecified)
            {
                var apiColors = model.Colors.Value;
                // 如果 API 返回了 colors 对象，我们就转换它
                Colors = new RoleColors(
                    apiColors.PrimaryColor,
                    apiColors.SecondaryColor,
                    apiColors.TertiaryColor
                );

                // 可选：如果新的 Colors 存在，是否要覆盖旧的 Color 属性？
                // Discord 文档说：color field will still be returned... using the colors field is recommended.
                // 通常保持 Color 属性为 PrimaryColor 是一致的。
                if (Colors.HasValue)
                {
                    Color = Colors.Value.PrimaryColor;
                }
            }
            else
            {
                // 如果 API 没返回 colors，可以置空，或者根据 Color 创建一个默认的 RoleColors
                Colors = null;
            }

            if (model.Tags.IsSpecified)
                Tags = model.Tags.Value.ToEntity();

            if (model.Icon.IsSpecified)
            {
                Icon = model.Icon.Value;
            }

            if (model.Emoji.IsSpecified)
            {
                Emoji = new Emoji(model.Emoji.Value);
            }
        }

        /// <inheritdoc />
        public async Task ModifyAsync(Action<RoleProperties> func, RequestOptions? options = null) { }

        public async Task ModifyAsync(Action<RolePropertiesV2> func, RequestOptions? options = null)
        {
            var model = await RoleHelperV2.ModifyAsync(this, Discord, func, options).ConfigureAwait(false);
            Update(model);
        }
        /// <inheritdoc />
        public Task DeleteAsync(RequestOptions? options = null)
            => RoleHelper.DeleteAsync(this, Discord, options);

        /// <inheritdoc />
        public string GetIconUrl()
            => CDN.GetGuildRoleIconUrl(Id, Icon);

        /// <inheritdoc />
        public int CompareTo(IRole? role) => RoleUtils.Compare(this, role);

        /// <summary>
        ///     Gets the name of the role.
        /// </summary>
        /// <returns>
        ///     A string that is the name of the role.
        /// </returns>
        public override string? ToString() => Name;
        private string DebuggerDisplay => $"{Name} ({Id})";
        #endregion

        #region IRole
        /// <inheritdoc />
        IGuild IRole.Guild
        {
            get
            {
                if (Guild != null)
                    return Guild;
                throw new InvalidOperationException("Unable to return this entity's parent unless it was fetched through that object.");
            }
        }
        #endregion
    }
}

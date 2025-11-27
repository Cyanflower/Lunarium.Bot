using Newtonsoft.Json;

namespace Discord.API;

#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
internal class RoleColors
{
    [JsonProperty("primary_color")]
    public uint PrimaryColor { get; set; }

    [JsonProperty("secondary_color")]
    public uint? SecondaryColor { get; set; }

    [JsonProperty("tertiary_color")]
    public uint? TertiaryColor { get; set; }
}

internal class RoleV2
{
    [JsonProperty("id")]
    public ulong Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("icon")]
    public Optional<string> Icon { get; set; }

    [JsonProperty("unicode_emoji")]
    public Optional<string> Emoji { get; set; }

    [JsonProperty("color")]
    public uint Color { get; set; }
    
    [JsonProperty("colors")]
    public Optional<RoleColors> Colors { get; set; }

    [JsonProperty("hoist")]
    public bool Hoist { get; set; }

    [JsonProperty("mentionable")]
    public bool Mentionable { get; set; }

    [JsonProperty("position")]
    public int Position { get; set; }

    [JsonProperty("permissions"), Int53]
    public string Permissions { get; set; }

    [JsonProperty("managed")]
    public bool Managed { get; set; }

    [JsonProperty("tags")]
    public Optional<RoleTags> Tags { get; set; }

    [JsonProperty("flags")]
    public RoleFlags Flags { get; set; }
}
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
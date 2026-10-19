
namespace Lunarium.Bot.Framework.Interaction;

public record HasPrefixSessionId(string Prefix, string SessionId)
{
    public static HasPrefixSessionId? TryParse(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        string[] result = id.Split('|', 2);
        if (result.Length < 2) return null;

        return new HasPrefixSessionId(result[0], result[1]);
    }
}
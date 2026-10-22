using System.Text.Json.Serialization;

namespace Lunarium.Bot.Ticket.Model;

public sealed class TicketId
{
    private int _lastTicketId;

    public int LastTicketId
    {
        get => Volatile.Read(ref _lastTicketId);
        set => Volatile.Write(ref _lastTicketId, value);
    }

    /// <summary>
    /// 原子获取下一个 Ticket ID 并自增(线程安全发号)
    /// </summary>
    public int Next()
    {
        return Interlocked.Increment(ref _lastTicketId);
    }
}
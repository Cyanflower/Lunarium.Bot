namespace Lunarium.Bot.Framework.RateLimiting;

public record Work(Func<CancellationToken, Task> Func, CancellationToken Ct, TaskCompletionSource Tcs);

/// <summary>
/// 有返回值的 <see cref="Work"/>。执行结果写入 <see cref="Result"/>，
/// 基类的 <see cref="Work.Tcs"/> 仍由 <see cref="RateLimitedExecutor"/> 用于标记“本次执行已结束”
/// （成功、取消或拒绝）。
/// </summary>
public sealed record Work<T> : Work
{
    public Work(Func<CancellationToken, Task<T>> func, CancellationToken ct, TaskCompletionSource<T> result)
        : base(async token =>
        {
            try
            {
                result.TrySetResult(await func(token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                result.TrySetCanceled(token);
            }
            catch (Exception ex)
            {
                result.TrySetException(ex);
            }
        }, ct, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
    {
        Result = result;
    }

    internal TaskCompletionSource<T> Result { get; }
}

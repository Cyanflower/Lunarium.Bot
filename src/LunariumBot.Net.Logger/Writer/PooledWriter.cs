
// namespace LunariumBot.Net.Logger.Writer;

// internal readonly struct PooledWriter<T> : IDisposable where T : LogWriter, new()
// {
//     private readonly T _writer;

//     public T Writer => _writer;

//     public PooledWriter(T writer)
//     {
//         _writer = writer;
//     }

//     public void Dispose()
//     {
//         WriterPool.Return(_writer);
//     }
// }
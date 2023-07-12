namespace Gracious;

internal readonly struct SemaphoreSlimWrapper : IDisposable
{
    private readonly SemaphoreSlim _sem;

    public SemaphoreSlimWrapper(int initialCount, int maxCount)
    {
        _sem = new(initialCount, maxCount);
    }

    /// <inheritdoc cref="SemaphoreSlim.Wait(int)"/>
    public Ticket? Wait(int millisecondsTimeout)
    {
        return _sem.Wait(millisecondsTimeout)
            ? new(_sem)
            : null;
    }

    /// <inheritdoc cref="SemaphoreSlim.Wait(CancellationToken)"/>
    public Ticket Wait(CancellationToken cancellationToken = default)
    {
        _sem.Wait(cancellationToken);
        return new(_sem);
    }

    /// <inheritdoc cref="SemaphoreSlim.WaitAsync(CancellationToken)"/>
    public async ValueTask<Ticket> WaitAsync(CancellationToken cancellationToken = default)
    {
        await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new(_sem);
    }

    public void Dispose()
    {
        _sem.Dispose();
    }

    public sealed class Ticket : IDisposable
    {
        private readonly SemaphoreSlim _sem;

        private int _disposed;

        internal Ticket(SemaphoreSlim sem)
        {
            _sem = sem;
            _disposed = 0;
        }

        public bool IsActiveFor(SemaphoreSlimWrapper wrapper)
        {
            return _sem == wrapper._sem && _disposed == 0;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                _sem.Release();
            }
        }
    }
}

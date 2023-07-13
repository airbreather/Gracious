/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
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

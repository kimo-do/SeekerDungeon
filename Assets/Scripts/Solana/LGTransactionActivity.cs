using System;
using System.Threading;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Tracks global on-chain transaction activity so UI can show a shared indicator.
    /// </summary>
    public static class LGTransactionActivity
    {
        private static int _activeCount;

        public static event Action<bool> OnActiveStateChanged;
        public static event Action<bool> OnTransactionCompleted;

        public static bool IsActive => Volatile.Read(ref _activeCount) > 0;

        public static Handle Begin()
        {
            var newCount = Interlocked.Increment(ref _activeCount);
            if (newCount == 1)
            {
                OnActiveStateChanged?.Invoke(true);
            }

            return new Handle();
        }

        private static void End(bool success)
        {
            var remaining = Interlocked.Decrement(ref _activeCount);
            if (remaining < 0)
            {
                Interlocked.Exchange(ref _activeCount, 0);
                remaining = 0;
            }

            OnTransactionCompleted?.Invoke(success);
            if (remaining == 0)
            {
                OnActiveStateChanged?.Invoke(false);
            }
        }

        public sealed class Handle : IDisposable
        {
            private bool _isCompleted;

            public void Complete(bool success)
            {
                if (_isCompleted)
                {
                    return;
                }

                _isCompleted = true;
                End(success);
            }

            public void Dispose()
            {
                Complete(false);
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PraxisCore
{
    public sealed class SimpleLockable
    {
        static ConcurrentDictionary<string, SimpleLockable> updateLocks = new ConcurrentDictionary<string, SimpleLockable>();
        public long counter { get; set; }

        /// <summary>
        /// Get a lock for the given lockId.
        /// </summary>
        /// <param name="lockId">id of the lock you want to hold, as a string</param>
        /// <returns>the SimpleLockable for the given string.</returns>
        public static SimpleLockable GetLock(string lockId)
        {
            updateLocks.TryAdd(lockId, new SimpleLockable());
            var entityLock = updateLocks[lockId];
            entityLock.counter++;
            System.Threading.Monitor.Enter(entityLock);
            return entityLock;
        }

        /// <summary>
        /// Release a previously acquired lock.
        /// </summary>
        /// <param name="lockId">the name of the lock held</param>
        /// <param name="entityLock">the SimpleLock acquired with GetLock</param>
        public static void DropLock(string lockId, SimpleLockable entityLock)
        {
            entityLock.counter--;
            System.Threading.Monitor.Exit(entityLock);
            if (entityLock.counter <= 0)
                updateLocks.TryRemove(lockId, out entityLock);            
        }

        /// <summary>
        /// Release a previously acquired lock
        /// </summary>
        /// <param name="lockId">the is used to acquire the lock</param>
        public static void DropLock(string lockId)
        {
            var entityLock = updateLocks[lockId];
            entityLock.counter--;
            System.Threading.Monitor.Exit(entityLock);
            if (entityLock.counter <= 0)
                updateLocks.TryRemove(lockId, out entityLock);
            
        }

        public static void PerformWithLock(string lockId, Action a)
        {
            updateLocks.TryAdd(lockId, new SimpleLockable());
            var entityLock = updateLocks[lockId];
            try
            {
                entityLock.counter++;
                System.Threading.Monitor.Enter(entityLock);
                a();
                entityLock.counter--;
                if (entityLock.counter <= 0)
                    updateLocks.TryRemove(lockId, out _);
            }
            finally
            {
                System.Threading.Monitor.Exit(entityLock);
            }
        }

        public static Task PerformWithLockAsTask(string lockedKey, Action a)
        {
            return Task.Run(() => PerformWithLock(lockedKey, a));
        }
    }
}

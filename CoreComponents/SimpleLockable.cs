using System.Collections.Concurrent;

namespace PraxisCore
{
    public sealed class SimpleLockable
    {
        static ConcurrentDictionary<string, SimpleLockable> updateLocks = new ConcurrentDictionary<string, SimpleLockable>();
        public long counter { get; set; }

        public static SimpleLockable GetUpdateLock(string lockedKey)
        {
            updateLocks.TryAdd(lockedKey, new SimpleLockable());
            var entityLock = updateLocks[lockedKey];
            entityLock.counter++;
            System.Threading.Monitor.Enter(entityLock);
            return entityLock;
        }

        public static void DropUpdateLock(string lockId, SimpleLockable entityLock)
        {
            entityLock.counter--;
            if (entityLock.counter <= 0)
                updateLocks.TryRemove(lockId, out entityLock);
        }

        public static void DropUpdateLock(string lockId)
        {
            var entityLock = updateLocks[lockId];
            entityLock.counter--; 
            if (entityLock.counter <= 0)
                updateLocks.TryRemove(lockId, out entityLock);
            System.Threading.Monitor.Exit(entityLock);
        }
    }
}

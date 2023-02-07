using System.Collections.Concurrent;

namespace PraxisCore
{
    public class SimpleLockable
    {
        static ConcurrentDictionary<string, SimpleLockable> updateLocks = new ConcurrentDictionary<string, SimpleLockable>();
        public long counter { get; set; }

        public static SimpleLockable GetUpdateLock(string lockedKey)
        {
            //NOTE: replaced ReaderWriterLockSlim with this basic counting class, to avoid weird rare cases where a thread in the IIS thread pool can, rarely,
            //get the same RWLS and look like its acting recursively, and either throw an exception (bad, errors) or allow recusion to get the write lock (differently bad, not actually a lock.)
            updateLocks.TryAdd(lockedKey, new SimpleLockable());
            var entityLock = updateLocks[lockedKey];
            entityLock.counter++;
            System.Threading.Monitor.Enter(entityLock);
            return entityLock;
        }

        public static void DropUpdateLock(string lockId, SimpleLockable entityLock)
        {
            entityLock.counter--; //NOTE: this isn't mission-critical, its to keep the list of locks from growing infintely. Its OK if this isn't Interlocked or lives until the next call.
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

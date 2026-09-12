using System;
using System.Threading;
using System.Windows.Media;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests
{
    /// <summary>
    /// Headless tests for the icon pipeline plumbing: TargetChecker tick re-entrancy and the
    /// IconManager cache cap. The "missing" placeholder singletons themselves are NOT covered
    /// here — creating them needs pack:// application resources, which require the WPF resource
    /// system of the real app and are unavailable under the test host. IsPlaceholderIcon's
    /// negative paths (null / arbitrary ImageSource) are covered, since they must not force
    /// placeholder creation.
    /// </summary>
    public class IconPipelineTargetCheckerTests
    {
        [Fact]
        public void OnTimedEvent_SlowAction_NeverOverlaps()
        {
            // Arrange: tick every 25 ms, but each pass takes ~150 ms. Without the re-entrancy
            // guard the thread-pool Elapsed callbacks stack up and run the action concurrently.
            using var checker = new TargetChecker(25);
            int concurrent = 0;
            int maxConcurrent = 0;
            int executions = 0;

            checker.AddCheckAction("slow", () =>
            {
                int now = Interlocked.Increment(ref concurrent);
                int seen;
                while (now > (seen = Volatile.Read(ref maxConcurrent)))
                    Interlocked.CompareExchange(ref maxConcurrent, now, seen);

                Interlocked.Increment(ref executions);
                Thread.Sleep(150);
                Interlocked.Decrement(ref concurrent);
            }, isFolder: false);

            // Act: let several ticks fire. The wait is bounded by observed progress rather than
            // a fixed sleep: Timer.Elapsed is raised on the thread pool, and when the full suite
            // runs collections in parallel the pool can be saturated long enough that a fixed
            // sleep expires before a single callback is ever scheduled.
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (Volatile.Read(ref executions) < 2 && DateTime.UtcNow < deadline)
                Thread.Sleep(25);
            checker.Dispose();

            // Assert: passes kept running (the guard is released), but never concurrently.
            Volatile.Read(ref executions).ShouldBeGreaterThanOrEqualTo(2);
            Volatile.Read(ref maxConcurrent).ShouldBe(1);
        }

        [Fact]
        public void Dispose_StopsFurtherTicks()
        {
            var checker = new TargetChecker(20);
            int executions = 0;
            checker.AddCheckAction("count", () => Interlocked.Increment(ref executions), isFolder: false);

            Thread.Sleep(150);
            checker.Dispose();
            Thread.Sleep(60); // drain any in-flight callback

            int afterDispose = Volatile.Read(ref executions);
            afterDispose.ShouldBeGreaterThan(0);

            Thread.Sleep(150);

            Volatile.Read(ref executions).ShouldBe(afterDispose);
        }
    }

    public class IconPipelineCacheTests
    {
        private static ImageSource NewIcon() => new DrawingImage();

        [Fact]
        public void CacheIcon_ExceedingCap_ClearsAndKeepsNewEntry()
        {
            IconManager.ClearIconCache();
            try
            {
                for (int i = 0; i < IconManager.MaxCacheEntries; i++)
                    IconManager.CacheIcon($@"C:\fake\cap-{i}.txt", NewIcon());
                IconManager.GetCacheSize().ShouldBe(IconManager.MaxCacheEntries);

                // One more NEW key exceeds the cap: documented policy is clear-and-log,
                // then the incoming entry is stored.
                IconManager.CacheIcon(@"C:\fake\overflow.txt", NewIcon());
                IconManager.GetCacheSize().ShouldBe(1);
            }
            finally
            {
                IconManager.ClearIconCache();
            }
        }

        [Fact]
        public void CacheIcon_UpdatingExistingKeyAtCap_DoesNotClear()
        {
            IconManager.ClearIconCache();
            try
            {
                for (int i = 0; i < IconManager.MaxCacheEntries; i++)
                    IconManager.CacheIcon($@"C:\fake\upd-{i}.txt", NewIcon());

                // Re-writing an existing key at the cap must not trigger the clear.
                IconManager.CacheIcon(@"C:\fake\upd-0.txt", NewIcon());
                IconManager.GetCacheSize().ShouldBe(IconManager.MaxCacheEntries);
            }
            finally
            {
                IconManager.ClearIconCache();
            }
        }

        [Fact]
        public void CacheIcon_NullArguments_AreIgnored()
        {
            IconManager.ClearIconCache();
            try
            {
                IconManager.CacheIcon(null!, NewIcon());
                IconManager.CacheIcon("", NewIcon());
                IconManager.CacheIcon(@"C:\fake\null-icon.txt", null!);

                IconManager.GetCacheSize().ShouldBe(0);
            }
            finally
            {
                IconManager.ClearIconCache();
            }
        }

        [Fact]
        public void ClearIconCache_EmptiesTheCache()
        {
            IconManager.CacheIcon(@"C:\fake\clear-me.txt", NewIcon());
            IconManager.GetCacheSize().ShouldBeGreaterThan(0);

            IconManager.ClearIconCache();

            IconManager.GetCacheSize().ShouldBe(0);
        }

        [Fact]
        public void IsPlaceholderIcon_NonPlaceholderSources_ReturnFalse()
        {
            // Must not force creation of the pack:// placeholders (headless-safe negative path).
            IconManager.IsPlaceholderIcon(null!).ShouldBeFalse();
            IconManager.IsPlaceholderIcon(NewIcon()).ShouldBeFalse();
        }
    }
}

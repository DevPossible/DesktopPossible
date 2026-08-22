using System;
using System.Collections.Generic;
using System.Timers;

namespace Desktop_Frames
{
    public class TargetChecker : IDisposable
    {
        private readonly Timer _timer;
        private readonly Dictionary<string, (Action checkAction, bool isFolder)> _checkActions;
        private readonly object _lockObject = new object();
        private bool _disposed;
        // Re-entrancy guard: System.Timers.Timer raises Elapsed on the thread pool, so a slow
        // pass (network/COM probes) could overlap the next tick. 1 = a pass is in progress.
        private int _checkInProgress;

        public TargetChecker(double interval)
        {
            _timer = new Timer(interval);
            _timer.Elapsed += OnTimedEvent;
            _checkActions = new Dictionary<string, (Action, bool)>();
            Start(); // Start the timer immediately
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        /// <summary>Stops and releases the underlying timer. Replaced instances (e.g. during
        /// ReloadFrames) must be disposed, not just stopped, so they don't accumulate.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _timer.Stop();
                _timer.Elapsed -= OnTimedEvent;
                _timer.Dispose();
            }
            catch { }
        }

        public void AddCheckAction(string key, Action checkAction, bool isFolder)
        {
            if (checkAction == null) return;

            lock (_lockObject)
            {
                if (!_checkActions.ContainsKey(key))
                {
                    _checkActions.Add(key, (checkAction, isFolder));
                }
            }



        }
        public void RemoveCheckAction(string key)
        {
            lock (_lockObject)
            {
                if (_checkActions.ContainsKey(key))
                {
                    _checkActions.Remove(key);
                }
            }
        }

        private void OnTimedEvent(object sender, ElapsedEventArgs e)
        {
            // Skip this tick entirely if the previous pass is still running — overlapping
            // passes stack up thread-pool threads and hammer the same targets concurrently.
            if (System.Threading.Interlocked.Exchange(ref _checkInProgress, 1) == 1) return;
            try
            {
                List<(Action checkAction, bool isFolder)> actionsSnapshot;

                // Create a thread-safe snapshot of the actions
                // lock (_checkActions)
                lock (_lockObject)
                {
                    if (_checkActions.Count == 0)
                        return;

                    actionsSnapshot = new List<(Action, bool)>(_checkActions.Count);
                    foreach (var kvp in _checkActions)
                    {
                        actionsSnapshot.Add(kvp.Value);
                    }
                }

                // Execute actions outside the lock to prevent deadlocks
                foreach (var (checkAction, isFolder) in actionsSnapshot)
                {
                    try
                    {
                        checkAction?.Invoke();
                    }
                    catch (Exception actionEx)
                    {
                        if (SettingsManager.EnableBackgroundValidationLogging)
                        {
                            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.BackgroundValidation,
                                $"Error in target check action: {actionEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log timer event errors
                if (SettingsManager.EnableBackgroundValidationLogging)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.BackgroundValidation,
                        $"Error in TargetChecker timer event: {ex.Message}");
                }
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _checkInProgress, 0);
            }
        }
    }
}
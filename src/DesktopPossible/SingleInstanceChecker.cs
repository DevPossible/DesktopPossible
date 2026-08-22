using System;
using System.Diagnostics;
using System.Threading;

namespace Desktop_Frames
{
    /// <summary>
    /// Single Instance Checker for the DesktopPossible application.
    /// Uses a named per-session mutex acquired at startup and held for the process lifetime,
    /// so two simultaneous launches race safely (exactly one wins). The losing instance
    /// hands off to the winner through the existing registry trigger channel and exits.
    /// </summary>
    public static class SingleInstanceChecker
    {
        // Renamed from Local\DesktopFramesPossible_SingleInstance at the DesktopPossible rebrand.
        private const string MutexName = @"Local\DesktopPossible_SingleInstance";

        // Held (never released) for the process lifetime; the OS releases it on exit.
        private static Mutex _mutex;
        private static bool _isFirstInstance;

        /// <summary>
        /// Tries to acquire the single-instance mutex. Call once at application startup,
        /// before any major initialization. The mutex is held until the process exits.
        /// </summary>
        /// <returns>True if this is the first instance, false if another instance holds the mutex.</returns>
        public static bool TryAcquireSingleInstance()
        {
            if (_mutex != null) return _isFirstInstance;

            try
            {
                _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
                _isFirstInstance = createdNew;

                if (!createdNew)
                {
                    // Not the creator — try a zero-wait acquire in case the previous owner
                    // died without the OS having cleaned up yet.
                    try
                    {
                        _isFirstInstance = _mutex.WaitOne(TimeSpan.Zero);
                    }
                    catch (AbandonedMutexException)
                    {
                        // Previous owner exited without releasing — we now own it.
                        _isFirstInstance = true;
                    }
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"SingleInstanceChecker: Mutex acquired={_isFirstInstance} ({MutexName})");
                return _isFirstInstance;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"SingleInstanceChecker: Error acquiring mutex: {ex.Message}");
                // On error, allow the application to continue (safer than unexpected exit).
                _isFirstInstance = true;
                return true;
            }
        }

        /// <summary>
        /// Handles the case where another instance is already running: writes the registry
        /// trigger (the hand-off channel the running instance polls) so it can react —
        /// e.g. wake its frames, or start draw mode for a "-create" launch.
        /// The caller should exit afterwards.
        /// </summary>
        /// <param name="triggerValue">Optional command (e.g. "CMD_DRAW|guid"); null writes a plain wake trigger.</param>
        public static void HandleDuplicateInstance(string triggerValue = null)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    "SingleInstanceChecker: Another instance is running - writing registry trigger and exiting");

                bool triggerWritten = RegistryHelper.WriteTrigger(triggerValue);

                if (!triggerWritten)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                        "SingleInstanceChecker: Failed to write registry trigger - still exiting duplicate instance");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"SingleInstanceChecker: Error handling duplicate instance: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets information about the current process for debugging.
        /// </summary>
        public static string GetProcessInfo()
        {
            try
            {
                using Process currentProcess = Process.GetCurrentProcess();
                return $"PID: {currentProcess.Id}, Name: {currentProcess.ProcessName}, " +
                       $"Path: {Environment.ProcessPath}, FirstInstance: {_isFirstInstance}";
            }
            catch (Exception ex)
            {
                return $"Error getting process info: {ex.Message}";
            }
        }
    }
}

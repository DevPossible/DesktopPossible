// Inherited upstream code predates nullable reference types: nullable WARNINGS are off for this
// file until it is annotated (annotations remain valid). New files are fully nullable-clean.
#nullable disable warnings

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Desktop_Frames
{
    public class IconLoadRequest
    {
        public string FilePath { get; set; }
        public string TargetPath { get; set; }
        public bool IsFolder { get; set; }
        public bool IsLink { get; set; }
        public bool IsShortcut { get; set; }
        public System.Collections.Generic.IDictionary<string, object> IconDict { get; set; }
        public Image TargetImage { get; set; }
        public Action OnLoaded { get; set; }
    }

    public static class LazyIconLoader
    {
        private static readonly ConcurrentQueue<IconLoadRequest> _loadQueue = new ConcurrentQueue<IconLoadRequest>();
        private static CancellationTokenSource _cts;
        private static Task _loaderTask;
        private static bool _isRunning = false;

        public static void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _cts = new CancellationTokenSource();
            _loaderTask = Task.Run(() => ProcessLoadQueue(_cts.Token));
        }

        /// <summary>Cancels the background loader task (e.g. on app shutdown). Safe to call
        /// when the loader was never started; Start() can be called again afterwards.</summary>
        public static void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch { }
            _cts = null;
        }

        public static void RequestIcon(IconLoadRequest request)
        {
            if (string.IsNullOrEmpty(request.FilePath) || request.TargetImage == null) return;
            _loadQueue.Enqueue(request);
        }

        /// <summary>Drops all pending requests. Called during ReloadFrames teardown so dead
        /// requests from closed frames don't delay icons for the freshly created ones.</summary>
        public static void ClearQueue()
        {
            try
            {
                while (_loadQueue.TryDequeue(out _)) { }
            }
            catch { }
        }

        private static async Task ProcessLoadQueue(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    int processed = 0;
                    // Batch size of 15 prevents stuttering
                    while (processed < 15 && _loadQueue.TryDequeue(out var request))
                    {
                        if (token.IsCancellationRequested) break;

                        ImageSource icon = null;
                        lock (IconManager.IconCache)
                        {
                            if (IconManager.IconCache.TryGetValue(request.FilePath, out var cached))
                                icon = cached;
                        }

                        if (icon == null)
                        {
                            icon = IconManager.GetIconForFile(request.TargetPath, request.FilePath, request.IsFolder, request.IsLink, request.IsShortcut, request.IconDict);
                            if (icon != null && icon.CanFreeze && !icon.IsFrozen) icon.Freeze();
                        }

                        if (icon != null)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                request.TargetImage.Source = icon;
                                // A throwing OnLoaded callback must not take down the loader loop.
                                try { request.OnLoaded?.Invoke(); }
                                catch (Exception cbEx)
                                {
                                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling,
                                        $"LazyIconLoader OnLoaded callback failed for {request.FilePath}: {cbEx.Message}");
                                }
                            }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                        processed++;
                    }
                    await Task.Delay(processed > 0 ? 30 : 100, token);
                }
                catch (OperationCanceledException)
                {
                    break; // shutdown/Stop requested
                }
                catch (Exception ex)
                {
                    // BUG FIX: a single stray exception used to kill the loader for the whole
                    // session (every icon stayed a placeholder, nothing logged). Log and keep going.
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling,
                        $"LazyIconLoader queue error (loader continues): {ex.Message}");
                    try { await Task.Delay(250, token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
    }
}
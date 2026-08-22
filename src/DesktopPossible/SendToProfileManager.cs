using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// "Send to Profile..." on the frame heart menu: copies or moves a frame — its JSON
    /// record plus every file it owns (frame-store folder, profile Shortcuts, image-frame
    /// assets) — into another profile's frames.json without switching profiles.
    /// The frame keeps its on-screen position when that spot is free in the target
    /// profile; otherwise it is placed in the nearest free grid slot.
    /// </summary>
    public static class SendToProfileManager
    {
        public static void Show(dynamic frame)
        {
            try
            {
                string frameId = frame?.Id?.ToString() ?? "";
                dynamic live = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId) ?? frame;
                if (live == null) return;

                var others = ProfileManager.GetProfiles()
                    .Select(p => p.Name)
                    .Where(n => !n.Equals(ProfileManager.CurrentProfileName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (others.Count == 0)
                {
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm("There is no other profile to send this frame to.\n\nCreate one in Options > Profiles first.", "Send to Profile");
                    return;
                }

                string title = live.Title?.ToString() ?? "";
                var choice = SendToProfileDialog.Show(title, others);
                if (choice == null) return;

                SendFrame(live, choice.Value.Profile, choice.Value.Move);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Send to Profile failed: {ex.Message}");
                MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Send to Profile failed: {ex.Message}", "Error");
            }
        }

        /// <summary>Copies (or moves) the live frame into <paramref name="targetProfile"/>.</summary>
        public static void SendFrame(dynamic liveFrame, string targetProfile, bool move)
        {
            string sourceProfile = ProfileManager.CurrentProfileName;
            string sourceId = liveFrame.Id?.ToString() ?? "";
            if (sourceId.Length == 0) throw new InvalidOperationException("Frame has no Id.");
            if (targetProfile.Equals(sourceProfile, StringComparison.OrdinalIgnoreCase)) return;

            string targetDir = ProfileManager.GetProfileDir(targetProfile);
            if (!Directory.Exists(targetDir)) throw new DirectoryNotFoundException($"Profile folder not found: {targetDir}");
            string targetJsonPath = Path.Combine(targetDir, "frames.json");
            JArray targetFrames = ReadFrames(targetJsonPath);

            // Copies get a fresh Id so the two frames never collide in the store; a move keeps it.
            string newId = move ? sourceId : Guid.NewGuid().ToString();
            JObject record = JObject.FromObject(liveFrame);
            record["Id"] = newId;

            string itemsType = record["ItemsType"]?.ToString() ?? "Data";
            if (itemsType == "Data") TransferDataFiles(record, sourceProfile, sourceId, targetDir, targetProfile, newId, move);
            if (itemsType == "Image") TransferDirectory(
                Path.Combine(ProfileManager.CurrentProfileDir, "ImageFrames", sourceId),
                Path.Combine(targetDir, "ImageFrames", newId), move);

            // Placement in the target layout.
            var wa = SystemParameters.WorkArea;
            var rect = (X: ReadDouble(record["X"], 100), Y: ReadDouble(record["Y"], 100),
                        W: ReadDouble(record["Width"], 230), H: ReadDouble(record["Height"], 130));
            var occupied = targetFrames.OfType<JObject>()
                .Select(f => (ReadDouble(f["X"], 100), ReadDouble(f["Y"], 100), ReadDouble(f["Width"], 230), ReadDouble(f["Height"], 130)))
                .ToList();
            var (x, y) = ChoosePosition(rect, occupied, (wa.X, wa.Y, wa.Width, wa.Height));
            record["X"] = x;
            record["Y"] = y;

            targetFrames.Add(record);
            AtomicFile.WriteAllText(targetJsonPath, JsonConvert.SerializeObject(targetFrames, Formatting.Indented));

            if (move) Framemanager.DetachFrame(sourceId);

            string title = record["Title"]?.ToString() ?? "";
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"Send to Profile: {(move ? "moved" : "copied")} frame '{title}' ({sourceId} -> {newId}) to profile '{targetProfile}' at {x},{y}");
            MessageBoxesManager.ShowOKOnlyMessageBoxForm(
                $"Frame '{title}' {(move ? "moved" : "copied")} to profile '{targetProfile}'.", "Send to Profile");
        }

        #region Pure helpers (headless-testable)

        /// <summary>
        /// Keeps the frame's own rect when it lies inside the work area and overlaps none of
        /// the occupied rects; otherwise the first free grid slot; otherwise a small cascade.
        /// </summary>
        public static (double X, double Y) ChoosePosition(
            (double X, double Y, double W, double H) frame,
            IReadOnlyCollection<(double X, double Y, double W, double H)> occupied,
            (double X, double Y, double W, double H) workArea)
        {
            bool insideWorkArea = frame.X >= workArea.X && frame.Y >= workArea.Y
                && frame.X + frame.W <= workArea.X + workArea.W && frame.Y + frame.H <= workArea.Y + workArea.H;
            if (insideWorkArea && !occupied.Any(r => Intersects(frame, r))) return (frame.X, frame.Y);

            var free = AppCategorizer.FindFreePosition(frame.W, frame.H, occupied, workArea,
                gap: FrameGrid.UnitWidth, gridW: FrameGrid.UnitWidth, gridH: FrameGrid.UnitHeight);
            if (free.HasValue) return free.Value;

            int cascade = occupied.Count;
            return (workArea.X + 40 + cascade * 30, workArea.Y + 40 + cascade * 30);
        }

        private static bool Intersects((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b)
            => a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

        /// <summary>Every item array of a frame record: the main list plus each tab's list.</summary>
        public static IEnumerable<JArray> AllItemArrays(JObject record)
        {
            if (record["Items"] is JArray main) yield return main;
            if (record["Tabs"] is JArray tabs)
                foreach (var tab in tabs.OfType<JObject>())
                    if (tab["Items"] is JArray tabItems) yield return tabItems;
        }

        #endregion

        #region File transfer

        /// <summary>
        /// Data frames own two kinds of files: the frame-store folder (absolute item paths)
        /// and legacy profile "Shortcuts" entries (paths relative to the profile folder).
        /// Both are carried over and every item Filename is rewritten to its new home.
        /// </summary>
        private static void TransferDataFiles(JObject record, string sourceProfile, string sourceId,
            string targetDir, string targetProfile, string newId, bool move)
        {
            string sourceStore = FrameStore.BuildFrameFolderPath(FrameStore.RootDir, sourceProfile, sourceId);
            string targetStore = FrameStore.BuildFrameFolderPath(FrameStore.RootDir, targetProfile, newId);
            string sourceProfileDir = ProfileManager.CurrentProfileDir;
            string targetShortcuts = Path.Combine(targetDir, "Shortcuts");

            foreach (var items in AllItemArrays(record))
            {
                foreach (var item in items.OfType<JObject>())
                {
                    string? filename = item["Filename"]?.ToString();
                    if (string.IsNullOrWhiteSpace(filename)) continue;

                    if (FrameFileOperations.IsInsideFolder(sourceStore, filename))
                    {
                        string relative = Path.GetRelativePath(Path.GetFullPath(sourceStore), Path.GetFullPath(filename));
                        item["Filename"] = Path.Combine(targetStore, relative);
                        continue;
                    }

                    // Profile-relative shortcut ("Shortcuts\x.lnk") or an absolute path inside the profile folder.
                    string full;
                    try { full = Path.GetFullPath(Path.Combine(sourceProfileDir, filename)); }
                    catch { continue; }
                    if (!File.Exists(full) || !FrameFileOperations.IsInsideFolder(sourceProfileDir, full)) continue;

                    string dest = FrameStore.UniqueDestinationPath(targetShortcuts, Path.GetFileName(full));
                    Directory.CreateDirectory(targetShortcuts);
                    if (move) File.Move(full, dest); else File.Copy(full, dest);
                    item["Filename"] = Path.Combine("Shortcuts", Path.GetFileName(dest));
                }
            }

            TransferDirectory(sourceStore, targetStore, move);
        }

        /// <summary>Copies (or moves, with copy+delete fallback across volumes) a whole directory; no-op when the source is missing.</summary>
        private static void TransferDirectory(string source, string dest, bool move)
        {
            if (!Directory.Exists(source)) return;
            if (Directory.Exists(dest)) throw new IOException($"Target folder already exists: {dest}");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            if (move)
            {
                try { Directory.Move(source, dest); return; }
                catch (IOException) { /* cross-volume: fall through to copy + delete */ }
            }
            CopyDirectory(source, dest);
            if (move)
            {
                try { Directory.Delete(source, recursive: true); }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling,
                        $"Send to Profile: copied '{source}' but could not remove the original: {ex.Message}");
                }
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        private static JArray ReadFrames(string path)
        {
            if (!File.Exists(path)) return new JArray();
            var token = JToken.Parse(File.ReadAllText(path));
            return token as JArray ?? new JArray(token);
        }

        private static double ReadDouble(JToken? token, double fallback)
        {
            if (token == null) return fallback;
            return double.TryParse(token.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }

        #endregion
    }

    /// <summary>Modal chooser: Copy or Move, and which profile. Returns null on cancel.</summary>
    public class SendToProfileDialog : Window
    {
        private (string Profile, bool Move)? _result;
        private readonly Color _accent;
        private readonly string _frameTitle;
        private readonly List<string> _profiles;

        private RadioButton? _rbMove;
        private ComboBox? _cbProfile;

        public static (string Profile, bool Move)? Show(string frameTitle, List<string> profiles)
        {
            var dlg = new SendToProfileDialog(frameTitle, profiles);
            dlg.ShowDialog();
            return dlg._result;
        }

        private SendToProfileDialog(string frameTitle, List<string> profiles)
        {
            _frameTitle = frameTitle;
            _profiles = profiles;
            _accent = Utility.GetColorFromName(SettingsManager.SelectedColor);

            Title = "Send to Profile";
            Width = 440;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            if (DialogIconCache.AppIcon != null) Icon = DialogIconCache.AppIcon;
            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            BuildContent();

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
            };
            Focusable = true;
            Loaded += (s, e) => Focus();
        }

        private void BuildContent()
        {
            Border mainCard = new Border
            {
                Background = Brushes.White,
                Margin = new Thickness(8, 8, 8, 1),
                Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, BlurRadius = 10, Opacity = 0.1 }
            };

            StackPanel root = new StackPanel();
            root.Children.Add(new Border { Background = new SolidColorBrush(_accent), Height = 8 });

            StackPanel body = new StackPanel { Margin = new Thickness(24, 20, 24, 16) };
            body.Children.Add(new TextBlock
            {
                Text = "Send to Profile",
                FontFamily = new FontFamily("Segoe UI"), FontSize = 18, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)), Margin = new Thickness(0, 0, 0, 6)
            });
            body.Children.Add(new TextBlock
            {
                Text = $"Frame '{_frameTitle}' and the files it holds will be sent to the profile you pick.",
                FontFamily = new FontFamily("Segoe UI"), FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)), Margin = new Thickness(0, 0, 0, 14)
            });

            var rbCopy = new RadioButton { Content = "Copy  (keep it in this profile too)", IsChecked = true, GroupName = "mode", Margin = new Thickness(0, 0, 0, 6), FontFamily = new FontFamily("Segoe UI"), FontSize = 13 };
            _rbMove = new RadioButton { Content = "Move  (remove it from this profile)", GroupName = "mode", Margin = new Thickness(0, 0, 0, 14), FontFamily = new FontFamily("Segoe UI"), FontSize = 13 };
            body.Children.Add(rbCopy);
            body.Children.Add(_rbMove);

            body.Children.Add(new TextBlock { Text = "Target profile", FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)), Margin = new Thickness(0, 0, 0, 4) });
            _cbProfile = new ComboBox { ItemsSource = _profiles, SelectedIndex = 0, Height = 30, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Margin = new Thickness(0, 0, 0, 18) };
            body.Children.Add(_cbProfile);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button btnCancel = MakeButton("Cancel", Color.FromRgb(95, 99, 104), 90);
            btnCancel.Click += (s, e) => Close();
            Button btnSend = MakeButton("Send", _accent, 100);
            btnSend.Click += (s, e) => Accept();
            buttons.Children.Add(btnCancel);
            buttons.Children.Add(btnSend);
            body.Children.Add(buttons);

            root.Children.Add(body);
            mainCard.Child = root;
            Content = mainCard;
        }

        private void Accept()
        {
            if (_cbProfile?.SelectedItem is not string profile) return;
            _result = (profile, _rbMove?.IsChecked == true);
            Close();
        }

        private static Button MakeButton(string text, Color color, double width)
        {
            var btn = new Button
            {
                Content = text, Width = width, Height = 32,
                FontFamily = new FontFamily("Segoe UI"), FontSize = 13, FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(color), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0)
            };
            Color hover = Color.FromRgb((byte)Math.Max(0, color.R - 25), (byte)Math.Max(0, color.G - 25), (byte)Math.Max(0, color.B - 25));
            btn.MouseEnter += (s, e) => btn.Background = new SolidColorBrush(hover);
            btn.MouseLeave += (s, e) => btn.Background = new SolidColorBrush(color);
            return btn;
        }
    }
}

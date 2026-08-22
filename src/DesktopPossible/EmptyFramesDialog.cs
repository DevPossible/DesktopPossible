using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// "Empty Frames to Desktop…": a checkbox tree of every profile (parent, tri-state)
    /// and its frames (children, "Title  (N files)"). Frames with stored files are
    /// checked by default; frames with none are shown disabled. Apply moves every stored
    /// file of each checked frame to the Desktop and removes those items from the frame
    /// (the frames themselves survive). Non-active profiles are edited on disk without
    /// switching profiles. Cancel closes without touching anything.
    /// </summary>
    public class EmptyFramesDialog : Window
    {
        private sealed class FrameRow
        {
            public string ProfileName = "";
            public string FrameId = "";
            public string Title = "";
            public int FileCount;
            public CheckBox Box = null!;
        }

        private sealed class ProfileGroup
        {
            public string ProfileName = "";
            public CheckBox Box = null!;
            public List<FrameRow> Frames = new();
            public bool Syncing;
        }

        private readonly List<ProfileGroup> _groups = new();
        private readonly Color _accent;

        public static void ShowDialogOnUiThread()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                try { new EmptyFramesDialog().ShowDialog(); }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"EmptyFramesDialog failed: {ex.Message}");
                }
            });
        }

        public EmptyFramesDialog()
        {
            _accent = Utility.GetColorFromName(SettingsManager.SelectedColor);

            Title = "Empty Frames to Desktop";
            Width = 520;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            if (DialogIconCache.AppIcon != null) Icon = DialogIconCache.AppIcon;

            BuildContent();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        }

        private void BuildContent()
        {
            Border mainBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(8),
                Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, BlurRadius = 10, Opacity = 0.1 }
            };

            Grid rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            Border header = new Border { Background = new SolidColorBrush(_accent), Padding = new Thickness(15), Height = 50 };
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(new TextBlock { Text = "Empty Frames to Desktop", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            Button closeBtn = new Button { Content = "✕", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, FontSize = 16, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            closeBtn.Click += (s, e) => Close();
            headerGrid.Children.Add(closeBtn); Grid.SetColumn(closeBtn, 1);
            header.Child = headerGrid;
            header.MouseLeftButtonDown += (s, e) => DragMove();
            rootGrid.Children.Add(header);

            // Explanation
            TextBlock intro = new TextBlock
            {
                Text = "Moves the files stored inside the selected frames to your Desktop and removes them from the frames. The frames themselves are kept. Items that link to files elsewhere are left untouched.",
                FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16, 12, 16, 4)
            };
            Grid.SetRow(intro, 1);
            rootGrid.Children.Add(intro);

            // Tree
            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 6) };
            StackPanel list = new StackPanel { Margin = new Thickness(16, 0, 16, 0) };
            scroll.Content = list;
            PopulateTree(list);
            Grid.SetRow(scroll, 2);
            rootGrid.Children.Add(scroll);

            // Footer
            Border footer = new Border
            {
                Padding = new Thickness(16),
                Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button btnCancel = new Button
            {
                Content = "Cancel", Width = 100, Height = 34, Cursor = Cursors.Hand,
                Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)), BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0)
            };
            btnCancel.Click += (s, e) => Close();
            Button btnApply = new Button
            {
                Content = "Apply", Width = 100, Height = 34, Cursor = Cursors.Hand,
                Background = new SolidColorBrush(_accent), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold
            };
            btnApply.Click += (s, e) => Apply();
            buttons.Children.Add(btnCancel);
            buttons.Children.Add(btnApply);
            footer.Child = buttons;
            Grid.SetRow(footer, 3);
            rootGrid.Children.Add(footer);

            mainBorder.Child = rootGrid;
            Content = mainBorder;
        }

        private void PopulateTree(StackPanel list)
        {
            foreach (var profile in ProfileManager.GetProfiles())
            {
                var group = new ProfileGroup { ProfileName = profile.Name };
                bool isActive = string.Equals(profile.Name, ProfileManager.CurrentProfileName, StringComparison.OrdinalIgnoreCase);

                group.Box = new CheckBox
                {
                    Content = isActive ? $"{profile.Name}  (active)" : profile.Name,
                    IsThreeState = true,
                    FontFamily = new FontFamily("Segoe UI"), FontSize = 14, FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 4)
                };
                list.Children.Add(group.Box);

                foreach (var (id, title) in ReadFrames(profile.Name, isActive))
                {
                    int count = FrameFileOperations.EnumerateFrameFiles(profile.Name, id).Count;
                    var row = new FrameRow { ProfileName = profile.Name, FrameId = id, Title = title, FileCount = count };
                    row.Box = new CheckBox
                    {
                        Content = $"{title}  ({count} {(count == 1 ? "file" : "files")})",
                        IsChecked = count > 0,
                        IsEnabled = count > 0,
                        FontFamily = new FontFamily("Segoe UI"), FontSize = 13,
                        Margin = new Thickness(24, 2, 0, 2),
                        Opacity = count > 0 ? 1.0 : 0.5
                    };
                    var g = group;
                    row.Box.Checked += (s, e) => SyncParent(g);
                    row.Box.Unchecked += (s, e) => SyncParent(g);
                    group.Frames.Add(row);
                    list.Children.Add(row.Box);
                }

                if (group.Frames.Count == 0)
                {
                    list.Children.Add(new TextBlock { Text = "No frames", FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(24, 2, 0, 2) });
                    group.Box.IsEnabled = false;
                    group.Box.IsChecked = false;
                }
                else if (group.Frames.All(f => f.FileCount == 0))
                {
                    group.Box.IsEnabled = false;
                    group.Box.IsChecked = false;
                }

                var gg = group;
                group.Box.Click += (s, e) =>
                {
                    // Clicking the parent toggles all enabled children (indeterminate is
                    // only ever derived from the children, never a user target).
                    if (gg.Syncing) return;
                    // WPF cycles true -> null -> false; treat the indeterminate step as "uncheck all".
                    bool target = gg.Box.IsChecked == true;
                    gg.Syncing = true;
                    try
                    {
                        gg.Box.IsChecked = target;
                        foreach (var f in gg.Frames.Where(f => f.Box.IsEnabled)) f.Box.IsChecked = target;
                    }
                    finally { gg.Syncing = false; }
                    SyncParent(gg);
                };

                _groups.Add(group);
                SyncParent(group);
            }
        }

        private static void SyncParent(ProfileGroup group)
        {
            if (group.Syncing) return;
            var enabled = group.Frames.Where(f => f.Box.IsEnabled).ToList();
            if (enabled.Count == 0) return;
            int on = enabled.Count(f => f.Box.IsChecked == true);
            group.Syncing = true;
            try
            {
                group.Box.IsChecked = on == 0 ? false : on == enabled.Count ? true : (bool?)null;
            }
            finally { group.Syncing = false; }
        }

        /// <summary>(Id, Title) of every frame of a profile — from memory for the active profile, from its frames.json otherwise.</summary>
        private static List<(string Id, string Title)> ReadFrames(string profileName, bool isActive)
        {
            var result = new List<(string, string)>();
            try
            {
                if (isActive && FrameDataManager.FrameData != null)
                {
                    foreach (dynamic f in FrameDataManager.FrameData)
                    {
                        string id = f.Id?.ToString() ?? "";
                        if (id.Length == 0) continue;
                        result.Add((id, f.Title?.ToString() ?? "(untitled)"));
                    }
                    return result;
                }

                string path = Path.Combine(ProfileManager.GetProfileDir(profileName), "frames.json");
                if (!File.Exists(path)) return result;
                var token = JToken.Parse(File.ReadAllText(path));
                var arr = token as JArray ?? new JArray(token);
                foreach (var f in arr.OfType<JObject>())
                {
                    string id = f["Id"]?.ToString() ?? "";
                    if (id.Length == 0) continue;
                    result.Add((id, f["Title"]?.ToString() ?? "(untitled)"));
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"EmptyFramesDialog: could not read frames of '{profileName}': {ex.Message}");
            }
            return result;
        }

        private void Apply()
        {
            var selected = _groups.SelectMany(g => g.Frames).Where(f => f.Box.IsEnabled && f.Box.IsChecked == true).ToList();
            if (selected.Count == 0) { Close(); return; }

            int movedFiles = 0, framesTouched = 0;
            foreach (var row in selected)
            {
                try
                {
                    int moved = FrameFileOperations.MoveFrameFilesToDesktop(row.ProfileName, row.FrameId);
                    FrameFileOperations.ClearFrameItems(row.ProfileName, row.FrameId);
                    movedFiles += moved;
                    framesTouched++;
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.IconHandling,
                        $"EmptyFrames: '{row.Title}' ({row.ProfileName}) moved {moved} file(s) to the desktop");
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.IconHandling,
                        $"EmptyFrames: failed for '{row.Title}' ({row.ProfileName}): {ex.Message}");
                }
                // Keep the UI responsive between frames (file moves are quick but not free).
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            }

            Close();
            SmartToast.Show("Frames emptied", $"Moved {movedFiles} files from {framesTouched} frames to the desktop");
        }
    }
}

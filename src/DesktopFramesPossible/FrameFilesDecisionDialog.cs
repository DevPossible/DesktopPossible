using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Desktop_Frames
{
    /// <summary>Outcome of <see cref="FrameFilesDecisionDialog"/>.</summary>
    public enum FrameFilesDecision
    {
        /// <summary>Abort everything — the caller must not delete the frame/profile.</summary>
        Cancel,
        /// <summary>Move the stored files to the user's Desktop first.</summary>
        MoveToDesktop,
        /// <summary>Permanently delete the stored files (typed confirmation given).</summary>
        DeleteFiles
    }

    /// <summary>
    /// "N files are stored in &lt;scope&gt;. What should happen to them?" — shown before a
    /// frame or profile that owns stored files is deleted. Two primary choices:
    /// Move files to Desktop (safe, accent) and Delete files (danger, red). Picking
    /// Delete reveals a confirmation TextBox; the Delete button only enables while the
    /// text is exactly "DELETE MY FILES" (case-sensitive). Cancel / Escape abort.
    /// Code-built WPF in the house MessageBoxesManager style.
    /// </summary>
    public class FrameFilesDecisionDialog : Window
    {
        /// <summary>The exact phrase that unlocks permanent deletion.</summary>
        public const string ConfirmationPhrase = "DELETE MY FILES";

        private FrameFilesDecision _result = FrameFilesDecision.Cancel;
        private readonly Color _accent;
        private readonly string _scope;
        private readonly int _fileCount;

        private StackPanel? _confirmPanel;
        private TextBox? _confirmBox;
        private Button? _btnDelete;

        /// <summary>
        /// Shows the dialog modally. <paramref name="scopeDescription"/> is inserted into the
        /// message, e.g. "frame 'Games'" or "profile 'Work'".
        /// </summary>
        public static FrameFilesDecision Show(Window? owner, string scopeDescription, int fileCount)
        {
            try
            {
                var dlg = new FrameFilesDecisionDialog(scopeDescription, fileCount);
                if (owner != null && owner.IsLoaded)
                {
                    dlg.Owner = owner;
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                dlg.ShowDialog();
                return dlg._result;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error showing FrameFilesDecisionDialog: {ex.Message}");
                return FrameFilesDecision.Cancel; // never proceed destructively on a UI failure
            }
        }

        private FrameFilesDecisionDialog(string scopeDescription, int fileCount)
        {
            _scope = scopeDescription;
            _fileCount = fileCount;
            _accent = Utility.GetColorFromName(SettingsManager.SelectedColor);

            Title = "Stored Files";
            Width = 520;
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
                if (e.Key == Key.Escape) { _result = FrameFilesDecision.Cancel; Close(); e.Handled = true; }
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

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border accentHeader = new Border { Background = new SolidColorBrush(_accent), Height = 8 };
            Grid.SetRow(accentHeader, 0);

            Grid contentGrid = new Grid { Margin = new Thickness(24, 24, 24, 16) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(contentGrid, 1);

            // Icon
            Border iconContainer = new Border
            {
                Width = 48, Height = 48,
                Background = new SolidColorBrush(Color.FromArgb(15, _accent.R, _accent.G, _accent.B)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock
                {
                    Text = "⚠", FontFamily = new FontFamily("Segoe UI"), FontSize = 32, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(_accent),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(iconContainer, 0); Grid.SetRow(iconContainer, 0);

            // Title + message + (hidden) confirmation
            StackPanel messageArea = new StackPanel { Margin = new Thickness(8, 4, 0, 0) };
            messageArea.Children.Add(new TextBlock
            {
                Text = "Stored Files",
                FontFamily = new FontFamily("Segoe UI"), FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            string fileWord = _fileCount == 1 ? "file is" : "files are";
            messageArea.Children.Add(new TextBlock
            {
                Text = $"{_fileCount} {fileWord} stored in {_scope}. What should happen to them?",
                FontFamily = new FontFamily("Segoe UI"), FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 10, 12)
            });
            messageArea.Children.Add(new TextBlock
            {
                Text = "Move files to Desktop keeps every file. Delete files removes them permanently — this cannot be undone.",
                FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(128, 134, 139)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 10, 8)
            });

            _confirmPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 10, 8) };
            _confirmPanel.Children.Add(new TextBlock
            {
                Text = $"Type {ConfirmationPhrase} to confirm",
                FontFamily = new FontFamily("Segoe UI"), FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(234, 67, 53)),
                Margin = new Thickness(0, 0, 0, 6)
            });
            _confirmBox = new TextBox
            {
                Height = 32,
                FontFamily = new FontFamily("Segoe UI"), FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(1)
            };
            _confirmBox.TextChanged += (s, e) => UpdateDeleteEnabled();
            _confirmBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && _btnDelete != null && _btnDelete.IsEnabled) { _btnDelete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            };
            _confirmPanel.Children.Add(_confirmBox);
            messageArea.Children.Add(_confirmPanel);

            Grid.SetColumn(messageArea, 1); Grid.SetRow(messageArea, 0);

            // Buttons: Cancel | Move files to Desktop | Delete files
            StackPanel buttonArea = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 8, 0, 0)
            };

            Button btnCancel = MakeButton("Cancel", Color.FromRgb(95, 99, 104), 90);
            btnCancel.Click += (s, e) => { _result = FrameFilesDecision.Cancel; Close(); };

            Button btnMove = MakeButton("Move files to Desktop", _accent, 170);
            btnMove.Click += (s, e) => { _result = FrameFilesDecision.MoveToDesktop; Close(); };

            _btnDelete = MakeButton("Delete files", Color.FromRgb(234, 67, 53), 120);
            _btnDelete.Click += (s, e) =>
            {
                if (_confirmPanel!.Visibility != Visibility.Visible)
                {
                    // First click only reveals the typed confirmation; the button stays
                    // disabled until the phrase matches exactly.
                    _confirmPanel.Visibility = Visibility.Visible;
                    UpdateDeleteEnabled();
                    _confirmBox!.Focus();
                    return;
                }
                if (!IsConfirmed()) return; // belt and braces — the button is disabled anyway
                _result = FrameFilesDecision.DeleteFiles;
                Close();
            };

            buttonArea.Children.Add(btnCancel);
            buttonArea.Children.Add(btnMove);
            buttonArea.Children.Add(_btnDelete);
            Grid.SetColumn(buttonArea, 1); Grid.SetRow(buttonArea, 1);

            contentGrid.Children.Add(iconContainer);
            contentGrid.Children.Add(messageArea);
            contentGrid.Children.Add(buttonArea);
            mainGrid.Children.Add(accentHeader);
            mainGrid.Children.Add(contentGrid);
            mainCard.Child = mainGrid;
            Content = mainCard;
        }

        private bool IsConfirmed() => string.Equals(_confirmBox?.Text, ConfirmationPhrase, StringComparison.Ordinal);

        private void UpdateDeleteEnabled()
        {
            if (_btnDelete == null || _confirmPanel == null) return;
            bool revealed = _confirmPanel.Visibility == Visibility.Visible;
            _btnDelete.IsEnabled = !revealed || IsConfirmed();
            _btnDelete.Opacity = _btnDelete.IsEnabled ? 1.0 : 0.45;
        }

        private static Button MakeButton(string text, Color color, double width)
        {
            var btn = new Button
            {
                Content = text,
                Width = width,
                Height = 32,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(color),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0)
            };
            Color hover = Color.FromRgb((byte)Math.Max(0, color.R - 25), (byte)Math.Max(0, color.G - 25), (byte)Math.Max(0, color.B - 25));
            btn.MouseEnter += (s, e) => btn.Background = new SolidColorBrush(hover);
            btn.MouseLeave += (s, e) => btn.Background = new SolidColorBrush(color);
            return btn;
        }
    }
}

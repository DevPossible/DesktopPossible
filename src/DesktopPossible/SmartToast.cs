using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Desktop_Frames
{
    public class SmartToast : Window
    {
        private readonly DispatcherTimer _timer;
        private static SmartToast? _editModeHint;

        public static void Show(string title, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                new SmartToast(title, message, TimeSpan.FromSeconds(3.5), showCloseButton: false, positionAtCursor: false).Show();
            });
        }

        /// <summary>
        /// Hint shown when a frame drag is blocked because Edit Frames Mode is off. Appears
        /// beside the mouse pointer, has a close button, fades away after 10 seconds, and
        /// never shows more than one instance at a time.
        /// </summary>
        public static void ShowEditModeHint()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_editModeHint != null) return; // one at a time
                var toast = new SmartToast(
                    "Frames are locked",
                    "To move or resize frames, enable 'Edit Frames Mode' from the tray icon menu.",
                    TimeSpan.FromSeconds(10), showCloseButton: true, positionAtCursor: true);
                _editModeHint = toast;
                toast.Closed += (s, e) => { if (ReferenceEquals(_editModeHint, toast)) _editModeHint = null; };
                toast.Show();
            });
        }

        private SmartToast(string title, string message, TimeSpan lifetime, bool showCloseButton, bool positionAtCursor)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false; // a notification must never steal focus
            Width = 320;

            if (positionAtCursor)
            {
                SizeToContent = SizeToContent.Height;
                Loaded += (s, e) => PositionAtCursor();
            }
            else
            {
                // Bottom right of primary screen (original behavior)
                Height = 80;
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Right - Width - 20;
                Top = workArea.Bottom - Height - 20;
            }

            Border mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(41, 74, 122)), // Smart Desktop Navy Theme
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(15, 10, 15, 10),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 10, Opacity = 0.3, ShadowDepth = 2 }
            };

            StackPanel sp = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Foreground = Brushes.White, FontSize = 14, Margin = new Thickness(0, 0, 0, 5) });
            var body = new TextBlock { Text = message, Foreground = new SolidColorBrush(Color.FromRgb(220, 230, 240)), FontSize = 12 };
            // The closable hint wraps its full message; plain toasts keep the original single-line trim.
            if (showCloseButton) body.TextWrapping = TextWrapping.Wrap;
            else body.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(body);

            if (showCloseButton)
            {
                sp.Margin = new Thickness(0, 0, 18, 0); // keep text clear of the close glyph

                var closeBrush = new SolidColorBrush(Color.FromRgb(200, 214, 229));
                TextBlock close = new TextBlock
                {
                    Text = "✕",
                    Foreground = closeBrush,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Padding = new Thickness(8, 0, 0, 8), // generous hit area
                    Cursor = Cursors.Hand
                };
                close.MouseEnter += (s, e) => close.Foreground = Brushes.White;
                close.MouseLeave += (s, e) => close.Foreground = closeBrush;
                close.MouseLeftButtonDown += (s, e) => { e.Handled = true; CloseNow(); };

                Grid grid = new Grid();
                grid.Children.Add(sp);
                grid.Children.Add(close);
                mainBorder.Child = grid;
            }
            else
            {
                mainBorder.Child = sp;
            }

            Content = mainBorder;

            _timer = new DispatcherTimer { Interval = lifetime };
            _timer.Tick += (s, e) =>
            {
                _timer.Stop();
                var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500));
                anim.Completed += (s2, e2) => Close();
                BeginAnimation(OpacityProperty, anim);
            };
            _timer.Start();
        }

        private void CloseNow()
        {
            _timer.Stop();
            BeginAnimation(OpacityProperty, null);
            Close();
        }

        /// <summary>
        /// Places the toast just below-right of the mouse pointer, clamped to that monitor's
        /// work area. Cursor and screen coordinates arrive in device pixels; the window's
        /// TransformFromDevice converts them to WPF units.
        /// </summary>
        private void PositionAtCursor()
        {
            try
            {
                var cursor = System.Windows.Forms.Cursor.Position;
                var area = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;

                var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
                Point cursorDip = transform?.Transform(new Point(cursor.X, cursor.Y)) ?? new Point(cursor.X, cursor.Y);
                Point areaTopLeft = transform?.Transform(new Point(area.Left, area.Top)) ?? new Point(area.Left, area.Top);
                Point areaBottomRight = transform?.Transform(new Point(area.Right, area.Bottom)) ?? new Point(area.Right, area.Bottom);

                double left = Math.Min(cursorDip.X + 16, areaBottomRight.X - ActualWidth - 8);
                double top = Math.Min(cursorDip.Y + 16, areaBottomRight.Y - ActualHeight - 8);
                Left = Math.Max(areaTopLeft.X + 8, left);
                Top = Math.Max(areaTopLeft.Y + 8, top);
            }
            catch { /* fall back to wherever WPF placed it */ }
        }
    }
}

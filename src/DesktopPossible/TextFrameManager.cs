using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Desktop_Frames
{
    /// <summary>
    /// Text frames (ItemsType == "Text"): bginfo-style transparent text painted over the
    /// wallpaper from a token template (see <see cref="TextTemplate"/> /
    /// <see cref="SystemInfoTokens"/>), re-expanded on a per-frame timer.
    ///
    /// They are wallpaper, not desktop: always at the very bottom of the Z-order (pushed
    /// back down whenever another frame is created), and outside Frame Edit Mode they
    /// are click-through with no chrome at all. Edit Mode (tray toggle) brings back the
    /// title bar/border so they can be moved, resized and right-clicked like any frame.
    /// </summary>
    public static class TextFramemanager
    {
        public const string Type = "Text";

        // ---- Per-frame property keys (persisted in frames.json) ----
        public const string KeyTemplate = "TextTemplate";
        public const string KeyFont = "TextFont";
        public const string KeySize = "TextSize";
        public const string KeyBold = "TextBold";
        public const string KeyItalic = "TextItalic";
        public const string KeyColor = "TextColor";
        public const string KeyAlign = "TextAlign";
        public const string KeyDrawMode = "TextDrawMode";
        public const string KeyRefresh = "TextRefreshSeconds";
        public const string KeyOpacity = "TextOpacity";

        public static readonly string[] DrawModes = { "Normal", "Shadow", "Glow", "Outline", "Emboss", "Engrave" };
        public static readonly string[] Alignments = { "Left", "Center", "Right" };

        public const string DefaultTemplate = "{ComputerName}\n{UserName} @ {Domain}\n{OS}\n{IP}";

        private static readonly Dictionary<string, Grid> _hosts = new();
        private static readonly Dictionary<string, DispatcherTimer> _timers = new();
        private static readonly Dictionary<string, TextTemplate.TokenResolver> _tokens = SystemInfoTokens.Build();
        private static bool _externalIpHooked;

        #region Property access

        public static string Template(dynamic frame) => Prop(frame, KeyTemplate, DefaultTemplate);
        public static string Font(dynamic frame) => Prop(frame, KeyFont, "Segoe UI");
        public static double Size(dynamic frame) => ParseDouble(Prop(frame, KeySize, "14"), 14);
        public static bool Bold(dynamic frame) => Prop(frame, KeyBold, "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        public static bool Italic(dynamic frame) => Prop(frame, KeyItalic, "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        public static string ColorHex(dynamic frame) => Prop(frame, KeyColor, "#FFFFFFFF");
        public static string Align(dynamic frame) => Prop(frame, KeyAlign, "Left");
        public static string DrawMode(dynamic frame) => Prop(frame, KeyDrawMode, "Shadow");
        public static int RefreshSeconds(dynamic frame) => Math.Max(0, (int)ParseDouble(Prop(frame, KeyRefresh, "60"), 60));
        public static double Opacity(dynamic frame) => Math.Clamp(ParseDouble(Prop(frame, KeyOpacity, "100"), 100) / 100.0, 0.05, 1.0);

        private static string Prop(dynamic frame, string key, string fallback)
        {
            try
            {
                object? value = frame is IDictionary<string, object> d ? (d.TryGetValue(key, out var v) ? v : null)
                              : frame is Newtonsoft.Json.Linq.JObject jo ? jo[key]
                              : null;
                string? s = value?.ToString();
                return string.IsNullOrEmpty(s) ? fallback : s;
            }
            catch { return fallback; }
        }

        /// <summary>Writes one property on the LIVE frame record and persists.</summary>
        public static void SetProp(string frameId, string key, string value)
        {
            dynamic? live = LiveFrame(frameId);
            if (live == null) return;
            if (live is IDictionary<string, object> d) d[key] = value;
            else if (live is Newtonsoft.Json.Linq.JObject jo) jo[key] = value;
            FrameDataManager.SaveFrameData();
        }

        private static double ParseDouble(string s, double fallback) =>
            double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;

        private static dynamic? LiveFrame(string? frameId) =>
            string.IsNullOrEmpty(frameId) ? null : FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);

        public static bool IsTextFrame(dynamic? frame)
        {
            try { return frame?.ItemsType?.ToString() == Type; } catch { return false; }
        }

        #endregion

        #region Content

        /// <summary>Builds the text host into the frame's content area and starts its refresh timer.</summary>
        public static FrameworkElement CreateTextContent(dynamic frame, DockPanel dp)
        {
            string frameId = frame.Id?.ToString() ?? "";
            var host = new Grid { Margin = new Thickness(6), Background = Brushes.Transparent };
            dp.Children.Add(host);

            // No title bar: in Edit Mode the whole surface drags the frame (respects the position lock).
            host.MouseLeftButtonDown += (s, e) =>
            {
                if (!SettingsManager.FrameEditMode || e.ChangedButton != MouseButton.Left) return;
                if (Window.GetWindow(host) is not NonActivatingWindow win) return;
                dynamic? live = LiveFrame(win.Tag?.ToString());
                bool locked = false;
                try { locked = live?.IsLocked?.ToString().ToLower() == "true"; } catch { }
                if (locked) return;
                win.DragMove();
                Framemanager.SnapWindowToGrid(win);
                e.Handled = true;
            };
            if (frameId.Length > 0) _hosts[frameId] = host;

            if (!_externalIpHooked)
            {
                _externalIpHooked = true;
                SystemInfoTokens.ExternalIpChanged += () =>
                    Application.Current?.Dispatcher.BeginInvoke(new Action(RefreshAll));
            }

            Render(frame, host);
            RestartTimer(frameId, RefreshSeconds(frame));
            return host;
        }

        /// <summary>Re-expands and repaints one frame (and re-arms its timer).</summary>
        public static void Refresh(dynamic frame)
        {
            string? rawId = frame?.Id?.ToString();
            string frameId = rawId ?? "";
            if (!_hosts.TryGetValue(frameId, out var host)) return;
            dynamic? live = frame;
            dynamic? found = LiveFrame(frameId);
            if (found is not null) live = found;
            if (live is null) return;
            Render(live, host);
            RestartTimer(frameId, RefreshSeconds(live));
        }

        public static void RefreshAll()
        {
            foreach (string id in _hosts.Keys.ToList())
            {
                if (!_hosts.TryGetValue(id, out var host)) continue;
                dynamic? live = LiveFrame(id);
                if (live != null) Render(live, host);
            }
        }

        /// <summary>Forget every host and stop every timer (frame reload teardown).</summary>
        public static void ResetAll()
        {
            foreach (var t in _timers.Values) { try { t.Stop(); } catch { } }
            _timers.Clear();
            _hosts.Clear();
        }

        private static void RestartTimer(string frameId, int seconds)
        {
            if (frameId.Length == 0) return;
            if (_timers.TryGetValue(frameId, out var old)) { old.Stop(); _timers.Remove(frameId); }
            if (seconds <= 0) return;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (s, e) =>
            {
                if (!_hosts.TryGetValue(frameId, out var host)) { timer.Stop(); _timers.Remove(frameId); return; }
                dynamic? live = LiveFrame(frameId);
                if (live == null) { timer.Stop(); _timers.Remove(frameId); return; }
                Render(live, host);
            };
            timer.Start();
            _timers[frameId] = timer;
        }

        private static void Render(dynamic frame, Grid host)
        {
            try
            {
                string text = TextTemplate.Expand(Template(frame), _tokens);
                host.Children.Clear();
                host.Children.Add(BuildText(text, Font(frame), Size(frame), Bold(frame), Italic(frame),
                    ParseColor(ColorHex(frame)), Align(frame), DrawMode(frame)));
                host.Opacity = Opacity(frame);
                AutoSize(frame, host);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Text frame render failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Text frames are never resized by hand: the window always fits the rendered text
        /// (plus host margin and outline), re-measured after every refresh. Right- and
        /// centre-aligned frames keep their right/centre edge anchored so a top-right frame
        /// grows leftward. Persists the new size when it changed.
        /// </summary>
        private static void AutoSize(dynamic frame, Grid host)
        {
            if (Window.GetWindow(host) is not NonActivatingWindow win) return;
            if (host.Children.Count == 0) return;

            var content = host.Children[0];
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = content.DesiredSize;
            double pad = host.Margin.Left + host.Margin.Right + 2; // outline allowance
            double width = Math.Max(40, Math.Ceiling(desired.Width + pad));
            double height = Math.Max(20, Math.Ceiling(desired.Height + host.Margin.Top + host.Margin.Bottom + 2));

            if (Math.Abs(win.Width - width) < 0.5 && Math.Abs(win.Height - height) < 0.5) return;

            string align = Align(frame);
            double delta = width - win.Width;
            if (align == "Right") win.Left -= delta;
            else if (align == "Center") win.Left -= delta / 2;
            win.Width = width;
            win.Height = height;

            string frameId = win.Tag?.ToString() ?? "";
            dynamic? live = LiveFrame(frameId);
            if (live == null) return;
            SetValue(live, "X", win.Left);
            SetValue(live, "Width", width);
            SetValue(live, "Height", height);
            SetValue(live, "UnrolledHeight", height);
            FrameDataManager.SaveFrameData();
        }

        private static void SetValue(dynamic frame, string key, double value)
        {
            if (frame is IDictionary<string, object> d) d[key] = value;
            else if (frame is Newtonsoft.Json.Linq.JObject jo) jo[key] = value;
        }

        /// <summary>
        /// The text with its draw mode: stacked TextBlocks offset by a pixel or two give
        /// outline / emboss / engrave; shadow and glow use a DropShadowEffect.
        /// </summary>
        public static UIElement BuildText(string text, string fontFamily, double size, bool bold, bool italic,
            Color color, string align, string drawMode)
        {
            var layers = new Grid { IsHitTestVisible = false };
            Color contrast = Luminance(color) > 0.5 ? Color.FromArgb(200, 0, 0, 0) : Color.FromArgb(200, 255, 255, 255);

            TextBlock Make(Color c, double dx, double dy)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontFamily = new FontFamily(fontFamily),
                    FontSize = Math.Max(6, size),
                    FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
                    Foreground = new SolidColorBrush(c),
                    TextWrapping = TextWrapping.NoWrap,
                    TextAlignment = align switch { "Center" => TextAlignment.Center, "Right" => TextAlignment.Right, _ => TextAlignment.Left },
                    HorizontalAlignment = align switch { "Center" => HorizontalAlignment.Center, "Right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Left },
                    VerticalAlignment = VerticalAlignment.Top,
                    RenderTransform = new TranslateTransform(dx, dy)
                };
                TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Display);
                return tb;
            }

            var main = Make(color, 0, 0);
            switch (drawMode)
            {
                case "Shadow":
                    main.Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 2, BlurRadius = 4, Opacity = 0.85, Direction = 315 };
                    break;
                case "Glow":
                    main.Effect = new DropShadowEffect { Color = contrast, ShadowDepth = 0, BlurRadius = 14, Opacity = 1 };
                    break;
                case "Outline":
                    foreach (var (dx, dy) in new[] { (-1.0, -1.0), (0.0, -1.0), (1.0, -1.0), (-1.0, 0.0), (1.0, 0.0), (-1.0, 1.0), (0.0, 1.0), (1.0, 1.0) })
                        layers.Children.Add(Make(contrast, dx * 1.5, dy * 1.5));
                    break;
                case "Emboss":
                    layers.Children.Add(Make(Color.FromArgb(160, 255, 255, 255), -1, -1));
                    layers.Children.Add(Make(Color.FromArgb(160, 0, 0, 0), 1, 1));
                    break;
                case "Engrave":
                    layers.Children.Add(Make(Color.FromArgb(160, 0, 0, 0), -1, -1));
                    layers.Children.Add(Make(Color.FromArgb(160, 255, 255, 255), 1, 1));
                    break;
            }
            layers.Children.Add(main);
            return layers;
        }

        private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        public static Color ParseColor(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.White; }
        }

        #endregion

        #region Chrome, click-through and Z-order

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        /// <summary>
        /// Edit Mode on: normal chrome (title bar, border, background) and a solid window so it
        /// can be dragged/resized/right-clicked. Off: chrome hidden, transparent, click-through.
        /// </summary>
        public static void ApplyEditMode(NonActivatingWindow win, dynamic frame, bool editMode)
        {
            if (win == null) return;
            if (!IsTextFrame(frame)) return;
            try
            {
                string id = win.Tag?.ToString() ?? "";
                var border = win.Content as Border;
                var dock = border?.Child as DockPanel;
                var titleGrid = dock?.Children.OfType<Grid>().FirstOrDefault(g => DockPanel.GetDock(g) == Dock.Top);

                // Never a title bar or a real border. Edit Mode shows a faint outline so the
                // frame can be found, dragged (by its surface) and resized (grip).
                if (border != null)
                {
                    border.Background = editMode ? new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)) : Brushes.Transparent;
                    border.BorderBrush = editMode ? new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)) : Brushes.Transparent;
                    border.BorderThickness = new Thickness(editMode ? 1 : 0);
                }
                if (titleGrid != null) titleGrid.Visibility = Visibility.Collapsed;
                win.ResizeMode = ResizeMode.NoResize; // sized to the text, never by hand

                var hwnd = new WindowInteropHelper(win).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                    ex = editMode ? ex & ~WS_EX_TRANSPARENT : ex | WS_EX_TRANSPARENT | WS_EX_LAYERED;
                    SetWindowLong(hwnd, GWL_EXSTYLE, ex);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"Text frame edit-mode apply failed: {ex.Message}");
            }
        }


        #endregion

        #region Creation / seeding

        /// <summary>Creates a Text frame record with defaults and shows it. Returns the frame.</summary>
        public static dynamic CreateNew(string title, double x, double y, double width = 360, double height = 160)
        {
            dynamic frame = FrameDataManager.CreateNewFrame(title, Type, x, y);
            var d = (IDictionary<string, object>)frame;
            d["Title"] = title;
            d["Width"] = width;
            d["Height"] = height;
            d["UnrolledHeight"] = height;
            d[KeyTemplate] = DefaultTemplate;
            d[KeyFont] = "Segoe UI";
            d[KeySize] = "14";
            d[KeyBold] = "false";
            d[KeyItalic] = "false";
            d[KeyColor] = "#FFFFFFFF";
            d[KeyAlign] = "Left";
            d[KeyDrawMode] = "Shadow";
            d[KeyRefresh] = "60";
            d[KeyOpacity] = "100";

            // CreateFrame reads optional keys (IsLocked, AlwaysOnTop, ...) that the defaults don't set.
            // An ExpandoObject THROWS on a missing member; a JObject (what every frame loaded from
            // disk is) yields null — so hand the rest of the app the JObject form.
            var record = Newtonsoft.Json.Linq.JObject.FromObject(frame);
            int index = FrameDataManager.FrameData.IndexOf(frame);
            if (index >= 0) FrameDataManager.FrameData[index] = record; else FrameDataManager.FrameData.Add(record);
            FrameDataManager.SaveFrameData();
            return record;
        }

        /// <summary>
        /// One-time starter for the Default profile: a system-information frame in the top-right
        /// corner of the primary monitor, refreshing every 3 minutes. Adds data only — the
        /// caller's normal frame loading shows it. Guarded by an app-level flag so it never
        /// returns once created (or deleted).
        /// </summary>
        public static void SeedSystemInfoFrameIfNeeded()
        {
            try
            {
                if (SettingsManager.SystemInfoFrameCreated) return;
                if (!string.Equals(ProfileManager.CurrentProfileName, "Default", StringComparison.OrdinalIgnoreCase)) return;

                var wa = SystemParameters.WorkArea;
                const double width = 440, height = 300;
                var record = (Newtonsoft.Json.Linq.JObject)CreateNew("System Info", wa.Right - width - 24, wa.Top + 24, width, height);
                record[KeyTemplate] =
                    "{ComputerName}\n" +
                    "CPU: {CPU}  ({Cores} threads, {CPUUsage} busy)\n" +
                    "RAM: {RAMUsed} of {RAM} in use ({RAMUsage})\n" +
                    "Internal IP: {IP}\n" +
                    "External IP: {ExternalIP}\n" +
                    "\n" +
                    "{Disks}";
                record[KeyAlign] = "Right";
                record[KeySize] = "13";
                record[KeyRefresh] = "180";
                FrameDataManager.SaveFrameData();

                SettingsManager.SystemInfoFrameCreated = true;
                SettingsManager.SaveSettings();
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation, "Seeded the starter System Info text frame on the Default profile.");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation, $"Seeding the System Info text frame failed: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>Editor for one Text frame; every change is applied and persisted immediately.</summary>
    public class TextFrameEditorDialog : Window
    {
        private readonly string _frameId;
        private readonly Color _accent;
        private bool _loading = true;

        public static void Show(string frameId)
        {
            try
            {
                var dlg = new TextFrameEditorDialog(frameId);
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Text frame editor failed: {ex.Message}");
            }
        }

        private TextFrameEditorDialog(string frameId)
        {
            _frameId = frameId;
            _accent = Utility.GetColorFromName(SettingsManager.SelectedColor);

            Title = "Text Frame";
            Width = 640;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            if (DialogIconCache.AppIcon != null) Icon = DialogIconCache.AppIcon;

            BuildContent();
            _loading = false;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        private dynamic? Live() => FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == _frameId);

        private void Apply(string key, string value)
        {
            if (_loading) return;
            TextFramemanager.SetProp(_frameId, key, value);
            dynamic? live = Live();
            if (live != null) TextFramemanager.Refresh(live);
        }

        private void BuildContent()
        {
            dynamic? frame = Live();
            if (frame == null) { Close(); return; }

            var card = new Border
            {
                Background = Brushes.White,
                Margin = new Thickness(8, 8, 8, 1),
                Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, BlurRadius = 10, Opacity = 0.1 }
            };
            var root = new StackPanel();
            var header = new Border { Background = new SolidColorBrush(_accent), Height = 36 };
            var headerGrid = new Grid();
            headerGrid.Children.Add(new TextBlock
            {
                Text = $"Text Frame — {frame.Title}", Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"),
                FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0)
            });
            var close = new Button { Content = "✕", Width = 32, Height = 32, Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.Hand };
            close.Click += (s, e) => Close();
            headerGrid.Children.Add(close);
            header.Child = headerGrid;
            header.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            root.Children.Add(header);

            var body = new Grid { Margin = new Thickness(20, 16, 20, 16) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });

            // ---- Left: template + reference ----
            var left = new StackPanel();
            left.Children.Add(Label("Template"));
            var template = new TextBox
            {
                Text = TextFramemanager.Template(frame), AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
                Height = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(6)
            };
            var templateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            templateTimer.Tick += (s, e) => { templateTimer.Stop(); Apply(TextFramemanager.KeyTemplate, template.Text); };
            template.TextChanged += (s, e) => { templateTimer.Stop(); templateTimer.Start(); };
            left.Children.Add(template);

            left.Children.Add(Label("Tokens (click to insert)"));
            var tokens = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var (token, description) in SystemInfoTokens.Reference)
            {
                var chip = new Button
                {
                    Content = token, ToolTip = description, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 2, 6, 2),
                    FontFamily = new FontFamily("Consolas"), FontSize = 11, Background = new SolidColorBrush(Color.FromRgb(241, 243, 244)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand
                };
                string insert = token;
                chip.Click += (s, e) =>
                {
                    int at = template.CaretIndex;
                    template.Text = template.Text.Insert(at, insert);
                    template.CaretIndex = at + insert.Length;
                    template.Focus();
                };
                tokens.Children.Add(chip);
            }
            left.Children.Add(tokens);
            Grid.SetColumn(left, 0);
            body.Children.Add(left);

            // ---- Right: appearance ----
            var right = new StackPanel();

            right.Children.Add(Label("Font"));
            var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n).ToList();
            var font = new ComboBox { ItemsSource = fonts, IsEditable = true, Text = TextFramemanager.Font(frame), Height = 28 };
            font.SelectionChanged += (s, e) => { if (font.SelectedItem is string f) Apply(TextFramemanager.KeyFont, f); };
            font.LostFocus += (s, e) => { if (!string.IsNullOrWhiteSpace(font.Text)) Apply(TextFramemanager.KeyFont, font.Text); };
            right.Children.Add(font);

            var sizeRow = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            sizeRow.ColumnDefinitions.Add(new ColumnDefinition());
            sizeRow.ColumnDefinitions.Add(new ColumnDefinition());
            var sizeCol = new StackPanel();
            sizeCol.Children.Add(Label("Size"));
            var size = new Slider { Minimum = 8, Maximum = 96, Value = TextFramemanager.Size(frame), TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 8, 0) };
            size.ValueChanged += (s, e) => Apply(TextFramemanager.KeySize, ((int)e.NewValue).ToString());
            sizeCol.Children.Add(size);
            Grid.SetColumn(sizeCol, 0); sizeRow.Children.Add(sizeCol);
            var styleCol = new StackPanel { Margin = new Thickness(8, 18, 0, 0) };
            var bold = new CheckBox { Content = "Bold", IsChecked = TextFramemanager.Bold(frame) };
            bold.Click += (s, e) => Apply(TextFramemanager.KeyBold, bold.IsChecked == true ? "true" : "false");
            var italic = new CheckBox { Content = "Italic", IsChecked = TextFramemanager.Italic(frame), Margin = new Thickness(0, 4, 0, 0) };
            italic.Click += (s, e) => Apply(TextFramemanager.KeyItalic, italic.IsChecked == true ? "true" : "false");
            styleCol.Children.Add(bold); styleCol.Children.Add(italic);
            Grid.SetColumn(styleCol, 1); sizeRow.Children.Add(styleCol);
            right.Children.Add(sizeRow);

            right.Children.Add(Label("Colour (hex)"));
            var colorRow = new DockPanel();
            var swatch = new Border { Width = 26, Height = 26, Margin = new Thickness(6, 0, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(TextFramemanager.ParseColor(TextFramemanager.ColorHex(frame))) };
            DockPanel.SetDock(swatch, Dock.Right);
            var color = new TextBox { Text = TextFramemanager.ColorHex(frame), Height = 26, VerticalContentAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
            color.TextChanged += (s, e) =>
            {
                try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color.Text)); }
                catch { return; }
                Apply(TextFramemanager.KeyColor, color.Text);
            };
            colorRow.Children.Add(swatch); colorRow.Children.Add(color);
            right.Children.Add(colorRow);

            var presets = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (string hex in new[] { "#FFFFFFFF", "#FF000000", "#FFFFD54F", "#FF4FC3F7", "#FF81C784", "#FFFF8A65", "#FFB0BEC5" })
            {
                var b = new Border { Width = 20, Height = 20, Margin = new Thickness(0, 0, 4, 0), Background = new SolidColorBrush(TextFramemanager.ParseColor(hex)), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                string h = hex;
                b.MouseLeftButtonDown += (s, e) => color.Text = h;
                presets.Children.Add(b);
            }
            right.Children.Add(presets);

            right.Children.Add(Label("Alignment"));
            var align = new ComboBox { ItemsSource = TextFramemanager.Alignments, SelectedItem = TextFramemanager.Align(frame), Height = 28 };
            align.SelectionChanged += (s, e) => { if (align.SelectedItem is string a) Apply(TextFramemanager.KeyAlign, a); };
            right.Children.Add(align);

            right.Children.Add(Label("Draw mode"));
            var mode = new ComboBox { ItemsSource = TextFramemanager.DrawModes, SelectedItem = TextFramemanager.DrawMode(frame), Height = 28 };
            mode.SelectionChanged += (s, e) => { if (mode.SelectedItem is string m) Apply(TextFramemanager.KeyDrawMode, m); };
            right.Children.Add(mode);

            right.Children.Add(Label("Refresh every (seconds, 0 = never)"));
            var refresh = new TextBox { Text = TextFramemanager.RefreshSeconds(frame).ToString(), Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
            refresh.TextChanged += (s, e) => { if (int.TryParse(refresh.Text, out int secs) && secs >= 0) Apply(TextFramemanager.KeyRefresh, secs.ToString()); };
            right.Children.Add(refresh);

            right.Children.Add(Label("Opacity"));
            var opacity = new Slider { Minimum = 5, Maximum = 100, Value = TextFramemanager.Opacity(frame) * 100, TickFrequency = 5, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 0) };
            opacity.ValueChanged += (s, e) => Apply(TextFramemanager.KeyOpacity, ((int)e.NewValue).ToString());
            right.Children.Add(opacity);

            Grid.SetColumn(right, 2);
            body.Children.Add(right);
            root.Children.Add(body);

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 0, 20, 16) };
            var hint = new TextBlock { Text = "Changes apply immediately.  Move/resize the frame in Edit Frames Mode (tray menu).", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var done = new Button { Content = "Close", Width = 90, Height = 30, Background = new SolidColorBrush(_accent), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            done.Click += (s, e) => Close();
            footer.Children.Add(hint); footer.Children.Add(done);
            root.Children.Add(footer);

            card.Child = root;
            Content = card;
        }

        private static TextBlock Label(string text) => new()
        {
            Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
            Margin = new Thickness(0, 10, 0, 3)
        };
    }
}

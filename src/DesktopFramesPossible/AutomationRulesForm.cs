using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Desktop_Frames
{
    public class AutomationRulesForm : Window
    {
        private ListBox _rulesList;
        private ComboBox _profileCombo;
        private ComboBox _processSearchCombo;
        private CheckBox _persistedChk;
        private Slider _delaySlider;
        private TextBlock _delayValText;

        // Trigger type selection (process vs. virtual desktop)
        private RadioButton _procTrigRadio;
        private RadioButton _vdTrigRadio;
        private TextBlock _processLabel;
        private Grid _procGrid;
        private TextBlock _desktopLabel;
        private ComboBox _desktopCombo;
        // Live desktops snapshot: (Guid string, display name). Empty on unsupported builds.
        private List<(string Id, string Name)> _desktopCache = new List<(string Id, string Name)>();

        // Colors
        private Color _colorPurple = Color.FromRgb(128, 0, 128);
        private Color _colorGreen = Color.FromRgb(34, 139, 34);
        private Color _colorRed = Color.FromRgb(220, 53, 69);
        private Color _userAccentColor;

        // Win32 Imports
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point p);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
        private const uint GA_ROOT = 2;

        public AutomationRulesForm()
        {
            // Snapshot desktops first so both the dropdown and the rules list can use it.
            // GetDesktopsSafe never throws (empty list on unsupported Windows builds).
            _desktopCache = VirtualDesktopAutomationManager.GetDesktopsSafe();

            InitializeComponent();
            LoadRules();
            RefreshProcessList();
            RefreshDesktopList();
        }

        private void InitializeComponent()
        {
            try
            {
                _userAccentColor = Utility.GetColorFromName(SettingsManager.SelectedColor);

                this.Title = "Automation Rules";
                this.Width = 550;
                this.Height = 750;
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.WindowStyle = WindowStyle.None;
                this.AllowsTransparency = true;
                this.Background = new SolidColorBrush(Color.FromRgb(248, 249, 250));
                this.ResizeMode = ResizeMode.NoResize;

                // Main Card
                Border mainCard = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(8),
                    Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, BlurRadius = 10, Opacity = 0.1 }
                };

                Grid mainGrid = new Grid();
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) }); // Header
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) }); // Footer

                CreateHeader(mainGrid);
                CreateContent(mainGrid);
                CreateFooter(mainGrid);

                mainCard.Child = mainGrid;
                this.Content = mainCard;

                PositionFormOnMouseScreen();
                this.KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            }
            catch (Exception ex)
            {
                MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Error initializing: {ex.Message}", "Error");
            }
        }

        private void CreateHeader(Grid parent)
        {
            Border headerBorder = new Border { Background = new SolidColorBrush(_userAccentColor), Height = 50 };
            Grid.SetRow(headerBorder, 0);

            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            StackPanel titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            titleStack.Children.Add(new TextBlock { Text = "Manage Automation Rules", FontFamily = new FontFamily("Segoe UI"), FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White });

            Button closeButton = new Button { Content = "✕", Width = 32, Height = 32, FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            closeButton.Click += (s, e) => Close();

            Grid.SetColumn(titleStack, 0); headerGrid.Children.Add(titleStack);
            Grid.SetColumn(closeButton, 1); headerGrid.Children.Add(closeButton);
            headerBorder.Child = headerGrid;
            headerBorder.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            parent.Children.Add(headerBorder);
        }

        private void CreateContent(Grid parent)
        {
            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            Grid.SetRow(scroll, 1);

            StackPanel contentStack = new StackPanel();

            // --- 1. EXISTING RULES ---
            GroupBox listGroup = new GroupBox
            {
                Header = "Existing Rules",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(_userAccentColor),
                Margin = new Thickness(0, 0, 0, 20),
                Padding = new Thickness(8)
            };

            StackPanel listPanel = new StackPanel();
            _rulesList = new ListBox { Height = 150, Margin = new Thickness(0, 0, 0, 10), BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)) };
            _rulesList.MouseDoubleClick += (s, e) => LoadSelectedRuleForEdit();

            // RED Delete Button (Width 120)
            Button btnDelete = new Button
            {
                Content = "Delete Selected",
                Width = 120,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(_colorRed),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btnDelete.Click += (s, e) => DeleteRule();

            listPanel.Children.Add(new TextBlock { Text = "Double-click to edit rule", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 5) });
            listPanel.Children.Add(_rulesList);
            listPanel.Children.Add(btnDelete);
            listGroup.Content = listPanel;
            contentStack.Children.Add(listGroup);

            // --- 2. RULE DEFINITION ---
            GroupBox ruleGroup = new GroupBox
            {
                Header = "Rule Definition",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(_userAccentColor),
                Padding = new Thickness(8)
            };

            StackPanel ruleStack = new StackPanel();

            // Trigger Type (process vs. virtual desktop)
            CreateLabel(ruleStack, "Trigger:");
            StackPanel trigPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            _procTrigRadio = new RadioButton { Content = "When app is running", GroupName = "TriggerType", IsChecked = true, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
            _vdTrigRadio = new RadioButton { Content = "When on virtual desktop", GroupName = "TriggerType", VerticalAlignment = VerticalAlignment.Center };
            if (_desktopCache.Count == 0)
            {
                // Unsupported Windows build (or COM failure): keep the form usable,
                // just grey out the virtual desktop option with an explanation.
                _vdTrigRadio.IsEnabled = false;
                _vdTrigRadio.ToolTip = "Virtual desktops are not supported on this version of Windows.";
                ToolTipService.SetShowOnDisabled(_vdTrigRadio, true);
            }
            _procTrigRadio.Checked += (s, e) => UpdateTriggerUI();
            _vdTrigRadio.Checked += (s, e) => UpdateTriggerUI();
            trigPanel.Children.Add(_procTrigRadio);
            trigPanel.Children.Add(_vdTrigRadio);
            ruleStack.Children.Add(trigPanel);

            // Process Name + Pick Button
            _processLabel = CreateLabel(ruleStack, "Process Name (Pick or Type):");
            Grid procGrid = new Grid();
            _procGrid = procGrid;
            procGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            procGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _processSearchCombo = new ComboBox { IsEditable = true, Height = 34, Margin = new Thickness(0, 0, 10, 10), VerticalContentAlignment = VerticalAlignment.Center };

            // PURPLE Pick Button (Width 120 to match Delete)
            Button btnPick = new Button
            {
                Content = "🎯 Pick Window",
                Width = 120,
                Height = 34,
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush(_colorPurple),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btnPick.Click += async (s, e) => await PickWindowInteractively();

            procGrid.Children.Add(_processSearchCombo);
            procGrid.Children.Add(btnPick); Grid.SetColumn(btnPick, 1);
            ruleStack.Children.Add(procGrid);

            // Virtual Desktop selector (shown instead of the process input for VD rules)
            _desktopLabel = CreateLabel(ruleStack, "Virtual Desktop:");
            _desktopCombo = new ComboBox { Height = 34, Margin = new Thickness(0, 0, 0, 10), VerticalContentAlignment = VerticalAlignment.Center };
            ruleStack.Children.Add(_desktopCombo);
            _desktopLabel.Visibility = Visibility.Collapsed;
            _desktopCombo.Visibility = Visibility.Collapsed;

            // Target Profile
            CreateLabel(ruleStack, "Target Profile:");
            _profileCombo = new ComboBox { Height = 34, Margin = new Thickness(0, 0, 0, 10), VerticalContentAlignment = VerticalAlignment.Center };
            foreach (var p in ProfileManager.GetProfiles()) _profileCombo.Items.Add(p.Name);
            ruleStack.Children.Add(_profileCombo);

            // Delay Slider
            CreateLabel(ruleStack, "Activation Delay:");
            Grid delayGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            delayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            delayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _delaySlider = new Slider { Minimum = 0, Maximum = 10, TickFrequency = 1, IsSnapToTickEnabled = true, Value = 1 };
            _delayValText = new TextBlock { Text = "1s", FontWeight = FontWeights.Bold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _delaySlider.ValueChanged += (s, e) => { if (_delayValText != null) _delayValText.Text = $"{(int)e.NewValue}s"; };

            delayGrid.Children.Add(_delaySlider);
            delayGrid.Children.Add(_delayValText); Grid.SetColumn(_delayValText, 1);
            ruleStack.Children.Add(delayGrid);

            // Persistence
            _persistedChk = new CheckBox { Content = "Persisted Mode (Stay on profile after close)", Margin = new Thickness(0, 5, 0, 0) };
            ruleStack.Children.Add(_persistedChk);

            ruleGroup.Content = ruleStack;
            contentStack.Children.Add(ruleGroup);

            scroll.Content = contentStack;
            parent.Children.Add(scroll);
        }

        private void CreateFooter(Grid parent)
        {
            Border footerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 8, 16, 8)
            };
            Grid.SetRow(footerBorder, 2);

            StackPanel buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            // Close
            Button btnCancel = new Button
            {
                Content = "Close",
                Width = 100,
                Height = 34,
                Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => Close();

            // GREEN Apply
            Button btnApply = new Button
            {
                Content = "Apply",
                Width = 100,
                Height = 34,
                Background = new SolidColorBrush(_colorGreen),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand
            };
            btnApply.Click += (s, e) => ApplyRule(closeAfter: false);

            // THEME Save
            Button btnSave = new Button
            {
                Content = "Save",
                Width = 100,
                Height = 34,
                Background = new SolidColorBrush(_userAccentColor),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnSave.Click += (s, e) => ApplyRule(closeAfter: true);

            buttonPanel.Children.Add(btnCancel);
            buttonPanel.Children.Add(btnApply);
            buttonPanel.Children.Add(btnSave);
            footerBorder.Child = buttonPanel;
            parent.Children.Add(footerBorder);
        }

        private TextBlock CreateLabel(StackPanel p, string text)
        {
            var label = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 5) };
            p.Children.Add(label);
            return label;
        }

        // --- Logic ---

        private void LoadRules()
        {
            _rulesList.Items.Clear();
            foreach (var rule in ProfileManager.AutomationRules)
            {
                string mode = rule.IsPersisted ? "[P]" : "[D]";
                if (rule.TriggerType == AutomationTriggerType.VirtualDesktop)
                {
                    // Resolve the live desktop name; refresh the cached name when resolvable,
                    // otherwise show the stale cached name with a "(missing)" cue.
                    var live = _desktopCache.FirstOrDefault(d => string.Equals(d.Id, rule.VirtualDesktopId, StringComparison.OrdinalIgnoreCase));
                    string display;
                    if (live.Id != null)
                    {
                        rule.VirtualDesktopName = live.Name;
                        display = live.Name;
                    }
                    else
                    {
                        display = $"{rule.VirtualDesktopName ?? rule.VirtualDesktopId} (missing)";
                    }
                    _rulesList.Items.Add($"{mode} [Desktop] {display} → {rule.TargetProfile}");
                }
                else
                {
                    _rulesList.Items.Add($"{mode} [App] {rule.ProcessName} → {rule.TargetProfile} ({rule.DelaySeconds}s)");
                }
            }
        }

        private void ApplyRule(bool closeAfter)
        {
            bool isVdRule = _vdTrigRadio.IsChecked == true;

            // Silent Validation
            bool triggerMissing = isVdRule
                ? _desktopCombo.SelectedItem == null
                : string.IsNullOrWhiteSpace(_processSearchCombo.Text);
            if (triggerMissing || _profileCombo.SelectedItem == null)
            {
                // If the user clicked "Save" (closeAfter=true) but fields are empty,
                // treat it as a "Cancel/Close" action instead of doing nothing.
                if (closeAfter) Close();
                return;
            }

            if (isVdRule)
            {
                var item = (ComboBoxItem)_desktopCombo.SelectedItem;
                string desktopId = item.Tag?.ToString();

                var existing = ProfileManager.AutomationRules.FirstOrDefault(r =>
                    r.TriggerType == AutomationTriggerType.VirtualDesktop &&
                    string.Equals(r.VirtualDesktopId, desktopId, StringComparison.OrdinalIgnoreCase));
                if (existing != null) ProfileManager.AutomationRules.Remove(existing);

                ProfileManager.AutomationRules.Add(new AutomationRule
                {
                    TriggerType = AutomationTriggerType.VirtualDesktop,
                    VirtualDesktopId = desktopId,
                    VirtualDesktopName = item.Content?.ToString(),
                    TargetProfile = _profileCombo.SelectedItem.ToString(),
                    IsPersisted = _persistedChk.IsChecked == true,
                    // A desktop switch is a discrete event; a settle delay adds nothing, so
                    // the delay field is disabled for VD rules and the value stays 0.
                    DelaySeconds = 0
                });
            }
            else
            {
                var existing = ProfileManager.AutomationRules.FirstOrDefault(r =>
                    r.TriggerType == AutomationTriggerType.Process &&
                    r.ProcessName != null &&
                    r.ProcessName.Equals(_processSearchCombo.Text, StringComparison.OrdinalIgnoreCase));
                if (existing != null) ProfileManager.AutomationRules.Remove(existing);

                ProfileManager.AutomationRules.Add(new AutomationRule
                {
                    TriggerType = AutomationTriggerType.Process,
                    ProcessName = _processSearchCombo.Text.Trim(),
                    TargetProfile = _profileCombo.SelectedItem.ToString(),
                    IsPersisted = _persistedChk.IsChecked == true,
                    DelaySeconds = (int)_delaySlider.Value
                });
            }

            ProfileManager.SaveConfigInternal();
            LoadRules();

            if (closeAfter) Close();
        }

        private void LoadSelectedRuleForEdit()
        {
            if (_rulesList.SelectedIndex < 0) return;
            var rule = ProfileManager.AutomationRules[_rulesList.SelectedIndex];

            if (rule.TriggerType == AutomationTriggerType.VirtualDesktop)
            {
                if (_vdTrigRadio.IsEnabled) _vdTrigRadio.IsChecked = true;
                _desktopCombo.SelectedItem = _desktopCombo.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => string.Equals(i.Tag?.ToString(), rule.VirtualDesktopId, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                _procTrigRadio.IsChecked = true;
                _processSearchCombo.Text = rule.ProcessName;
            }

            _profileCombo.SelectedItem = rule.TargetProfile;
            _delaySlider.Value = rule.DelaySeconds;
            _persistedChk.IsChecked = rule.IsPersisted;
        }

        private void UpdateTriggerUI()
        {
            // Radios fire Checked during construction ordering edge cases; guard nulls.
            if (_procGrid == null || _desktopCombo == null) return;

            bool isVdRule = _vdTrigRadio.IsChecked == true;

            _processLabel.Visibility = isVdRule ? Visibility.Collapsed : Visibility.Visible;
            _procGrid.Visibility = isVdRule ? Visibility.Collapsed : Visibility.Visible;
            _desktopLabel.Visibility = isVdRule ? Visibility.Visible : Visibility.Collapsed;
            _desktopCombo.Visibility = isVdRule ? Visibility.Visible : Visibility.Collapsed;

            // Delay is meaningless for VD rules (the event is discrete) — disable, keep 0 on save.
            _delaySlider.IsEnabled = !isVdRule;
            _delayValText.Foreground = isVdRule ? Brushes.LightGray : Brushes.Black;
        }

        private void RefreshDesktopList()
        {
            _desktopCombo.Items.Clear();
            foreach (var (id, name) in _desktopCache)
            {
                _desktopCombo.Items.Add(new ComboBoxItem { Content = name, Tag = id });
            }
            if (_desktopCombo.Items.Count > 0) _desktopCombo.SelectedIndex = 0;
        }

        private void DeleteRule()
        {
            if (_rulesList.SelectedIndex < 0) return;
            ProfileManager.AutomationRules.RemoveAt(_rulesList.SelectedIndex);
            ProfileManager.SaveConfigInternal();
            LoadRules();
        }

        private void RefreshProcessList()
        {
            var currentText = _processSearchCombo.Text;
            _processSearchCombo.Items.Clear();
            var processes = Process.GetProcesses().Select(p => p.ProcessName).Distinct().OrderBy(n => n);
            foreach (var n in processes) _processSearchCombo.Items.Add(n);
            _processSearchCombo.Text = currentText;
        }

        private async Task PickWindowInteractively()
        {
            this.Visibility = Visibility.Hidden;
            await Task.Delay(150);

            Window overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Topmost = true,
                ShowInTaskbar = false,
                Cursor = Cursors.Cross,
                ForceCursor = true,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight
            };

            Grid overlayGrid = new Grid();

            // --- UNIFIED UI REPLACEMENT ---
            // Replaced manual Border/TextBlock with the Centralized Factory
            var msgBorder = MessageBoxesManager.CreateUnifiedMessage("CLICK ON THE TARGET WINDOW");

            // Important: Center the message in the grid
            msgBorder.HorizontalAlignment = HorizontalAlignment.Center;
            msgBorder.VerticalAlignment = VerticalAlignment.Center;

            overlayGrid.Children.Add(msgBorder);
            // -----------------------------

            overlay.Content = overlayGrid;

            bool picked = false;
            overlay.PreviewMouseDown += (s, e) =>
            {
                picked = true;
                try
                {
                    System.Drawing.Point p = System.Windows.Forms.Cursor.Position;
                    overlay.Close();

                    IntPtr hWnd = WindowFromPoint(p);
                    if (hWnd != IntPtr.Zero)
                    {
                        IntPtr root = GetAncestor(hWnd, GA_ROOT);
                        if (root != IntPtr.Zero) hWnd = root;

                        GetWindowThreadProcessId(hWnd, out uint pid);
                        Process proc = Process.GetProcessById((int)pid);
                        _processSearchCombo.Text = proc.ProcessName;
                    }
                }
                catch { }
            };

            overlay.Show();
            overlay.KeyDown += (s, e) => { if (e.Key == Key.Escape) overlay.Close(); };

            while (overlay.IsVisible)
            {
                await Task.Delay(100);
            }

            this.Visibility = Visibility.Visible;
            this.Activate();
        }

        // --- MULTI-SCREEN PICKER LOGIC ---
        //private async Task PickWindowInteractively()
        //{
        //    this.Visibility = Visibility.Hidden;
        //    await Task.Delay(150);

        //    Window overlay = new Window
        //    {
        //        WindowStyle = WindowStyle.None,
        //        AllowsTransparency = true,
        //        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), // Nearly invisible hit-test layer
        //        Topmost = true,
        //        ShowInTaskbar = false,
        //        Cursor = Cursors.Cross, // FORCE CROSSHAIR CURSOR
        //        ForceCursor = true, // Ensure child elements (if any) don't override it
        //        // Span all screens manually
        //        Left = SystemParameters.VirtualScreenLeft,
        //        Top = SystemParameters.VirtualScreenTop,
        //        Width = SystemParameters.VirtualScreenWidth,
        //        Height = SystemParameters.VirtualScreenHeight
        //    };

        //    Grid overlayGrid = new Grid();
        //    Border msgBorder = new Border
        //    {
        //        Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
        //        CornerRadius = new CornerRadius(10),
        //        HorizontalAlignment = HorizontalAlignment.Center,
        //        VerticalAlignment = VerticalAlignment.Center,
        //        Padding = new Thickness(20)
        //    };
        //    TextBlock msg = new TextBlock
        //    {
        //        Text = "CLICK ON THE TARGET WINDOW",
        //        Foreground = Brushes.White,
        //        FontSize = 24,
        //        FontWeight = FontWeights.Bold
        //    };
        //    msgBorder.Child = msg;
        //    overlayGrid.Children.Add(msgBorder);
        //    overlay.Content = overlayGrid;

        //    bool picked = false;
        //    overlay.PreviewMouseDown += (s, e) =>
        //    {
        //        picked = true;
        //        try
        //        {
        //            System.Drawing.Point p = System.Windows.Forms.Cursor.Position;
        //            overlay.Close();

        //            IntPtr hWnd = WindowFromPoint(p);
        //            if (hWnd != IntPtr.Zero)
        //            {
        //                IntPtr root = GetAncestor(hWnd, GA_ROOT);
        //                if (root != IntPtr.Zero) hWnd = root;

        //                GetWindowThreadProcessId(hWnd, out uint pid);
        //                Process proc = Process.GetProcessById((int)pid);
        //                _processSearchCombo.Text = proc.ProcessName;
        //            }
        //        }
        //        catch { }
        //    };

        //    overlay.Show();
        //    overlay.KeyDown += (s, e) => { if (e.Key == Key.Escape) overlay.Close(); };

        //    while (overlay.IsVisible)
        //    {
        //        await Task.Delay(100);
        //    }

        //    this.Visibility = Visibility.Visible;
        //    this.Activate();
        //}

        private void PositionFormOnMouseScreen()
        {
            try
            {
                var mousePosition = System.Windows.Forms.Cursor.Position;
                var mouseScreen = System.Windows.Forms.Screen.FromPoint(mousePosition);
                double dpiScale = GetFormDpiScaleFactor();
                double centerX = (mouseScreen.Bounds.Left / dpiScale) + ((mouseScreen.Bounds.Width / dpiScale) - this.Width) / 2;
                double centerY = (mouseScreen.Bounds.Top / dpiScale) + ((mouseScreen.Bounds.Height / dpiScale) - this.Height) / 2;
                this.Left = centerX;
                this.Top = centerY;
            }
            catch { this.WindowStartupLocation = WindowStartupLocation.CenterScreen; }
        }

        private double GetFormDpiScaleFactor()
        {
            try { using (var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) return graphics.DpiX / 96.0; }
            catch { return 1.0; }
        }
    }
}
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Desktop_Frames
{
    /// <summary>
    /// Manages all modern WPF forms with proper DPI scaling
    /// Progressively replaces Windows Forms dialogs
    /// </summary>
    public static class AboutFormManager
    {
        /// <summary>
        /// Shows the modern About form with DPI scaling
        /// </summary>
        public static void ShowAboutForm()
        {
            try
            {
                var aboutWindow = new Window
                {
                    Title = "About DesktopPossible",
                    Width = 480,
                    Height = 670,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.None,
                    Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                    AllowsTransparency = true
                };

                // Set icon from executable (cached; avoids leaking an HICON per dialog open)
                if (DialogIconCache.AppIcon != null) aboutWindow.Icon = DialogIconCache.AppIcon;

                // Main container with white background and shadow
                Border mainBorder = new Border
                {
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(0),
                    Margin = new Thickness(8),
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 270,
                        ShadowDepth = 2,
                        BlurRadius = 8,
                        Opacity = 0.1
                    }
                };

                // Root grid layout
                Grid rootGrid = new Grid();
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

                // HEADER: Logo, Title, Version, Close Button
                CreateHeader(rootGrid);

                // CONTENT: Scrollable content area
                CreateContent(rootGrid);

                // FOOTER: DevPossible section
                CreateFooter(rootGrid);

                mainBorder.Child = rootGrid;
                aboutWindow.Content = mainBorder;

                // Make window draggable ONLY by header to avoid button click conflicts
                bool isDragging = false;
                Point clickPosition = new Point();

                // Get the header from the root grid for dragging
                var headerElement = rootGrid.Children.OfType<Border>().FirstOrDefault();
                if (headerElement != null)
                {
                    headerElement.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.LeftButton == MouseButtonState.Pressed)
                        {
                            try
                            {
                                aboutWindow.DragMove();
                            }
                            catch { } // Ignore DragMove exceptions
                        }
                    };
                }

                aboutWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error showing About form: {ex.Message}");
                MessageBox.Show($"Error showing About form: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Loads an embedded resource image fully into memory (CacheOption.OnLoad) so the
        /// manifest stream can be disposed immediately instead of being held by WPF's
        /// default lazy decode. Returns null if the resource is missing.
        /// </summary>
        private static BitmapImage LoadEmbeddedBitmap(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
            {
                if (resourceStream == null) return null;

                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = resourceStream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }

        private static void CreateHeader(Grid rootGrid)
        {
            Border headerBorder = new Border
            {
                Background = Brushes.White,
                Padding = new Thickness(16, 16, 16, 0),
                Height = 90
            };

            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Logo
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Title area
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Close button

            // Logo (64x64 placeholder) with Ctrl+Click Easter Egg
            Border logoPlaceholder = new Border
            {
                Width = 64,
                Height = 64,
                Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)), // Green placeholder
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 16, 0),
                Cursor = Cursors.Hand
            };

            // Load logo from resources if available
            try
            {
                var bitmap = LoadEmbeddedBitmap("Desktop_Frames.Resources.logo1.png");
                if (bitmap != null)
                {
                    Image logoImage = new Image
                    {
                        Source = bitmap,
                        Width = 64,
                        Height = 64,
                        Stretch = Stretch.Uniform
                    };
                    logoPlaceholder.Child = logoImage;
                    logoPlaceholder.Background = Brushes.Transparent;
                }
            }
            catch { } // Use placeholder if logo fails to load

            // Easter Egg: Ctrl+Click on logo
            logoPlaceholder.MouseLeftButtonDown += (s, e) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    ShowEasterEggForm();
                }
            };

            // Title and Version area
            StackPanel titleArea = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };

            TextBlock titleText = new TextBlock
            {
                Text = "DesktopPossible",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 22, // Your improved font size
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                Margin = new Thickness(0, 0, 0, 4)
            };

            TextBlock versionText = new TextBlock
            {
                Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version}",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14, // Your improved font size
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104))
            };

            titleArea.Children.Add(titleText);
            titleArea.Children.Add(versionText);

            // Close Button
            Button closeButton = new Button
            {
                Content = "✕",
                Width = 32,
                Height = 32,
                FontSize = 22, // Your improved font size
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -8, 0, 0)
            };

            closeButton.MouseEnter += (s, e) => closeButton.Background = new SolidColorBrush(Color.FromRgb(241, 243, 244));
            closeButton.MouseLeave += (s, e) => closeButton.Background = Brushes.Transparent;
            closeButton.Click += (s, e) => ((Window)((FrameworkElement)s).TemplatedParent ?? Window.GetWindow((FrameworkElement)s)).Close();

            headerGrid.Children.Add(logoPlaceholder);
            headerGrid.Children.Add(titleArea);
            headerGrid.Children.Add(closeButton);
            Grid.SetColumn(titleArea, 1);
            Grid.SetColumn(closeButton, 2);

            headerBorder.Child = headerGrid;
            Grid.SetRow(headerBorder, 0);
            rootGrid.Children.Add(headerBorder);
        }

        private static void CreateContent(Grid rootGrid)
        {
            ScrollViewer scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(20, 0, 20, 20)
            };

            StackPanel contentStack = new StackPanel();

            // About Section
            CreateSection(contentStack, "About", "Organize your desktop like magic!",
                "DesktopPossible creates virtual frames on your desktop, allowing you to group and organize icons in a clean and convenient way.", 20);

            // Credits Section
            CreateSection(contentStack, "Credits", null,
                "DesktopPossible is a hard fork of Desktop Frames + by Nikos Georgousis (limbo666), which was originally created by HakanKokcu under the name BirdyFences.\n\nDesktopPossible is maintained by DevPossible.", 20);

            // Support Development Section
            CreateSupportSection(contentStack);

            // MIT License Section
            CreateLicenseSection(contentStack);

            scrollViewer.Content = contentStack;
            Grid.SetRow(scrollViewer, 1);
            rootGrid.Children.Add(scrollViewer);
        }

        private static void CreateSection(StackPanel parent, string title, string subtitle, string content, double bottomMargin)
        {
            StackPanel section = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, bottomMargin)
            };

            // Title
            TextBlock titleBlock = new TextBlock
            {
                Text = title,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 18, // Your improved font size
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                Margin = new Thickness(0, 0, 0, subtitle != null ? 8 : 12)
            };
            section.Children.Add(titleBlock);

            // Subtitle (optional)
            if (!string.IsNullOrEmpty(subtitle))
            {
                TextBlock subtitleBlock = new TextBlock
                {
                    Text = subtitle,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14, // Your improved font size
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(60, 64, 67)),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                section.Children.Add(subtitleBlock);
            }

            // Content
            TextBlock contentBlock = new TextBlock
            {
                Text = content,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14, // Your improved font size
                Foreground = new SolidColorBrush(Color.FromRgb(60, 64, 67)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 16
            };
            section.Children.Add(contentBlock);

            parent.Children.Add(section);
        }

        private static void CreateSupportSection(StackPanel parent)
        {
            StackPanel section = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 20)
            };

            // Title
            TextBlock titleBlock = new TextBlock
            {
                Text = "Support Development",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 18, // Your improved font size
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                Margin = new Thickness(0, 0, 0, 12)
            };
            section.Children.Add(titleBlock);

            // Buttons container
            StackPanel buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };

            // GitHub Button
            Button githubButton = new Button
            {
                Content = "⚡ Visit GitHub repository",
                Height = 36,
                Padding = new Thickness(16, 0, 16, 0),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14, // Your improved font size
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(138, 43, 226)), // Purple
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 12, 0)
            };

            githubButton.MouseEnter += (s, e) => githubButton.Background = new SolidColorBrush(Color.FromRgb(108, 30, 180));
            githubButton.MouseLeave += (s, e) => githubButton.Background = new SolidColorBrush(Color.FromRgb(138, 43, 226));
            githubButton.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/DevPossible/DesktopPossible",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error opening GitHub link: {ex.Message}");
                }
            };

            // DevPossible website Button
            Button devPossibleButton = new Button
            {
                Content = "🌐 devpossible.com",
                Height = 36,
                Padding = new Thickness(16, 0, 16, 0),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14, // Your improved font size
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(66, 133, 244)), // Blue
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            devPossibleButton.MouseEnter += (s, e) => devPossibleButton.Background = new SolidColorBrush(Color.FromRgb(50, 105, 200));
            devPossibleButton.MouseLeave += (s, e) => devPossibleButton.Background = new SolidColorBrush(Color.FromRgb(66, 133, 244));
            devPossibleButton.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://devpossible.com",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error opening DevPossible link: {ex.Message}");
                }
            };

            buttonsPanel.Children.Add(githubButton);
            buttonsPanel.Children.Add(devPossibleButton);
            section.Children.Add(buttonsPanel);

            // Plain link crediting the upstream project
            TextBlock upstreamLink = new TextBlock
            {
                Text = "Support the upstream project: Desktop Frames + by limbo666",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 115, 232)),
                TextDecorations = TextDecorations.Underline,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 12, 0, 0)
            };
            upstreamLink.MouseLeftButtonDown += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/limbo666/DesktopFramesPlus",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error opening upstream link: {ex.Message}");
                }
            };
            section.Children.Add(upstreamLink);

            parent.Children.Add(section);
        }

        private static void CreateLicenseSection(StackPanel parent)
        {
            // Separator
            Border separator = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                Margin = new Thickness(0, 10, 0, 15)
            };
            parent.Children.Add(separator);

            // MIT License
            TextBlock licenseBlock = new TextBlock
            {
                Text = "⚖ MIT License",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14, // Your improved font size
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            };
            parent.Children.Add(licenseBlock);

            // Second separator line
            Border separator2 = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                Margin = new Thickness(0, 0, 0, 0)
            };
            parent.Children.Add(separator2);

            // --- NEW: Sound Credits ---
            TextBlock soundCreditsBlock = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10, // Tiny text
                Foreground = new SolidColorBrush(Color.FromRgb(150, 154, 158)), // Muted grey
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 15, 0, 10) // --- FIX: Added elegant breathing room above and below the credits ---
            };

            // Using fully qualified 'Run' to avoid needing to add System.Windows.Documents to the using directives
            soundCreditsBlock.Inlines.Add(new System.Windows.Documents.Run("Using free sound files from "));
            soundCreditsBlock.Inlines.Add(new System.Windows.Documents.Run("pixabay.com") { FontWeight = FontWeights.Bold });
            soundCreditsBlock.Inlines.Add(new System.Windows.Documents.Run(" and "));
            soundCreditsBlock.Inlines.Add(new System.Windows.Documents.Run("pexels.com") { FontWeight = FontWeights.Bold });

            parent.Children.Add(soundCreditsBlock);
        }

        private static void CreateFooter(Grid rootGrid)
        {
            Border footerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(220, 220, 235)),
                Height = 60,
                Padding = new Thickness(10, 10, 10, 10)
            };

            // Centered container for logo and text
            StackPanel centerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // DevPossible text
            TextBlock devPossibleText = new TextBlock
            {
                Text = "DevPossible",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(66, 133, 244)),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };

            devPossibleText.MouseLeftButtonDown += (s, e) => OpenDevPossibleLink();

            centerPanel.Children.Add(devPossibleText);
            footerBorder.Child = centerPanel;

            Grid.SetRow(footerBorder, 2);
            rootGrid.Children.Add(footerBorder);
        }



        private static void OpenDevPossibleLink()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://devpossible.com",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error opening DevPossible link: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows the Easter Egg form (triggered by Ctrl+Click on logo)
        /// Advanced version with animations, image changes, and progressive surprises
        /// </summary>
        private static void ShowEasterEggForm()
        {
            try
            {
                var easterWindow = new Window
                {
                    Title = "It is great to have you here",
                    Width = 400,
                    Height = 520, // Taller to accommodate image
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.None,
                    Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                    AllowsTransparency = true,
                    Topmost = true
                };

                // Track state for progressive changes
                bool hasClickedCloseOnce = false;

                // Main container
                Border mainBorder = new Border
                {
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(8),
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 270,
                        ShadowDepth = 4,
                        BlurRadius = 12,
                        Opacity = 0.2
                    }
                };

                Grid rootGrid = new Grid();
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Image
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Message
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

                // HEADER: Purple gradient like in image
                Border headerBorder = new Border
                {
                    Background = new LinearGradientBrush(
                        Color.FromRgb(121, 85, 172), // Purple gradient
                        Color.FromRgb(88, 42, 114),
                        0),
                    Height = 50,
                    Padding = new Thickness(16, 12, 16, 12)
                };

                Grid headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Dynamic title text
                TextBlock titleText = new TextBlock
                {
                    Text = "It is great to have you here",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };

              
                Button closeButton = new Button
                {
                    Content = "✕",
                    Width = 28,
                    Height = 28,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };

                closeButton.MouseEnter += (s, e) => closeButton.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
                closeButton.MouseLeave += (s, e) => closeButton.Background = Brushes.Transparent;

                headerGrid.Children.Add(titleText);
                headerGrid.Children.Add(closeButton);
                Grid.SetColumn(closeButton, 1);
                headerBorder.Child = headerGrid;

                // IMAGE: Main content area with dynamic image
                Border imageBorder = new Border
                {
                    Background = Brushes.White,
                    Margin = new Thickness(20, 20, 20, 10)
                };

                Image mainImage = new Image
                {
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

               
                try
                {
                    var bitmap = LoadEmbeddedBitmap("Desktop_Frames.Resources.Feed.png");
                    if (bitmap != null) mainImage.Source = bitmap;
                }
                catch (Exception imgEx)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error loading Feed.png: {imgEx.Message}");
                }

                imageBorder.Child = mainImage;

                
                TextBlock messageText = new TextBlock
                {
                    Text = "Don't feed the dragons",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20, 10, 20, 20)
                };

                Border footerBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                    Height = 60,
                    Padding = new Thickness(20, 15, 20, 15)
                };

                Button okButton = new Button
                {
                    Content = "OK",
                    Height = 40,
                    MinWidth = 160,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Background = new LinearGradientBrush(
                        Color.FromRgb(121, 85, 172), // Purple like header
                        Color.FromRgb(88, 42, 114),
                        0),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center, // KEY FIX!
                    VerticalContentAlignment = VerticalAlignment.Center,     // KEY FIX!
                    Padding = new Thickness(16, 8, 16, 8) // Proper padding
                };

                okButton.Click += (s, e) => easterWindow.Close();

                footerBorder.Child = okButton;

                
                closeButton.Click += async (s, e) =>
                {
                    if (!hasClickedCloseOnce)
                    {
                   
                        hasClickedCloseOnce = true;

              
                        titleText.Text = "What!?";
                        headerBorder.Background = new LinearGradientBrush(
                            Color.FromRgb(180, 20, 60), // Dramatic red
                            Color.FromRgb(120, 10, 40),
                            0);

                     
                        messageText.Text = "Are you sure? Think again!";


                        okButton.Content = "Don't go!";
                        okButton.Background = new SolidColorBrush(Color.FromRgb(220, 20, 60)); // Crimson

                   
                        try
                        {
                            // Decode fully up front (stream disposed immediately), then animate.
                            var dragonBitmap = LoadEmbeddedBitmap("Desktop_Frames.Resources.dragon.png");
                            if (dragonBitmap != null)
                            {
                                // Fade out current image
                                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300));
                                mainImage.BeginAnimation(UIElement.OpacityProperty, fadeOut);

                                await System.Threading.Tasks.Task.Delay(300);

                                mainImage.Source = dragonBitmap;

                                // Fade in dragon image
                                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300));
                                mainImage.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                            }
                        }
                        catch (Exception imgEx)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error loading dragon.png: {imgEx.Message}");
                        }

                       
                        var originalLeft = easterWindow.Left;
                        var originalTop = easterWindow.Top;

                        for (int i = 0; i < 8; i++)
                        {
                            int shakeAmount = 12;
                            easterWindow.Left = originalLeft + (i % 2 == 0 ? shakeAmount : -shakeAmount);
                            easterWindow.Top = originalTop + (i % 4 < 2 ? shakeAmount / 2 : -shakeAmount / 2);
                            await System.Threading.Tasks.Task.Delay(80);
                        }

                        // Return to original position
                        easterWindow.Left = originalLeft;
                        easterWindow.Top = originalTop;

                        var originalButtonBrush = okButton.Background;
                        for (int i = 0; i < 4; i++)
                        {
                            okButton.Background = new SolidColorBrush(Color.FromRgb(255, 255, 100)); // Bright yellow flash
                            await System.Threading.Tasks.Task.Delay(150);
                            okButton.Background = originalButtonBrush;
                            await System.Threading.Tasks.Task.Delay(150);
                        }

                       
                        closeButton.Content = "✕✕";
                        closeButton.Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 200)); // Light red tint
                    }
                    else
                    {
                        // Second click - actually close (but make it modal-friendly)
                        easterWindow.Close();
                    }
                };

                // Assembly
                Grid.SetRow(headerBorder, 0);
                Grid.SetRow(imageBorder, 1);
                Grid.SetRow(messageText, 2);
                Grid.SetRow(footerBorder, 3);

                rootGrid.Children.Add(headerBorder);
                rootGrid.Children.Add(imageBorder);
                rootGrid.Children.Add(messageText);
                rootGrid.Children.Add(footerBorder);

                mainBorder.Child = rootGrid;
                easterWindow.Content = mainBorder;

                // Make draggable ONLY by header - not the entire window
                bool isDragging = false;
                Point clickPosition = new Point();

                headerBorder.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed)
                    {
                        isDragging = true;
                        clickPosition = e.GetPosition(headerBorder);
                        headerBorder.CaptureMouse();
                        e.Handled = true;
                    }
                };

                headerBorder.MouseMove += (s, e) =>
                {
                    if (isDragging && e.LeftButton == MouseButtonState.Pressed && headerBorder.IsMouseCaptured)
                    {
                        try
                        {
                            Point currentPosition = e.GetPosition(headerBorder);
                            easterWindow.Left += currentPosition.X - clickPosition.X;
                            easterWindow.Top += currentPosition.Y - clickPosition.Y;
                        }
                        catch { } // Ignore positioning errors
                    }
                };

                headerBorder.MouseLeftButtonUp += (s, e) =>
                {
                    if (isDragging)
                    {
                        isDragging = false;
                        if (headerBorder.IsMouseCaptured)
                        {
                            headerBorder.ReleaseMouseCapture();
                        }
                        e.Handled = true;
                    }
                };

                // CRITICAL: Ensure mouse capture is always released when window closes
                easterWindow.Closed += (s, e) =>
                {
                    try
                    {
                        isDragging = false;
                        if (headerBorder.IsMouseCaptured)
                        {
                            headerBorder.ReleaseMouseCapture();
                        }
                        // Force release any system mouse capture
                        Mouse.Capture(null);
                    }
                    catch { } // Ignore cleanup errors
                };

                // Also release on deactivation
                easterWindow.Deactivated += (s, e) =>
                {
                    try
                    {
                        isDragging = false;
                        if (headerBorder.IsMouseCaptured)
                        {
                            headerBorder.ReleaseMouseCapture();
                        }
                    }
                    catch { } // Ignore cleanup errors
                };

                // Show as modal dialog - this prevents the bug you mentioned
                easterWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error showing Easter Egg form: {ex.Message}");
            }
        }
    }
}
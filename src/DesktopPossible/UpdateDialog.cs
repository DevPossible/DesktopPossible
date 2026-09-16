using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Desktop_Frames;

/// <summary>
/// "Update Now" flow: downloads the latest installer with a progress bar, verifies it
/// (see <see cref="UpdateInstaller.Verify"/>), then hands off to msiexec and exits the app.
/// Any failure leaves the dialog open with the reason and a link to the releases page.
/// </summary>
public static class UpdateDialog
{
    private static Window? _open;

    public static void Show(Version latest, UpdateChecker.ReleaseAsset installer)
    {
        if (_open != null) { _open.Activate(); return; }

        var window = new Window
        {
            Title = "Update DesktopPossible",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = true,
        };
        _open = window;

        var mainBorder = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(8),
            Effect = new DropShadowEffect { Color = Colors.Black, Direction = 315, ShadowDepth = 2, BlurRadius = 8, Opacity = 0.2 },
        };
        var root = new StackPanel();
        mainBorder.Child = root;
        window.Content = mainBorder;

        // Header (accent-coloured, like the other dialogs)
        var header = new Border
        {
            Background = AccentBrush(),
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            Padding = new Thickness(20, 15, 15, 15),
        };
        var headerPanel = new StackPanel();
        headerPanel.Children.Add(new TextBlock
        {
            Text = $"Update to v{latest.ToString(3)}",
            FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = $"You are running v{UpdateChecker.CurrentVersion.ToString(3)}",
            FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            Margin = new Thickness(0, 2, 0, 0),
        });
        header.Child = headerPanel;
        root.Children.Add(header);

        // Body
        var body = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        root.Children.Add(body);

        var status = new TextBlock
        {
            Text = "Downloading installer…",
            FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
            TextWrapping = TextWrapping.Wrap,
        };
        body.Children.Add(status);

        var progress = new ProgressBar { Height = 10, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 12, 0, 6), Foreground = AccentBrush() };
        body.Children.Add(progress);

        var detail = new TextBlock
        {
            Text = installer.Name,
            FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
        };
        body.Children.Add(detail);

        var relaunch = new CheckBox
        {
            Content = "Restart DesktopPossible after installing",
            IsChecked = true,
            Margin = new Thickness(0, 14, 0, 0),
            FontSize = 12,
        };
        body.Children.Add(relaunch);

        // Footer
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 0, 20, 18) };
        root.Children.Add(footer);

        var releasesLink = FlatButton("Open releases page");
        releasesLink.Visibility = Visibility.Collapsed;
        releasesLink.Click += (s, e) =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = UpdateChecker.ReleaseUrl, UseShellExecute = true }); } catch { }
        };
        footer.Children.Add(releasesLink);

        var cancel = FlatButton("Cancel");
        footer.Children.Add(cancel);

        var cts = new CancellationTokenSource();
        cancel.Click += (s, e) => { cts.Cancel(); window.Close(); };
        window.Closed += (s, e) => { cts.Cancel(); _open = null; };

        void Fail(string reason)
        {
            status.Text = reason;
            status.Foreground = new SolidColorBrush(Color.FromRgb(180, 30, 30));
            progress.IsIndeterminate = false;
            relaunch.IsEnabled = false;
            releasesLink.Visibility = Visibility.Visible;
            cancel.Content = "Close";
        }

        window.Loaded += async (s, e) =>
        {
            string target = Path.Combine(UpdateInstaller.DownloadFolder, installer.Name);
            try
            {
                var reporter = new Progress<(long Done, long Total)>(p =>
                {
                    if (p.Total > 0)
                    {
                        progress.IsIndeterminate = false;
                        progress.Value = 100.0 * p.Done / p.Total;
                        detail.Text = $"{p.Done / 1048576.0:F1} MB of {p.Total / 1048576.0:F1} MB";
                    }
                    else
                    {
                        progress.IsIndeterminate = true;
                        detail.Text = $"{p.Done / 1048576.0:F1} MB";
                    }
                });
                await UpdateInstaller.DownloadAsync(installer, target, reporter, cts.Token);

                status.Text = "Verifying signature…";
                progress.IsIndeterminate = true;
                cancel.IsEnabled = false;
                string? problem = await Task.Run(() => UpdateInstaller.Verify(target, installer), cts.Token);
                if (problem != null)
                {
                    try { File.Delete(target); } catch { }
                    cancel.IsEnabled = true;
                    Fail(problem);
                    return;
                }

                status.Text = "Starting the installer. DesktopPossible will close now…";
                progress.IsIndeterminate = false;
                progress.Value = 100;
                bool restart = relaunch.IsChecked == true;
                relaunch.IsEnabled = false;
                await Task.Delay(600, cts.Token);
                UpdateInstaller.LaunchInstaller(target, restart);
            }
            catch (OperationCanceledException)
            {
                try { File.Delete(target); } catch { }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"UpdateDialog: {ex.Message}");
                try { File.Delete(target); } catch { }
                cancel.IsEnabled = true;
                Fail($"Download failed: {ex.Message}");
            }
        };

        window.Show();
    }

    private static Button FlatButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(14, 6, 14, 6),
        Margin = new Thickness(8, 0, 0, 0),
        FontSize = 12,
        Background = new SolidColorBrush(Color.FromRgb(243, 244, 246)),
        Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
        Cursor = System.Windows.Input.Cursors.Hand,
    };

    private static SolidColorBrush AccentBrush()
    {
        try { return new SolidColorBrush(Utility.GetColorFromName(SettingsManager.SelectedColor)); }
        catch { return new SolidColorBrush(Color.FromRgb(66, 133, 244)); }
    }
}

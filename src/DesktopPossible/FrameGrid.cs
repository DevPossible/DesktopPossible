using System;

namespace Desktop_Frames
{
    /// <summary>
    /// The frame grid: pure geometry shared by frame drag/resize snapping, draw-mode
    /// and category-frame default sizes, and App-Categorize placement, so frames hold
    /// whole icon rows/columns and auto-placed frames sit on the same grid users drag to.
    ///
    /// All numbers are WPF device-independent pixels (DIPs). WPF scales DIPs to device
    /// pixels per monitor, and icon panels are measured in DIPs too, so no extra DPI
    /// scaling is applied here.
    /// </summary>
    public static class FrameGrid
    {
        /// <summary>Icon StackPanel width (IconManager.AddIcon: Width = 60).</summary>
        public const double IconPanelWidth = 60;

        /// <summary>Default per-frame IconSpacing (CreateNewFrame: IconSpacing = 5).</summary>
        public const int DefaultIconSpacing = 5;

        /// <summary>Default per-frame IconSize ("Medium" = 32px, CoreUtilities.GetIconSizePixels).</summary>
        public const string DefaultIconSize = "Medium";

        /// <summary>
        /// One label line: icons render through Framemanager.AddIcon, whose label is a
        /// default-size (12 DIP) wrapped TextBlock — a ~15.96 DIP Segoe UI line box,
        /// rounded up. The cell height budgets <see cref="LabelLines"/> lines so a row
        /// fits an icon with a two-to-three-line label without clipping.
        /// </summary>
        public const double LabelLineHeight = 16;
        public const int LabelLines = 3;

        /// <summary>The icon Image's own Margin(5) top+bottom (Framemanager.AddIcon).</summary>
        public const double IconImageMargin = 10;

        /// <summary>Frame border thickness per side (CreateFrame default FrameBorderThickness = 2).</summary>
        public const double BorderThickness = 2;

        /// <summary>
        /// Title bar height: the title Label (FontSize 12 + Label padding) — this is also the
        /// rolled-up window height used throughout FrameManager (28).
        /// </summary>
        public const double TitleBarHeight = 28;

        /// <summary>Width kept free for the icon ScrollViewer's vertical scrollbar plus panel slack.</summary>
        public const double ScrollbarAllowance = 26;

        /// <summary>Horizontal chrome: both borders + scrollbar allowance (30).</summary>
        public const double ChromeWidth = 2 * BorderThickness + ScrollbarAllowance;

        /// <summary>Vertical chrome: title bar + both borders (32).</summary>
        public const double ChromeHeight = TitleBarHeight + 2 * BorderThickness;

        /// <summary>Fallback gap between auto-placed frames when no grid unit is available.</summary>
        public const double FallbackGap = 16;

        /// <summary>
        /// Width of one icon cell: the exact slot a WrapPanel cell occupies — icon panel
        /// (60) plus its Margin(spacing) on both sides plus the neighbour's = 60 + 4*spacing.
        /// Mirrors FrameManager.GetFreeArrangeCellWidth. Default spacing 5 → 80.
        /// </summary>
        public static double UnitWidthFor(int iconSpacing) => IconPanelWidth + 4 * Math.Max(0, iconSpacing);

        /// <summary>
        /// Height of one icon cell: icon image (plus its own margins) + budgeted label
        /// lines + the panel's top/bottom Margin(spacing).
        /// Default (Medium 32px, spacing 5) → 32 + 10 + 3*16 + 10 = 100.
        /// </summary>
        public static double UnitHeightFor(int iconSizePixels, int iconSpacing) =>
            Math.Max(1, iconSizePixels) + IconImageMargin + LabelLines * LabelLineHeight + 2 * Math.Max(0, iconSpacing);

        /// <summary>Grid unit width for the default frame settings (80).</summary>
        public static double UnitWidth => UnitWidthFor(DefaultIconSpacing);

        /// <summary>Grid unit height for the default frame settings (100).</summary>
        public static double UnitHeight => UnitHeightFor(CoreUtilities.GetIconSizePixels(DefaultIconSize), DefaultIconSpacing);

        /// <summary>
        /// Snaps an outer frame size to chrome + whole units: width = ChromeWidth + N*UnitWidth,
        /// height = ChromeHeight + M*UnitHeight with N, M ≥ 1, nearest.
        /// </summary>
        public static (double Width, double Height) SnapSize(double width, double height) =>
            SnapSize(width, height, UnitWidth, UnitHeight);

        public static (double Width, double Height) SnapSize(double width, double height, double unitWidth, double unitHeight)
        {
            int n = Math.Max(1, (int)Math.Round((width - ChromeWidth) / unitWidth));
            int m = Math.Max(1, (int)Math.Round((height - ChromeHeight) / unitHeight));
            return (ChromeWidth + n * unitWidth, ChromeHeight + m * unitHeight);
        }

        /// <summary>
        /// Snaps a top-left position to the nearest grid point: multiples of
        /// (UnitWidth, UnitHeight) measured from the work-area origin.
        /// </summary>
        public static (double X, double Y) SnapPosition(double x, double y, double originX, double originY) =>
            SnapPosition(x, y, originX, originY, UnitWidth, UnitHeight);

        public static (double X, double Y) SnapPosition(double x, double y, double originX, double originY,
            double unitWidth, double unitHeight)
        {
            double sx = originX + Math.Round((x - originX) / unitWidth) * unitWidth;
            double sy = originY + Math.Round((y - originY) / unitHeight) * unitHeight;
            return (sx, sy);
        }
    }
}

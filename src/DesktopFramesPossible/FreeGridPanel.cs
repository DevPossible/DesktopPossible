using System;
using System.Windows;
using System.Windows.Controls;

namespace Desktop_Frames
{
    /// <summary>
    /// The icon host panel for Data/Portal frames.
    ///
    /// Normal mode (FreeArrange == false): behaves exactly like the stock WrapPanel it
    /// derives from — every existing `as WrapPanel` cast, FindWrapPanel helper, and
    /// WrapPanel-typed API in the codebase (including IconManager.OptimizeFramePanel)
    /// keeps working unchanged.
    ///
    /// Free-arrange mode (Data frames with the "Free arrange" toggle on): children are
    /// placed canvas-style at absolute grid cells taken from the GridCol/GridRow attached
    /// properties (mirrors of the per-item persisted GridCol/GridRow JSON values).
    /// Cell width comes from the frame's icon spacing settings (identical to the slot a
    /// WrapPanel cell occupies); cell height is the tallest child, matching WrapPanel's
    /// uniform row height. Out-of-range columns are clamped at render time only.
    /// The panel reports an extent height of (max occupied row + 1) * cell height so the
    /// surrounding ScrollViewer scrolls exactly as before.
    /// </summary>
    public class FreeGridPanel : WrapPanel
    {
        #region GridCol / GridRow attached properties

        public static readonly DependencyProperty GridColProperty = DependencyProperty.RegisterAttached(
            "GridCol", typeof(int), typeof(FreeGridPanel),
            new FrameworkPropertyMetadata(-1,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

        public static readonly DependencyProperty GridRowProperty = DependencyProperty.RegisterAttached(
            "GridRow", typeof(int), typeof(FreeGridPanel),
            new FrameworkPropertyMetadata(-1,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

        public static void SetGridCol(UIElement element, int value) => element?.SetValue(GridColProperty, value);
        public static int GetGridCol(UIElement element) => element == null ? -1 : (int)element.GetValue(GridColProperty);
        public static void SetGridRow(UIElement element, int value) => element?.SetValue(GridRowProperty, value);
        public static int GetGridRow(UIElement element) => element == null ? -1 : (int)element.GetValue(GridRowProperty);

        #endregion

        private bool _freeArrange;
        /// <summary>Absolute grid placement on/off. Off = stock WrapPanel flow.</summary>
        public bool FreeArrange
        {
            get => _freeArrange;
            set
            {
                if (_freeArrange == value) return;
                _freeArrange = value;
                InvalidateMeasure();
            }
        }

        private double _cellWidth = 80;
        /// <summary>
        /// Pixel width of one grid cell — the same slot width a WrapPanel cell occupies
        /// (icon StackPanel width + its margins). Set from the frame's IconSpacing setting.
        /// </summary>
        public double CellWidth
        {
            get => _cellWidth;
            set
            {
                if (value <= 0 || double.IsNaN(value) || Math.Abs(_cellWidth - value) < 0.01) return;
                _cellWidth = value;
                InvalidateMeasure();
            }
        }

        /// <summary>Pixel height of one grid cell (tallest child; computed during measure).</summary>
        public double CellHeight { get; private set; } = 80;

        /// <summary>Visible column count from the last layout pass (min 1).</summary>
        public int Columns { get; private set; } = 1;

        protected override Size MeasureOverride(Size constraint)
        {
            if (!FreeArrange) return base.MeasureOverride(constraint);

            double cw = _cellWidth > 0 ? _cellWidth : 80;
            double maxChildHeight = 0;
            int maxRow = -1;

            foreach (UIElement child in InternalChildren)
            {
                if (child == null) continue;
                child.Measure(new Size(cw, double.PositiveInfinity));
                if (child.DesiredSize.Height > maxChildHeight) maxChildHeight = child.DesiredSize.Height;
                int row = Math.Max(0, GetGridRow(child));
                if (row > maxRow) maxRow = row;
            }

            CellHeight = maxChildHeight > 0 ? maxChildHeight : cw;

            double width = double.IsInfinity(constraint.Width) || double.IsNaN(constraint.Width)
                ? cw
                : constraint.Width;
            Columns = GridLayout.ColumnsForWidth(width, cw);

            double extentHeight = GridLayout.ExtentHeight(maxRow, CellHeight);
            double desiredWidth = double.IsInfinity(constraint.Width) || double.IsNaN(constraint.Width)
                ? Columns * cw
                : constraint.Width;

            return new Size(desiredWidth, extentHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (!FreeArrange) return base.ArrangeOverride(finalSize);

            double cw = _cellWidth > 0 ? _cellWidth : 80;
            double ch = CellHeight > 0 ? CellHeight : cw;
            Columns = GridLayout.ColumnsForWidth(finalSize.Width, cw);

            foreach (UIElement child in InternalChildren)
            {
                if (child == null) continue;
                // Clamp out-of-range columns at render only — the persisted value is untouched.
                int col = GridLayout.ClampColumn(Math.Max(0, GetGridCol(child)), Columns);
                int row = Math.Max(0, GetGridRow(child));
                child.Arrange(new Rect(col * cw, row * ch, cw, ch));
            }

            return finalSize;
        }
    }
}

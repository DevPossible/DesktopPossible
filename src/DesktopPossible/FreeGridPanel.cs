using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
    /// uniform row height. Resizing the frame NEVER moves an icon: the panel reports its
    /// true extent — (max occupied column + 1) × cell width by (max occupied row + 1) ×
    /// cell height — so icons outside the viewport are clipped and reachable by scrolling
    /// (or by the hidden-scrollbar pan in <see cref="GridOverflowNavigator"/>), never
    /// folded onto the last visible column.
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

        /// <summary>Column count that fits the VISIBLE viewport (min 1) — where new items are placed.</summary>
        public int Columns { get; private set; } = 1;

        /// <summary>Column count of the occupied grid: max occupied column + 1 (min 1).</summary>
        public int ExtentColumns { get; private set; } = 1;

        /// <summary>Columns reachable by a drop: the larger of the viewport and the occupied grid.</summary>
        public int DropColumns => Math.Max(Columns, ExtentColumns);

        private ScrollViewer? _scrollViewer;

        /// <summary>
        /// Width the panel is shown in. The hosting ScrollViewer measures us with infinite
        /// width once horizontal scrolling is allowed (free-arrange frames), so the
        /// visible column count has to come from its viewport rather than the constraint.
        /// </summary>
        private double VisibleWidth(Size constraint)
        {
            if (!double.IsInfinity(constraint.Width) && !double.IsNaN(constraint.Width) && constraint.Width > 0)
                return constraint.Width;

            _scrollViewer ??= FindScrollViewer();
            if (_scrollViewer != null)
            {
                if (_scrollViewer.ViewportWidth > 0) return _scrollViewer.ViewportWidth;
                if (_scrollViewer.ActualWidth > 0) return _scrollViewer.ActualWidth;
            }
            return 0;
        }

        private ScrollViewer? FindScrollViewer()
        {
            DependencyObject? node = this;
            while (node != null)
            {
                if (node is ScrollViewer sv) return sv;
                node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
            }
            return null;
        }

        protected override Size MeasureOverride(Size constraint)
        {
            if (!FreeArrange) return base.MeasureOverride(constraint);

            double cw = _cellWidth > 0 ? _cellWidth : 80;
            double maxChildHeight = 0;
            int maxRow = -1, maxCol = -1;

            foreach (UIElement child in InternalChildren)
            {
                if (child == null) continue;
                child.Measure(new Size(cw, double.PositiveInfinity));
                // Cell height comes from ICON panels only. Adorner children (drop-preview
                // ghost Borders) have no intrinsic height — letting them into this max
                // once collapsed CellHeight to ~4px on an empty frame, which made
                // CellFromPoint compute an enormous row for a top-of-frame drop.
                if (child is StackPanel && child.DesiredSize.Height > maxChildHeight)
                    maxChildHeight = child.DesiredSize.Height;
                int row = Math.Max(0, GetGridRow(child));
                int col = Math.Max(0, GetGridCol(child));
                if (row > maxRow) maxRow = row;
                if (col > maxCol) maxCol = col;
            }

            CellHeight = maxChildHeight > 0 ? maxChildHeight : cw;
            ExtentColumns = Math.Max(1, maxCol + 1);

            double visibleWidth = VisibleWidth(constraint);
            Columns = visibleWidth > 0 ? GridLayout.ColumnsForWidth(visibleWidth, cw) : 1;

            // True extent: the occupied grid, never smaller than what is visible so the
            // panel keeps filling the frame (drops on empty space still hit us).
            double extentWidth = Math.Max(visibleWidth, ExtentColumns * cw);
            double extentHeight = GridLayout.ExtentHeight(maxRow, CellHeight);
            return new Size(extentWidth, extentHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (!FreeArrange) return base.ArrangeOverride(finalSize);

            double cw = _cellWidth > 0 ? _cellWidth : 80;
            double ch = CellHeight > 0 ? CellHeight : cw;

            foreach (UIElement child in InternalChildren)
            {
                if (child == null) continue;
                // Persisted cell, verbatim: a frame too narrow for it clips, never reflows.
                int col = Math.Max(0, GetGridCol(child));
                int row = Math.Max(0, GetGridRow(child));
                child.Arrange(new Rect(col * cw, row * ch, cw, ch));
            }

            return finalSize;
        }
    }
}

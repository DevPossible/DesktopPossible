using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Desktop_Frames
{
    /// <summary>
    /// Free-arrange frames with scrollbars turned off (Options > "Disable frame scrollbars"):
    /// draws a small arrow at each edge where content is clipped, and lets the user pan
    /// the frame surface by dragging empty space (an icon drag is still an icon drag).
    /// Nothing is drawn or captured while scrollbars are visible or in flow mode — the
    /// ScrollViewer's own bars do the job there.
    /// </summary>
    public static class GridOverflowNavigator
    {
        private sealed class PanState
        {
            public Point Start;
            public double StartH, StartV;
            public bool Pressed, Panning;
        }

        /// <summary>Wires the arrows adorner and the pan gesture onto a frame's icon ScrollViewer.</summary>
        public static void Attach(ScrollViewer sv)
        {
            if (sv == null) return;
            var state = new PanState();
            OverflowArrowsAdorner? adorner = null;

            void EnsureAdorner()
            {
                if (adorner != null) return;
                var layer = AdornerLayer.GetAdornerLayer(sv);
                if (layer == null) return;
                adorner = new OverflowArrowsAdorner(sv);
                layer.Add(adorner);
            }

            sv.Loaded += (s, e) => EnsureAdorner();
            if (sv.IsLoaded) EnsureAdorner();
            sv.ScrollChanged += (s, e) => adorner?.InvalidateVisual();
            sv.SizeChanged += (s, e) => adorner?.InvalidateVisual();

            // Empty frame surface must be hit-testable or a press there falls through to the
            // frame border and never reaches us. Portal frames already paint a watermark.
            sv.Background ??= Brushes.Transparent;

            // handledEventsToo: an icon/child marking the press handled must not starve the pan.
            sv.AddHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) =>
            {
                if (!IsPanMode(sv) || !HasOverflow(sv) || IsOnIconOrControl(e.OriginalSource as DependencyObject, sv)) return;
                state.Start = e.GetPosition(sv);
                state.StartH = sv.HorizontalOffset;
                state.StartV = sv.VerticalOffset;
                state.Pressed = true;
                state.Panning = false;
            }), true);

            sv.AddHandler(UIElement.MouseMoveEvent, new MouseEventHandler((s, e) =>
            {
                if (!state.Pressed || e.LeftButton != MouseButtonState.Pressed) { state.Pressed = state.Panning = false; return; }
                Point p = e.GetPosition(sv);
                double dx = p.X - state.Start.X, dy = p.Y - state.Start.Y;
                if (!state.Panning)
                {
                    if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance &&
                        Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance) return;
                    state.Panning = true;
                    sv.CaptureMouse();
                    sv.Cursor = Cursors.SizeAll;
                }
                sv.ScrollToHorizontalOffset(state.StartH - dx);
                sv.ScrollToVerticalOffset(state.StartV - dy);
                e.Handled = true;
            }), true);

            sv.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler((s, e) =>
            {
                bool wasPanning = state.Panning;
                state.Pressed = state.Panning = false;
                if (!wasPanning) return;
                if (sv.IsMouseCaptured) sv.ReleaseMouseCapture();
                sv.Cursor = null;
                e.Handled = true; // a pan is not a click on the surface
            }), true);

            sv.LostMouseCapture += (s, e) => { state.Pressed = state.Panning = false; sv.Cursor = null; };
        }

        /// <summary>Free-arrange content with the scrollbars hidden — the only case we handle.</summary>
        public static bool IsPanMode(ScrollViewer sv)
            => sv?.Content is FreeGridPanel panel && panel.FreeArrange
               && sv.VerticalScrollBarVisibility == ScrollBarVisibility.Hidden;

        private static bool HasOverflow(ScrollViewer sv)
            => sv.ExtentWidth > sv.ViewportWidth + 0.5 || sv.ExtentHeight > sv.ViewportHeight + 0.5;

        /// <summary>True when the press landed on an icon (tagged StackPanel) or an interactive control.</summary>
        private static bool IsOnIconOrControl(DependencyObject? node, ScrollViewer sv)
        {
            while (node != null && node != sv)
            {
                if (node is StackPanel sp && sp.Tag != null) return true;
                if (node is ButtonBase || node is ScrollBar || node is TextBox) return true;
                node = node is FrameworkContentElement fce ? fce.Parent : VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        /// <summary>Paints one arrow per clipped edge over the ScrollViewer; never hit-testable.</summary>
        private sealed class OverflowArrowsAdorner : Adorner
        {
            private readonly ScrollViewer _sv;
            private static readonly Brush Fill = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255));
            private static readonly Pen Outline = new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 1);

            public OverflowArrowsAdorner(ScrollViewer sv) : base(sv)
            {
                _sv = sv;
                IsHitTestVisible = false;
                Fill.Freeze();
                Outline.Freeze();
            }

            protected override void OnRender(DrawingContext dc)
            {
                if (!IsPanMode(_sv)) return;
                double w = _sv.ActualWidth, h = _sv.ActualHeight;
                if (w <= 0 || h <= 0) return;

                const double size = 7, inset = 6;
                if (_sv.HorizontalOffset > 0.5) DrawArrow(dc, new Point(inset, h / 2), new Vector(-1, 0), size);
                if (_sv.HorizontalOffset + _sv.ViewportWidth < _sv.ExtentWidth - 0.5) DrawArrow(dc, new Point(w - inset, h / 2), new Vector(1, 0), size);
                if (_sv.VerticalOffset > 0.5) DrawArrow(dc, new Point(w / 2, inset), new Vector(0, -1), size);
                if (_sv.VerticalOffset + _sv.ViewportHeight < _sv.ExtentHeight - 0.5) DrawArrow(dc, new Point(w / 2, h - inset), new Vector(0, 1), size);
            }

            /// <summary>Isosceles triangle with its tip at <paramref name="tip"/> pointing along <paramref name="dir"/>.</summary>
            private static void DrawArrow(DrawingContext dc, Point tip, Vector dir, double size)
            {
                Vector back = -dir * size;
                Vector side = new Vector(-dir.Y, dir.X) * (size * 0.8);
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(tip, true, true);
                    ctx.LineTo(tip + back + side, true, false);
                    ctx.LineTo(tip + back - side, true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(Fill, Outline, geo);
            }
        }
    }
}

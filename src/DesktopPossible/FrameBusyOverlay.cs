using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Desktop_Frames
{
    /// <summary>
    /// A dimming "busy" overlay with a short message (e.g. "Migrating…") drawn over a frame
    /// window while a background operation works on its items. Drawn as an adorner on the
    /// frame's content, so it needs no change to how frames are built; a frame refresh
    /// rebuilds the content and so drops the overlay too. UI thread only.
    /// </summary>
    public static class FrameBusyOverlay
    {
        private static readonly Dictionary<string, (AdornerLayer Layer, Adorner Adorner)> _shown = new();

        public static void Show(string frameId, string message)
        {
            Hide(frameId);
            var win = Application.Current?.Windows.OfType<NonActivatingWindow>().FirstOrDefault(w => w.Tag?.ToString() == frameId);
            if (win?.Content is not UIElement content) return;
            var layer = AdornerLayer.GetAdornerLayer(content);
            if (layer == null) return;
            var adorner = new BusyAdorner(content, message);
            layer.Add(adorner);
            _shown[frameId] = (layer, adorner);
        }

        public static void Hide(string frameId)
        {
            if (!_shown.Remove(frameId, out var entry)) return;
            try { entry.Layer.Remove(entry.Adorner); } catch { }
        }

        public static void HideAll()
        {
            foreach (string id in _shown.Keys.ToList()) Hide(id);
        }

        private sealed class BusyAdorner : Adorner
        {
            private readonly string _message;

            public BusyAdorner(UIElement adorned, string message) : base(adorned)
            {
                _message = message;
                IsHitTestVisible = true; // block clicks on items that are being moved
            }

            protected override void OnRender(DrawingContext dc)
            {
                var size = AdornedElement.RenderSize;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null,
                    new Rect(size), 6, 6);

                var text = new FormattedText(_message, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 14, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new Point((size.Width - text.Width) / 2, (size.Height - text.Height) / 2));
            }
        }
    }
}

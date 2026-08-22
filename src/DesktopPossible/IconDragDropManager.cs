using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// Manages drag and drop operations for icon reordering within Data frames.
    /// Updated to be TAB-AWARE (supports reordering inside specific tabs).
    /// </summary>
    public static class IconDragDropManager
    {
        #region Win32 API for cursor position
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
        #endregion

        #region Private Fields
        // Drag and drop state management for icon reordering
        private static bool _isDragging = false;
        private static StackPanel _draggedIcon = null;
        private static System.Windows.Point _dragStartPoint;

        private static dynamic _draggedItem = null;
        private static dynamic _sourceFrame = null;
        private static JArray _sourceItemsList = null; // FIX: The specific list we are editing (Main or Tab)

        private static WrapPanel _sourceWrapPanel = null;
        private static Window _dragPreviewWindow = null;
        // DPI scale of the source window, captured at drag start; used to convert the
        // physical-pixel cursor position into DIPs for the preview window's Left/Top.
        private static double _dragPreviewDpiScale = 1.0;
        private static System.Windows.Point _lastDropIndicatorPosition = new System.Windows.Point(-1, -1);
        private static int _lastDropIndicatorIndex = -1;

        // Free-arrange drag state: live displacement preview + ghost drop preview.
        private static (int Col, int Row) _lastPreviewCell = (-1, -1);
        private static Border _ghostPreview = null;              // ghost of the dragged icon at the target cell
        private static ImageSource _dragSnapshot = null;         // pixel-perfect snapshot of the dragged icon
        private static double _draggedIconOpacity = 1.0;         // original opacity of the dragged icon

        // OLE drag-out state: set while a shell drag (DoDragDrop) started from a frame is running.
        private static string _oleDragSourceFrameId = null;

        // "+" copy badge on the drag preview — visible while Ctrl is held (clone drop).
        private static Border _dragCopyBadge = null;
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets whether a drag operation is currently in progress
        /// </summary>
        public static bool IsDragging => _isDragging;

        /// <summary>True while an OLE (shell) drag that started from one of our frames is running.</summary>
        public static bool IsOleDragInProgress => _oleDragSourceFrameId != null;

        /// <summary>True when the running OLE drag started from the given frame (self-drop detection).</summary>
        public static bool IsOleDragFromFrame(string frameId) =>
            _oleDragSourceFrameId != null && _oleDragSourceFrameId == frameId;
        #endregion

        #region Public Methods
        /// <summary>
        /// Starts a drag operation for icon reordering
        /// </summary>
        /// <param name="iconStackPanel">The icon being dragged</param>
        /// <param name="startPoint">The starting point of the drag</param>
        public static void StartIconDrag(StackPanel iconStackPanel, System.Windows.Point startPoint)
        {
            try
            {
                // Only allow dragging in Data frames, not Portal frames
                NonActivatingWindow parentWindow = FindVisualParent<NonActivatingWindow>(iconStackPanel);
                if (parentWindow == null) return;

                string frameId = parentWindow.Tag?.ToString();
                if (string.IsNullOrEmpty(frameId)) return;

                var FrameData = Framemanager.GetFrameData();
                dynamic frame = FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                if (frame == null || frame.ItemsType?.ToString() != "Data") return;

                // Find the WrapPanel containing the icons
                WrapPanel wrapPanel = FindWrapPanel(parentWindow);
                if (wrapPanel == null) return;

                // Get the dragged item data from the icon's Tag
                var tagData = iconStackPanel.Tag;
                if (tagData == null) return;

                string filePath = tagData.GetType().GetProperty("FilePath")?.GetValue(tagData)?.ToString();
                if (string.IsNullOrEmpty(filePath)) return;

                // --- FIX: TAB-AWARE LIST SELECTION ---
                // Determine which JArray we are modifying (Main Items vs Active Tab Items)
                JArray targetList = null;
                bool tabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";

                if (tabsEnabled)
                {
                    var tabs = frame.Tabs as JArray;
                    int currentTabIndex = Convert.ToInt32(frame.CurrentTab?.ToString() ?? "0");

                    if (tabs != null && currentTabIndex >= 0 && currentTabIndex < tabs.Count)
                    {
                        var activeTab = tabs[currentTabIndex] as JObject;
                        targetList = activeTab?["Items"] as JArray;
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Drag started in Tab {currentTabIndex}");
                    }
                }

                // Fallback to Main Items if tabs disabled or invalid
                if (targetList == null)
                {
                    targetList = frame.Items as JArray;
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Drag started in Main Items");
                }

                if (targetList == null) return;

                // Find the specific item in the specific list
                dynamic draggedItem = null;
                foreach (var item in targetList)
                {
                    if (item["Filename"]?.ToString() == filePath)
                    {
                        draggedItem = item;
                        break;
                    }
                }

                if (draggedItem == null)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"Cannot start drag: Item {filePath} not found in the active list.");
                    return;
                }

                // Set drag state
                _isDragging = true;
                _draggedIcon = iconStackPanel;
                _dragStartPoint = startPoint;
                _draggedItem = draggedItem;
                _sourceFrame = frame;
                _sourceItemsList = targetList; // Store the specific list reference!
                _sourceWrapPanel = wrapPanel;

                // Capture mouse
                iconStackPanel.CaptureMouse();

                // Focus parent for Key events (Escape)
                if (parentWindow.Focusable) parentWindow.Focus();

                // Create visual drag preview (snapshots the real icon BEFORE we hide it)
                CreateDragPreview(iconStackPanel);

                // FREE ARRANGE: visually vacate the origin cell — the cursor-following
                // preview and the ghost at the target cell represent the icon while dragging,
                // and displaced icons may be previewed into the origin. (Opacity keeps the
                // mouse capture alive; Visibility would drop it.)
                if (wrapPanel is FreeGridPanel sourceFreePanel && sourceFreePanel.FreeArrange)
                {
                    _draggedIconOpacity = iconStackPanel.Opacity;
                    iconStackPanel.Opacity = 0.0;
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Started drag for {filePath}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error starting drag: {ex.Message}");
                CancelDrag();
            }
        }

        /// <summary>
        /// Cancels the current drag operation
        /// </summary>
        public static void CancelDrag()
        {
            try
            {
                if (_isDragging)
                {
                    if (_draggedIcon != null) _draggedIcon.ReleaseMouseCapture();

                    if (_dragPreviewWindow != null)
                    {
                        _dragPreviewWindow.Close();
                        _dragPreviewWindow = null;
                    }

                    if (_sourceWrapPanel != null) RemoveDropZoneIndicators(_sourceWrapPanel);

                    // FREE ARRANGE: revert any live displacement preview to the persisted
                    // layout (the JSON is only written on commit, so re-reading it restores
                    // the pre-drag positions) and un-hide the dragged icon.
                    if (_sourceWrapPanel is FreeGridPanel freePanel && freePanel.FreeArrange)
                    {
                        try
                        {
                            if (_draggedIcon != null) _draggedIcon.Opacity = _draggedIconOpacity;
                            RestoreAttachedCellsFromData(freePanel);
                        }
                        catch { }
                    }

                    _isDragging = false;
                    _draggedIcon = null;
                    _draggedItem = null;
                    _sourceFrame = null;
                    _sourceItemsList = null;
                    _sourceWrapPanel = null;
                    _lastDropIndicatorPosition = new System.Windows.Point(-1, -1);
                    _lastPreviewCell = (-1, -1);
                    _ghostPreview = null;
                    _dragSnapshot = null;
                    _dragCopyBadge = null;
                    _draggedIconOpacity = 1.0;
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error cancelling drag: {ex.Message}");
                // Force reset state
                _isDragging = false;
            }
        }

        /// <summary>
        /// Handles mouse move during drag operation
        /// </summary>
        public static void HandleDragMove(System.Windows.Point screenPosition)
        {
            if (!_isDragging || _draggedIcon == null) return;

            try
            {
                UpdateDragPreviewPosition(screenPosition);

                // Live "+" badge: Ctrl held = this drop will CLONE the icon.
                if (_dragCopyBadge != null)
                {
                    _dragCopyBadge.Visibility =
                        System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                }

                // OLE ESCALATION: once the cursor leaves the source frame's bounds the
                // internal move becomes a real shell drag (Explorer handles desktop drops
                // natively; other frames receive it via their existing FileDrop handler).
                // Works from both layout modes.
                if (_sourceWrapPanel != null)
                {
                    NonActivatingWindow sourceWindow = FindVisualParent<NonActivatingWindow>(_sourceWrapPanel);
                    if (sourceWindow != null)
                    {
                        System.Windows.Point windowPoint = sourceWindow.PointFromScreen(screenPosition);
                        if (windowPoint.X < 0 || windowPoint.Y < 0 ||
                            windowPoint.X > sourceWindow.ActualWidth || windowPoint.Y > sourceWindow.ActualHeight)
                        {
                            EscalateToOleDrag(sourceWindow);
                            return;
                        }
                    }
                }

                if (_sourceWrapPanel != null)
                {
                    System.Windows.Point wrapPanelPosition = _sourceWrapPanel.PointFromScreen(screenPosition);
                    ShowDropZoneIndicators(_sourceWrapPanel, wrapPanelPosition);
                }
            }
            catch { }
        }

        /// <summary>
        /// Completes the drag operation and performs reordering
        /// </summary>
        public static void CompleteDrag(System.Windows.Point finalPosition)
        {
            if (!_isDragging || _draggedIcon == null || _sourceWrapPanel == null) return;

            try
            {
                // FREE ARRANGE: commit the previewed layout (dragged item at the hover cell,
                // occupants chain-pushed row-major) instead of reordering the flow list.
                if (_sourceWrapPanel is FreeGridPanel freePanel && freePanel.FreeArrange)
                {
                    CommitFreeDrag(freePanel, finalPosition);
                    CancelDrag(); // cleanup; on an outside-drop this reverts to the pre-drag layout
                    return;
                }

                // Calculate where to drop the item relative to the panel
                int dropPosition = CalculateDropPosition(_sourceWrapPanel, finalPosition);

                // Perform the reordering on the specific list
                ReorderframeItems(dropPosition);

                CancelDrag();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error completing drag: {ex.Message}");
                CancelDrag();
            }
        }
        #endregion

        #region OLE Drag-Out (frame -> desktop / other frames)

        /// <summary>
        /// Escalates the internal in-frame drag into a real OLE drag-drop so the item can
        /// leave the frame: cancels the internal preview (reverting any displaced icons),
        /// then hands the item's backing file (the .lnk/.url or real file in the frame's
        /// store folder, or a legacy path) to the shell via DoDragDrop with FileDrop data.
        /// On Move the drop target (Explorer or another frame) has already taken the file,
        /// so the source only removes its item entry — it never deletes the file itself.
        ///
        /// Effect semantics: Move (default) = the drop target took the item (Explorer moved
        /// the file to the desktop, or another frame added it) -> the source frame removes
        /// its item and persists. Copy (Ctrl held) or None (cancelled / dropped back on the
        /// source frame) -> the source item stays.
        /// </summary>
        private static void EscalateToOleDrag(NonActivatingWindow sourceWindow)
        {
            string frameId = null;
            string itemKey = null;
            StackPanel dragSource = _draggedIcon;

            try
            {
                try { frameId = _sourceFrame?.Id?.ToString(); } catch { }
                itemKey = DraggedKey();
                if (string.IsNullOrEmpty(itemKey)) { CancelDrag(); return; }

                // Spacers and items without an existing backing file cannot leave the frame;
                // keep the internal drag alive instead of escalating.
                if (itemKey.StartsWith("INTERNAL_BLANK_")) return;

                string absolutePath = null;
                try { absolutePath = System.IO.Path.GetFullPath(itemKey); } catch { }
                if (absolutePath == null ||
                    (!System.IO.File.Exists(absolutePath) && !System.IO.Directory.Exists(absolutePath)))
                {
                    return;
                }

                // End the internal drag first: closes the cursor preview, reverts the
                // free-arrange displacement preview, restores the icon, releases capture.
                CancelDrag();

                _oleDragSourceFrameId = frameId;
                DragDropEffects result = DragDropEffects.None;
                try
                {
                    var data = new DataObject(DataFormats.FileDrop, new string[] { absolutePath });
                    result = DragDrop.DoDragDrop(
                        (DependencyObject)dragSource ?? sourceWindow,
                        data,
                        DragDropEffects.Move | DragDropEffects.Copy);
                }
                finally
                {
                    _oleDragSourceFrameId = null;
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"OLE drag-out of '{itemKey}' finished with effect {result}");

                if ((result & DragDropEffects.Move) != 0)
                {
                    RemoveItemFromSourceFrame(frameId, itemKey);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error escalating to OLE drag: {ex.Message}");
                _oleDragSourceFrameId = null;
                CancelDrag();
            }
        }

        /// <summary>
        /// Removes the dragged-out item from its source frame after a completed MOVE.
        /// Mirrors the icon context menu's Remove path: re-resolve the frame by Id (never
        /// a captured reference — those go stale), find the tabs-aware item list, remove
        /// by Filename, persist, refresh.
        /// </summary>
        private static void RemoveItemFromSourceFrame(string frameId, string itemKey)
        {
            try
            {
                if (string.IsNullOrEmpty(frameId) || string.IsNullOrEmpty(itemKey)) return;

                dynamic frame = Framemanager.GetFrameData().FirstOrDefault(f => f.Id?.ToString() == frameId);
                if (frame == null) return;

                JArray targetArray = frame.Items as JArray;
                bool tabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";
                if (tabsEnabled)
                {
                    JArray tabs = frame.Tabs as JArray;
                    int tabIdx = Convert.ToInt32(frame.CurrentTab?.ToString() ?? "0");
                    if (tabs != null && tabIdx >= 0 && tabIdx < tabs.Count)
                        targetArray = tabs[tabIdx]["Items"] as JArray;
                }
                if (targetArray == null) return;

                var itemToRemove = targetArray.OfType<JObject>()
                    .FirstOrDefault(i => i["Filename"]?.ToString() == itemKey);
                if (itemToRemove == null) return;

                targetArray.Remove(itemToRemove);
                FrameDataManager.SaveFrameData();

                var win = Application.Current?.Windows.OfType<NonActivatingWindow>()
                    .FirstOrDefault(w => w.Tag?.ToString() == frameId);
                if (win != null) Framemanager.RefreshFrameUsingFormApproach(win, frame);

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Removed '{itemKey}' from frame after OLE move-out");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error removing item after OLE move: {ex.Message}");
            }
        }

        #endregion

        #region Free Arrange (live displacement preview + commit)

        /// <summary>Filename key of the item being dragged.</summary>
        private static string DraggedKey()
        {
            try { return (string)_draggedItem?["Filename"]?.ToString(); }
            catch { return null; }
        }

        /// <summary>Current (persisted) placements of every item except the dragged one.</summary>
        private static List<(string Key, int Col, int Row)> CollectOtherPlacements()
        {
            var others = new List<(string Key, int Col, int Row)>();
            string draggedKey = DraggedKey();
            if (_sourceItemsList == null) return others;

            foreach (var token in _sourceItemsList)
            {
                if (!(token is JObject item)) continue;
                string key = item["Filename"]?.ToString();
                if (string.IsNullOrEmpty(key) || key == draggedKey) continue;
                if (GridLayout.TryGetCell(item, out var cell)) others.Add((key, cell.Col, cell.Row));
            }
            return others;
        }

        /// <summary>
        /// Live drag feedback in free-arrange mode: computes the would-be layout for the
        /// hovered cell (GridLayout.PreviewDisplacement) and applies it to the panel's
        /// attached cell properties only — the JSON stays untouched until commit, so a
        /// cancel simply re-reads it. Also shows a ghost of the dragged icon at the target
        /// cell. Icons displaced by the hover visibly shift, and shift back when the cursor
        /// moves elsewhere.
        /// </summary>
        private static void UpdateFreeDragPreview(FreeGridPanel panel, System.Windows.Point mousePosition)
        {
            try
            {
                if (_sourceItemsList == null || _draggedItem == null) return;

                double cellWidth = panel.CellWidth;
                double cellHeight = panel.CellHeight;
                if (cellWidth <= 0 || cellHeight <= 0) return;

                int columns = Math.Max(1, panel.DropColumns);
                var hover = GridLayout.CellFromPoint(mousePosition.X, mousePosition.Y, cellWidth, cellHeight, columns);
                if (hover == _lastPreviewCell) return; // same cell — preview already applied
                _lastPreviewCell = hover;

                var map = GridLayout.PreviewDisplacement(CollectOtherPlacements(), DraggedKey(), hover, columns);
                ApplyPreviewToPanel(panel, map);
                ShowGhostAt(panel, hover);
            }
            catch { }
        }

        /// <summary>Moves the visuals (attached cell props only) to the previewed layout.</summary>
        private static void ApplyPreviewToPanel(FreeGridPanel panel, Dictionary<string, (int Col, int Row)> map)
        {
            foreach (var child in panel.Children.OfType<StackPanel>())
            {
                if (ReferenceEquals(child, _draggedIcon)) continue;
                var tagData = child.Tag;
                string key = tagData?.GetType().GetProperty("FilePath")?.GetValue(tagData)?.ToString();
                if (key != null && map.TryGetValue(key, out var cell))
                {
                    FreeGridPanel.SetGridCol(child, cell.Col);
                    FreeGridPanel.SetGridRow(child, cell.Row);
                }
            }
        }

        /// <summary>
        /// Ghost drop preview: a dimmed, outlined copy of the ACTUAL dragged icon at the
        /// cell it would land in, moved live as the cursor changes cells.
        /// </summary>
        private static void ShowGhostAt(FreeGridPanel panel, (int Col, int Row) cell)
        {
            if (_ghostPreview == null || !panel.Children.Contains(_ghostPreview))
            {
                RemoveDropZoneIndicators(panel);
                _ghostPreview = new Border
                {
                    Tag = "DropIndicator", // reuses the existing indicator cleanup
                    IsHitTestVisible = false,
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 150, 255)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(28, 0, 150, 255))
                };

                if (_dragSnapshot != null && _draggedIcon != null)
                {
                    _ghostPreview.Child = new System.Windows.Controls.Image
                    {
                        Source = _dragSnapshot,
                        Width = Math.Max(1, _draggedIcon.ActualWidth),
                        Height = Math.Max(1, _draggedIcon.ActualHeight),
                        Opacity = 0.5,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Top
                    };
                }
                panel.Children.Add(_ghostPreview);
            }

            FreeGridPanel.SetGridCol(_ghostPreview, cell.Col);
            FreeGridPanel.SetGridRow(_ghostPreview, cell.Row);
        }

        /// <summary>
        /// Free-arrange drop: persists the previewed layout — the dragged item at the hover
        /// cell plus every chain-pushed occupant — in one save. Dropping outside the panel
        /// (left/right/above) commits nothing; the caller's CancelDrag reverts the preview.
        /// Dropping below the last row is valid (the grid grows downward).
        /// </summary>
        private static void CommitFreeDrag(FreeGridPanel panel, System.Windows.Point dropPosition)
        {
            try
            {
                if (_sourceItemsList == null || _draggedItem == null) return;

                double cellWidth = panel.CellWidth;
                double cellHeight = panel.CellHeight;
                if (cellWidth <= 0 || cellHeight <= 0) return;

                if (dropPosition.X < 0 || dropPosition.X > panel.ActualWidth || dropPosition.Y < 0)
                {
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                        "Free-arrange drop outside the panel — reverting to pre-drag layout");
                    return;
                }

                int columns = Math.Max(1, panel.DropColumns);
                var hover = GridLayout.CellFromPoint(dropPosition.X, dropPosition.Y, cellWidth, cellHeight, columns);

                // Ctrl at release = QUICK CLONE: the original stays at its cell and an
                // independent copy (own shortcut file) lands at the hover cell. Falls
                // back to a normal move when the item can't be cloned (raw-path items).
                if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
                {
                    RestoreAttachedCellsFromData(panel); // revert the displacement preview
                    if (Framemanager.CloneItemInFrame(_sourceFrame, _draggedItem as JObject, _sourceItemsList, hover.Col, hover.Row))
                    {
                        RefreshFrameUI();
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                            $"Ctrl-drop clone at cell ({hover.Col},{hover.Row})");
                        return;
                    }
                    // clone refused -> fall through to the normal move commit
                }

                var map = GridLayout.PreviewDisplacement(CollectOtherPlacements(), DraggedKey(), hover, columns);

                foreach (var token in _sourceItemsList)
                {
                    if (!(token is JObject item)) continue;
                    string key = item["Filename"]?.ToString();
                    if (key != null && map.TryGetValue(key, out var cell))
                    {
                        item[GridLayout.ColKey] = cell.Col;
                        item[GridLayout.RowKey] = cell.Row;
                    }
                }

                FrameDataManager.SaveFrameData();
                RefreshFrameUI();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Free-arrange drop committed at cell ({hover.Col},{hover.Row})");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error committing free-arrange drop: {ex.Message}");
            }
        }

        /// <summary>
        /// Reverts the visuals to the persisted layout (used on cancel — the JSON is only
        /// ever written by CommitFreeDrag, so it still holds the pre-drag positions).
        /// </summary>
        private static void RestoreAttachedCellsFromData(FreeGridPanel panel)
        {
            if (panel == null || _sourceItemsList == null) return;

            var cellsByKey = new Dictionary<string, (int Col, int Row)>();
            foreach (var token in _sourceItemsList)
            {
                if (token is JObject item &&
                    item["Filename"]?.ToString() is string key &&
                    GridLayout.TryGetCell(item, out var cell))
                {
                    cellsByKey[key] = cell;
                }
            }

            foreach (var child in panel.Children.OfType<StackPanel>())
            {
                var tagData = child.Tag;
                string key = tagData?.GetType().GetProperty("FilePath")?.GetValue(tagData)?.ToString();
                if (key != null && cellsByKey.TryGetValue(key, out var cell))
                {
                    FreeGridPanel.SetGridCol(child, cell.Col);
                    FreeGridPanel.SetGridRow(child, cell.Row);
                }
            }
        }

        #endregion

        #region Reordering Logic (The Core Fix)

        private static int CalculateDropPosition(WrapPanel wrapPanel, System.Windows.Point mousePosition)
        {
            try
            {
                if (wrapPanel == null || _draggedIcon == null) return 0;

                var iconPanels = wrapPanel.Children.OfType<StackPanel>().Where(sp => sp != _draggedIcon).ToList();
                if (iconPanels.Count == 0) return 0;

                double closestDistance = double.MaxValue;
                int bestInsertIndex = 0;

                // We assume items in the WrapPanel match the order in _sourceItemsList
                // But we must be careful if the visual list and data list are out of sync.
                // Best bet is to find the index of the closest icon in the source list.

                for (int i = 0; i < iconPanels.Count; i++)
                {
                    var iconPanel = iconPanels[i];
                    try
                    {
                        var iconPosition = iconPanel.TranslatePoint(new System.Windows.Point(0, 0), wrapPanel);
                        var iconCenter = new System.Windows.Point(
                            iconPosition.X + iconPanel.ActualWidth / 2,
                            iconPosition.Y + iconPanel.ActualHeight / 2
                        );

                        double distance = Math.Sqrt(Math.Pow(mousePosition.X - iconCenter.X, 2) + Math.Pow(mousePosition.Y - iconCenter.Y, 2));

                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            bool insertBefore = mousePosition.X < iconCenter.X;

                            // Find this visual icon's corresponding data index
                            var tagData = iconPanel.Tag;
                            string filePath = tagData?.GetType().GetProperty("FilePath")?.GetValue(tagData)?.ToString();

                            int dataIndex = -1;
                            if (_sourceItemsList != null && !string.IsNullOrEmpty(filePath))
                            {
                                for (int k = 0; k < _sourceItemsList.Count; k++)
                                {
                                    if (_sourceItemsList[k]["Filename"]?.ToString() == filePath)
                                    {
                                        dataIndex = k;
                                        break;
                                    }
                                }
                            }

                            if (dataIndex != -1)
                            {
                                bestInsertIndex = insertBefore ? dataIndex : dataIndex + 1;
                            }
                            else
                            {
                                // Fallback: Use visual index if data match fails
                                bestInsertIndex = insertBefore ? i : i + 1;
                            }
                        }
                    }
                    catch (Exception panelEx)
                    {
                        // Log (don't silently skip): a failing panel measurement quietly skews
                        // the computed drop index.
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                            $"CalculateDropPosition: skipping unmeasurable icon panel: {panelEx.Message}");
                    }
                }

                // Bounds Check
                int maxCount = _sourceItemsList?.Count ?? 0;
                return Math.Max(0, Math.Min(bestInsertIndex, maxCount));
            }
            catch (Exception ex)
            {
                // Log (don't swallow): a silent index-0 result reorders the item to the front
                // with no trace of why.
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                    $"CalculateDropPosition failed; defaulting to drop index 0: {ex.Message}");
                return 0;
            }
        }

        private static void ReorderframeItems(int newPosition)
        {
            try
            {
                // FIX: Use _sourceItemsList instead of _sourceFrame.Items
                if (_sourceItemsList == null || _draggedItem == null) return;

                int currentPosition = -1;
                for (int i = 0; i < _sourceItemsList.Count; i++)
                {
                    if (_sourceItemsList[i]["Filename"]?.ToString() == _draggedItem["Filename"]?.ToString())
                    {
                        currentPosition = i;
                        break;
                    }
                }

                if (currentPosition == -1) return;
                if (currentPosition == newPosition || (currentPosition + 1 == newPosition)) return;

                // Move logic
                var itemToMove = _sourceItemsList[currentPosition];
                _sourceItemsList.RemoveAt(currentPosition);

                int adjustedPosition = newPosition;
                if (currentPosition < newPosition) adjustedPosition--;

                adjustedPosition = Math.Max(0, Math.Min(adjustedPosition, _sourceItemsList.Count));
                _sourceItemsList.Insert(adjustedPosition, itemToMove);

                // Update DisplayOrder
                for (int i = 0; i < _sourceItemsList.Count; i++)
                {
                    _sourceItemsList[i]["DisplayOrder"] = i;
                }

                FrameDataManager.SaveFrameData();

                // Refresh UI
                RefreshFrameUI();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error reordering items: {ex.Message}");
            }
        }

        private static void RefreshFrameUI()
        {
            try
            {
                if (_sourceFrame == null || _sourceWrapPanel == null) return;

                NonActivatingWindow parentWindow = FindVisualParent<NonActivatingWindow>(_sourceWrapPanel);
                if (parentWindow == null) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // FIX: Delegate to Framemanager's robust refresh logic
                    // This handles Tabs vs Main logic automatically
                    Framemanager.RefreshFrameUsingFormApproach(parentWindow, _sourceFrame);
                });
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error refreshing frame UI: {ex.Message}");
            }
        }

        #endregion

        #region Private Helper Methods (Visuals & Utils)

        // ... (Standard FindVisualParent, FindWrapPanel, GetCursorPos, DragPreview logic remains same) ...
        // Included for completeness to ensure the file compiles without missing refs

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as T;
        }

        private static WrapPanel FindWrapPanel(DependencyObject parent, int depth = 0, int maxDepth = 10)
        {
            if (parent == null || depth > maxDepth) return null;
            if (parent is WrapPanel wrapPanel) return wrapPanel;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var result = FindWrapPanel(VisualTreeHelper.GetChild(parent, i), depth + 1, maxDepth);
                if (result != null) return result;
            }
            return null;
        }

        private static System.Windows.Point GetCursorPosition()
        {
            POINT point;
            GetCursorPos(out point);
            return new System.Windows.Point(point.X, point.Y);
        }

        private static double GetDpiScaleFactor(Window window)
        {
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)window.Left, (int)window.Top));
            using (var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
            {
                return graphics.DpiX / 96.0;
            }
        }

        /// <summary>
        /// Pixel-perfect snapshot of the icon exactly as rendered on screen (icon image,
        /// overlays, label, effects). Rendered at 2x for crispness while scaling.
        /// </summary>
        private static ImageSource SnapshotIcon(FrameworkElement icon)
        {
            try
            {
                double width = icon?.ActualWidth ?? 0;
                double height = icon?.ActualHeight ?? 0;
                if (width < 1 || height < 1) return null;

                // VisualBrush avoids the layout-offset artifact of rendering the element directly.
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(new VisualBrush(icon), null, new Rect(0, 0, width, height));
                }

                var bitmap = new RenderTargetBitmap(
                    (int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2),
                    192, 192, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        #region External drop preview (OLE drags from outside the app onto a free-arrange frame)

        private static Border _externalGhost;

        /// <summary>
        /// The cell an external FileDrop would land in at the given panel point:
        /// the hovered cell when free, otherwise the first free cell (row-major) —
        /// mirroring PlaceItemInFreeGrid's placement so the ghost never lies.
        /// </summary>
        public static (int Col, int Row) GetExternalDropCell(FreeGridPanel panel, System.Windows.Point panelPoint)
        {
            var occupied = new HashSet<(int, int)>();
            foreach (UIElement child in panel.Children)
            {
                if (child is StackPanel sp)
                {
                    int c = FreeGridPanel.GetGridCol(sp), r = FreeGridPanel.GetGridRow(sp);
                    if (c >= 0 && r >= 0) occupied.Add((c, r));
                }
            }

            int columns = Math.Max(1, panel.DropColumns);
            // Defensive cell height: never trust a degenerate measured value (an empty
            // frame's metrics) — a tiny divisor turns a top-of-frame drop into row 40.
            double cellHeight = panel.CellHeight > 8 ? panel.CellHeight : panel.CellWidth;
            var hover = GridLayout.CellFromPoint(panelPoint.X, panelPoint.Y, panel.CellWidth, cellHeight, columns);
            if (!occupied.Contains(hover)) return hover;

            for (int idx = 0; ; idx++)
            {
                var cell = (idx % columns, idx / columns);
                if (!occupied.Contains(cell)) return cell;
            }
        }

        /// <summary>Shows/moves the dashed landing-cell ghost during an external drag-over.</summary>
        public static void ShowExternalDropPreview(FreeGridPanel panel, System.Windows.Point panelPoint)
        {
            try
            {
                if (panel == null || !panel.FreeArrange) return;

                var cell = GetExternalDropCell(panel, panelPoint);
                if (_externalGhost == null || !ReferenceEquals(_externalGhost.Parent, panel))
                {
                    ClearExternalDropPreview();
                    _externalGhost = new Border
                    {
                        // Explicit cell-sized dimensions: the ghost must never influence
                        // (or depend on) the panel's measured cell metrics.
                        Width = panel.CellWidth,
                        Height = panel.CellHeight > 8 ? panel.CellHeight : panel.CellWidth,
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 150, 255)),
                        BorderThickness = new Thickness(2),
                        CornerRadius = new CornerRadius(6),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0, 150, 255)),
                        IsHitTestVisible = false,
                        Tag = "ExternalDropGhost"
                    };
                    panel.Children.Add(_externalGhost);
                }
                FreeGridPanel.SetGridCol(_externalGhost, cell.Col);
                FreeGridPanel.SetGridRow(_externalGhost, cell.Row);
            }
            catch { }
        }

        /// <summary>Removes the external drag-over ghost (DragLeave/Drop).</summary>
        public static void ClearExternalDropPreview()
        {
            try
            {
                if (_externalGhost?.Parent is Panel parent) parent.Children.Remove(_externalGhost);
            }
            catch { }
            _externalGhost = null;
        }

        #endregion

        private static void CreateDragPreview(StackPanel originalIcon)
        {
            try
            {
                if (_dragPreviewWindow != null)
                {
                    _dragPreviewWindow.Close();
                    _dragPreviewWindow = null;
                }

                NonActivatingWindow parentWindow = FindVisualParent<NonActivatingWindow>(originalIcon);
                // Per-window DPI (correct on mixed-DPI multi-monitor setups); cached for the
                // whole drag so UpdateDragPreviewPosition converts screen pixels -> DIPs with
                // the same factor (it previously hardcoded 1.0, putting the preview far from
                // the cursor on scaled displays).
                double dpiScale = 1.0;
                try
                {
                    if (parentWindow != null)
                    {
                        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(parentWindow);
                        dpiScale = dpi.DpiScaleX;
                    }
                }
                catch { dpiScale = parentWindow != null ? GetDpiScaleFactor(parentWindow) : 1.0; }
                _dragPreviewDpiScale = dpiScale;

                _dragPreviewWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Width = originalIcon.ActualWidth > 0 ? originalIcon.ActualWidth : 60,
                    Height = originalIcon.ActualHeight > 0 ? originalIcon.ActualHeight : 80,
                    IsHitTestVisible = false,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

                // The drag visual must BE the icon being moved: snapshot the actual rendered
                // icon (image + label, exactly as on screen) and float a semi-transparent
                // copy under the cursor. Falls back to the legacy manual clone if the
                // snapshot fails (e.g. zero-size element).
                _dragSnapshot = SnapshotIcon(originalIcon);
                if (_dragSnapshot != null)
                {
                    // Snapshot + a "+" copy badge (shown while Ctrl is held = clone drop).
                    var previewGrid = new Grid();
                    previewGrid.Children.Add(new System.Windows.Controls.Image
                    {
                        Source = _dragSnapshot,
                        Width = _dragPreviewWindow.Width,
                        Height = _dragPreviewWindow.Height,
                        Opacity = 0.75,
                        Stretch = Stretch.Fill
                    });
                    _dragCopyBadge = new Border
                    {
                        Width = 16,
                        Height = 16,
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 215)),
                        BorderBrush = System.Windows.Media.Brushes.White,
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(2),
                        Visibility = Visibility.Collapsed,
                        Child = new TextBlock
                        {
                            Text = "+",
                            Foreground = System.Windows.Media.Brushes.White,
                            FontWeight = FontWeights.Bold,
                            FontSize = 12,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, -2, 0, 0)
                        }
                    };
                    previewGrid.Children.Add(_dragCopyBadge);
                    _dragPreviewWindow.Content = previewGrid;
                    System.Windows.Point cursor = GetCursorPosition();
                    _dragPreviewWindow.Left = (cursor.X / dpiScale) + 10;
                    _dragPreviewWindow.Top = (cursor.Y / dpiScale) - 10;
                    _dragPreviewWindow.Show();
                    return;
                }

                // Clone visual content for preview (legacy fallback)
                StackPanel previewContent = new StackPanel { Width = originalIcon.Width, Margin = originalIcon.Margin, Opacity = 0.7 };

                // Copy Image/Grid
                var originalGrid = originalIcon.Children.OfType<Grid>().FirstOrDefault();
                var originalImage = originalIcon.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();

                if (originalGrid != null)
                {
                    // Clone Grid (Network Icon)
                    Grid previewGrid = new Grid { Width = originalGrid.Width, Height = originalGrid.Height, Margin = originalGrid.Margin };
                    var gridImage = originalGrid.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                    if (gridImage != null) previewGrid.Children.Add(new System.Windows.Controls.Image { Source = gridImage.Source, Width = gridImage.Width, Height = gridImage.Height, Margin = gridImage.Margin });

                    var netInd = originalGrid.Children.OfType<TextBlock>().FirstOrDefault();
                    if (netInd != null) previewGrid.Children.Add(new TextBlock { Text = netInd.Text, FontSize = netInd.FontSize, Foreground = netInd.Foreground, Margin = netInd.Margin });

                    previewContent.Children.Add(previewGrid);
                }
                else if (originalImage != null)
                {
                    previewContent.Children.Add(new System.Windows.Controls.Image { Source = originalImage.Source, Width = originalImage.Width, Height = originalImage.Height, Margin = originalImage.Margin });
                }

                // Copy Label
                var originalLabel = originalIcon.Children.OfType<TextBlock>().FirstOrDefault();
                if (originalLabel != null)
                {
                    previewContent.Children.Add(new TextBlock
                    {
                        Text = originalLabel.Text,
                        TextWrapping = originalLabel.TextWrapping,
                        TextAlignment = originalLabel.TextAlignment,
                        Foreground = originalLabel.Foreground,
                        Width = originalLabel.Width
                    });
                }

                _dragPreviewWindow.Content = previewContent;

                System.Windows.Point cursorPos = GetCursorPosition();
                _dragPreviewWindow.Left = (cursorPos.X / dpiScale) + 10;
                _dragPreviewWindow.Top = (cursorPos.Y / dpiScale) - 10;
                _dragPreviewWindow.Show();
            }
            catch { }
        }

        private static void UpdateDragPreviewPosition(System.Windows.Point screenPosition)
        {
            if (_dragPreviewWindow != null)
            {
                // screenPosition is physical pixels (GetCursorPos); Window.Left/Top are DIPs.
                // Use the DPI scale captured at drag start (was hardcoded 1.0, which threw the
                // preview off by the scale factor on high-DPI displays).
                double dpiScale = _dragPreviewDpiScale;
                _dragPreviewWindow.Left = (screenPosition.X / dpiScale) + 10;
                _dragPreviewWindow.Top = (screenPosition.Y / dpiScale) - 10;
            }
        }

        private static void ShowDropZoneIndicators(WrapPanel wrapPanel, System.Windows.Point mousePosition)
        {
            // FREE ARRANGE: live displacement preview + ghost at the target cell instead
            // of the flow-mode insertion indicator.
            if (wrapPanel is FreeGridPanel freePanel && freePanel.FreeArrange)
            {
                UpdateFreeDragPreview(freePanel, mousePosition);
                return;
            }

            // Simple optimization
            if ((mousePosition - _lastDropIndicatorPosition).Length < 15) return;
            _lastDropIndicatorPosition = mousePosition;

            RemoveDropZoneIndicators(wrapPanel);

            var iconPanels = wrapPanel.Children.OfType<StackPanel>().Where(sp => sp != _draggedIcon).ToList();
            if (iconPanels.Count == 0) return;

            StackPanel closestIcon = null;
            double closestDist = double.MaxValue;
            bool insertBefore = true;

            foreach (var panel in iconPanels)
            {
                var pos = panel.TranslatePoint(new System.Windows.Point(0, 0), wrapPanel);
                var center = new System.Windows.Point(pos.X + panel.ActualWidth / 2, pos.Y + panel.ActualHeight / 2);
                double dist = (mousePosition - center).Length;

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIcon = panel;
                    insertBefore = mousePosition.X < center.X;
                }
            }

            if (closestIcon != null)
            {
                var indicator = new Border
                {
                    Width = 3,
                    Height = closestIcon.ActualHeight,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 150, 255)),
                    CornerRadius = new CornerRadius(1.5),
                    Tag = "DropIndicator",
                    Margin = new Thickness(2, 5, 2, 5),
                    Effect = new DropShadowEffect { Color = System.Windows.Media.Color.FromRgb(0, 150, 255), BlurRadius = 8, ShadowDepth = 0 }
                };

                int idx = wrapPanel.Children.IndexOf(closestIcon);
                if (insertBefore) wrapPanel.Children.Insert(idx, indicator);
                else wrapPanel.Children.Insert(idx + 1, indicator);
            }
        }

        private static void RemoveDropZoneIndicators(WrapPanel wrapPanel)
        {
            if (wrapPanel == null) return;
            var toRemove = wrapPanel.Children.OfType<Border>().Where(b => "DropIndicator".Equals(b.Tag?.ToString())).ToList();
            foreach (var item in toRemove) wrapPanel.Children.Remove(item);
        }

        #endregion
    }
}
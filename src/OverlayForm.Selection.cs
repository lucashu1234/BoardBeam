using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed partial class OverlayForm
    {
        private const int HandleSize = 10;
        private const int HandleHitTolerance = 8;
        private static readonly string[] ToolbarLabels = { "选择", "画笔", "荧光", "直线", "矩形", "椭圆", "箭头", "文字", "遮罩", "马赛克", "马赛克区", "编号", "印章", "橡皮", "细", "粗", "填充", "撤销", "重做", "复制", "保存", "贴图", "关闭" };
        private static readonly string[] ToolbarShortcuts = { "Q", "P", "H", "L", "R", "O", "A", "T", "V", "X", "N", "M", "I", "E", "-", "+", "F", "Ctrl+Z", "Ctrl+Y", "Ctrl+C", "Ctrl+S", "P", "Esc" };
        private static readonly DrawingTool[] ToolbarToolMap = {
            DrawingTool.Select,
            DrawingTool.Pen, DrawingTool.Highlighter, DrawingTool.Line, DrawingTool.Rectangle,
            DrawingTool.Ellipse, DrawingTool.Arrow, DrawingTool.Text, DrawingTool.Cover,
            DrawingTool.Blur, DrawingTool.Pixelate, DrawingTool.NumberMarker, DrawingTool.Stamp, DrawingTool.Eraser
        };
        private static readonly string[] DrawingToolNames = {
            "画笔", "荧光笔", "直线", "箭头", "矩形", "椭圆", "遮罩", "编号", "马赛克", "橡皮", "文字", "印章", "测距"
        };
        private static readonly Color[] PaletteColors = {
            Color.Red, Color.Orange, Color.Gold, Color.Yellow,
            Color.LimeGreen, Color.DeepSkyBlue, Color.Blue, Color.Magenta,
            Color.Cyan, Color.White, Color.Black, Color.Gray,
            Color.FromArgb(255, 105, 180), Color.FromArgb(139, 69, 19), Color.FromArgb(0, 100, 0), Color.FromArgb(75, 0, 130)
        };
        private static readonly Keys[] SelectionToolKeys = {
            Keys.P, Keys.H, Keys.L, Keys.A, Keys.R, Keys.O, Keys.V, Keys.M, Keys.X, Keys.E, Keys.T, Keys.I, Keys.None, Keys.Q, Keys.N
        };
        private const int ToolbarItemWidth = 42;
        private const int ToolbarItemHeight = 38;
        private const int ToolbarPadding = 2;
        private const int ToolbarRowSpacing = 2;

        private void BeginSelection(SelectionAction action)
        {
            CommitTextInput(true);
            selectionAction = action;
            isSelecting = false;
            isMovingSelection = false;
            isResizingSelection = false;
            activeResizeHandle = -1;
            hoveredToolbarItem = -1;
            hasHoveredWindow = false;
            selectionStartView = Point.Empty;
            selectionCurrentView = Point.Empty;
            selectionMoveStartView = Point.Empty;
            selectionMoveOriginalView = Rectangle.Empty;
            selectedRegionView = Rectangle.Empty;
            hasSelectedRegion = false;
            Cursor = Cursors.Cross;
            ShowToast("拖选区域", GetSelectionHint(action));
            Invalidate();
        }

        private void CompleteSelection()
        {
            Rectangle rect = Normalize(selectionStartView, selectionCurrentView);
            rect = Clip(rect, new Rectangle(0, 0, Width, Height));
            SelectionAction action = selectionAction;

            if (rect.Width < 4 || rect.Height < 4)
            {
                ShowToast("选择区域太小", "请拖选更大的区域");
                Invalidate();
                return;
            }

            if (action == SelectionAction.PixPinCapture)
            {
                selectedRegionView = rect;
                hasSelectedRegion = true;
                isAnnotating = true;
                marchTimer.Start();
                string toolHint = AnnotationToolName(annotationTool);
                ShowToast("已进入标注模式", "双击或 Enter 复制  Alt+拖拽移动选区  右键更多");
                Invalidate();
                return;
            }

            selectionAction = SelectionAction.None;
            CompleteSelectionAction(action, rect);

            if (ShouldCloseAfterSelection(action))
            {
                Close();
            }
            else
            {
                Invalidate();
            }
        }

        private string GetSelectionHint(SelectionAction action)
        {
            if (action == SelectionAction.CopyRegion || action == SelectionAction.CopyCrop) return "松开后复制到剪贴板";
            if (action == SelectionAction.SaveRegion || action == SelectionAction.SaveCrop) return "松开后保存为图片";
            if (action == SelectionAction.PinRegion) return "松开后贴到屏幕";
            if (action == SelectionAction.RecordRegion) return "拖选后开始 GIF 录屏；Space 选窗口后 Enter 开始；Ctrl+5 停止";
            if (action == SelectionAction.ScrollCapture) return "松开后自动滚动并保存长截图";
            if (action == SelectionAction.OcrCapture) return "松开后识别区域文字并复制到剪贴板";
            return "点击窗口或拖选区域";
        }

        private void ShowSelectionContextMenu(Point location)
        {
            var menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer();
            menu.Items.Add("复制截图  Ctrl+C", null, delegate { CommitTextInput(true); CopyAnnotatedRegion(); Close(); });
            menu.Items.Add("保存截图  Ctrl+S", null, delegate { CommitTextInput(true); SaveAnnotatedRegion(); Close(); });
            menu.Items.Add("贴到屏幕  P", null, delegate { CommitTextInput(true); PinAnnotatedRegion(); Close(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("OCR 识别文字", null, delegate
            {
                if (hasSelectedRegion)
                {
                    CommitTextInput(true);
                    Rectangle screenRect = ViewRectToScreenRect(selectedRegionView);
                    Opacity = 0;
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(40);
                    OcrTool.RecognizeRegion(screenRect, Program.AppContext);
                    Close();
                }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("关闭  Esc", null, delegate { Close(); });
            menu.Show(this, location);
        }

        private bool HandleSelectionKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (isAnnotating)
                {
                    CommitTextInput(false);
                    if (selectionAnnotations.Count > 0)
                    {
                        // 先保存到 Undo 栈，让用户可以 Ctrl+Z 恢复
                        selectionUndoStack.Add(CloneAnnotations(selectionAnnotations));
                        selectionRedoStack.Clear();
                        if (selectionUndoStack.Count > MaxUndoStates)
                            selectionUndoStack.RemoveAt(0);
                    }
                    selectionAnnotations.Clear();
                    selectionNextMarker = 1;
                    isAnnotating = false;
                    marchTimer.Stop();
                    ShowToast("已清除标注", "Ctrl+Z 可恢复  双击或 Enter 复制  Esc 退出选区");
                    Invalidate();
                    return true;
                }
                if (hasSelectedRegion)
                {
                    // 第2次 Esc：取消选区
                    hasSelectedRegion = false;
                    selectedRegionView = Rectangle.Empty;
                    isAnnotating = false;
                    marchTimer.Stop();
                    Invalidate();
                }
                else
                {
                    // 第3次 Esc：关闭
                    Close();
                }
                return true;
            }

            if (e.KeyCode == Keys.Space && !isAnnotating)
            {
                CaptureWindowUnderCursor();
                return true;
            }

            // Tab / Shift+Tab：在元素层级间切换（子控件 ↔ 根窗口），Snipaste 风格
            if (!isAnnotating && !hasSelectedRegion && e.KeyCode == Keys.Tab)
            {
                if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                    elementCycleDepth = elementCycleDepth > 0 ? elementCycleDepth - 1 : 0;
                else
                    elementCycleDepth = elementCycleDepth < 3 ? elementCycleDepth + 1 : 3;
                lastHoverCheckTick = 0; // 强制立即重新检测
                Invalidate();
                return true;
            }

            // Ctrl+A：全选当前屏幕
            if (e.Control && e.KeyCode == Keys.A && !hasSelectedRegion && !isSelecting)
            {
                selectionStartView = new Point(0, 0);
                selectionCurrentView = new Point(Width, Height);
                CompleteSelection();
                return true;
            }

            // Alt+1..9：把当前选区存为快贴槽位
            if (e.Alt && hasSelectedRegion && selectedRegionView.Width >= 4 &&
                e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
            {
                int slot = e.KeyCode - Keys.D0;
                PresenterApplicationContext.SaveQuickSlot(slot, ViewRectToScreenRect(selectedRegionView));
                ShowToast("已存入槽位 " + slot, "在托盘菜单「快贴槽位」中可一键重截贴图");
                return true;
            }

            if (e.KeyCode == Keys.C && !e.Control && !e.Shift)
            {
                CopyPixelColor(Cursor.Position, false);
                return true;
            }

            if (e.KeyCode == Keys.C && e.Shift && !e.Control)
            {
                CopyPixelColor(Cursor.Position, true);
                return true;
            }

            if (hasSelectedRegion && selectionAction == SelectionAction.PixPinCapture)
            {
                if (e.Control && e.KeyCode == Keys.Z)
                {
                    SelectionUndo();
                    e.Handled = true;
                    return true;
                }
                if (e.Control && e.KeyCode == Keys.Y)
                {
                    SelectionRedo();
                    e.Handled = true;
                    return true;
                }
            }

            if (!hasSelectedRegion)
            {
                return false;
            }

            if (hasSelectedRegion && selectionAction == SelectionAction.PixPinCapture && isAnnotating)
            {
                // 颜色快捷键 1-9
                if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
                {
                    int ci = e.KeyCode - Keys.D1;
                    if (ci < PaletteColors.Length) { currentColor = PaletteColors[ci]; Invalidate(); return true; }
                }
                // 按 0 打开自定义颜色
                if (e.KeyCode == Keys.D0)
                {
                    using (var dlg = new ColorDialog())
                    {
                        dlg.Color = currentColor;
                        dlg.FullOpen = true;
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            currentColor = dlg.Color;
                            Invalidate();
                        }
                    }
                    return true;
                }
                // Delete 删除当前选中的标注（仅在选择工具激活时）
                if ((e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back) &&
                    annotationTool == DrawingTool.Select && selectedAnnotationIndex >= 0 &&
                    selectedAnnotationIndex < selectionAnnotations.Count)
                {
                    SelectionSaveUndo();
                    selectionAnnotations.RemoveAt(selectedAnnotationIndex);
                    selectedAnnotationIndex = -1;
                    selectionRedoStack.Clear();
                    Invalidate();
                    return true;
                }
                // 工具快捷键
                for (int ti = 0; ti < SelectionToolKeys.Length; ti++)
                {
                    if (e.KeyCode == SelectionToolKeys[ti])
                    {
                        annotationTool = (DrawingTool)ti;
                        Invalidate();
                        return true;
                    }
                }
                if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus) { currentWidth += 1.0f; if (currentWidth > 40.0f) currentWidth = 40.0f; Invalidate(); return true; }
                if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus) { currentWidth -= 1.0f; if (currentWidth < 1.0f) currentWidth = 1.0f; Invalidate(); return true; }
                if (e.KeyCode == Keys.F) { shapeFilled = !shapeFilled; Invalidate(); return true; }
                // Y 切换形状投影阴影
                if (e.KeyCode == Keys.Y) { shapeShadow = !shapeShadow; Invalidate(); return true; }
                // G 切换序号连线（NumberMarker 工具下连续编号自动画箭头）
                if (e.KeyCode == Keys.G) { linkNumberMarkers = !linkNumberMarkers; hasLastNumberMarker = false; Invalidate(); return true; }
                // D 切换线型：实线 → 虚线 → 点线 → 实线
                if (e.KeyCode == Keys.D)
                {
                    if (currentDashStyle == System.Drawing.Drawing2D.DashStyle.Solid) currentDashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    else if (currentDashStyle == System.Drawing.Drawing2D.DashStyle.Dash) currentDashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                    else currentDashStyle = System.Drawing.Drawing2D.DashStyle.Solid;
                    Invalidate();
                    return true;
                }
                // B 切换文字背景框
                if (e.KeyCode == Keys.B) { textHasBackground = !textHasBackground; Invalidate(); return true; }
                // [ ] 调节当前工具透明度（10% 一档）；马赛克工具下用 , . 调节强度
                if (e.KeyCode == Keys.OemOpenBrackets) { currentOpacity = Math.Max(0.1f, currentOpacity - 0.1f); Invalidate(); return true; }
                if (e.KeyCode == Keys.OemCloseBrackets) { currentOpacity = Math.Min(1.0f, currentOpacity + 0.1f); Invalidate(); return true; }
                if (annotationTool == DrawingTool.Blur)
                {
                    if (e.KeyCode == Keys.Oemcomma) { currentBlurIntensity = Math.Max(0.2f, currentBlurIntensity - 0.2f); Invalidate(); return true; }
                    if (e.KeyCode == Keys.OemPeriod) { currentBlurIntensity = Math.Min(3.0f, currentBlurIntensity + 0.2f); Invalidate(); return true; }
                }
            }

            if (selectionAction != SelectionAction.PixPinCapture)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    SelectionAction action = selectionAction;
                    selectionAction = SelectionAction.None;
                    CompleteSelectionAction(action, selectedRegionView);
                    if (ShouldCloseAfterSelection(action)) Close();
                    else Invalidate();
                    return true;
                }
            }

            if (selectionAction == SelectionAction.PixPinCapture && (e.KeyCode == Keys.Enter || (e.Control && e.KeyCode == Keys.C)))
            {
                CopyAnnotatedRegion();
                Close();
                return true;
            }

            if (e.KeyCode == Keys.S || (e.Control && e.KeyCode == Keys.S))
            {
                if (selectionAction == SelectionAction.PixPinCapture || selectionAction == SelectionAction.SaveRegion || selectionAction == SelectionAction.SaveCrop)
                {
                    SaveAnnotatedRegion();
                    Close();
                    return true;
                }
            }

            if (selectionAction == SelectionAction.PixPinCapture && !isAnnotating && (e.KeyCode == Keys.P || e.KeyCode == Keys.T))
            {
                PinAnnotatedRegion();
                Close();
                return true;
            }

            int step = e.Shift ? 10 : 1;
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                if (e.Control)
                {
                    selectedRegionView = ResizeSelection(selectedRegionView, e.KeyCode, step);
                }
                else
                {
                    selectedRegionView = MoveSelection(selectedRegionView, e.KeyCode, step);
                }
                Invalidate();
                return true;
            }

            return false;
        }

        private Rectangle MoveSelection(Rectangle rect, Keys key, int step)
        {
            if (key == Keys.Left) rect.X -= step;
            if (key == Keys.Right) rect.X += step;
            if (key == Keys.Up) rect.Y -= step;
            if (key == Keys.Down) rect.Y += step;

            if (rect.X < 0) rect.X = 0;
            if (rect.Y < 0) rect.Y = 0;
            if (rect.Right > Width) rect.X = Math.Max(0, Width - rect.Width);
            if (rect.Bottom > Height) rect.Y = Math.Max(0, Height - rect.Height);
            return rect;
        }

        private Rectangle MoveSelectionByDelta(Rectangle rect, int dx, int dy)
        {
            rect.X += dx;
            rect.Y += dy;
            if (rect.X < 0) rect.X = 0;
            if (rect.Y < 0) rect.Y = 0;
            if (rect.Right > Width) rect.X = Math.Max(0, Width - rect.Width);
            if (rect.Bottom > Height) rect.Y = Math.Max(0, Height - rect.Height);
            return rect;
        }

        private Rectangle ResizeSelection(Rectangle rect, Keys key, int step)
        {
            if ((ModifierKeys & Keys.Shift) != 0 && rect.Height > 0)
            {
                float ratio = (float)rect.Width / rect.Height;
                if (key == Keys.Left || key == Keys.Right)
                {
                    if (key == Keys.Right) rect.Width += step;
                    if (key == Keys.Left) rect.Width -= step;
                    rect.Height = (int)(rect.Width / ratio);
                }
                else
                {
                    if (key == Keys.Down) rect.Height += step;
                    if (key == Keys.Up) rect.Height -= step;
                    rect.Width = (int)(rect.Height * ratio);
                }
            }
            else
            {
                if (key == Keys.Left) rect.Width -= step;
                if (key == Keys.Right) rect.Width += step;
                if (key == Keys.Up) rect.Height -= step;
                if (key == Keys.Down) rect.Height += step;
            }
            if (rect.Width < 4) rect.Width = 4;
            if (rect.Height < 4) rect.Height = 4;
            return Clip(rect, new Rectangle(0, 0, Width, Height));
        }

        private void CaptureWindowUnderCursor()
        {
            Opacity = 0;
            Application.DoEvents();
            Thread.Sleep(20);

            Rectangle screenRect;
            bool found = CaptureTool.TryGetWindowUnderCursor(out screenRect);
            Opacity = 1;
            Application.DoEvents();
            if (!found) return;
            Rectangle viewRect = new Rectangle(
                screenRect.Left - virtualBounds.Left,
                screenRect.Top - virtualBounds.Top,
                screenRect.Width,
                screenRect.Height);
            viewRect = Clip(viewRect, new Rectangle(0, 0, Width, Height));
            if (viewRect.Width < 4 || viewRect.Height < 4) return;

            selectedRegionView = viewRect;
            hasSelectedRegion = true;
            isAnnotating = true;
            ShowToast("已进入标注模式", "双击或 Enter 复制  右键更多");
            selectionStartView = viewRect.Location;
            selectionCurrentView = new Point(viewRect.Right, viewRect.Bottom);
            marchTimer.Start();
            Invalidate();
        }

        private void CompleteSelectionAction(SelectionAction action, Rectangle rect)
        {
            // 记录上次截图区域（屏幕坐标），供"重截上次区域"使用
            if (rect.Width >= 4 && rect.Height >= 4)
            {
                Rectangle screenRect = ViewRectToScreenRect(rect);
                AppSettings prefs = SettingsStore.Load();
                prefs.HasLastRegion = true;
                prefs.LastRegionX = screenRect.X;
                prefs.LastRegionY = screenRect.Y;
                prefs.LastRegionW = screenRect.Width;
                prefs.LastRegionH = screenRect.Height;
                SettingsStore.Save(prefs);
            }

            if (action == SelectionAction.CopyRegion || action == SelectionAction.CopyCrop)
            {
                CopySelectedRegion(rect);
            }
            else if (action == SelectionAction.SaveRegion || action == SelectionAction.SaveCrop)
            {
                SaveSelectedRegion(rect);
            }
            else if (action == SelectionAction.PinRegion)
            {
                PinSelectedRegion(rect);
            }
            else if (action == SelectionAction.RecordRegion)
            {
                StartRecordingRegion(rect);
            }
            else if (action == SelectionAction.ScrollCapture)
            {
                StartScrollingCapture(rect);
            }
            else if (action == SelectionAction.OcrCapture)
            {
                StartOcrCapture(rect);
            }
        }

        private bool ShouldCloseAfterSelection(SelectionAction action)
        {
            return action == SelectionAction.CopyRegion ||
                   action == SelectionAction.SaveRegion ||
                   action == SelectionAction.PinRegion ||
                   action == SelectionAction.RecordRegion ||
                   action == SelectionAction.ScrollCapture ||
                   action == SelectionAction.OcrCapture;
        }

        private Rectangle ViewRectToScreenRect(Rectangle rect)
        {
            return new Rectangle(virtualBounds.Left + rect.Left, virtualBounds.Top + rect.Top, rect.Width, rect.Height);
        }

        private void StartRecordingRegion(Rectangle rect)
        {
            Rectangle screenRect = ViewRectToScreenRect(rect);
            Opacity = 0;
            Application.DoEvents();
            Thread.Sleep(40);
            RecordingTool.Start(screenRect);
        }

        private void StartScrollingCapture(Rectangle rect)
        {
            Rectangle screenRect = ViewRectToScreenRect(rect);
            Opacity = 0;
            Application.DoEvents();
            Thread.Sleep(40);
            try
            {
                string file = ScrollingCaptureTool.Capture(screenRect);
                if (file == null)
                    MessageBox.Show("长截图已取消（捕获的帧不足）。", "BoardBeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show("长截图已保存：\n" + file, "BoardBeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("长截图失败：\n" + ex.Message, "BoardBeam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void StartOcrCapture(Rectangle rect)
        {
            Rectangle screenRect = ViewRectToScreenRect(rect);
            Opacity = 0;
            Application.DoEvents();
            Thread.Sleep(40);
            OcrTool.RecognizeRegion(screenRect, GetApplicationContext());
        }

        private PresenterApplicationContext GetApplicationContext()
        {
            // 通过通知图标获取 context - 使用静态引用
            return Program.AppContext;
        }

        private void CopyPixelColor(Point screenPoint, bool rgbFormat)
        {
            int x = screenPoint.X - virtualBounds.Left;
            int y = screenPoint.Y - virtualBounds.Top;
            if (x < 0 || y < 0 || x >= background.Width || y >= background.Height) return;

            Color color = background.GetPixel(x, y);
            string text = rgbFormat
                ? "rgb(" + color.R + ", " + color.G + ", " + color.B + ")"
                : "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
            string error;
            if (ClipboardService.TrySetText(text, out error))
            {
                ShowToast("已复制颜色", text);
            }
            else
            {
                ShowToast("复制颜色失败", error);
            }
        }

        private void CopySelectedRegion(Rectangle rect)
        {
            using (Bitmap crop = RenderRegion(rect))
            {
                string error;
                if (!ClipboardService.TrySetImage(crop, out error))
                {
                    ShowToast("复制截图失败", error);
                    return;
                }
                CaptureStore.Add((Bitmap)crop.Clone());
            }
            PlayCaptureSound();
            ShowToast("已复制区域截图", rect.Width + " x " + rect.Height);
        }

        private void SaveSelectedRegion(Rectangle rect)
        {
            string file = AppPaths.NewImagePath("_region");
            string savedFile;
            using (Bitmap crop = RenderRegion(rect))
            {
                savedFile = SaveImageAutoFormat((Bitmap)crop.Clone(), file);
                CaptureStore.Add((Bitmap)crop.Clone());
            }
            PlayCaptureSound();
            ShowToast("已保存区域截图", savedFile);
        }

        private void PinSelectedRegion(Rectangle rect)
        {
            using (Bitmap crop = RenderRegion(rect))
            {
                Point location = new Point(virtualBounds.Left + rect.Left + 24, virtualBounds.Top + rect.Top + 24);
                PinManager.Show((Bitmap)crop.Clone(), location, true);
                CaptureStore.Add((Bitmap)crop.Clone());
            }
            ShowToast("已贴图", rect.Width + " x " + rect.Height);
        }

        private Rectangle FindWindowAtViewPoint(Point viewPoint)
        {
            if (windowRects == null) return Rectangle.Empty;
            Point screenPoint = new Point(viewPoint.X + virtualBounds.Left, viewPoint.Y + virtualBounds.Top);
            Rectangle best = Rectangle.Empty;
            int bestArea = int.MaxValue;
            for (int i = 0; i < windowRects.Count; i++)
            {
                Rectangle wr = windowRects[i];
                if (wr.Contains(screenPoint))
                {
                    int area = wr.Width * wr.Height;
                    if (area < bestArea)
                    {
                        bestArea = area;
                        best = wr;
                    }
                }
            }
            if (best == Rectangle.Empty) return Rectangle.Empty;
            return new Rectangle(best.X - virtualBounds.Left, best.Y - virtualBounds.Top, best.Width, best.Height);
        }

        private void UpdateSelectionHover(Point viewLocation)
        {
            if (isAnnotating)
            {
                if (hasSelectedRegion)
                {
                    hoveredToolbarItem = HitTestToolbar(viewLocation);
                }
                else
                {
                    hoveredToolbarItem = -1;
                }
                return;
            }

            if (selectionAction != SelectionAction.PixPinCapture || hasSelectedRegion || isSelecting)
            {
                if (hasHoveredWindow)
                {
                    hasHoveredWindow = false;
                    hoveredWindowTitle = null;
                }
                hoveredToolbarItem = -1;
                return;
            }

            // 节流：50ms 内不重复调用 Win32 窗口检测
            int now = System.Environment.TickCount;
            if (now - lastHoverCheckTick < 50)
                return;
            lastHoverCheckTick = now;

            // 实时窗口检测（替代静态列表）
            Point screenPoint = new Point(viewLocation.X + virtualBounds.Left, viewLocation.Y + virtualBounds.Top);
            Rectangle screenRect;
            string title;
            // 元素模式：悬停时优先高亮最深的子控件；elementLevel>0 时回退到根窗口
            bool elementMode = elementCycleDepth == 0;
            if (CaptureTool.GetWindowInfoAtPoint(screenPoint, out screenRect, out title, elementMode))
            {
                hoveredWindowView = new Rectangle(
                    screenRect.Left - virtualBounds.Left,
                    screenRect.Top - virtualBounds.Top,
                    screenRect.Width,
                    screenRect.Height);
                hoveredWindowView = Clip(hoveredWindowView, new Rectangle(0, 0, Width, Height));
                hasHoveredWindow = hoveredWindowView.Width > 4 && hoveredWindowView.Height > 4;
                hoveredWindowTitle = title;
            }
            else
            {
                hasHoveredWindow = false;
                hoveredWindowTitle = null;
            }

            if (hasSelectedRegion)
            {
                hoveredToolbarItem = HitTestToolbar(viewLocation);
            }
            else
            {
                hoveredToolbarItem = -1;
            }
        }

        private void UpdateSelectionCursor(Point location)
        {
            if (isAnnotating && hasSelectedRegion && selectedRegionView.Contains(location))
            {
                // 根据工具类型切换光标
                if (annotationTool == DrawingTool.Text)
                    Cursor = Cursors.IBeam;
                else if (annotationTool == DrawingTool.Select)
                    Cursor = GetSelectCursor(location);
                else if (annotationTool == DrawingTool.Eraser)
                    Cursor = Cursors.Hand;
                else if (annotationTool == DrawingTool.NumberMarker || annotationTool == DrawingTool.Stamp)
                    Cursor = Cursors.Cross;
                else
                    Cursor = Cursors.Cross;
                return;
            }

            // 标注模式下选区外显示默认箭头（暗示不可绘制）
            if (isAnnotating && hasSelectedRegion && !selectedRegionView.Contains(location))
            {
                int tb = HitTestToolbar(location);
                if (tb >= 0)
                {
                    Cursor = Cursors.Hand;
                    return;
                }
                int handle = HitTestResizeHandle(location);
                if (handle >= 0)
                {
                    Cursor = HandleCursor(handle);
                    return;
                }
                Cursor = Cursors.Default;
                return;
            }

            if (hasSelectedRegion)
            {
                int tb = HitTestToolbar(location);
                if (tb >= 0)
                {
                    Cursor = Cursors.Hand;
                    return;
                }

                int handle = HitTestResizeHandle(location);
                if (handle >= 0)
                {
                    Cursor = HandleCursor(handle);
                    return;
                }

                if (selectedRegionView.Contains(location))
                {
                    Cursor = Cursors.SizeAll;
                    return;
                }
            }

            if (selectionAction == SelectionAction.PixPinCapture && hasHoveredWindow)
            {
                Cursor = Cursors.Hand;
                return;
            }

            Cursor = Cursors.Cross;
        }

        /// <summary>选择工具的光标：命中手柄用对应缩放光标，命中标注用移动光标，否则默认箭头。</summary>
        private Cursor GetSelectCursor(Point location)
        {
            if (selectedAnnotationIndex >= 0 && selectedAnnotationIndex < selectionAnnotations.Count)
            {
                Graphics mg = GetMeasureGraphics();
                int h = selectionAnnotations[selectedAnnotationIndex].HitTestHandle(location, mg);
                if (h >= 0)
                {
                    // 0=TL 4=BR 对角；2=TR 6=BL 反对角；1=5 上下；3=7 左右
                    if (h == 0 || h == 4) return Cursors.SizeNWSE;
                    if (h == 2 || h == 6) return Cursors.SizeNESW;
                    if (h == 1 || h == 5) return Cursors.SizeNS;
                    if (h == 3 || h == 7) return Cursors.SizeWE;
                }
                RectangleF ab = selectionAnnotations[selectedAnnotationIndex].GetBounds(mg);
                if (ab.Contains(location)) return Cursors.SizeAll;
            }
            // 检测是否悬停在其他标注上
            Graphics mg2 = GetMeasureGraphics();
            for (int i = selectionAnnotations.Count - 1; i >= 0; i--)
            {
                if (selectionAnnotations[i].HitTest(location, 4f, mg2)) return Cursors.SizeAll;
            }
            return Cursors.Default;
        }

        private int HitTestResizeHandle(Point location)
        {
            if (!hasSelectedRegion) return -1;
            Point[] handles = GetHandlePositions(selectedRegionView);
            for (int i = 0; i < handles.Length; i++)
            {
                if (Math.Abs(location.X - handles[i].X) <= HandleHitTolerance &&
                    Math.Abs(location.Y - handles[i].Y) <= HandleHitTolerance)
                {
                    return i;
                }
            }
            return -1;
        }

        private int HitTestToolbar(Point location)
        {
            if (!hasSelectedRegion || selectionAction != SelectionAction.PixPinCapture) return -1;
            Rectangle toolbar = GetToolbarBounds(selectedRegionView);
            if (!toolbar.Contains(location)) return -1;
            for (int i = 0; i < ToolbarLabels.Length; i++)
            {
                Rectangle item = GetToolbarItemRect(toolbar, i);
                if (item.Contains(location)) return i;
            }
            return -1;
        }

        private void ExecuteToolbarItem(int index)
        {
            if (index >= 0 && index < ToolbarToolMap.Length)
            {
                isAnnotating = true;
                annotationTool = ToolbarToolMap[index];
                Invalidate();
                return;
            }
            switch (index)
            {
                case 14: currentWidth = Math.Max(1.0f, currentWidth - 2.0f); Invalidate(); break;  // 细
                case 15: currentWidth = Math.Min(40.0f, currentWidth + 2.0f); Invalidate(); break;  // 粗
                case 16: shapeFilled = !shapeFilled; Invalidate(); break;                            // 填充
                case 17: SelectionUndo(); break;
                case 18: SelectionRedo(); break;
                case 19: CommitTextInput(true); CopyAnnotatedRegion(); Close(); break;
                case 20: CommitTextInput(true); SaveAnnotatedRegion(); Close(); break;
                case 21: CommitTextInput(true); PinAnnotatedRegion(); Close(); break;
                case 22: Close(); break;
            }
        }

        private Rectangle ResizeByHandle(Rectangle original, int handle, Point mouse)
        {
            int left = original.Left;
            int top = original.Top;
            int right = original.Right;
            int bottom = original.Bottom;

            switch (handle)
            {
                case 0: left = mouse.X; top = mouse.Y; break;
                case 1: top = mouse.Y; break;
                case 2: right = mouse.X; top = mouse.Y; break;
                case 3: right = mouse.X; break;
                case 4: right = mouse.X; bottom = mouse.Y; break;
                case 5: bottom = mouse.Y; break;
                case 6: left = mouse.X; bottom = mouse.Y; break;
                case 7: left = mouse.X; break;
            }

            // Shift 锁定原始宽高比（对角和边手柄）
            if ((ModifierKeys & Keys.Shift) != 0 && resizeOriginalAspectRatio > 0)
            {
                int w = right - left;
                int h = bottom - top;
                if (handle == 0 || handle == 2 || handle == 4 || handle == 6)
                {
                    // 对角手柄：按宽度调整高度
                    h = (int)(w / resizeOriginalAspectRatio);
                    if (handle == 0 || handle == 6) bottom = top + h;
                    else top = bottom - h;
                }
                else if (handle == 3 || handle == 7)
                {
                    // 左右手柄：按宽度调整高度
                    h = (int)(w / resizeOriginalAspectRatio);
                    int mid = (top + bottom) / 2;
                    top = mid - h / 2;
                    bottom = top + h;
                }
                else if (handle == 1 || handle == 5)
                {
                    // 上下手柄：按高度调整宽度
                    w = (int)(h * resizeOriginalAspectRatio);
                    int mid = (left + right) / 2;
                    left = mid - w / 2;
                    right = left + w;
                }
            }

            if (right - left < 4) right = left + 4;
            if (bottom - top < 4) bottom = top + 4;
            left = Math.Max(0, left);
            top = Math.Max(0, top);
            right = Math.Min(Width, right);
            bottom = Math.Min(Height, bottom);

            return Rectangle.FromLTRB(
                Math.Min(left, right),
                Math.Min(top, bottom),
                Math.Max(left, right),
                Math.Max(top, bottom));
        }

        /// <summary>选择工具：根据手柄索引和鼠标位置，从起始包围盒计算新的包围盒。</summary>
        private static RectangleF ComputeResizedBounds(RectangleF original, int handle, Point mouse)
        {
            float left = original.Left, top = original.Top, right = original.Right, bottom = original.Bottom;
            switch (handle)
            {
                case 0: left = mouse.X; top = mouse.Y; break;
                case 1: top = mouse.Y; break;
                case 2: right = mouse.X; top = mouse.Y; break;
                case 3: right = mouse.X; break;
                case 4: right = mouse.X; bottom = mouse.Y; break;
                case 5: bottom = mouse.Y; break;
                case 6: left = mouse.X; bottom = mouse.Y; break;
                case 7: left = mouse.X; break;
            }
            if (right - left < 8f) right = left + 8f;
            if (bottom - top < 8f) bottom = top + 8f;
            return RectangleF.FromLTRB(
                Math.Min(left, right), Math.Min(top, bottom),
                Math.Max(left, right), Math.Max(top, bottom));
        }

        /// <summary>返回包围盒的 8 个手柄点（供绘制选择框用），顺序与 HitTestRectHandles 一致。</summary>
        private static PointF[] RectHandlePointsPublic(RectangleF bounds)
        {
            float l = bounds.X, t = bounds.Y, r = bounds.Right, b = bounds.Bottom;
            float cx = (l + r) / 2f, cy = (t + b) / 2f;
            return new PointF[]
            {
                new PointF(l, t), new PointF(cx, t), new PointF(r, t), new PointF(r, cy),
                new PointF(r, b), new PointF(cx, b), new PointF(l, b), new PointF(l, cy),
            };
        }

        private static Cursor HandleCursor(int handle)
        {
            switch (handle)
            {
                case 0: case 4: return Cursors.SizeNWSE;
                case 1: case 5: return Cursors.SizeNS;
                case 2: case 6: return Cursors.SizeNESW;
                case 3: case 7: return Cursors.SizeWE;
                default: return Cursors.Cross;
            }
        }

        private static Point[] GetHandlePositions(Rectangle rect)
        {
            return new Point[]
            {
                new Point(rect.Left, rect.Top),
                new Point(rect.Left + rect.Width / 2, rect.Top),
                new Point(rect.Right, rect.Top),
                new Point(rect.Right, rect.Top + rect.Height / 2),
                new Point(rect.Right, rect.Bottom),
                new Point(rect.Left + rect.Width / 2, rect.Bottom),
                new Point(rect.Left, rect.Bottom),
                new Point(rect.Left, rect.Top + rect.Height / 2)
            };
        }

        private Rectangle GetToolbarBounds(Rectangle selectionRect)
        {
            int totalItems = ToolbarLabels.Length;
            int totalWidth = totalItems * (ToolbarItemWidth + ToolbarPadding) - ToolbarPadding;
            int availWidth = Math.Max(selectionRect.Width + 80, 380);

            int itemsPerRow = totalItems;
            if (totalWidth > availWidth)
            {
                itemsPerRow = Math.Max(6, (availWidth + ToolbarPadding) / (ToolbarItemWidth + ToolbarPadding));
                if (itemsPerRow > totalItems) itemsPerRow = totalItems;
            }

            int rows = (totalItems + itemsPerRow - 1) / itemsPerRow;
            int rowWidth = itemsPerRow * (ToolbarItemWidth + ToolbarPadding) - ToolbarPadding;
            // 工具按钮行 + 颜色行
            int colorRowHeight = 24;
            int totalHeight = rows * ToolbarItemHeight + (rows - 1) * ToolbarRowSpacing + colorRowHeight + 4;

            int x = selectionRect.Left + (selectionRect.Width - rowWidth) / 2;
            int y = selectionRect.Bottom + 10;

            // 用选区中心点所在屏幕的工作区做边界检查
            Rectangle screenRect = new Rectangle(
                selectionRect.X + virtualBounds.Left + selectionRect.Width / 2,
                selectionRect.Y + virtualBounds.Top + selectionRect.Height / 2, 1, 1);
            Rectangle workingArea = Screen.FromRectangle(screenRect).WorkingArea;
            int screenBottom = workingArea.Bottom - virtualBounds.Top;
            int screenTop = workingArea.Top - virtualBounds.Top;

            if (y + totalHeight > screenBottom)
                y = selectionRect.Top - totalHeight - 10;
            if (y < screenTop)
                y = screenTop + 2;

            if (x < 4) x = 4;

            return new Rectangle(x, y, rowWidth, totalHeight);
        }

        private static Rectangle GetToolbarItemRect(Rectangle toolbar, int index)
        {
            int itemsPerRow = (toolbar.Width + ToolbarPadding) / (ToolbarItemWidth + ToolbarPadding);
            if (itemsPerRow < 1) itemsPerRow = 1;

            int row = index / itemsPerRow;
            int col = index % itemsPerRow;

            return new Rectangle(
                toolbar.X + col * (ToolbarItemWidth + ToolbarPadding),
                toolbar.Y + row * (ToolbarItemHeight + ToolbarRowSpacing),
                ToolbarItemWidth,
                ToolbarItemHeight);
        }

        private void SelectionSaveUndo()
        {
            selectionUndoStack.Insert(0, CloneAnnotations(selectionAnnotations));
            TrimStack(selectionUndoStack, MaxUndoStates);
        }

        private void SelectionUndo()
        {
            if (selectionUndoStack.Count == 0) return;
            selectionRedoStack.Insert(0, CloneAnnotations(selectionAnnotations));
            selectionAnnotations.Clear();
            selectionAnnotations.AddRange(selectionUndoStack[0]);
            selectionUndoStack.RemoveAt(0);
            selectedAnnotationIndex = -1;        // 撤销后旧选中索引失效
            annotationEditMode = AnnotationEditMode.None;
            ShowStatusHint("撤销 (" + selectionUndoStack.Count + " 步可撤销)");
            Invalidate();
        }

        private void SelectionRedo()
        {
            if (selectionRedoStack.Count == 0) return;
            selectionUndoStack.Insert(0, CloneAnnotations(selectionAnnotations));
            selectionAnnotations.Clear();
            selectionAnnotations.AddRange(selectionRedoStack[0]);
            selectionRedoStack.RemoveAt(0);
            selectedAnnotationIndex = -1;        // 重做后旧选中索引失效
            annotationEditMode = AnnotationEditMode.None;
            ShowStatusHint("重做 (" + selectionRedoStack.Count + " 步可重做)");
            Invalidate();
        }

        private void SelectionEraseAt(PointF point)
        {
            Graphics g = GetMeasureGraphics();
            for (int i = selectionAnnotations.Count - 1; i >= 0; i--)
            {
                if (selectionAnnotations[i].HitTest(point, 12.0f, g))
                {
                    selectionAnnotations.RemoveAt(i);
                }
            }
        }

        private Bitmap RenderAnnotatedRegion()
        {
            var bmp = new Bitmap(selectedRegionView.Width, selectedRegionView.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawImage(background,
                    new Rectangle(0, 0, selectedRegionView.Width, selectedRegionView.Height),
                    selectedRegionView,
                    GraphicsUnit.Pixel);

                g.TranslateTransform(-selectedRegionView.Left, -selectedRegionView.Top);
                for (int i = 0; i < selectionAnnotations.Count; i++)
                {
                    selectionAnnotations[i].Draw(g);
                }
                if (activeAnnotationStroke != null) activeAnnotationStroke.Draw(g);
                if (activeAnnotationShape != null) activeAnnotationShape.Draw(g);
                if (activePixelateArea != null) activePixelateArea.Draw(g);
                g.ResetTransform();
                OverlayForm.DrawWatermark(g, bmp.Width, bmp.Height);
            }
            return bmp;
        }

        private void CopyAnnotatedRegion()
        {
            Bitmap bmp = RenderAnnotatedRegion();
            string error;
            if (!ClipboardService.TrySetImage(bmp, out error))
            {
                bmp.Dispose();
                ShowToast("复制截图失败", error);
                return;
            }
            CaptureStore.Add(bmp);
            PlayCaptureSound();
            ShowToast("已复制截图", selectedRegionView.Width + " x " + selectedRegionView.Height);
        }

        private void SaveAnnotatedRegion()
        {
            string file = AppPaths.NewImagePath("_region");
            Bitmap bmp = RenderAnnotatedRegion();
            try
            {
                string savedFile = SaveImageAutoFormat(bmp, file);
                CaptureStore.Add(bmp);
                PlayCaptureSound();
                ShowToast("已保存截图", savedFile);
            }
            catch
            {
                bmp.Dispose();
            }
        }

        private void PinAnnotatedRegion()
        {
            Bitmap bmp = RenderAnnotatedRegion();
            CaptureStore.Add((Bitmap)bmp.Clone());
            Point location = new Point(virtualBounds.Left + selectedRegionView.Left + 24, virtualBounds.Top + selectedRegionView.Top + 24);
            PinManager.Show(bmp, location, true);
            ShowToast("已贴图", selectedRegionView.Width + " x " + selectedRegionView.Height);
        }

        private static string AnnotationToolName(DrawingTool t)
        {
            int i = (int)t;
            return (i >= 0 && i < DrawingToolNames.Length) ? DrawingToolNames[i] : "";
        }

        private void DrawSelection(Graphics g)
        {
            if (selectionAction == SelectionAction.None) return;

            Rectangle full = new Rectangle(0, 0, Width, Height);
            Rectangle rect = Rectangle.Empty;
            if (isSelecting)
            {
                rect = Normalize(selectionStartView, selectionCurrentView);
            }
            else if (hasSelectedRegion)
            {
                rect = selectedRegionView;
            }

            if (!isSelecting && !hasSelectedRegion && hasHoveredWindow && selectionAction == SelectionAction.PixPinCapture)
            {
                // 窗口高亮加内边距
                Rectangle padded = Rectangle.Inflate(hoveredWindowView, 3, 3);
                using (var fill = new SolidBrush(Color.FromArgb(30, 0, 120, 215)))
                using (var pen = new Pen(Color.FromArgb(120, 0, 140, 255), 2.5f))
                {
                    g.FillRectangle(fill, padded);
                    g.DrawRectangle(pen, padded);
                }

                // 显示窗口标题
                if (!string.IsNullOrEmpty(hoveredWindowTitle))
                {
                    using (var font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var bg = new SolidBrush(Color.FromArgb(200, 0, 100, 200)))
                    {
                        string displayTitle = hoveredWindowTitle.Length > 50 ? hoveredWindowTitle.Substring(0, 50) + "..." : hoveredWindowTitle;
                        SizeF textSize = g.MeasureString(displayTitle, font);
                        float tx = padded.Left;
                        float ty = padded.Top - textSize.Height - 6;
                        if (ty < 2) ty = padded.Top + 4;
                        g.FillRectangle(bg, tx, ty, textSize.Width + 10, textSize.Height + 4);
                        g.DrawString(displayTitle, font, Brushes.White, tx + 5, ty + 2);
                    }
                }
            }

            using (var dim = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            using (var path = new GraphicsPath())
            {
                path.FillMode = FillMode.Alternate;
                path.AddRectangle(full);
                if (!rect.IsEmpty && rect.Width > 1 && rect.Height > 1)
                {
                    path.AddRectangle(rect);
                }
                g.FillPath(dim, path);
            }

            // 十字参考线（所有选区模式拖拽时显示）
            if (isSelecting)
            {
                using (var linePen = new Pen(Color.FromArgb(50, 0, 120, 215), 1))
                {
                    linePen.DashStyle = DashStyle.Dot;
                    g.DrawLine(linePen, selectionCurrentView.X, 0, selectionCurrentView.X, Height);
                    g.DrawLine(linePen, 0, selectionCurrentView.Y, Width, selectionCurrentView.Y);
                }
            }

            if (!rect.IsEmpty && rect.Width > 1 && rect.Height > 1)
            {
                using (var border = new Pen(Color.FromArgb(0, 120, 215), 2))
                {
                    if (hasSelectedRegion && selectionAction == SelectionAction.PixPinCapture)
                    {
                        // 蓝白交替蚂蚁线
                        border.DashStyle = DashStyle.Dash;
                        border.DashPattern = new float[] { 6, 3 };
                        border.DashOffset = marchOffset;
                    }
                    g.DrawRectangle(border, rect);
                }

                // 蚂蚁线模式下叠加白色虚线，形成蓝白交替效果
                if (hasSelectedRegion && selectionAction == SelectionAction.PixPinCapture)
                {
                    using (var whiteBorder = new Pen(Color.White, 1))
                    {
                        whiteBorder.DashStyle = DashStyle.Dash;
                        whiteBorder.DashPattern = new float[] { 6, 3 };
                        whiteBorder.DashOffset = marchOffset + 4.5f;
                        g.DrawRectangle(whiteBorder, rect);
                    }
                }

                // 尺寸/坐标标签（标注模式下的信息栏另有显示）
                bool showSizeLabel = !(isAnnotating && hasSelectedRegion && selectionAction == SelectionAction.PixPinCapture);
                // 拖拽手柄时也显示实时尺寸
                if (isResizingSelection)
                    showSizeLabel = true;

                if (showSizeLabel)
                {
                    using (var font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                    {
                        string text = "(" + rect.X + ", " + rect.Y + ")  " + rect.Width + " × " + rect.Height;
                        SizeF textSize = g.MeasureString(text, font);
                        float labelX = rect.Left;
                        float labelY = Math.Max(4, rect.Top - 24);
                        g.FillRectangle(bg, labelX, labelY, textSize.Width + 8, textSize.Height + 4);
                        g.DrawString(text, font, Brushes.White, labelX + 4, labelY + 2);
                    }
                }

                if (hasSelectedRegion && selectionAction == SelectionAction.PixPinCapture)
                {
                    DrawResizeHandles(g, rect);

                    GraphicsState state = g.Save();
                    g.SetClip(rect);
                    for (int i = 0; i < selectionAnnotations.Count; i++)
                    {
                        selectionAnnotations[i].Draw(g);
                    }
                    if (activeAnnotationStroke != null) activeAnnotationStroke.Draw(g);
                    if (activePixelateArea != null) activePixelateArea.Draw(g);
                    if (activeAnnotationShape != null)
                    {
                        activeAnnotationShape.Draw(g);
                        // QQ截图：绘制形状时在右下角实时显示尺寸
                        DrawShapeDimensionLabel(g, activeAnnotationShape);
                    }
                    g.Restore(state);

                    // 选择工具：在选中标注上绘制包围盒 + 8 个手柄
                    if (annotationTool == DrawingTool.Select && selectedAnnotationIndex >= 0 &&
                        selectedAnnotationIndex < selectionAnnotations.Count)
                    {
                        Graphics mg = GetMeasureGraphics();
                        RectangleF ab = selectionAnnotations[selectedAnnotationIndex].GetBounds(mg);
                        if (ab.Width > 0 && ab.Height > 0)
                        {
                            using (var boxPen = new Pen(Color.FromArgb(80, 100, 180, 255), 1.2f))
                            {
                                boxPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                                g.DrawRectangle(boxPen, ab.X, ab.Y, ab.Width, ab.Height);
                            }
                            PointF[] handles = RectHandlePointsPublic(ab);
                            using (var hBrush = new SolidBrush(Color.White))
                            using (var hPen = new Pen(Color.FromArgb(180, 100, 180, 255)))
                            {
                                for (int i = 0; i < handles.Length; i++)
                                {
                                    g.FillRectangle(hBrush, handles[i].X - 4, handles[i].Y - 4, 8, 8);
                                    g.DrawRectangle(hPen, handles[i].X - 4, handles[i].Y - 4, 8, 8);
                                }
                            }
                        }
                    }

                    DrawToolbar(g, rect);

                    if (isAnnotating)
                    {
                        using (var font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                        {
                            string fillIndicator = (shapeFilled && (annotationTool == DrawingTool.Rectangle || annotationTool == DrawingTool.Ellipse)) ? "  [填充]" : "";
                            string info = AnnotationToolName(annotationTool) + fillIndicator + "  " + currentWidth.ToString("0") + "px  " + rect.Width + "×" + rect.Height;
                            SizeF size = g.MeasureString(info, font);
                            float ix = rect.Left;
                            float iy = Math.Max(4, rect.Top - 26);
                            g.FillRectangle(bg, ix, iy, size.Width + 32, size.Height + 4);
                            g.DrawString(info, font, Brushes.White, ix + 4, iy + 2);

                            // 颜色指示色块
                            using (var brush = new SolidBrush(currentColor))
                            {
                                g.FillEllipse(brush, ix + size.Width + 10, iy + (size.Height - 14) / 2, 14, 14);
                            }
                            using (var cpen = new Pen(Color.White, 1.5f))
                            {
                                g.DrawEllipse(cpen, ix + size.Width + 10, iy + (size.Height - 14) / 2, 14, 14);
                            }
                        }
                    }
                }
            }

            if (selectionAction == SelectionAction.PixPinCapture)
            {
                Point cursor = PointToClient(Cursor.Position);
                bool inSelection = hasSelectedRegion && selectedRegionView.Contains(cursor);

                if (!isAnnotating)
                {
                    if (isSelecting)
                    {
                        DrawMagnifier(g, selectionCurrentView);
                    }
                    else
                    {
                        DrawMagnifier(g, cursor);
                        DrawCursorPosition(g);
                    }
                }
                else if (!inSelection)
                {
                    // 标注模式下鼠标在选区外仍显示放大镜
                    DrawMagnifier(g, cursor);
                }
            }

            // 标注模式下绘制工具半径预览圈
            if (isAnnotating && hasSelectedRegion && selectionAction == SelectionAction.PixPinCapture)
            {
                DrawToolRadiusPreview(g);
            }
        }

        private void DrawResizeHandles(Graphics g, Rectangle rect)
        {
            Point[] handles = GetHandlePositions(rect);
            int half = HandleSize / 2;
            using (var brush = new SolidBrush(Color.White))
            using (var pen = new Pen(Color.FromArgb(0, 120, 215), 1))
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    Rectangle hr = new Rectangle(handles[i].X - half, handles[i].Y - half, HandleSize, HandleSize);
                    g.FillRectangle(brush, hr);
                    g.DrawRectangle(pen, hr);
                }
            }
        }

        private void DrawToolbar(Graphics g, Rectangle selectionRect)
        {
            Rectangle toolbar = GetToolbarBounds(selectionRect);
            int radius = 6;

            // 计算按钮行数
            int totalItems = ToolbarLabels.Length;
            int totalWidth = totalItems * (ToolbarItemWidth + ToolbarPadding) - ToolbarPadding;
            int availWidth = Math.Max(selectionRect.Width + 80, 380);
            int itemsPerRow = totalItems;
            if (totalWidth > availWidth)
            {
                itemsPerRow = Math.Max(6, (availWidth + ToolbarPadding) / (ToolbarItemWidth + ToolbarPadding));
                if (itemsPerRow > totalItems) itemsPerRow = totalItems;
            }
            int rows = (totalItems + itemsPerRow - 1) / itemsPerRow;

            // 阴影
            using (var shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
            {
                var shadowRect = new Rectangle(toolbar.X + 2, toolbar.Y + 3, toolbar.Width, toolbar.Height);
                FillRoundedRectangle(g, shadow, shadowRect, radius);
            }

            // 工具栏主体 - 圆角背景
            using (var bg = new SolidBrush(Color.FromArgb(235, 38, 38, 42)))
            using (var border = new Pen(Color.FromArgb(90, 255, 255, 255)))
            {
                FillRoundedRectangle(g, bg, toolbar, radius);
                DrawRoundedRectangle(g, border, toolbar, radius);
            }

            using (var hoverBg = new SolidBrush(Color.FromArgb(60, 0, 120, 215)))
            using (var activeBg = new SolidBrush(Color.FromArgb(100, 0, 120, 215)))
            using (var font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                // 分组分隔线（绘图 | 调整 | 操作）
                using (var sepPen = new Pen(Color.FromArgb(50, 255, 255, 255)))
                {
                    for (int si = 0; si < ToolbarLabels.Length; si++)
                    {
                        if (si == 12 || si == 15)  // 橡皮后、填充后
                        {
                            Rectangle prevItem = GetToolbarItemRect(toolbar, si - 1);
                            int sepX = prevItem.Right + ToolbarPadding / 2;
                            int row = si / itemsPerRow;
                            float sepTop = toolbar.Y + row * (ToolbarItemHeight + ToolbarRowSpacing) + 4;
                            float sepBottom = sepTop + ToolbarItemHeight - 8;
                            g.DrawLine(sepPen, sepX, sepTop, sepX, sepBottom);
                        }
                    }
                }

                for (int i = 0; i < ToolbarLabels.Length; i++)
                {
                    Rectangle item = GetToolbarItemRect(toolbar, i);
                    if (isAnnotating && IsToolbarItemActiveTool(i))
                    {
                        g.FillRectangle(activeBg, item);
                    }
                    else if (i == hoveredToolbarItem)
                    {
                        g.FillRectangle(hoverBg, item);
                    }
                    // 工具项（0-11）绘制图标 + 文字
                    if (i < ToolbarToolMap.Length)
                    {
                        DrawToolbarIcon(g, ToolbarToolMap[i], item);
                        using (var format = new StringFormat())
                        {
                            format.Alignment = StringAlignment.Center;
                            format.LineAlignment = StringAlignment.Far;
                            g.DrawString(ToolbarLabels[i], font, Brushes.White, item, format);
                        }
                    }
                    // 线宽按钮（14-15）绘制线宽预览 + 文字
                    else if (i == 14 || i == 15)
                    {
                        float lineW = i == 14 ? 1.5f : 5.0f;
                        float cx = item.X + item.Width / 2.0f;
                        float cy = item.Y + 10;
                        using (var wpen = new Pen(Color.White, lineW))
                        {
                            g.DrawLine(wpen, cx - 8, cy, cx + 8, cy);
                        }
                        using (var format = new StringFormat())
                        {
                            format.Alignment = StringAlignment.Center;
                            format.LineAlignment = StringAlignment.Far;
                            g.DrawString(ToolbarLabels[i], font, Brushes.White, item, format);
                        }
                    }
                    // 填充按钮（16）绘制填充/空心矩形 + 文字
                    else if (i == 16)
                    {
                        float cx = item.X + item.Width / 2.0f;
                        float cy = item.Y + 10;
                        using (var fpen = new Pen(Color.White, 1.5f))
                        {
                            if (shapeFilled)
                            {
                                using (var fbrush = new SolidBrush(Color.FromArgb(200, 100, 180, 255)))
                                    g.FillRectangle(fbrush, cx - 6, cy - 6, 12, 12);
                            }
                            g.DrawRectangle(fpen, cx - 6, cy - 6, 12, 12);
                        }
                        using (var format = new StringFormat())
                        {
                            format.Alignment = StringAlignment.Center;
                            format.LineAlignment = StringAlignment.Far;
                            g.DrawString(ToolbarLabels[i], font, shapeFilled ? Brushes.LightSkyBlue : Brushes.White, item, format);
                        }
                    }
                    // 操作项（15+）只绘制文字
                    else
                    {
                        using (var format = new StringFormat())
                        {
                            format.Alignment = StringAlignment.Center;
                            format.LineAlignment = StringAlignment.Center;
                            g.DrawString(ToolbarLabels[i], font, Brushes.White, item, format);
                        }
                    }
                }

                // 在工具栏底部集成颜色色块行
                int colorRowY = toolbar.Y + rows * (ToolbarItemHeight + ToolbarRowSpacing) - ToolbarRowSpacing + 2;
                int swatchSize = 16;
                int swatchSpacing = 3;
                int swatchStartX = toolbar.X + 4;
                using (var sepPen = new Pen(Color.FromArgb(50, 255, 255, 255)))
                {
                    g.DrawLine(sepPen, toolbar.X + 4, colorRowY - 1, toolbar.Right - 4, colorRowY - 1);
                }
                for (int ci = 0; ci < PaletteColors.Length; ci++)
                {
                    int sx = swatchStartX + ci * (swatchSize + swatchSpacing);
                    var srect = new Rectangle(sx, colorRowY + 2, swatchSize, swatchSize);
                    using (var sbrush = new SolidBrush(PaletteColors[ci]))
                    {
                        g.FillEllipse(sbrush, srect);
                    }
                    if (PaletteColors[ci] == currentColor)
                    {
                        using (var spen = new Pen(Color.White, 2))
                            g.DrawEllipse(spen, srect);
                    }
                    else
                    {
                        using (var spen = new Pen(Color.FromArgb(80, 255, 255, 255), 1))
                            g.DrawEllipse(spen, srect);
                    }
                }
                // "+" 自定义颜色按钮
                {
                    int plusIdx = PaletteColors.Length;
                    int px = swatchStartX + plusIdx * (swatchSize + swatchSpacing);
                    var prect = new Rectangle(px, colorRowY + 2, swatchSize, swatchSize);
                    using (var ppen = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f))
                    {
                        g.DrawEllipse(ppen, prect);
                        int pmid = swatchSize / 2;
                        g.DrawLine(ppen, px + pmid - 3, colorRowY + 2 + pmid, px + pmid + 3, colorRowY + 2 + pmid);
                        g.DrawLine(ppen, px + pmid, colorRowY + 2 + pmid - 3, px + pmid, colorRowY + 2 + pmid + 3);
                    }
                }

                // Hover 提示气泡
                if (hoveredToolbarItem >= 0 && hoveredToolbarItem < ToolbarLabels.Length)
                {
                    Rectangle hoverItem = GetToolbarItemRect(toolbar, hoveredToolbarItem);
                    string tipText = ToolbarLabels[hoveredToolbarItem];
                    if (hoveredToolbarItem < ToolbarShortcuts.Length && !string.IsNullOrEmpty(ToolbarShortcuts[hoveredToolbarItem]))
                    {
                        tipText += " (" + ToolbarShortcuts[hoveredToolbarItem] + ")";
                    }

                    using (var tipFont = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        SizeF tipSize = g.MeasureString(tipText, tipFont);
                        float tipX = hoverItem.X + (hoverItem.Width - tipSize.Width) / 2.0f - 4;
                        float tipY = toolbar.Y - tipSize.Height - 10;
                        if (tipY < 2) tipY = toolbar.Bottom + 4;

                        using (var tipBg = new SolidBrush(Color.FromArgb(220, 20, 20, 24)))
                        {
                            g.FillRectangle(tipBg, tipX, tipY, tipSize.Width + 8, tipSize.Height + 6);
                        }
                        using (var tipBorder = new Pen(Color.FromArgb(80, 255, 255, 255)))
                        {
                            g.DrawRectangle(tipBorder, tipX, tipY, tipSize.Width + 8, tipSize.Height + 6);
                        }
                        g.DrawString(tipText, tipFont, Brushes.White, tipX + 4, tipY + 3);
                    }
                }
            }
        }

        private static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        private static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle rect, int radius)
        {
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
            }
        }

        private bool IsToolbarItemActiveTool(int index)
        {
            return index >= 0 && index < ToolbarToolMap.Length && annotationTool == ToolbarToolMap[index];
        }

        private void DrawToolbarIcon(Graphics g, DrawingTool tool, Rectangle item)
        {
            // 图标区域在按钮上半部分
            float cx = item.X + item.Width / 2.0f;
            float cy = item.Y + 10;
            float s = 5.0f; // 图标半尺寸

            using (var pen = new Pen(Color.White, 1.5f))
            {
                switch (tool)
                {
                    case DrawingTool.Select:
                        // 虚线选择框 + 四角点
                        var dashPen = new Pen(Color.White, 1.2f);
                        dashPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        g.DrawRectangle(dashPen, cx - s, cy - s, s * 2, s * 2);
                        dashPen.Dispose();
                        g.FillEllipse(Brushes.White, cx - s - 1.5f, cy - s - 1.5f, 3, 3);
                        g.FillEllipse(Brushes.White, cx + s - 1.5f, cy - s - 1.5f, 3, 3);
                        g.FillEllipse(Brushes.White, cx - s - 1.5f, cy + s - 1.5f, 3, 3);
                        g.FillEllipse(Brushes.White, cx + s - 1.5f, cy + s - 1.5f, 3, 3);
                        break;
                    case DrawingTool.Pen:
                        // 对角线 + 圆点笔尖
                        g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s);
                        g.FillEllipse(Brushes.White, cx + s - 2, cy - s - 2, 4, 4);
                        break;
                    case DrawingTool.Highlighter:
                        // 粗半透明线
                        using (var hpen = new Pen(Color.FromArgb(160, 255, 255, 0), 3.5f))
                        {
                            g.DrawLine(hpen, cx - s, cy + s, cx + s, cy - s);
                        }
                        break;
                    case DrawingTool.Line:
                        g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s);
                        break;
                    case DrawingTool.Arrow:
                        g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s);
                        g.DrawLine(pen, cx + s, cy - s, cx + s - 4, cy - s + 1);
                        g.DrawLine(pen, cx + s, cy - s, cx + s - 1, cy - s + 4);
                        break;
                    case DrawingTool.Rectangle:
                        g.DrawRectangle(pen, cx - s, cy - s, s * 2, s * 2);
                        break;
                    case DrawingTool.Ellipse:
                        g.DrawEllipse(pen, cx - s, cy - s, s * 2, s * 2);
                        break;
                    case DrawingTool.Text:
                        using (var tfont = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            g.DrawString("T", tfont, Brushes.White, cx, cy, sf);
                        }
                        break;
                    case DrawingTool.Cover:
                        using (var cbrush = new SolidBrush(Color.FromArgb(200, 246, 246, 235)))
                        {
                            g.FillRectangle(cbrush, cx - s, cy - s, s * 2, s * 2);
                        }
                        g.DrawRectangle(pen, cx - s, cy - s, s * 2, s * 2);
                        break;
                    case DrawingTool.Blur:
                        // 小网格表示马赛克
                        int bs = 3;
                        for (int by = -1; by <= 1; by++)
                        {
                            for (int bx = -1; bx <= 1; bx++)
                            {
                                g.DrawRectangle(pen, cx + bx * bs - 1, cy + by * bs - 1, bs, bs);
                            }
                        }
                        break;
                    case DrawingTool.Pixelate:
                        // 实心大方块表示区域马赛克
                        g.FillRectangle(Brushes.White, cx - s, cy - s, s * 2, s * 2);
                        g.DrawRectangle(pen, cx - s, cy - s, s * 2, s * 2);
                        break;
                    case DrawingTool.NumberMarker:
                        g.DrawEllipse(pen, cx - s, cy - s, s * 2, s * 2);
                        using (var nfont = new Font(FontFamily.GenericSansSerif, 8, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            g.DrawString("1", nfont, Brushes.White, cx, cy, sf);
                        }
                        break;
                    case DrawingTool.Eraser:
                        g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                        g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
                        break;
                    case DrawingTool.Stamp:
                        using (var sfont = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            g.DrawString("★", sfont, Brushes.White, cx, cy, sf);
                        }
                        break;
                }
            }
        }

        private void DrawCursorPosition(Graphics g)
        {
            Point cursor = PointToClient(Cursor.Position);
            if (cursor.X < 0 || cursor.Y < 0 || cursor.X >= Width || cursor.Y >= Height) return;

            // 屏幕坐标
            int screenX = cursor.X + virtualBounds.Left;
            int screenY = cursor.Y + virtualBounds.Top;
            string posText = screenX + ", " + screenY;

            using (var font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            {
                SizeF size = g.MeasureString(posText, font);
                float x = cursor.X + 18;
                float y = cursor.Y + 18;
                if (x + size.Width + 6 > Width) x = cursor.X - size.Width - 24;
                if (y + size.Height + 4 > Height) y = cursor.Y - size.Height - 24;
                g.FillRectangle(bg, x, y, size.Width + 6, size.Height + 4);
                g.DrawString(posText, font, Brushes.White, x + 3, y + 2);
            }
        }


        /// <summary>获取工具栏内颜色行的 Y 坐标起始位置。</summary>
        private int GetToolbarColorRowY(Rectangle toolbar)
        {
            int totalItems = ToolbarLabels.Length;
            int totalWidth = totalItems * (ToolbarItemWidth + ToolbarPadding) - ToolbarPadding;
            int availWidth = toolbar.Width;
            int itemsPerRow = Math.Min(totalItems, Math.Max(6, (availWidth + ToolbarPadding) / (ToolbarItemWidth + ToolbarPadding)));
            int btnRows = (totalItems + itemsPerRow - 1) / itemsPerRow;
            return toolbar.Y + btnRows * (ToolbarItemHeight + ToolbarRowSpacing) - ToolbarRowSpacing + 2;
        }

        private int HitTestColorPalette(Point location)
        {
            if (!hasSelectedRegion || selectionAction != SelectionAction.PixPinCapture || !isAnnotating)
                return -1;

            Rectangle toolbar = GetToolbarBounds(selectedRegionView);
            if (!toolbar.Contains(location)) return -1;

            int colorRowY = GetToolbarColorRowY(toolbar);
            int swatchSize = 16;
            int swatchSpacing = 3;
            int startX = toolbar.X + 4;

            // 检查点击是否在颜色行区域内
            if (location.Y < colorRowY || location.Y > colorRowY + swatchSize + 4)
                return -1;

            for (int i = 0; i < PaletteColors.Length; i++)
            {
                int x = startX + i * (swatchSize + swatchSpacing);
                var rect = new Rectangle(x, colorRowY + 2, swatchSize, swatchSize);
                if (rect.Contains(location)) return i;
            }

            // 检查自定义颜色按钮（"+"）
            {
                int px = startX + PaletteColors.Length * (swatchSize + swatchSpacing);
                var rect = new Rectangle(px, colorRowY + 2, swatchSize, swatchSize);
                if (rect.Contains(location)) return PaletteColors.Length;
            }
            return -1;
        }

        private void DrawToolRadiusPreview(Graphics g)
        {
            Point cursor = PointToClient(Cursor.Position);
            if (!selectedRegionView.Contains(cursor)) return;

            float radius = 0;
            bool show = false;

            if (annotationTool == DrawingTool.Eraser)
            {
                radius = 12.0f;
                show = true;
            }
            else if (annotationTool == DrawingTool.Pen || annotationTool == DrawingTool.Highlighter)
            {
                radius = currentWidth / 2.0f;
                show = true;
            }
            else if (annotationTool == DrawingTool.Blur)
            {
                radius = currentWidth * 3.5f;
                show = true;
            }

            if (!show || radius < 2) return;

            using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1))
            {
                g.DrawEllipse(pen, cursor.X - radius, cursor.Y - radius, radius * 2, radius * 2);
            }
        }


        /// <summary>拖拽时在光标旁显示紧凑版放大镜（像素网格 + RGB + 尺寸）。</summary>
        private void DrawMagnifier(Graphics g, Point cursor)
        {
            int sourceX = cursor.X;
            int sourceY = cursor.Y;
            if (sourceX < 0 || sourceY < 0 || sourceX >= background.Width || sourceY >= background.Height) return;

            int gridSize = 7;
            int cellSize = 11;
            int padding = 6;
            int infoHeight = 24;
            int boxWidth = padding * 2 + gridSize * cellSize;
            int boxHeight = padding * 2 + gridSize * cellSize + infoHeight;

            // 定位：右下方，靠近光标但不遮挡选区
            int left = cursor.X + 20;
            int top = cursor.Y + 20;
            if (left + boxWidth > Width) left = cursor.X - boxWidth - 20;
            if (top + boxHeight > Height) top = cursor.Y - boxHeight - 20;

            // 使用缓存的像素数据，仅当光标位置变化时重新读取
            Color centerColor = EnsureMagnifierCache(sourceX, sourceY, gridSize);

            // 如果缓存失效，重新生成放大镜像素网格 Bitmap
            int gridPixelW = gridSize * cellSize;
            int gridPixelH = gridSize * cellSize;
            if (magnifierGridBmp == null || magnifierGridBmp.Width != gridPixelW || magnifierGridBmp.Height != gridPixelH
                || magnifierGridCenter != new Point(sourceX, sourceY))
            {
                if (magnifierGridBmp != null) magnifierGridBmp.Dispose();
                magnifierGridBmp = new Bitmap(gridPixelW, gridPixelH, PixelFormat.Format32bppArgb);
                magnifierGridCenter = new Point(sourceX, sourceY);
                using (var gg = Graphics.FromImage(magnifierGridBmp))
                {
                    for (int y = 0; y < gridSize; y++)
                    {
                        for (int x = 0; x < gridSize; x++)
                        {
                            int idx = y * gridSize + x;
                            Color c = (magnifierCachePixels != null && idx < magnifierCachePixels.Length)
                                ? magnifierCachePixels[idx]
                                : Color.FromArgb(32, 32, 32);
                            using (var brush = new SolidBrush(c))
                            {
                                gg.FillRectangle(brush, x * cellSize, y * cellSize, cellSize - 1, cellSize - 1);
                            }
                        }
                    }
                }
            }

            // 阴影
            using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            {
                g.FillRectangle(shadow, left + 2, top + 2, boxWidth, boxHeight);
            }

            // 背景
            using (var bg = new SolidBrush(Color.FromArgb(230, 28, 28, 32)))
            {
                g.FillRectangle(bg, left, top, boxWidth, boxHeight);
            }
            using (var border = new Pen(Color.FromArgb(100, 255, 255, 255)))
            {
                g.DrawRectangle(border, left, top, boxWidth - 1, boxHeight - 1);
            }

            // 绘制缓存的像素网格
            int pixelX = left + padding;
            int pixelY = top + padding;
            g.DrawImage(magnifierGridBmp, pixelX, pixelY);

            // 中心像素边框高亮
            int cx = pixelX + (gridSize / 2) * cellSize;
            int cy = pixelY + (gridSize / 2) * cellSize;
            using (var highlight = new Pen(Color.White, 2))
            {
                g.DrawRectangle(highlight, cx - 1, cy - 1, cellSize + 1, cellSize + 1);
            }

            // 底部信息行：RGB + 坐标
            using (var font = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var infoBg = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            {
                int infoY = top + padding + gridSize * cellSize + 3;
                string rgb = "#" + centerColor.R.ToString("X2") + centerColor.G.ToString("X2") + centerColor.B.ToString("X2");
                string pos = (sourceX + virtualBounds.Left) + "," + (sourceY + virtualBounds.Top);
                string infoText = rgb + "  " + pos;

                // 颜色预览色块
                using (var swatch = new SolidBrush(centerColor))
                {
                    g.FillRectangle(swatch, left + padding, infoY + 3, 14, 14);
                }
                using (var swatchBorder = new Pen(Color.FromArgb(180, 255, 255, 255)))
                {
                    g.DrawRectangle(swatchBorder, left + padding, infoY + 3, 14, 14);
                }

                g.DrawString(infoText, font, Brushes.White, left + padding + 18, infoY + 3);
            }
        }

        /// <summary>仅在光标位置变化时读取背景像素，缓存结果避免每帧 LockBits。</summary>
        private Color EnsureMagnifierCache(int sourceX, int sourceY, int gridSize)
        {
            // 命中缓存
            if (magnifierCachePixels != null && magnifierCacheCenter == new Point(sourceX, sourceY) && magnifierCacheGridSize == gridSize)
                return magnifierCacheCenterColor;

            int total = gridSize * gridSize;
            if (magnifierCachePixels == null || magnifierCachePixels.Length != total)
                magnifierCachePixels = new Color[total];

            int startX = sourceX - gridSize / 2;
            int startY = sourceY - gridSize / 2;

            // 一次性读取所需的矩形区域
            int lockX = Math.Max(0, startX);
            int lockY = Math.Max(0, startY);
            int lockRight = Math.Min(background.Width, startX + gridSize);
            int lockBottom = Math.Min(background.Height, startY + gridSize);
            int lockW = lockRight - lockX;
            int lockH = lockBottom - lockY;

            byte[] rawPixels = null;
            int stride = 0;
            if (lockW > 0 && lockH > 0)
            {
                var lockRect = new Rectangle(lockX, lockY, lockW, lockH);
                var bmpData = background.LockBits(lockRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                stride = bmpData.Stride;
                rawPixels = new byte[stride * lockH];
                Marshal.Copy(bmpData.Scan0, rawPixels, 0, rawPixels.Length);
                background.UnlockBits(bmpData);
            }

            Color centerColor = Color.Gray;
            for (int y = 0; y < gridSize; y++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    int px = startX + x;
                    int py = startY + y;
                    Color c;
                    if (rawPixels != null && px >= lockX && px < lockRight && py >= lockY && py < lockBottom)
                    {
                        int offset = (py - lockY) * stride + (px - lockX) * 4;
                        c = Color.FromArgb(rawPixels[offset + 2], rawPixels[offset + 1], rawPixels[offset]);
                    }
                    else
                    {
                        c = Color.FromArgb(32, 32, 32);
                    }
                    magnifierCachePixels[y * gridSize + x] = c;
                    if (x == gridSize / 2 && y == gridSize / 2) centerColor = c;
                }
            }

            magnifierCacheCenter = new Point(sourceX, sourceY);
            magnifierCacheGridSize = gridSize;
            magnifierCacheCenterColor = centerColor;
            return centerColor;
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            return new Rectangle(
                Math.Min(a.X, b.X),
                Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X),
                Math.Abs(a.Y - b.Y));
        }

        private static Rectangle Clip(Rectangle rect, Rectangle bounds)
        {
            int left = Math.Max(rect.Left, bounds.Left);
            int top = Math.Max(rect.Top, bounds.Top);
            int right = Math.Min(rect.Right, bounds.Right);
            int bottom = Math.Min(rect.Bottom, bounds.Bottom);
            if (right <= left || bottom <= top) return Rectangle.Empty;
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        /// <summary>绘制形状旁边的尺寸标签（QQ截图风格）。</summary>
        private void DrawShapeDimensionLabel(Graphics g, ShapeAnnotation shape)
        {
            string dimText;
            float labelX, labelY;

            if (shape.Tool == DrawingTool.Line || shape.Tool == DrawingTool.Arrow)
            {
                float len = Distance(shape.Start, shape.End);
                dimText = ((int)len) + "px";
                labelX = (shape.Start.X + shape.End.X) / 2.0f;
                labelY = (shape.Start.Y + shape.End.Y) / 2.0f - 16;
            }
            else
            {
                int w = Math.Abs((int)(shape.End.X - shape.Start.X));
                int h = Math.Abs((int)(shape.End.Y - shape.Start.Y));
                dimText = w + " × " + h;
                labelX = Math.Max(shape.Start.X, shape.End.X) + 6;
                labelY = Math.Max(shape.Start.Y, shape.End.Y) + 4;
            }

            using (var font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            {
                SizeF size = g.MeasureString(dimText, font);
                g.FillRectangle(bg, labelX, labelY, size.Width + 6, size.Height + 2);
                g.DrawString(dimText, font, Brushes.White, labelX + 3, labelY + 1);
            }
        }
    }
}

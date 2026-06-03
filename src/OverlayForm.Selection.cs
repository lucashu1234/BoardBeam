using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed partial class OverlayForm
    {
        private const int HandleSize = 8;
        private const int HandleHitTolerance = 6;
        private static readonly string[] ToolbarLabels = { "画笔", "矩形", "椭圆", "箭头", "文字", "马赛克", "撤销", "复制", "保存", "贴图", "关闭" };
        private const int ToolbarItemWidth = 46;
        private const int ToolbarItemHeight = 28;
        private const int ToolbarPadding = 2;

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
                Invalidate();
                return;
            }

            if (action == SelectionAction.PixPinCapture)
            {
                selectedRegionView = rect;
                hasSelectedRegion = true;
                isAnnotating = true;
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
            return "点击窗口或拖选区域";
        }

        private bool HandleSelectionKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (isAnnotating)
                {
                    isAnnotating = false;
                    Invalidate();
                    return true;
                }
                if (hasSelectedRegion)
                {
                    hasSelectedRegion = false;
                    selectedRegionView = Rectangle.Empty;
                    selectionAnnotations.Clear();
                    selectionUndoStack.Clear();
                    selectionRedoStack.Clear();
                    isAnnotating = false;
                    Invalidate();
                }
                else
                {
                    Close();
                }
                return true;
            }

            if (e.KeyCode == Keys.Space && !isAnnotating)
            {
                CaptureWindowUnderCursor();
                return true;
            }

            if (e.KeyCode == Keys.C && !e.Control && !e.Shift && !isAnnotating)
            {
                CopyPixelColor(Cursor.Position, false);
                return true;
            }

            if (e.KeyCode == Keys.C && e.Shift && !e.Control && !isAnnotating)
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
                if (e.KeyCode == Keys.D1) { currentColor = Color.Red; Invalidate(); return true; }
                if (e.KeyCode == Keys.D2) { currentColor = Color.Gold; Invalidate(); return true; }
                if (e.KeyCode == Keys.D3) { currentColor = Color.LimeGreen; Invalidate(); return true; }
                if (e.KeyCode == Keys.D4) { currentColor = Color.DeepSkyBlue; Invalidate(); return true; }
                if (e.KeyCode == Keys.D5) { currentColor = Color.Blue; Invalidate(); return true; }
                if (e.KeyCode == Keys.D6) { currentColor = Color.Magenta; Invalidate(); return true; }
                if (e.KeyCode == Keys.D7) { currentColor = Color.White; Invalidate(); return true; }
                if (e.KeyCode == Keys.D8) { currentColor = Color.Black; Invalidate(); return true; }
                if (e.KeyCode == Keys.P) { annotationTool = DrawingTool.Pen; Invalidate(); return true; }
                if (e.KeyCode == Keys.H) { annotationTool = DrawingTool.Highlighter; Invalidate(); return true; }
                if (e.KeyCode == Keys.L) { annotationTool = DrawingTool.Line; Invalidate(); return true; }
                if (e.KeyCode == Keys.A) { annotationTool = DrawingTool.Arrow; Invalidate(); return true; }
                if (e.KeyCode == Keys.R) { annotationTool = DrawingTool.Rectangle; Invalidate(); return true; }
                if (e.KeyCode == Keys.O) { annotationTool = DrawingTool.Ellipse; Invalidate(); return true; }
                if (e.KeyCode == Keys.V) { annotationTool = DrawingTool.Cover; Invalidate(); return true; }
                if (e.KeyCode == Keys.X) { annotationTool = DrawingTool.Blur; Invalidate(); return true; }
                if (e.KeyCode == Keys.E) { annotationTool = DrawingTool.Eraser; Invalidate(); return true; }
                if (e.KeyCode == Keys.M) { annotationTool = DrawingTool.NumberMarker; Invalidate(); return true; }
                if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus) { currentWidth += 1.0f; if (currentWidth > 40.0f) currentWidth = 40.0f; Invalidate(); return true; }
                if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus) { currentWidth -= 1.0f; if (currentWidth < 1.0f) currentWidth = 1.0f; Invalidate(); return true; }
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

            if (selectionAction == SelectionAction.PixPinCapture && (e.KeyCode == Keys.P || e.KeyCode == Keys.T))
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
            if (key == Keys.Left) rect.Width -= step;
            if (key == Keys.Right) rect.Width += step;
            if (key == Keys.Up) rect.Height -= step;
            if (key == Keys.Down) rect.Height += step;
            if (rect.Width < 4) rect.Width = 4;
            if (rect.Height < 4) rect.Height = 4;
            return Clip(rect, new Rectangle(0, 0, Width, Height));
        }

        private void CaptureWindowUnderCursor()
        {
            Hide();
            Application.DoEvents();
            Thread.Sleep(80);

            Rectangle screenRect;
            bool found = CaptureTool.TryGetWindowUnderCursor(out screenRect);
            Show();
            Activate();
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
            selectionStartView = viewRect.Location;
            selectionCurrentView = new Point(viewRect.Right, viewRect.Bottom);
            Invalidate();
        }

        private void CompleteSelectionAction(SelectionAction action, Rectangle rect)
        {
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
        }

        private bool ShouldCloseAfterSelection(SelectionAction action)
        {
            return action == SelectionAction.CopyRegion ||
                   action == SelectionAction.SaveRegion ||
                   action == SelectionAction.PinRegion ||
                   action == SelectionAction.RecordRegion ||
                   action == SelectionAction.ScrollCapture;
        }

        private Rectangle ViewRectToScreenRect(Rectangle rect)
        {
            return new Rectangle(virtualBounds.Left + rect.Left, virtualBounds.Top + rect.Top, rect.Width, rect.Height);
        }

        private void StartRecordingRegion(Rectangle rect)
        {
            Rectangle screenRect = ViewRectToScreenRect(rect);
            Hide();
            Application.DoEvents();
            Thread.Sleep(160);
            RecordingTool.Start(screenRect);
        }

        private void StartScrollingCapture(Rectangle rect)
        {
            Rectangle screenRect = ViewRectToScreenRect(rect);
            Hide();
            Application.DoEvents();
            Thread.Sleep(160);
            try
            {
                string file = ScrollingCaptureTool.Capture(screenRect);
                MessageBox.Show("长截图已保存：\n" + file, "BoardBeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("长截图失败：\n" + ex.Message, "BoardBeam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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
            using (Bitmap bmp = RenderToBitmap())
            using (Bitmap crop = bmp.Clone(rect, PixelFormat.Format32bppArgb))
            {
                CaptureStore.Add(crop);
                string error;
                if (!ClipboardService.TrySetImage(crop, out error))
                {
                    ShowToast("复制截图失败", error);
                    return;
                }
            }
            ShowToast("已复制区域截图", rect.Width + " x " + rect.Height);
        }

        private void SaveSelectedRegion(Rectangle rect)
        {
            string file = AppPaths.NewImagePath("_region");
            using (Bitmap bmp = RenderToBitmap())
            using (Bitmap crop = bmp.Clone(rect, PixelFormat.Format32bppArgb))
            {
                CaptureStore.Add(crop);
                crop.Save(file, ImageFormat.Png);
            }
            ShowToast("已保存区域截图", file);
        }

        private void PinSelectedRegion(Rectangle rect)
        {
            using (Bitmap bmp = RenderToBitmap())
            using (Bitmap crop = bmp.Clone(rect, PixelFormat.Format32bppArgb))
            {
                CaptureStore.Add(crop);
                Point location = new Point(virtualBounds.Left + rect.Left + 24, virtualBounds.Top + rect.Top + 24);
                PinManager.Show(crop, location);
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
                hoveredToolbarItem = -1;
                return;
            }

            if (selectionAction != SelectionAction.PixPinCapture || hasSelectedRegion || isSelecting)
            {
                if (hasHoveredWindow)
                {
                    hasHoveredWindow = false;
                }
                hoveredToolbarItem = -1;
                return;
            }

            Rectangle found = FindWindowAtViewPoint(viewLocation);
            if (found != Rectangle.Empty)
            {
                hoveredWindowView = found;
                hasHoveredWindow = true;
            }
            else
            {
                hasHoveredWindow = false;
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
                Cursor = Cursors.Cross;
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
            switch (index)
            {
                case 0:
                    isAnnotating = true;
                    annotationTool = DrawingTool.Pen;
                    Invalidate();
                    break;
                case 1:
                    isAnnotating = true;
                    annotationTool = DrawingTool.Rectangle;
                    Invalidate();
                    break;
                case 2:
                    isAnnotating = true;
                    annotationTool = DrawingTool.Ellipse;
                    Invalidate();
                    break;
                case 3:
                    isAnnotating = true;
                    annotationTool = DrawingTool.Arrow;
                    Invalidate();
                    break;
                case 4:
                    isAnnotating = true;
                    annotationTool = DrawingTool.Pen;
                    Invalidate();
                    break;
                case 5:
                    isAnnotating = true;
                    annotationTool = DrawingTool.Blur;
                    Invalidate();
                    break;
                case 6:
                    SelectionUndo();
                    break;
                case 7:
                    CopyAnnotatedRegion();
                    Close();
                    break;
                case 8:
                    SaveAnnotatedRegion();
                    Close();
                    break;
                case 9:
                    PinAnnotatedRegion();
                    Close();
                    break;
                case 10:
                    Close();
                    break;
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

        private static Rectangle GetToolbarBounds(Rectangle selectionRect)
        {
            int totalWidth = ToolbarLabels.Length * (ToolbarItemWidth + ToolbarPadding) - ToolbarPadding;
            int x = selectionRect.Left + (selectionRect.Width - totalWidth) / 2;
            int y = selectionRect.Bottom + 10;
            if (y + ToolbarItemHeight > SystemInformation.VirtualScreen.Height) y = selectionRect.Top - ToolbarItemHeight - 10;
            if (x < 4) x = 4;
            return new Rectangle(x, y, totalWidth, ToolbarItemHeight);
        }

        private static Rectangle GetToolbarItemRect(Rectangle toolbar, int index)
        {
            return new Rectangle(
                toolbar.X + index * (ToolbarItemWidth + ToolbarPadding),
                toolbar.Y,
                ToolbarItemWidth,
                ToolbarItemHeight);
        }

        private void SelectionSaveUndo()
        {
            selectionUndoStack.Push(CloneAnnotations(selectionAnnotations));
            TrimStack(selectionUndoStack, MaxUndoStates);
        }

        private void SelectionUndo()
        {
            if (selectionUndoStack.Count == 0) return;
            selectionRedoStack.Push(CloneAnnotations(selectionAnnotations));
            selectionAnnotations.Clear();
            selectionAnnotations.AddRange(selectionUndoStack.Pop());
            Invalidate();
        }

        private void SelectionRedo()
        {
            if (selectionRedoStack.Count == 0) return;
            selectionUndoStack.Push(CloneAnnotations(selectionAnnotations));
            selectionAnnotations.Clear();
            selectionAnnotations.AddRange(selectionRedoStack.Pop());
            Invalidate();
        }

        private void SelectionEraseAt(PointF point)
        {
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                for (int i = selectionAnnotations.Count - 1; i >= 0; i--)
                {
                    if (selectionAnnotations[i].HitTest(point, 12.0f, g))
                    {
                        selectionAnnotations.RemoveAt(i);
                        selectionRedoStack.Clear();
                        return;
                    }
                }
            }
        }

        private Bitmap RenderAnnotatedRegion()
        {
            var bmp = new Bitmap(selectedRegionView.Width, selectedRegionView.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.DrawImage(background,
                    new Rectangle(0, 0, selectedRegionView.Width, selectedRegionView.Height),
                    selectedRegionView,
                    GraphicsUnit.Pixel);

                for (int i = 0; i < selectionAnnotations.Count; i++)
                {
                    selectionAnnotations[i].Draw(g);
                }
                if (activeAnnotationStroke != null) activeAnnotationStroke.Draw(g);
                if (activeAnnotationShape != null) activeAnnotationShape.Draw(g);
            }
            return bmp;
        }

        private void CopyAnnotatedRegion()
        {
            using (Bitmap bmp = RenderAnnotatedRegion())
            {
                CaptureStore.Add(bmp);
                string error;
                if (!ClipboardService.TrySetImage(bmp, out error))
                {
                    ShowToast("复制截图失败", error);
                    return;
                }
            }
            ShowToast("已复制截图", selectedRegionView.Width + " x " + selectedRegionView.Height);
        }

        private void SaveAnnotatedRegion()
        {
            string file = AppPaths.NewImagePath("_region");
            using (Bitmap bmp = RenderAnnotatedRegion())
            {
                CaptureStore.Add(bmp);
                bmp.Save(file, ImageFormat.Png);
            }
            ShowToast("已保存截图", file);
        }

        private void PinAnnotatedRegion()
        {
            using (Bitmap bmp = RenderAnnotatedRegion())
            {
                CaptureStore.Add(bmp);
                Point location = new Point(virtualBounds.Left + selectedRegionView.Left + 24, virtualBounds.Top + selectedRegionView.Top + 24);
                PinManager.Show(bmp, location);
            }
            ShowToast("已贴图", selectedRegionView.Width + " x " + selectedRegionView.Height);
        }

        private static string AnnotationToolName(DrawingTool t)
        {
            switch (t)
            {
                case DrawingTool.Pen: return "画笔";
                case DrawingTool.Highlighter: return "荧光笔";
                case DrawingTool.Line: return "直线";
                case DrawingTool.Arrow: return "箭头";
                case DrawingTool.Rectangle: return "矩形";
                case DrawingTool.Ellipse: return "椭圆";
                case DrawingTool.Cover: return "遮罩";
                case DrawingTool.Blur: return "马赛克";
                case DrawingTool.Eraser: return "橡皮";
                case DrawingTool.NumberMarker: return "编号";
                default: return "";
            }
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
                using (var fill = new SolidBrush(Color.FromArgb(25, 0, 120, 215)))
                using (var pen = new Pen(Color.FromArgb(100, 0, 120, 215), 3))
                {
                    g.FillRectangle(fill, hoveredWindowView);
                    g.DrawRectangle(pen, hoveredWindowView);
                }
            }

            using (var dim = new SolidBrush(Color.FromArgb(95, 0, 0, 0)))
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

            if (!rect.IsEmpty && rect.Width > 1 && rect.Height > 1)
            {
                using (var border = new Pen(Color.FromArgb(0, 120, 215), 2))
                {
                    g.DrawRectangle(border, rect);
                }

                using (var font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                {
                    string text = rect.Width + " x " + rect.Height;
                    SizeF textSize = g.MeasureString(text, font);
                    float labelX = rect.Left;
                    float labelY = Math.Max(4, rect.Top - 24);
                    g.FillRectangle(bg, labelX, labelY, textSize.Width + 8, textSize.Height + 4);
                    g.DrawString(text, font, Brushes.White, labelX + 4, labelY + 2);
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
                    if (activeAnnotationShape != null) activeAnnotationShape.Draw(g);
                    g.Restore(state);

                    DrawToolbar(g, rect);

                    if (isAnnotating)
                    {
                        using (var font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                        {
                            string info = AnnotationToolName(annotationTool) + "  宽度 " + currentWidth.ToString("0") + "  Ctrl+Z 撤销  Esc 退出标注";
                            SizeF size = g.MeasureString(info, font);
                            float ix = rect.Left;
                            float iy = rect.Top + 4;
                            g.FillRectangle(bg, ix, iy, size.Width + 8, size.Height + 4);
                            g.DrawString(info, font, Brushes.White, ix + 4, iy + 2);
                        }

                        using (var brush = new SolidBrush(currentColor))
                        {
                            g.FillEllipse(brush, rect.Right - 40, rect.Top + 8, 20, 20);
                        }
                        using (var pen = new Pen(Color.White, 2))
                        {
                            g.DrawEllipse(pen, rect.Right - 40, rect.Top + 8, 20, 20);
                        }
                    }
                }
            }

            if (selectionAction == SelectionAction.PixPinCapture)
            {
                DrawColorPicker(g);
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
            using (var bg = new SolidBrush(Color.FromArgb(230, 32, 32, 32)))
            using (var border = new Pen(Color.FromArgb(80, 255, 255, 255)))
            using (var hoverBg = new SolidBrush(Color.FromArgb(60, 0, 120, 215)))
            using (var font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.FillRectangle(bg, toolbar);
                g.DrawRectangle(border, toolbar.X, toolbar.Y, toolbar.Width - 1, toolbar.Height - 1);

                for (int i = 0; i < ToolbarLabels.Length; i++)
                {
                    Rectangle item = GetToolbarItemRect(toolbar, i);
                    if (i == hoveredToolbarItem)
                    {
                        g.FillRectangle(hoverBg, item);
                    }
                    using (var format = new StringFormat())
                    {
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        g.DrawString(ToolbarLabels[i], font, Brushes.White, item, format);
                    }
                }
            }
        }

        private void DrawColorPicker(Graphics g)
        {
            Point cursor = PointToClient(Cursor.Position);
            int sourceX = cursor.X;
            int sourceY = cursor.Y;
            if (sourceX < 0 || sourceY < 0 || sourceX >= background.Width || sourceY >= background.Height) return;

            Color color = background.GetPixel(sourceX, sourceY);
            int boxSize = 126;
            int left = cursor.X + 24;
            int top = cursor.Y + 24;
            if (left + boxSize > Width) left = cursor.X - boxSize - 24;
            if (top + boxSize > Height) top = cursor.Y - boxSize - 24;

            using (var bg = new SolidBrush(Color.FromArgb(225, 24, 24, 24)))
            {
                g.FillRectangle(bg, left, top, boxSize, boxSize);
            }

            int zoomCell = 9;
            int grid = 9;
            int startX = sourceX - grid / 2;
            int startY = sourceY - grid / 2;
            for (int y = 0; y < grid; y++)
            {
                for (int x = 0; x < grid; x++)
                {
                    int px = Math.Max(0, Math.Min(background.Width - 1, startX + x));
                    int py = Math.Max(0, Math.Min(background.Height - 1, startY + y));
                    using (var brush = new SolidBrush(background.GetPixel(px, py)))
                    {
                        g.FillRectangle(brush, left + 8 + x * zoomCell, top + 8 + y * zoomCell, zoomCell, zoomCell);
                    }
                }
            }

            using (var pen = new Pen(Color.White, 2))
            {
                int center = grid / 2;
                g.DrawRectangle(pen, left + 8 + center * zoomCell, top + 8 + center * zoomCell, zoomCell, zoomCell);
            }

            using (var swatch = new SolidBrush(color))
            using (var font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.FillRectangle(swatch, left + 8, top + 94, 24, 20);
                g.DrawString("#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2"), font, Brushes.White, left + 38, top + 96);
            }
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
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed partial class OverlayForm
    {
        private void BeginSelection(SelectionAction action)
        {
            CommitTextInput(true);
            selectionAction = action;
            isSelecting = false;
            isMovingSelection = false;
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
            return "拖选后 Enter 复制，S 保存，P 贴图，C 取色，Space 捕捉窗口";
        }

        private bool HandleSelectionKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (hasSelectedRegion)
                {
                    hasSelectedRegion = false;
                    selectedRegionView = Rectangle.Empty;
                    Invalidate();
                }
                else
                {
                    Close();
                }
                return true;
            }

            if (e.KeyCode == Keys.Space)
            {
                CaptureWindowUnderCursor();
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

            if (!hasSelectedRegion)
            {
                return false;
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
                CopySelectedRegion(selectedRegionView);
                Close();
                return true;
            }

            if (e.KeyCode == Keys.S || (e.Control && e.KeyCode == Keys.S))
            {
                if (selectionAction == SelectionAction.PixPinCapture || selectionAction == SelectionAction.SaveRegion || selectionAction == SelectionAction.SaveCrop)
                {
                    SaveSelectedRegion(selectedRegionView);
                    Close();
                    return true;
                }
            }

            if (selectionAction == SelectionAction.PixPinCapture && (e.KeyCode == Keys.P || e.KeyCode == Keys.T))
            {
                PinSelectedRegion(selectedRegionView);
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
                using (var pen = new Pen(Color.White, 2))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(pen, rect);
                }

                using (var font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    string text = rect.Width + " x " + rect.Height;
                    string actionText = "";
                    if (hasSelectedRegion && selectionAction == SelectionAction.PixPinCapture)
                    {
                        actionText = "拖动移动  Enter 复制  S 保存  P 贴图  右键贴图";
                    }
                    else if (hasSelectedRegion)
                    {
                        actionText = "拖动移动  Enter 确认  Esc 取消";
                    }
                    g.DrawString(text + (actionText.Length > 0 ? "    " + actionText : ""), font, Brushes.White, rect.Left + 8, Math.Max(8, rect.Top - 24));
                }
            }

            if (selectionAction == SelectionAction.PixPinCapture)
            {
                DrawColorPicker(g);
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


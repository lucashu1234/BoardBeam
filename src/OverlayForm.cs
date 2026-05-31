using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed partial class OverlayForm : Form
    {
        private const int MaxUndoStates = 80;
        private enum SelectionAction
        {
            None,
            CopyRegion,
            SaveRegion,
            CopyCrop,
            SaveCrop,
            PinRegion,
            PixPinCapture,
            RecordRegion,
            ScrollCapture
        }

        private readonly OverlayMode initialMode;
        private readonly Rectangle virtualBounds;
        private readonly Bitmap background;
        private readonly List<Annotation> annotations;
        private readonly Stack<List<Annotation>> undoStack;
        private readonly Stack<List<Annotation>> redoStack;
        private readonly System.Windows.Forms.Timer countdownTimer;

        private OverlayMode mode;
        private DrawingTool tool;
        private Color currentColor;
        private float currentWidth;
        private bool isDrawing;
        private StrokeAnnotation activeStroke;
        private ShapeAnnotation activeShape;
        private PointF shapeStart;
        private TextBox activeTextBox;
        private PointF activeTextLocation;
        private DrawingTool toolBeforeTemporaryEraser;
        private bool temporaryEraser;
        private bool rightAlignedText;
        private bool liveBackground;

        private float zoom;
        private PointF imageCenter;
        private PointF viewCenter;
        private bool isPanning;
        private Point panStartPoint;
        private PointF panStartImageCenter;

        private int countdownSeconds;
        private bool countdownRunning;
        private Point spotlightPoint;
        private int spotlightRadius;
        private int spotlightOpacity;
        private bool showHelp;
        private int nextMarkerNumber;
        private SelectionAction selectionAction;
        private bool isSelecting;
        private bool isMovingSelection;
        private Point selectionStartView;
        private Point selectionCurrentView;
        private Point selectionMoveStartView;
        private Rectangle selectionMoveOriginalView;
        private Rectangle selectedRegionView;
        private bool hasSelectedRegion;

        public OverlayForm(OverlayMode mode)
        {
            initialMode = mode;
            this.mode = mode;
            virtualBounds = SystemInformation.VirtualScreen;
            background = CaptureTool.CaptureScreen(virtualBounds);
            annotations = new List<Annotation>();
            undoStack = new Stack<List<Annotation>>();
            redoStack = new Stack<List<Annotation>>();

            tool = mode == OverlayMode.Text ? DrawingTool.Text : DrawingTool.Pen;
            liveBackground = mode == OverlayMode.LiveDraw;
            selectionAction = SelectionAction.None;
            selectedRegionView = Rectangle.Empty;
            hasSelectedRegion = false;
            if (mode == OverlayMode.RegionCopy) selectionAction = SelectionAction.CopyRegion;
            if (mode == OverlayMode.RegionSave) selectionAction = SelectionAction.SaveRegion;
            if (mode == OverlayMode.RegionPin) selectionAction = SelectionAction.PinRegion;
            if (mode == OverlayMode.PixPinCapture) selectionAction = SelectionAction.PixPinCapture;
            if (mode == OverlayMode.Recording) selectionAction = SelectionAction.RecordRegion;
            if (mode == OverlayMode.ScrollingCapture) selectionAction = SelectionAction.ScrollCapture;
            currentColor = Color.Red;
            currentWidth = 5.0f;
            zoom = mode == OverlayMode.Zoom ? 2.0f : 1.0f;
            viewCenter = new PointF(virtualBounds.Width / 2.0f, virtualBounds.Height / 2.0f);
            Point cursor = Cursor.Position;
            imageCenter = new PointF(cursor.X - virtualBounds.Left, cursor.Y - virtualBounds.Top);
            if (mode != OverlayMode.Zoom)
            {
                imageCenter = viewCenter;
            }
            spotlightPoint = PointToClient(Cursor.Position);

            countdownSeconds = 10 * 60;
            countdownRunning = true;
            spotlightRadius = 170;
            spotlightOpacity = 175;
            showHelp = false;
            nextMarkerNumber = 1;
            countdownTimer = new System.Windows.Forms.Timer();
            countdownTimer.Interval = 1000;
            countdownTimer.Tick += OnCountdownTick;
            if (mode == OverlayMode.Timer)
            {
                countdownTimer.Start();
            }

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virtualBounds;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            BackColor = Color.Black;
            if (liveBackground)
            {
                BackColor = Color.FromArgb(1, 2, 3);
                TransparencyKey = BackColor;
            }
        }

        public OverlayMode CurrentMode
        {
            get { return mode; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Focus();
            Invalidate();
            if (initialMode == OverlayMode.Text)
            {
                tool = DrawingTool.Text;
            }
            if (selectionAction != SelectionAction.None)
            {
                ShowToast("拖选区域", GetSelectionHint(selectionAction));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (background != null) background.Dispose();
                if (countdownTimer != null) countdownTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private PointF ViewToImage(Point p)
        {
            return new PointF(
                ((float)p.X - viewCenter.X) / zoom + imageCenter.X,
                ((float)p.Y - viewCenter.Y) / zoom + imageCenter.Y);
        }

        private Point ImageToView(PointF p)
        {
            return new Point(
                (int)Math.Round((p.X - imageCenter.X) * zoom + viewCenter.X),
                (int)Math.Round((p.Y - imageCenter.Y) * zoom + viewCenter.Y));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (selectionAction != SelectionAction.None)
            {
                if (hasSelectedRegion && e.Button == MouseButtons.Right)
                {
                    PinSelectedRegion(selectedRegionView);
                    Close();
                    return;
                }

                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                if (hasSelectedRegion && selectedRegionView.Contains(e.Location))
                {
                    isMovingSelection = true;
                    selectionMoveStartView = e.Location;
                    selectionMoveOriginalView = selectedRegionView;
                    Cursor = Cursors.SizeAll;
                    return;
                }

                isSelecting = true;
                selectionStartView = e.Location;
                selectionCurrentView = e.Location;
                Invalidate();
                return;
            }

            if (mode == OverlayMode.Timer)
            {
                countdownRunning = !countdownRunning;
                Invalidate();
                return;
            }

            if (mode == OverlayMode.Spotlight)
            {
                spotlightPoint = e.Location;
                Invalidate();
                return;
            }

            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Alt) == Keys.Alt))
            {
                BeginPan(e.Location);
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                PointF erasePoint = ViewToImage(e.Location);
                BeginTemporaryEraser(erasePoint);
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            PointF imagePoint = ViewToImage(e.Location);
            if (tool == DrawingTool.Text)
            {
                BeginTextInput(imagePoint);
                return;
            }

            if (tool == DrawingTool.NumberMarker)
            {
                SaveUndoState();
                var marker = new NumberMarkerAnnotation();
                marker.Location = imagePoint;
                marker.Number = nextMarkerNumber++;
                marker.Color = currentColor;
                marker.Radius = Math.Max(18.0f, currentWidth * 4.2f);
                annotations.Add(marker);
                redoStack.Clear();
                Invalidate();
                return;
            }

            if (tool == DrawingTool.Eraser)
            {
                SaveUndoState();
                EraseAt(imagePoint);
                isDrawing = true;
                Invalidate();
                return;
            }

            isDrawing = true;
            DrawingTool gestureTool = ApplyDrawGesture(tool);
            if (gestureTool == DrawingTool.Pen || gestureTool == DrawingTool.Highlighter || gestureTool == DrawingTool.Blur)
            {
                activeStroke = gestureTool == DrawingTool.Blur ? new BlurStrokeAnnotation(background) : new StrokeAnnotation();
                activeStroke.Color = currentColor;
                activeStroke.Width = currentWidth;
                activeStroke.Highlighter = gestureTool == DrawingTool.Highlighter;
                activeStroke.Points.Add(imagePoint);
            }
            else
            {
                shapeStart = imagePoint;
                activeShape = new ShapeAnnotation();
                activeShape.Tool = gestureTool;
                activeShape.Color = gestureTool == DrawingTool.Cover ? Color.FromArgb(246, 246, 235) : currentColor;
                activeShape.Width = gestureTool == DrawingTool.Cover ? 2.0f : currentWidth;
                activeShape.Highlighter = tool == DrawingTool.Highlighter;
                activeShape.Start = imagePoint;
                activeShape.End = imagePoint;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (selectionAction != SelectionAction.None)
            {
                if (isMovingSelection)
                {
                    selectedRegionView = MoveSelectionByDelta(selectionMoveOriginalView, e.Location.X - selectionMoveStartView.X, e.Location.Y - selectionMoveStartView.Y);
                    Invalidate();
                    return;
                }

                if (!isSelecting)
                {
                    Cursor = hasSelectedRegion && selectedRegionView.Contains(e.Location) ? Cursors.SizeAll : Cursors.Cross;
                    if (selectionAction == SelectionAction.PixPinCapture)
                    {
                        Invalidate();
                    }
                }
            }

            if (isSelecting)
            {
                selectionCurrentView = e.Location;
                Invalidate();
                return;
            }

            if (mode == OverlayMode.Spotlight)
            {
                spotlightPoint = e.Location;
                Invalidate();
                return;
            }

            if (isPanning)
            {
                imageCenter = new PointF(
                    panStartImageCenter.X - ((float)e.X - panStartPoint.X) / zoom,
                    panStartImageCenter.Y - ((float)e.Y - panStartPoint.Y) / zoom);
                Invalidate();
                return;
            }

            if (!isDrawing) return;

            PointF imagePoint = ViewToImage(e.Location);
            if (tool == DrawingTool.Eraser)
            {
                EraseAt(imagePoint);
                Invalidate();
                return;
            }

            if (activeStroke != null)
            {
                if (activeStroke.Points.Count == 0 || Distance(activeStroke.Points[activeStroke.Points.Count - 1], imagePoint) > 1.5f)
                {
                    activeStroke.Points.Add(imagePoint);
                    Invalidate();
                }
            }
            else if (activeShape != null)
            {
                activeShape.Start = shapeStart;
                activeShape.End = ApplyShapeConstraints(shapeStart, imagePoint);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (isMovingSelection)
            {
                isMovingSelection = false;
                Cursor = Cursors.Cross;
                Invalidate();
                return;
            }

            if (isSelecting)
            {
                selectionCurrentView = e.Location;
                isSelecting = false;
                CompleteSelection();
                return;
            }

            if (isPanning)
            {
                isPanning = false;
                Cursor = Cursors.Cross;
                return;
            }

            if (!isDrawing) return;

            isDrawing = false;
            if (activeStroke != null && activeStroke.Points.Count > 0)
            {
                SaveUndoState();
                annotations.Add(activeStroke);
                redoStack.Clear();
            }
            if (activeShape != null)
            {
                SaveUndoState();
                annotations.Add(activeShape);
                redoStack.Clear();
            }

            activeStroke = null;
            activeShape = null;
            if (temporaryEraser)
            {
                temporaryEraser = false;
                tool = toolBeforeTemporaryEraser;
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (mode == OverlayMode.Spotlight)
            {
                if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                {
                    spotlightOpacity += e.Delta > 0 ? 10 : -10;
                    if (spotlightOpacity < 60) spotlightOpacity = 60;
                    if (spotlightOpacity > 230) spotlightOpacity = 230;
                }
                else
                {
                    spotlightRadius += e.Delta > 0 ? 16 : -16;
                    if (spotlightRadius < 60) spotlightRadius = 60;
                    if (spotlightRadius > 520) spotlightRadius = 520;
                }
                Invalidate();
                return;
            }

            if ((ModifierKeys & Keys.Control) == Keys.Control || mode == OverlayMode.Zoom)
            {
                float oldZoom = zoom;
                if (e.Delta > 0) zoom *= 1.15f;
                if (e.Delta < 0) zoom /= 1.15f;
                if (zoom < 1.0f) zoom = 1.0f;
                if (zoom > 6.0f) zoom = 6.0f;

                PointF before = ViewToImage(e.Location);
                imageCenter = new PointF(
                    before.X - ((float)e.X - viewCenter.X) / zoom,
                    before.Y - ((float)e.Y - viewCenter.Y) / zoom);

                if (Math.Abs(oldZoom - zoom) > 0.001f) Invalidate();
            }
            else
            {
                currentWidth += e.Delta > 0 ? 1.0f : -1.0f;
                if (currentWidth < 1.0f) currentWidth = 1.0f;
                if (currentWidth > 40.0f) currentWidth = 40.0f;
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (activeTextBox != null)
            {
                return;
            }

            if (selectionAction != SelectionAction.None && HandleSelectionKey(e))
            {
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                if (showHelp)
                {
                    showHelp = false;
                    Invalidate();
                    return;
                }
                Close();
                return;
            }

            if (e.KeyCode == Keys.F1 || e.KeyCode == Keys.OemQuestion)
            {
                showHelp = !showHelp;
                Invalidate();
                return;
            }

            if (e.Control && e.KeyCode == Keys.Z)
            {
                Undo();
                e.Handled = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.Y)
            {
                Redo();
                e.Handled = true;
                return;
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.C)
            {
                BeginSelection(SelectionAction.CopyCrop);
                e.Handled = true;
                return;
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.S)
            {
                BeginSelection(SelectionAction.SaveCrop);
                e.Handled = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.C)
            {
                CopyToClipboard();
                e.Handled = true;
                return;
            }

            if (mode == OverlayMode.Timer)
            {
                HandleTimerKey(e);
                return;
            }

            if (mode == OverlayMode.Spotlight)
            {
                HandleSpotlightKey(e);
                return;
            }

            if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus)
            {
                currentWidth += 1.0f;
                if (currentWidth > 40.0f) currentWidth = 40.0f;
                Invalidate();
                return;
            }

            if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)
            {
                currentWidth -= 1.0f;
                if (currentWidth < 1.0f) currentWidth = 1.0f;
                Invalidate();
                return;
            }

            if (e.KeyCode == Keys.Left) { imageCenter.X -= 40.0f / zoom; Invalidate(); return; }
            if (e.KeyCode == Keys.Right) { imageCenter.X += 40.0f / zoom; Invalidate(); return; }
            if (e.KeyCode == Keys.Up) { imageCenter.Y -= 40.0f / zoom; Invalidate(); return; }
            if (e.KeyCode == Keys.Down) { imageCenter.Y += 40.0f / zoom; Invalidate(); return; }

            if (e.KeyCode == Keys.P) { tool = DrawingTool.Pen; mode = OverlayMode.Draw; Invalidate(); return; }
            if (e.KeyCode == Keys.H) { tool = DrawingTool.Highlighter; mode = OverlayMode.Draw; Invalidate(); return; }
            if (e.KeyCode == Keys.L) { tool = DrawingTool.Line; mode = OverlayMode.Draw; Invalidate(); return; }
            if (e.KeyCode == Keys.A) { tool = DrawingTool.Arrow; mode = OverlayMode.Draw; Invalidate(); return; }
            if (e.KeyCode == Keys.R) { tool = DrawingTool.Rectangle; mode = OverlayMode.Draw; Invalidate(); return; }
            if (e.KeyCode == Keys.O) { tool = DrawingTool.Ellipse; mode = OverlayMode.Draw; Invalidate(); return; }
            if (e.KeyCode == Keys.V) { tool = DrawingTool.Cover; mode = OverlayMode.Draw; Invalidate(); return; }
            if (e.KeyCode == Keys.M) { tool = DrawingTool.NumberMarker; mode = OverlayMode.Draw; Invalidate(); return; }
            if (e.KeyCode == Keys.X) { tool = DrawingTool.Blur; mode = OverlayMode.Draw; Invalidate(); return; }
            if (e.KeyCode == Keys.E) { tool = DrawingTool.Eraser; mode = OverlayMode.Draw; Invalidate(); return; }
            if (e.KeyCode == Keys.T) { tool = DrawingTool.Text; mode = OverlayMode.Text; rightAlignedText = e.Shift; Invalidate(); return; }
            if (e.KeyCode == Keys.W) { mode = OverlayMode.Whiteboard; zoom = 1.0f; imageCenter = viewCenter; Invalidate(); return; }
            if (e.KeyCode == Keys.K) { mode = OverlayMode.Blackboard; zoom = 1.0f; imageCenter = viewCenter; Invalidate(); return; }
            if (e.KeyCode == Keys.S) { SaveScreenshot(); return; }
            if (e.KeyCode == Keys.C) { ClearAnnotations(); return; }

            ApplyColorShortcut(e.KeyCode);
        }

        private void HandleTimerKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.D1) { countdownSeconds = 60; countdownRunning = true; Invalidate(); return; }
            if (e.KeyCode == Keys.D2) { countdownSeconds = 3 * 60; countdownRunning = true; Invalidate(); return; }
            if (e.KeyCode == Keys.D3) { countdownSeconds = 5 * 60; countdownRunning = true; Invalidate(); return; }
            if (e.KeyCode == Keys.D4) { countdownSeconds = 10 * 60; countdownRunning = true; Invalidate(); return; }
            if (e.KeyCode == Keys.D5) { countdownSeconds = 15 * 60; countdownRunning = true; Invalidate(); return; }
            if (e.KeyCode == Keys.D6) { countdownSeconds = 45 * 60; countdownRunning = true; Invalidate(); return; }

            if (e.KeyCode == Keys.Space)
            {
                countdownRunning = !countdownRunning;
                Invalidate();
                return;
            }

            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus)
            {
                countdownSeconds += 60;
                Invalidate();
                return;
            }

            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)
            {
                countdownSeconds -= 60;
                if (countdownSeconds < 0) countdownSeconds = 0;
                Invalidate();
                return;
            }

            if (e.KeyCode == Keys.R)
            {
                countdownSeconds = 10 * 60;
                countdownRunning = true;
                Invalidate();
                return;
            }
        }

        private void HandleSpotlightKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Up)
            {
                spotlightRadius += 16;
                if (spotlightRadius > 520) spotlightRadius = 520;
                Invalidate();
                return;
            }

            if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Down)
            {
                spotlightRadius -= 16;
                if (spotlightRadius < 60) spotlightRadius = 60;
                Invalidate();
                return;
            }

            if (e.KeyCode == Keys.Left)
            {
                spotlightOpacity += 10;
                if (spotlightOpacity > 230) spotlightOpacity = 230;
                Invalidate();
                return;
            }

            if (e.KeyCode == Keys.Right)
            {
                spotlightOpacity -= 10;
                if (spotlightOpacity < 60) spotlightOpacity = 60;
                Invalidate();
                return;
            }
        }

        private void ApplyColorShortcut(Keys key)
        {
            if (key == Keys.D1) currentColor = Color.Red;
            else if (key == Keys.D2) currentColor = Color.Gold;
            else if (key == Keys.D3) currentColor = Color.LimeGreen;
            else if (key == Keys.D4) currentColor = Color.DeepSkyBlue;
            else if (key == Keys.D5) currentColor = Color.Blue;
            else if (key == Keys.D6) currentColor = Color.Magenta;
            else if (key == Keys.D7) currentColor = Color.White;
            else if (key == Keys.D8) currentColor = Color.Black;
            else return;

            Invalidate();
        }

        private PointF ApplyShapeConstraints(PointF start, PointF end)
        {
            if ((ModifierKeys & Keys.Shift) != Keys.Shift)
            {
                return end;
            }

            DrawingTool shapeTool = activeShape != null ? activeShape.Tool : tool;
            if (shapeTool == DrawingTool.Rectangle || shapeTool == DrawingTool.Ellipse || shapeTool == DrawingTool.Cover)
            {
                float dx = end.X - start.X;
                float dy = end.Y - start.Y;
                float size = Math.Max(Math.Abs(dx), Math.Abs(dy));
                return new PointF(start.X + Math.Sign(dx) * size, start.Y + Math.Sign(dy) * size);
            }

            float adx = Math.Abs(end.X - start.X);
            float ady = Math.Abs(end.Y - start.Y);
            if (adx > ady * 1.8f) return new PointF(end.X, start.Y);
            if (ady > adx * 1.8f) return new PointF(start.X, end.Y);
            float d = Math.Max(adx, ady);
            return new PointF(start.X + Math.Sign(end.X - start.X) * d, start.Y + Math.Sign(end.Y - start.Y) * d);
        }

        private void BeginTextInput(PointF imagePoint)
        {
            CommitTextInput(false);

            activeTextLocation = imagePoint;
            Point viewPoint = ImageToView(imagePoint);
            activeTextBox = new TextBox();
            activeTextBox.Multiline = true;
            activeTextBox.AcceptsReturn = true;
            activeTextBox.BorderStyle = BorderStyle.FixedSingle;
            activeTextBox.Font = new Font(FontFamily.GenericSansSerif, Math.Max(12, currentWidth * 5), FontStyle.Bold, GraphicsUnit.Pixel);
            activeTextBox.ForeColor = currentColor;
            activeTextBox.BackColor = mode == OverlayMode.Blackboard ? Color.FromArgb(30, 36, 32) : Color.FromArgb(255, 255, 225);
            activeTextBox.TextAlign = rightAlignedText ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            activeTextBox.Width = 420;
            activeTextBox.Height = Math.Max(44, activeTextBox.Font.Height * 3 + 12);
            activeTextBox.Left = viewPoint.X;
            activeTextBox.Top = viewPoint.Y;
            activeTextBox.KeyDown += OnTextBoxKeyDown;
            Controls.Add(activeTextBox);
            activeTextBox.Focus();
        }

        private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (e.Control)
                {
                    CommitTextInput(true);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            }
            else if (e.KeyCode == Keys.Escape)
            {
                CommitTextInput(false);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void CommitTextInput(bool keep)
        {
            if (activeTextBox == null) return;

            string text = activeTextBox.Text;
            float fontSize = activeTextBox.Font.Size / zoom;
            Controls.Remove(activeTextBox);
            activeTextBox.Dispose();
            activeTextBox = null;

            if (keep && !string.IsNullOrWhiteSpace(text))
            {
                SaveUndoState();
                var annotation = new TextAnnotation();
                annotation.Location = activeTextLocation;
                annotation.Text = text;
                annotation.Color = currentColor;
                annotation.FontSize = fontSize;
                annotation.RightAligned = rightAlignedText;
                annotations.Add(annotation);
                redoStack.Clear();
                Invalidate();
            }
        }

        private void SaveUndoState()
        {
            undoStack.Push(CloneAnnotations(annotations));
            TrimStack(undoStack, MaxUndoStates);
        }

        private static void TrimStack(Stack<List<Annotation>> stack, int max)
        {
            if (stack.Count <= max) return;

            List<Annotation>[] items = stack.ToArray();
            stack.Clear();
            for (int i = max - 1; i >= 0; i--)
            {
                stack.Push(items[i]);
            }
        }

        private static List<Annotation> CloneAnnotations(List<Annotation> source)
        {
            var copy = new List<Annotation>();
            for (int i = 0; i < source.Count; i++)
            {
                copy.Add(source[i].Clone());
            }
            return copy;
        }

        private void Undo()
        {
            if (undoStack.Count == 0) return;
            redoStack.Push(CloneAnnotations(annotations));
            annotations.Clear();
            annotations.AddRange(undoStack.Pop());
            Invalidate();
        }

        private void Redo()
        {
            if (redoStack.Count == 0) return;
            undoStack.Push(CloneAnnotations(annotations));
            annotations.Clear();
            annotations.AddRange(redoStack.Pop());
            Invalidate();
        }

        private void ClearAnnotations()
        {
            if (annotations.Count == 0) return;
            SaveUndoState();
            annotations.Clear();
            redoStack.Clear();
            Invalidate();
        }

        private void EraseAt(PointF imagePoint)
        {
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                for (int i = annotations.Count - 1; i >= 0; i--)
                {
                    if (annotations[i].HitTest(imagePoint, 12.0f / zoom, g))
                    {
                        annotations.RemoveAt(i);
                        redoStack.Clear();
                        return;
                    }
                }
            }
        }

        private void BeginTemporaryEraser(PointF imagePoint)
        {
            if (tool == DrawingTool.Eraser)
            {
                SaveUndoState();
                EraseAt(imagePoint);
                isDrawing = true;
                Invalidate();
                return;
            }

            toolBeforeTemporaryEraser = tool;
            tool = DrawingTool.Eraser;
            temporaryEraser = true;
            SaveUndoState();
            EraseAt(imagePoint);
            isDrawing = true;
            Invalidate();
        }

        private void BeginPan(Point location)
        {
            isPanning = true;
            panStartPoint = location;
            panStartImageCenter = imageCenter;
            Cursor = Cursors.SizeAll;
        }

        private DrawingTool ApplyDrawGesture(DrawingTool requestedTool)
        {
            if (requestedTool != DrawingTool.Pen && requestedTool != DrawingTool.Highlighter)
            {
                return requestedTool;
            }

            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            if (ctrl && shift) return DrawingTool.Arrow;
            if (ctrl) return DrawingTool.Rectangle;
            if (shift) return DrawingTool.Line;
            return requestedTool;
        }

        private static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}


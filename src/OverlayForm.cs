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
            ScrollCapture,
            OcrCapture
        }

        private readonly OverlayMode initialMode;
        private readonly Rectangle virtualBounds;
        private readonly Bitmap background;
        private readonly List<Annotation> annotations;
        private readonly List<List<Annotation>> undoStack;
        private readonly List<List<Annotation>> redoStack;
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
        private Point rightClickStart;

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
        private bool showSelectionHelp;  // 选区模式帮助覆盖层
        private int nextMarkerNumber;
        private StampAnnotation.StampType currentStampType;
        private bool shapeFilled;
        private bool shapeShadow;
        private bool linkNumberMarkers;  // 序号连线：连续编号间自动画半透明箭头
        private Point lastNumberMarkerPoint;  // 上一个编号位置（用于连线）
        private bool hasLastNumberMarker;
        private bool textHasBackground;
        private float currentOpacity = 1.0f;  // 0..1，画笔/形状的当前透明度
        private float currentBlurIntensity = 1.0f;  // 0.2..3.0，马赛克笔的强度（独立于线宽）
        private System.Drawing.Drawing2D.DashStyle currentDashStyle = System.Drawing.Drawing2D.DashStyle.Solid;
        private SelectionAction selectionAction;
        private bool isSelecting;
        private bool isMovingSelection;
        private Point selectionStartView;
        private Point selectionCurrentView;
        private Point selectionMoveStartView;
        private Rectangle selectionMoveOriginalView;
        private Rectangle selectedRegionView;
        private bool hasSelectedRegion;

        private List<Rectangle> windowRects;
        private Rectangle hoveredWindowView;
        private string hoveredWindowTitle;
        private bool hasHoveredWindow;
        private bool isResizingSelection;
        private int activeResizeHandle;
        private Rectangle resizeOriginalRect;
        private float resizeOriginalAspectRatio;
        private int hoveredToolbarItem;
        private Point mouseDownLocation;

        private bool isAnnotating;
        private DrawingTool annotationTool;
        private StrokeAnnotation activeAnnotationStroke;
        private ShapeAnnotation activeAnnotationShape;
        private PixelateAreaAnnotation activePixelateArea;
        private CalloutAnnotation activeCallout;
        private PointF annotationShapeStart;
        private readonly List<Annotation> selectionAnnotations;
        private readonly List<List<Annotation>> selectionUndoStack;
        private readonly List<List<Annotation>> selectionRedoStack;
        private int selectionNextMarker;
        private bool isAnnotationTextInput;
        private bool isRightClickErasing;
        private System.Windows.Forms.Timer autosaveTimer;  // 标注变更后节流自动保存
        private bool suppressAutosave;  // 正在恢复/加载时不触发保存
        private float marchOffset;
        private int lastHoverCheckTick; // 窗口悬停检测节流
        private int elementCycleDepth;  // Tab 遍历窗口元素层级：0=最深的子控件，1+=回退到更上层窗口
        // 选择工具：已绘制标注的选中/移动/缩放编辑
        private int selectedAnnotationIndex = -1;
        private enum AnnotationEditMode { None, Move, Resize }
        private AnnotationEditMode annotationEditMode = AnnotationEditMode.None;
        private int annotationResizeHandle = -1;
        private Point annotationEditLastPoint;
        private RectangleF annotationEditStartBounds;
        private System.Windows.Forms.Timer marchTimer;
        private ToastForm activeToast;
        private System.Windows.Forms.Timer activeToastTimer;
        private static Graphics measureGraphics;
        // 放大镜缓存：避免每次 Paint 都 LockBits
        private Point magnifierCacheCenter;
        private Color[] magnifierCachePixels;
        private int magnifierCacheGridSize;
        private Color magnifierCacheCenterColor;
        private Bitmap magnifierGridBmp;
        private Point magnifierGridCenter;
        private static Bitmap measureBitmap;

        // 缓存的 GDI 对象 —— 在 Dispose 中统一释放，避免每帧反复创建
        private Font hudFont;
        private SolidBrush hudBgBrush;
        private Pen hudBorderPen;
        private SolidBrush colorBrush;
        private Pen colorBorderPen;
        private Font timerBigFont;
        private Font timerSmallFont;
        private SolidBrush timerBgBrush;
        private Font helpTitleFont;
        private Font helpBodyFont;
        private Pen helpSepPen;
        private SolidBrush helpBgBrush;
        private Pen helpBorderPen;

        private void EnsureHudResources()
        {
            if (hudFont == null)
            {
                hudFont = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Regular, GraphicsUnit.Pixel);
                hudBgBrush = new SolidBrush(Color.FromArgb(160, 20, 20, 20));
                hudBorderPen = new Pen(Color.FromArgb(80, Color.White));
                colorBrush = new SolidBrush(currentColor);
                colorBorderPen = new Pen(Color.White, 2);
            }
            // 颜色跟随 currentColor 变化
            if (colorBrush != null && colorBrush.Color != currentColor)
                colorBrush.Color = currentColor;
        }

        private void EnsureTimerResources()
        {
            if (timerBigFont == null)
            {
                timerBigFont = new Font(FontFamily.GenericSansSerif, 132, FontStyle.Bold, GraphicsUnit.Pixel);
                timerSmallFont = new Font(FontFamily.GenericSansSerif, 24, FontStyle.Regular, GraphicsUnit.Pixel);
                timerBgBrush = new SolidBrush(Color.FromArgb(145, 0, 0, 0));
            }
        }

        private void EnsureHelpResources()
        {
            if (helpTitleFont == null)
            {
                helpTitleFont = new Font(FontFamily.GenericSansSerif, 28, FontStyle.Bold, GraphicsUnit.Pixel);
                helpBodyFont = new Font(FontFamily.GenericSansSerif, 16.5f, FontStyle.Regular, GraphicsUnit.Pixel);
                helpSepPen = new Pen(Color.FromArgb(60, 255, 255, 255));
                helpBgBrush = new SolidBrush(Color.FromArgb(235, 18, 20, 24));
                helpBorderPen = new Pen(Color.FromArgb(100, 180, 200, 255));
            }
        }

        private void DisposeCachedResources()
        {
            if (hudFont != null) { hudFont.Dispose(); hudFont = null; }
            if (hudBgBrush != null) { hudBgBrush.Dispose(); hudBgBrush = null; }
            if (hudBorderPen != null) { hudBorderPen.Dispose(); hudBorderPen = null; }
            if (colorBrush != null) { colorBrush.Dispose(); colorBrush = null; }
            if (colorBorderPen != null) { colorBorderPen.Dispose(); colorBorderPen = null; }
            if (timerBigFont != null) { timerBigFont.Dispose(); timerBigFont = null; }
            if (timerSmallFont != null) { timerSmallFont.Dispose(); timerSmallFont = null; }
            if (timerBgBrush != null) { timerBgBrush.Dispose(); timerBgBrush = null; }
            if (helpTitleFont != null) { helpTitleFont.Dispose(); helpTitleFont = null; }
            if (helpBodyFont != null) { helpBodyFont.Dispose(); helpBodyFont = null; }
            if (helpSepPen != null) { helpSepPen.Dispose(); helpSepPen = null; }
            if (helpBgBrush != null) { helpBgBrush.Dispose(); helpBgBrush = null; }
            if (helpBorderPen != null) { helpBorderPen.Dispose(); helpBorderPen = null; }
            if (magnifierGridBmp != null) { magnifierGridBmp.Dispose(); magnifierGridBmp = null; }
        }

        public OverlayForm(OverlayMode mode)
        {
            initialMode = mode;
            this.mode = mode;
            virtualBounds = SystemInformation.VirtualScreen;
            bool wantCursor = (mode == OverlayMode.PixPinCapture) && SettingsStore.Load().IncludeCursorInCapture;
            background = wantCursor
                ? CaptureTool.CaptureScreenWithCursor(virtualBounds)
                : CaptureTool.CaptureScreen(virtualBounds);
            annotations = new List<Annotation>();
            undoStack = new List<List<Annotation>>();
            redoStack = new List<List<Annotation>>();

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
            if (mode == OverlayMode.OcrCapture) selectionAction = SelectionAction.OcrCapture;
            currentColor = Color.Red;
            currentWidth = 5.0f;

            // 加载用户偏好
            try
            {
                AppSettings prefs = SettingsStore.Load();
                currentColor = Color.FromArgb(prefs.DefaultColorArgb);
                currentWidth = prefs.DefaultWidth;
                currentStampType = (StampAnnotation.StampType)prefs.DefaultStampType;
                countdownSeconds = prefs.DefaultCountdownSeconds;
                // 恢复上次使用的工具
                if (prefs.LastTool >= 0 && prefs.LastTool < System.Enum.GetValues(typeof(DrawingTool)).Length)
                    tool = (DrawingTool)prefs.LastTool;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("BoardBeam: 加载设置失败 - " + ex.Message);
            }
            zoom = mode == OverlayMode.Zoom ? 2.0f : 1.0f;
            viewCenter = new PointF(virtualBounds.Width / 2.0f, virtualBounds.Height / 2.0f);
            Point cursor = Cursor.Position;
            imageCenter = new PointF(cursor.X - virtualBounds.Left, cursor.Y - virtualBounds.Top);
            if (mode != OverlayMode.Zoom)
            {
                imageCenter = viewCenter;
            }
            spotlightPoint = PointToClient(Cursor.Position);

            if (mode == OverlayMode.Timer || countdownSeconds <= 0)
                countdownSeconds = 10 * 60;
            countdownRunning = (mode == OverlayMode.Timer);
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

            if (mode == OverlayMode.PixPinCapture)
            {
                windowRects = CaptureTool.EnumerateVisibleWindows();
            }

            hasHoveredWindow = false;
            isResizingSelection = false;
            activeResizeHandle = -1;
            hoveredToolbarItem = -1;

            isAnnotating = false;
            annotationTool = DrawingTool.Pen;
            activeAnnotationStroke = null;
            activeAnnotationShape = null;
            selectionAnnotations = new List<Annotation>();
            selectionUndoStack = new List<List<Annotation>>();
            selectionRedoStack = new List<List<Annotation>>();
            selectionNextMarker = 1;

            marchOffset = 0;
            marchTimer = new System.Windows.Forms.Timer();
            marchTimer.Interval = 80;
            marchTimer.Tick += delegate { marchOffset = (marchOffset + 1) % 16; Invalidate(); };

            // 标注崩溃自动保存：节流 500ms 落盘
            autosaveTimer = new System.Windows.Forms.Timer();
            autosaveTimer.Interval = 500;
            autosaveTimer.Tick += delegate { autosaveTimer.Stop(); FlushAutosave(); };

            // 检测未保存的标注（仅绘图类模式）并提示恢复
            TryOfferAutosaveRestore();
        }

        private string AutosavePath()
        {
            return System.IO.Path.Combine(AppPaths.AutosaveDirectory, "autosave_" + (int)initialMode + ".bin");
        }

        private void ScheduleAutosave()
        {
            if (suppressAutosave) return;
            autosaveTimer.Stop();
            autosaveTimer.Start();
        }

        private void FlushAutosave()
        {
            // 仅对绘图类模式（主标注列表非空时）保存；选区标注不持久化（背景区域不稳定）
            if (mode != OverlayMode.Draw && mode != OverlayMode.Whiteboard && mode != OverlayMode.Blackboard) return;
            try { AnnotationSerializer.Save(AutosavePath(), annotations, new Size(background.Width, background.Height)); }
            catch { }
        }

        private void TryOfferAutosaveRestore()
        {
            if (mode != OverlayMode.Draw && mode != OverlayMode.Whiteboard && mode != OverlayMode.Blackboard) return;
            try
            {
                var restored = AnnotationSerializer.Load(AutosavePath(), new Size(background.Width, background.Height));
                if (restored != null && restored.Count > 0)
                {
                    suppressAutosave = true;
                    annotations.AddRange(restored);
                    suppressAutosave = false;
                    ShowToast("已恢复标注", "检测到上次未保存的 " + restored.Count + " 个标注，已自动恢复。Esc 关闭");
                }
            }
            catch { }
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
                ShowToast("截图", GetSelectionHint(selectionAction));
            }
        }

        /// <summary>PerMonitorV2：DPI 变化时按新 DPI 重新截取背景并全屏化（瞬态覆盖层）。</summary>
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            // 覆盖层为瞬态；DPI 变化时让 WinForms 自动缩放字体/控件即可，重绘
            DisposeCachedResources();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 保存用户偏好
                SavePreferences();

                if (activeToastTimer != null) { activeToastTimer.Stop(); activeToastTimer.Dispose(); activeToastTimer = null; }
                if (activeToast != null && !activeToast.IsDisposed) { activeToast.Close(); activeToast.Dispose(); activeToast = null; }
                if (marchTimer != null) { marchTimer.Stop(); marchTimer.Dispose(); marchTimer = null; }
                if (autosaveTimer != null) { autosaveTimer.Stop(); autosaveTimer.Dispose(); autosaveTimer = null; }
                // 正常关闭：清除崩溃自动保存（标注已被用户主动放弃或已保存截图）
                try { string ap = AutosavePath(); if (System.IO.File.Exists(ap)) System.IO.File.Delete(ap); } catch { }
                DisposeCachedResources();
                // 先清空标注和 undo/redo 栈，避免 BlurStrokeAnnotation 引用即将释放的 background
                annotations.Clear();
                undoStack.Clear();
                redoStack.Clear();
                selectionAnnotations.Clear();
                selectionUndoStack.Clear();
                selectionRedoStack.Clear();
                if (background != null) background.Dispose();
                if (countdownTimer != null) countdownTimer.Dispose();
                if (measureGraphics != null) { measureGraphics.Dispose(); measureGraphics = null; }
                if (measureBitmap != null) { measureBitmap.Dispose(); measureBitmap = null; }
            }
            base.Dispose(disposing);
        }

        private void SavePreferences()
        {
            try
            {
                AppSettings prefs = SettingsStore.Load();
                prefs.DefaultColorArgb = currentColor.ToArgb();
                prefs.DefaultWidth = currentWidth;
                prefs.DefaultStampType = (int)currentStampType;
                prefs.DefaultCountdownSeconds = countdownSeconds;
                prefs.LastTool = (int)tool;
                prefs.LastOverlayMode = (int)mode;
                SettingsStore.Save(prefs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("BoardBeam: 加载设置失败 - " + ex.Message);
            }
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
                CommitTextInput(true);

                if (hasSelectedRegion && e.Button == MouseButtons.Right)
                {
                    // 右键调色板：打开自定义颜色对话框
                    if (isAnnotating)
                    {
                        int colorIdx = HitTestColorPalette(e.Location);
                        if (colorIdx >= 0)
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
                            return;
                        }
                    }

                    if (isAnnotating)
                    {
                        // 标注模式下右键：记录起点，等 MouseUp 判断是拖拽橡皮还是点击菜单
                        rightClickStart = e.Location;
                        isDrawing = false;
                        isRightClickErasing = false;
                        return;
                    }
                    ShowSelectionContextMenu(e.Location);
                    return;
                }

                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                if (hasSelectedRegion)
                {
                    // 优先检测颜色调色板
                    int colorIdx = HitTestColorPalette(e.Location);
                    if (colorIdx >= 0)
                    {
                        if (colorIdx < PaletteColors.Length)
                        {
                            currentColor = PaletteColors[colorIdx];
                        }
                        else
                        {
                            // 自定义颜色按钮
                            using (var dlg = new ColorDialog())
                            {
                                dlg.Color = currentColor;
                                dlg.FullOpen = true;
                                if (dlg.ShowDialog(this) == DialogResult.OK)
                                    currentColor = dlg.Color;
                            }
                        }
                        Invalidate();
                        return;
                    }

                    int tb = HitTestToolbar(e.Location);
                    if (tb >= 0)
                    {
                        ExecuteToolbarItem(tb);
                        return;
                    }
                }

                if (hasSelectedRegion)
                {
                    int handle = HitTestResizeHandle(e.Location);
                    if (handle >= 0)
                    {
                        activeResizeHandle = handle;
                        resizeOriginalRect = selectedRegionView;
                        resizeOriginalAspectRatio = selectedRegionView.Height > 0 ? (float)selectedRegionView.Width / selectedRegionView.Height : 1.0f;
                        isResizingSelection = true;
                        return;
                    }
                }

                // 标注模式下 Alt+左键 或 中键 拖拽移动选区
                if (isAnnotating && hasSelectedRegion &&
                    (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Alt) == Keys.Alt)))
                {
                    isMovingSelection = true;
                    selectionMoveStartView = e.Location;
                    selectionMoveOriginalView = selectedRegionView;
                    Cursor = Cursors.SizeAll;
                    return;
                }

                if (isAnnotating && hasSelectedRegion && selectedRegionView.Contains(e.Location))
                {
                    PointF selPt = e.Location;

                    // ===== 选择工具：选中/移动/缩放已绘制标注 =====
                    if (annotationTool == DrawingTool.Select)
                    {
                        Graphics mg = GetMeasureGraphics();
                        // 1) 先检测当前已选中标注的调整手柄
                        if (selectedAnnotationIndex >= 0 && selectedAnnotationIndex < selectionAnnotations.Count)
                        {
                            int h = selectionAnnotations[selectedAnnotationIndex].HitTestHandle(selPt, mg);
                            if (h >= 0)
                            {
                                SelectionSaveUndo();
                                annotationEditMode = AnnotationEditMode.Resize;
                                annotationResizeHandle = h;
                                annotationEditLastPoint = e.Location;
                                annotationEditStartBounds = selectionAnnotations[selectedAnnotationIndex].GetBounds(mg);
                                return;
                            }
                        }
                        // 2) 从顶层往下 hit-test 标注
                        int hit = -1;
                        for (int i = selectionAnnotations.Count - 1; i >= 0; i--)
                        {
                            if (selectionAnnotations[i].HitTest(selPt, 4f, mg)) { hit = i; break; }
                        }
                        if (hit >= 0)
                        {
                            SelectionSaveUndo();
                            selectedAnnotationIndex = hit;
                            annotationEditMode = AnnotationEditMode.Move;
                            annotationEditLastPoint = e.Location;
                            Invalidate();
                            return;
                        }
                        // 3) 点击空白：取消选中
                        selectedAnnotationIndex = -1;
                        Invalidate();
                        return;
                    }

                    if (annotationTool == DrawingTool.NumberMarker)
                    {
                        SelectionSaveUndo();
                        // 序号连线：若启用且已有上一个编号，先画一条半透明箭头连接
                        if (linkNumberMarkers && hasLastNumberMarker)
                        {
                            var arrow = new ShapeAnnotation();
                            arrow.Tool = DrawingTool.Arrow;
                            arrow.Start = new PointF(lastNumberMarkerPoint.X, lastNumberMarkerPoint.Y);
                            arrow.End = selPt;
                            arrow.Color = Color.FromArgb(150, currentColor.R, currentColor.G, currentColor.B);
                            arrow.Width = 2.0f;
                            selectionAnnotations.Add(arrow);
                        }
                        var marker = new NumberMarkerAnnotation();
                        marker.Location = selPt;
                        marker.Number = selectionNextMarker++;
                        marker.Color = currentColor;
                        marker.Radius = Math.Max(18.0f, currentWidth * 4.2f);
                        selectionAnnotations.Add(marker);
                        lastNumberMarkerPoint = new Point((int)selPt.X, (int)selPt.Y);
                        hasLastNumberMarker = true;
                        selectionRedoStack.Clear();
                        Invalidate();
                        return;
                    }

                    if (annotationTool == DrawingTool.Stamp)
                    {
                        SelectionSaveUndo();
                        var stamp = new StampAnnotation();
                        stamp.Location = selPt;
                        stamp.Type = currentStampType;
                        stamp.Color = currentColor;
                        stamp.Radius = Math.Max(16.0f, currentWidth * 3.8f);
                        selectionAnnotations.Add(stamp);
                        selectionRedoStack.Clear();
                        Invalidate();
                        return;
                    }

                    if (annotationTool == DrawingTool.Eraser)
                    {
                        SelectionSaveUndo();
                        selectionRedoStack.Clear();
                        SelectionEraseAt(selPt);
                        isDrawing = true;
                        Invalidate();
                        return;
                    }

                    if (annotationTool == DrawingTool.Text)
                    {
                        isAnnotationTextInput = true;
                        activeTextLocation = selPt;
                        activeTextBox = new TextBox();
                        activeTextBox.Multiline = true;
                        activeTextBox.AcceptsReturn = true;
                        activeTextBox.BorderStyle = BorderStyle.FixedSingle;
                        activeTextBox.Font = new Font(FontFamily.GenericSansSerif, Math.Max(12, currentWidth * 5), FontStyle.Bold, GraphicsUnit.Pixel);
                        activeTextBox.ForeColor = currentColor;
                        activeTextBox.BackColor = Color.FromArgb(255, 255, 225);
                        activeTextBox.Width = 420;
                        activeTextBox.Height = Math.Max(44, activeTextBox.Font.Height * 3 + 12);
                        activeTextBox.Left = (int)selPt.X;
                        activeTextBox.Top = (int)selPt.Y;
                        activeTextBox.KeyDown += OnTextBoxKeyDown;
                        Controls.Add(activeTextBox);
                        activeTextBox.Focus();
                        return;
                    }

                    isDrawing = true;
                    if (annotationTool == DrawingTool.Pen || annotationTool == DrawingTool.Highlighter || annotationTool == DrawingTool.Blur)
                    {
                        activeAnnotationStroke = annotationTool == DrawingTool.Blur ? new BlurStrokeAnnotation(background) : new StrokeAnnotation();
                        activeAnnotationStroke.Color = currentColor;
                        activeAnnotationStroke.Width = currentWidth;
                        activeAnnotationStroke.Highlighter = annotationTool == DrawingTool.Highlighter;
                        activeAnnotationStroke.Opacity = currentOpacity;
                        if (annotationTool == DrawingTool.Blur) ((BlurStrokeAnnotation)activeAnnotationStroke).Intensity = currentBlurIntensity;
                        activeAnnotationStroke.Points.Add(selPt);
                    }
                    else if (annotationTool == DrawingTool.Pixelate)
                    {
                        annotationShapeStart = selPt;
                        activePixelateArea = new PixelateAreaAnnotation(background);
                        activePixelateArea.Start = selPt;
                        activePixelateArea.End = selPt;
                        activePixelateArea.BlockSize = Math.Max(4, (int)(currentWidth * 1.5f));
                    }
                    else if (annotationTool == DrawingTool.Callout)
                    {
                        // 从指向点(Tail)拖到主体对角：按下=Tail，拖动定义主体
                        isDrawing = true;
                        annotationShapeStart = selPt;
                        activeCallout = new CalloutAnnotation();
                        activeCallout.Tail = selPt;
                        activeCallout.Start = selPt;
                        activeCallout.End = selPt;
                        activeCallout.Color = currentColor;
                        activeCallout.BorderWidth = Math.Max(2, currentWidth * 0.8f);
                        activeCallout.FontSize = Math.Max(16, currentWidth * 4);
                    }
                    else
                    {
                        annotationShapeStart = selPt;
                        activeAnnotationShape = new ShapeAnnotation();
                        activeAnnotationShape.Tool = annotationTool;
                        activeAnnotationShape.Color = annotationTool == DrawingTool.Cover ? Color.FromArgb(246, 246, 235) : currentColor;
                        activeAnnotationShape.Width = annotationTool == DrawingTool.Cover ? 2.0f : currentWidth;
                        activeAnnotationShape.Highlighter = annotationTool == DrawingTool.Highlighter;
                        activeAnnotationShape.Filled = shapeFilled;
                        activeAnnotationShape.Opacity = currentOpacity;
                        activeAnnotationShape.DashStyle = currentDashStyle;
                        activeAnnotationShape.HasShadow = shapeShadow;
                        activeAnnotationShape.Start = selPt;
                        activeAnnotationShape.End = selPt;
                    }
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

                if (isAnnotating)
                {
                    return;
                }

                mouseDownLocation = e.Location;
                isSelecting = true;
                selectionStartView = e.Location;
                selectionCurrentView = e.Location;
                hasHoveredWindow = false;
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

            if (tool == DrawingTool.Stamp)
            {
                SaveUndoState();
                var stamp = new StampAnnotation();
                stamp.Location = imagePoint;
                stamp.Type = currentStampType;
                stamp.Color = currentColor;
                stamp.Radius = Math.Max(16.0f, currentWidth * 3.8f);
                annotations.Add(stamp);
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
                activeStroke.Opacity = currentOpacity;
                if (gestureTool == DrawingTool.Blur) ((BlurStrokeAnnotation)activeStroke).Intensity = currentBlurIntensity;
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
                activeShape.Filled = shapeFilled;
                activeShape.Opacity = currentOpacity;
                activeShape.DashStyle = currentDashStyle;
                activeShape.HasShadow = shapeShadow;
                activeShape.Start = imagePoint;
                activeShape.End = imagePoint;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (selectionAction != SelectionAction.None)
            {
                // 检测右键拖动：超过阈值则进入橡皮擦模式
                if (hasSelectedRegion && isAnnotating && !isDrawing && !isRightClickErasing && (e.Button & MouseButtons.Right) == MouseButtons.Right)
                {
                    int dragDist = Math.Abs(e.X - rightClickStart.X) + Math.Abs(e.Y - rightClickStart.Y);
                    if (dragDist > 4)
                    {
                        SelectionSaveUndo();
                        SelectionEraseAt((PointF)e.Location);
                        isDrawing = true;
                        isRightClickErasing = true;
                        Invalidate();
                        return;
                    }
                }

                if (isResizingSelection)
                {
                    selectedRegionView = ResizeByHandle(resizeOriginalRect, activeResizeHandle, e.Location);
                    Invalidate();
                    return;
                }

                if (isMovingSelection)
                {
                    selectedRegionView = MoveSelectionByDelta(selectionMoveOriginalView, e.Location.X - selectionMoveStartView.X, e.Location.Y - selectionMoveStartView.Y);
                    Invalidate();
                    return;
                }

                if (isSelecting)
                {
                    selectionCurrentView = e.Location;
                    Invalidate();
                    return;
                }

                // 选择工具：移动或缩放已选中标注
                if (isAnnotating && annotationTool == DrawingTool.Select &&
                    annotationEditMode != AnnotationEditMode.None &&
                    selectedAnnotationIndex >= 0 && selectedAnnotationIndex < selectionAnnotations.Count)
                {
                    Graphics mg = GetMeasureGraphics();
                    var ann = selectionAnnotations[selectedAnnotationIndex];
                    if (annotationEditMode == AnnotationEditMode.Move)
                    {
                        float dx = e.Location.X - annotationEditLastPoint.X;
                        float dy = e.Location.Y - annotationEditLastPoint.Y;
                        // 限制在选区内
                        RectangleF b = ann.GetBounds(mg);
                        b.X += dx; b.Y += dy;
                        if (b.Left >= selectedRegionView.Left - 2 && b.Top >= selectedRegionView.Top - 2 &&
                            b.Right <= selectedRegionView.Right + 2 && b.Bottom <= selectedRegionView.Bottom + 2)
                        {
                            ann.Translate(dx, dy);
                        }
                        annotationEditLastPoint = e.Location;
                    }
                    else if (annotationEditMode == AnnotationEditMode.Resize)
                    {
                        // 根据手柄重算包围盒：以对角锚点固定，鼠标位置为活动角
                        RectangleF nb = ComputeResizedBounds(annotationEditStartBounds, annotationResizeHandle, e.Location);
                        ann.ResizeByHandle(annotationResizeHandle, nb);
                    }
                    Invalidate();
                    return;
                }

                if (isDrawing && isAnnotating)
                {
                    PointF selPt = e.Location;
                    if (isRightClickErasing)
                    {
                        SelectionEraseAt(selPt);
                        Invalidate();
                        return;
                    }

                    if (annotationTool == DrawingTool.Eraser)
                    {
                        SelectionEraseAt(selPt);
                        Invalidate();
                        return;
                    }

                    if (activeAnnotationStroke != null)
                    {
                        if (activeAnnotationStroke.Points.Count == 0 || Distance(activeAnnotationStroke.Points[activeAnnotationStroke.Points.Count - 1], selPt) > 1.5f)
                        {
                            activeAnnotationStroke.Points.Add(selPt);
                            Invalidate();
                        }
                    }
                    else if (activePixelateArea != null)
                    {
                        activePixelateArea.End = selPt;
                        Invalidate();
                    }
                    else if (activeCallout != null)
                    {
                        activeCallout.End = selPt;
                        Invalidate();
                    }
                    else if (activeAnnotationShape != null)
                    {
                        activeAnnotationShape.Start = annotationShapeStart;
                        activeAnnotationShape.End = ApplyShapeConstraints(annotationShapeStart, selPt);
                        Invalidate();
                    }
                    return;
                }

                int prevHover = hoveredToolbarItem;
                bool prevWindow = hasHoveredWindow;
                UpdateSelectionHover(e.Location);
                UpdateSelectionCursor(e.Location);
                if (hoveredToolbarItem != prevHover || hasHoveredWindow != prevWindow)
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

            // 标注模式下右键松开：如果未拖动则弹出菜单
            if (e.Button == MouseButtons.Right && hasSelectedRegion && isAnnotating && !isRightClickErasing && selectionAction != SelectionAction.None)
            {
                int dragDist = Math.Abs(e.X - rightClickStart.X) + Math.Abs(e.Y - rightClickStart.Y);
                if (dragDist <= 4)
                {
                    ShowSelectionContextMenu(e.Location);
                    return;
                }
            }

            if (isAnnotating && annotationTool == DrawingTool.Select && annotationEditMode != AnnotationEditMode.None)
            {
                annotationEditMode = AnnotationEditMode.None;
                annotationResizeHandle = -1;
                Invalidate();
                return;
            }

            if (isDrawing && isAnnotating)
            {
                isDrawing = false;
                isRightClickErasing = false;
                if (activeAnnotationStroke != null && activeAnnotationStroke.Points.Count > 0)
                {
                    SelectionSaveUndo();
                    selectionAnnotations.Add(activeAnnotationStroke);
                    selectionRedoStack.Clear();
                }
                else if (activePixelateArea != null)
                {
                    RectangleF pr = new RectangleF(
                        Math.Min(activePixelateArea.Start.X, activePixelateArea.End.X),
                        Math.Min(activePixelateArea.Start.Y, activePixelateArea.End.Y),
                        Math.Abs(activePixelateArea.End.X - activePixelateArea.Start.X),
                        Math.Abs(activePixelateArea.End.Y - activePixelateArea.Start.Y));
                    if (pr.Width >= 4 && pr.Height >= 4)
                    {
                        SelectionSaveUndo();
                        selectionAnnotations.Add(activePixelateArea);
                        selectionRedoStack.Clear();
                    }
                    activePixelateArea = null;
                }
                else if (activeCallout != null)
                {
                    RectangleF cb = new RectangleF(
                        Math.Min(activeCallout.Start.X, activeCallout.End.X),
                        Math.Min(activeCallout.Start.Y, activeCallout.End.Y),
                        Math.Abs(activeCallout.End.X - activeCallout.Start.X),
                        Math.Abs(activeCallout.End.Y - activeCallout.Start.Y));
                    if (cb.Width >= 20 && cb.Height >= 16)
                    {
                        string text = InputDialog.Show(null, "气泡文字", "输入气泡内文字：", "");
                        activeCallout.Text = text ?? "";
                        SelectionSaveUndo();
                        selectionAnnotations.Add(activeCallout);
                        selectionRedoStack.Clear();
                    }
                    activeCallout = null;
                }
                else if (activeAnnotationShape != null)
                {
                    SelectionSaveUndo();
                    if (annotationTool == DrawingTool.Ruler)
                    {
                        var ruler = new RulerAnnotation();
                        ruler.Start = activeAnnotationShape.Start;
                        ruler.End = activeAnnotationShape.End;
                        ruler.Color = currentColor;
                        selectionAnnotations.Add(ruler);
                    }
                    else
                    {
                        selectionAnnotations.Add(activeAnnotationShape);
                    }
                    selectionRedoStack.Clear();
                }
                activeAnnotationStroke = null;
                activeAnnotationShape = null;
                activePixelateArea = null;
                activeCallout = null;
                Invalidate();
                return;
            }

            if (isResizingSelection)
            {
                isResizingSelection = false;
                activeResizeHandle = -1;
                Invalidate();
                return;
            }

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

                int dragDist = Math.Abs(e.Location.X - mouseDownLocation.X) + Math.Abs(e.Location.Y - mouseDownLocation.Y);
                if (dragDist < 5 && selectionAction == SelectionAction.PixPinCapture)
                {
                    // 实时窗口检测
                    Point screenPt = new Point(mouseDownLocation.X + virtualBounds.Left, mouseDownLocation.Y + virtualBounds.Top);
                    Rectangle screenRect;
                    string windowTitle;
                    if (CaptureTool.GetWindowInfoAtPoint(screenPt, out screenRect, out windowTitle))
                    {
                        Rectangle viewRect = new Rectangle(
                            screenRect.Left - virtualBounds.Left,
                            screenRect.Top - virtualBounds.Top,
                            screenRect.Width,
                            screenRect.Height);
                        viewRect = Clip(viewRect, new Rectangle(0, 0, Width, Height));
                        if (viewRect.Width >= 4 && viewRect.Height >= 4)
                        {
                            selectedRegionView = viewRect;
                            hasSelectedRegion = true;
                            hasHoveredWindow = false;
                            isAnnotating = true;
                            marchTimer.Start();
                            ShowToast("已进入标注模式", "双击或 Enter 复制  右键更多  Alt+拖拽移动选区");
                            Invalidate();
                            return;
                        }
                    }
                }

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
                if (tool == DrawingTool.Ruler)
                {
                    // 测距工具：转换为 RulerAnnotation
                    var ruler = new RulerAnnotation();
                    ruler.Start = activeShape.Start;
                    ruler.End = activeShape.End;
                    ruler.Color = currentColor;
                    annotations.Add(ruler);
                }
                else
                {
                    annotations.Add(activeShape);
                }
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

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);

            // 清理双击前 OnMouseDown 创建的垃圾标注
            if (activeAnnotationStroke != null || activeAnnotationShape != null)
            {
                activeAnnotationStroke = null;
                activeAnnotationShape = null;
                isDrawing = false;
                if (isRightClickErasing) isRightClickErasing = false;
            }

            // QQ截图核心操作：双击已选区域直接复制到剪贴板
            if (selectionAction != SelectionAction.None && hasSelectedRegion)
            {
                CommitTextInput(true);
                CopyAnnotatedRegion();
                Close();
            }
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

            if (e.Control && e.Shift && e.KeyCode == Keys.Z)
            {
                Redo();
                e.Handled = true;
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
            if (e.KeyCode == Keys.Home) { imageCenter = new PointF(virtualBounds.Width / 2.0f, virtualBounds.Height / 2.0f); zoom = 1.0f; Invalidate(); return; }

            if (e.KeyCode == Keys.P) { tool = DrawingTool.Pen; mode = OverlayMode.Draw; ApplyToolCursor(); ShowToolChangeHint(); return; }
            if (e.KeyCode == Keys.H) { tool = DrawingTool.Highlighter; mode = OverlayMode.Draw; ApplyToolCursor(); ShowToolChangeHint(); return; }
            if (e.KeyCode == Keys.L) { tool = DrawingTool.Line; mode = OverlayMode.Draw; ApplyToolCursor(); ShowToolChangeHint(); return; }
            if (e.KeyCode == Keys.A) { tool = DrawingTool.Arrow; mode = OverlayMode.Draw; ApplyToolCursor(); ShowToolChangeHint(); return; }
            if (e.KeyCode == Keys.R) { tool = DrawingTool.Rectangle; mode = OverlayMode.Draw; ApplyToolCursor(); ShowToolChangeHint(); return; }
            if (e.KeyCode == Keys.O) { tool = DrawingTool.Ellipse; mode = OverlayMode.Draw; ApplyToolCursor(); ShowToolChangeHint(); return; }
            if (e.KeyCode == Keys.V) { tool = DrawingTool.Cover; mode = OverlayMode.Draw; ApplyToolCursor(); ShowToolChangeHint(); return; }
            if (e.KeyCode == Keys.M) { tool = DrawingTool.NumberMarker; mode = OverlayMode.Draw; ApplyToolCursor(); ShowToolChangeHint(); return; }
            if (e.KeyCode == Keys.X) { tool = DrawingTool.Blur; mode = OverlayMode.Draw; ApplyToolCursor(); ShowToolChangeHint(); return; }
            if (e.KeyCode == Keys.E) { tool = DrawingTool.Eraser; mode = OverlayMode.Draw; ApplyToolCursor(); ShowToolChangeHint(); return; }
            if (e.KeyCode == Keys.I)
            {
                if (tool == DrawingTool.Stamp)
                {
                    // 再次按 I 循环切换印章类型
                    int next = ((int)currentStampType + 1) % StampAnnotation.StampNames.Length;
                    currentStampType = (StampAnnotation.StampType)next;
                }
                else
                {
                    tool = DrawingTool.Stamp;
                }
                mode = OverlayMode.Draw;
                ApplyToolCursor();
                ShowToolChangeHint();
                return;
            }
            if (e.KeyCode == Keys.T) { tool = DrawingTool.Text; mode = OverlayMode.Text; rightAlignedText = e.Shift; ApplyToolCursor(); ShowToolChangeHint(); return; }
            if (e.KeyCode == Keys.W) { mode = OverlayMode.Whiteboard; zoom = 1.0f; imageCenter = viewCenter; ShowStatusHint("白板模式"); Invalidate(); return; }
            if (e.KeyCode == Keys.K) { mode = OverlayMode.Blackboard; zoom = 1.0f; imageCenter = viewCenter; ShowStatusHint("黑板模式"); Invalidate(); return; }
            if (e.KeyCode == Keys.S) { SaveScreenshot(); return; }
            if (e.KeyCode == Keys.C) { ClearAnnotations(); return; }
            if (e.KeyCode == Keys.D) { tool = DrawingTool.Ruler; mode = OverlayMode.Draw; ApplyToolCursor(); ShowToolChangeHint(); return; }
            if (e.KeyCode == Keys.F && (tool == DrawingTool.Rectangle || tool == DrawingTool.Ellipse))
            {
                shapeFilled = !shapeFilled;
                Invalidate();
                return;
            }

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
            if (key >= Keys.D1 && key <= Keys.D9)
            {
                int ci = key - Keys.D1;
                if (ci < PaletteColors.Length)
                {
                    currentColor = PaletteColors[ci];
                    Invalidate();
                }
            }
            else if (key == Keys.D0)
            {
                // 按 0 打开自定义颜色选择器
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
            }
        }

        private PointF ApplyShapeConstraints(PointF start, PointF end)
        {
            if ((ModifierKeys & Keys.Shift) != Keys.Shift)
            {
                return end;
            }

            DrawingTool shapeTool = activeAnnotationShape != null ? activeAnnotationShape.Tool : (activeShape != null ? activeShape.Tool : (isAnnotating ? annotationTool : tool));
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
                if (isAnnotationTextInput)
                {
                    SelectionSaveUndo();
                    var annotation = new TextAnnotation();
                    annotation.Location = activeTextLocation;
                    annotation.Text = text;
                    annotation.Color = currentColor;
                    annotation.FontSize = fontSize;
                    annotation.RightAligned = rightAlignedText;
                    annotation.HasBackground = textHasBackground;
                    selectionAnnotations.Add(annotation);
                    selectionRedoStack.Clear();
                }
                else
                {
                    SaveUndoState();
                    var annotation = new TextAnnotation();
                    annotation.Location = activeTextLocation;
                    annotation.Text = text;
                    annotation.Color = currentColor;
                    annotation.FontSize = fontSize;
                    annotation.RightAligned = rightAlignedText;
                    annotation.HasBackground = textHasBackground;
                    annotations.Add(annotation);
                    redoStack.Clear();
                }
                Invalidate();
            }
            isAnnotationTextInput = false;
        }

        private void SaveUndoState()
        {
            undoStack.Insert(0, CloneAnnotations(annotations));
            TrimStack(undoStack, MaxUndoStates);
            ScheduleAutosave();
        }

        private static void TrimStack(List<List<Annotation>> stack, int max)
        {
            if (stack.Count <= max) return;
            for (int i = stack.Count - 1; i >= max; i--)
            {
                stack[i].Clear();
            }
            stack.RemoveRange(max, stack.Count - max);
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
            redoStack.Insert(0, CloneAnnotations(annotations));
            annotations.Clear();
            annotations.AddRange(undoStack[0]);
            undoStack.RemoveAt(0);
            ShowStatusHint("撤销 (" + undoStack.Count + " 步可撤销)");
            Invalidate();
        }

        private void Redo()
        {
            if (redoStack.Count == 0) return;
            undoStack.Insert(0, CloneAnnotations(annotations));
            annotations.Clear();
            annotations.AddRange(redoStack[0]);
            redoStack.RemoveAt(0);
            ShowStatusHint("重做 (" + redoStack.Count + " 步可重做)");
            Invalidate();
        }

        private void ShowStatusHint(string message)
        {
            ShowToast("", message);
        }

        private void ClearAnnotations()
        {
            if (annotations.Count == 0) return;
            SaveUndoState();
            annotations.Clear();
            redoStack.Clear();
            ShowStatusHint("已清屏 (" + undoStack.Count + " 步可撤销)");
            Invalidate();
        }

        private static Graphics GetMeasureGraphics()
        {
            if (measureGraphics == null)
            {
                measureBitmap = new Bitmap(1, 1);
                measureGraphics = Graphics.FromImage(measureBitmap);
            }
            return measureGraphics;
        }

        private void EraseAt(PointF imagePoint)
        {
            Graphics g = GetMeasureGraphics();
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

        /// <summary>根据当前工具切换光标样式。</summary>
        private void ApplyToolCursor()
        {
            if (tool == DrawingTool.Text)
                Cursor = Cursors.IBeam;
            else if (tool == DrawingTool.Eraser)
                Cursor = Cursors.Hand;
            else
                Cursor = Cursors.Cross;
            Invalidate();
        }

        /// <summary>工具切换时显示居中 Toast 提示。</summary>
        private void ShowToolChangeHint()
        {
            int ti = (int)tool;
            string name = (ti >= 0 && ti < DrawingToolNames.Length) ? DrawingToolNames[ti] : "画笔";
            if (tool == DrawingTool.Stamp)
            {
                int si = (int)currentStampType;
                name = "印章 " + (si < StampAnnotation.StampChars.Length ? StampAnnotation.StampChars[si].ToString() : "★");
            }
            ShowStatusHint(name);
            Invalidate();
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


using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace BoardBeam
{
    internal static class RecordingTool
    {
        private static RecordingControlForm activeForm;

        public static bool IsRecording
        {
            get { return activeForm != null && !activeForm.IsDisposed && !activeForm.IsCompleted; }
        }

        public static void Start(Rectangle screenRegion)
        {
            if (screenRegion.Width < 16 || screenRegion.Height < 16) return;
            Shutdown();

            var form = new RecordingControlForm(screenRegion);
            activeForm = form;
            form.FormClosed += delegate
            {
                if (object.ReferenceEquals(activeForm, form))
                {
                    activeForm = null;
                }
            };
            form.Show();
        }

        public static void StopActive()
        {
            if (activeForm == null || activeForm.IsDisposed) return;
            if (activeForm.IsCompleted)
            {
                activeForm.Close();
            }
            else
            {
                activeForm.StopRecording();
            }
        }

        public static void Shutdown()
        {
            if (activeForm == null || activeForm.IsDisposed) return;
            if (!activeForm.IsCompleted)
            {
                activeForm.StopRecording();
            }
            activeForm.Close();
            activeForm = null;
        }

        private sealed class RecordingControlForm : Form
        {
            private const int FramesPerSecond = 8;
            private const int MaxOutputWidth = 1280;
            private const int MaxOutputHeight = 720;
            private const int MaxFrames = FramesPerSecond * 120;

            private readonly Rectangle region;
            private readonly string outputFile;
            private readonly Label statusLabel;
            private readonly Button stopButton;
            private readonly Button openButton;
            private readonly Stopwatch stopwatch;
            private readonly Timer timer;
            private AnimatedGifWriter writer;
            private int frameCount;
            private bool completed;
            private bool stopping;
            private bool capturing;

            public RecordingControlForm(Rectangle region)
            {
                this.region = region;
                outputFile = AppPaths.NewRecordingPath("_recording");
                stopwatch = new Stopwatch();
                timer = new Timer();
                timer.Interval = 1000 / FramesPerSecond;
                timer.Tick += OnCaptureTick;

                Text = "BoardBeam 录屏";
                Width = 280;
                Height = 96;
                TopMost = true;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                ControlBox = false;
                StartPosition = FormStartPosition.Manual;

                statusLabel = new Label();
                statusLabel.Left = 10;
                statusLabel.Top = 10;
                statusLabel.Width = 160;
                statusLabel.Height = 48;
                statusLabel.Text = "准备录制...";

                stopButton = new Button();
                stopButton.Text = "停止";
                stopButton.Left = 184;
                stopButton.Top = 10;
                stopButton.Width = 76;
                stopButton.Height = 28;
                stopButton.Click += delegate
                {
                    if (completed) Close();
                    else StopRecording();
                };

                openButton = new Button();
                openButton.Text = "打开";
                openButton.Left = 184;
                openButton.Top = 44;
                openButton.Width = 76;
                openButton.Height = 28;
                openButton.Visible = false;
                openButton.Click += delegate { OpenOutputFile(); };

                Controls.Add(statusLabel);
                Controls.Add(stopButton);
                Controls.Add(openButton);
                Location = CalculateLocation(region);
            }

            public bool IsCompleted
            {
                get { return completed; }
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                try
                {
                    NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                }
                catch
                {
                }

                try
                {
                    writer = new AnimatedGifWriter(outputFile, region.Width, region.Height, FramesPerSecond, MaxOutputWidth, MaxOutputHeight);
                    stopwatch.Start();
                    timer.Start();
                    statusLabel.Text = "录制中\n0.0 秒 / 0 帧";
                }
                catch (Exception ex)
                {
                    completed = true;
                    statusLabel.Text = "启动失败\n" + ex.Message;
                    stopButton.Text = "关闭";
                }
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (!completed)
                {
                    StopRecording();
                }
                base.OnFormClosing(e);
            }

            public void StopRecording()
            {
                if (completed || stopping) return;
                stopping = true;
                timer.Stop();

                try
                {
                    if (writer != null)
                    {
                        writer.Finish();
                        writer.Dispose();
                        writer = null;
                    }

                    completed = true;
                    statusLabel.Text = "已保存 GIF\n" + frameCount + " 帧";
                    stopButton.Text = "关闭";
                    openButton.Visible = true;
                }
                catch (Exception ex)
                {
                    completed = true;
                    statusLabel.Text = "保存失败\n" + ex.Message;
                    stopButton.Text = "关闭";
                }
                finally
                {
                    stopping = false;
                }
            }

            private void OnCaptureTick(object sender, EventArgs e)
            {
                if (completed || stopping || capturing || writer == null) return;
                capturing = true;
                try
                {
                    using (Bitmap frame = CaptureTool.CaptureScreen(region))
                    {
                        writer.AddFrame(frame);
                    }

                    frameCount++;
                    statusLabel.Text = "录制中\n" + stopwatch.Elapsed.TotalSeconds.ToString("0.0") + " 秒 / " + frameCount + " 帧";
                    if (frameCount >= MaxFrames)
                    {
                        statusLabel.Text = "已到 120 秒上限\n正在保存...";
                        StopRecording();
                    }
                }
                catch (Exception ex)
                {
                    statusLabel.Text = "录制中断\n" + ex.Message;
                    StopRecording();
                }
                finally
                {
                    capturing = false;
                }
            }

            private void OpenOutputFile()
            {
                try
                {
                    Process.Start("explorer.exe", "/select,\"" + outputFile + "\"");
                }
                catch
                {
                    Process.Start(AppPaths.RecordingDirectory);
                }
            }

            private static Point CalculateLocation(Rectangle region)
            {
                Rectangle bounds = SystemInformation.VirtualScreen;
                int width = 280;
                int height = 96;
                int x = region.Right + 12;
                if (x + width > bounds.Right) x = region.Left - width - 12;
                if (x < bounds.Left) x = bounds.Right - width - 24;

                int y = region.Top;
                if (y < bounds.Top + 24) y = bounds.Top + 24;
                if (y + height > bounds.Bottom) y = bounds.Bottom - height - 24;
                return new Point(x, y);
            }
        }
    }
}


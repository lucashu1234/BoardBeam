using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BoardBeam
{
    internal static class OcrTool
    {
        public static string Recognize(Bitmap bitmap)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "boardbeam_ocr_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                bitmap.Save(tempFile, ImageFormat.Png);
                return RecognizeFile(tempFile);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        public static void ShowOcrCapture(PresenterApplicationContext context)
        {
            context.ShowOverlay(OverlayMode.OcrCapture);
        }

        public static void RecognizeRegion(Rectangle screenRegion, PresenterApplicationContext context)
        {
            Bitmap capture = null;
            try
            {
                capture = CaptureTool.CaptureScreen(screenRegion);
                string text = Recognize(capture);
                if (string.IsNullOrEmpty(text))
                {
                    context.Notify("OCR 未识别到文字", "请确认区域内包含可识别的文字，且系统语言已安装 OCR 包。");
                    return;
                }

                string trimmed = text.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    context.Notify("OCR 未识别到文字", "区域内未检测到可识别文字。");
                    return;
                }

                // 弹出结果窗：可编辑 + 一键格式化（合并行/表格）+ 复制
                var form = new OcrResultForm(trimmed, context);
                form.Show();
            }
            catch (Exception ex)
            {
                context.Notify("OCR 识别失败", ex.Message);
            }
            finally
            {
                if (capture != null) capture.Dispose();
            }
        }

        private static string RecognizeFile(string imagePath)
        {
            string script =
                "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
                "Add-Type -AssemblyName System.Runtime.WindowsRuntime; " +
                "$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetGenericArguments().Count -eq 1 })[0]; " +
                "Function Await($winRtTask, $resultType) { $asTask = $asTaskGeneric.MakeGenericMethod($resultType); $netTask = $asTask.Invoke($null, @($winRtTask)); $netTask.Wait(); $netTask.Result }; " +
                "[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime] | Out-Null; " +
                "[Windows.Media.Ocr.OcrEngine,Windows.Media,ContentType=WindowsRuntime] | Out-Null; " +
                "[Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics.Imaging,ContentType=WindowsRuntime] | Out-Null; " +
                "[Windows.Graphics.Imaging.SoftwareBitmap,Windows.Graphics.Imaging,ContentType=WindowsRuntime] | Out-Null; " +
                "$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages(); " +
                "if ($null -eq $engine) { Write-Error 'No OCR engine'; exit 1 }; " +
                "$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync('" + imagePath.Replace("'", "''") + "')) ([Windows.Storage.StorageFile]); " +
                "$stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream]); " +
                "$decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder]); " +
                "$bmp = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap]); " +
                "$result = Await ($engine.RecognizeAsync($bmp)) ([Windows.Media.Ocr.OcrResult]); " +
                "Write-Output $result.Text; " +
                "$stream.Close()";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = Process.Start(psi))
            {
                // 先读两个流再等待，避免死锁
                string output = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(15000);

                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { }
                    throw new Exception("OCR 执行超时（15秒），请重试。");
                }

                if (process.ExitCode != 0)
                {
                    string errMsg = !string.IsNullOrEmpty(stderr) ? stderr.Split('\n')[0].Trim() : "PowerShell 退出码 " + process.ExitCode;
                    throw new Exception("OCR 引擎错误: " + errMsg);
                }

                return output;
            }
        }
    }
}

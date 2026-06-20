using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BoardBeam
{
    internal static class OcrTool
    {
        public static void ShowOcrCapture(PresenterApplicationContext context)
        {
            context.ShowOverlay(OverlayMode.OcrCapture);
        }

        public static string Recognize(Bitmap bitmap)
        {
            // 临时文件路径（WinRT 用 StorageFile.FromPathAsync 读，PowerShell 兜底也用文件）
            string tempFile = Path.Combine(Path.GetTempPath(), "boardbeam_ocr_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                bitmap.Save(tempFile, ImageFormat.Png);
                // 主路径：WinRT 直连（快）；失败回退 PowerShell（兼容旧环境）
                try { return RecognizeViaWinrt(tempFile, null); }
                catch (Exception ex) { CrashLogger.Log("OCR WinRT 直连失败，回退 PowerShell", ex); }
                return RecognizeViaPowershell(tempFile);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        /// <summary>用指定语言识别；languageTag 为 null 则用用户配置语言。</summary>
        public static string Recognize(Bitmap bitmap, string languageTag)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "boardbeam_ocr_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                bitmap.Save(tempFile, ImageFormat.Png);
                try { return RecognizeViaWinrt(tempFile, languageTag); }
                catch (Exception ex) { CrashLogger.Log("OCR WinRT 直连失败（带语言），回退 PowerShell", ex); }
                return RecognizeViaPowershell(tempFile);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        /// <summary>列出系统已安装的 OCR 语言（LanguageTag），供结果窗切换重识别。</summary>
        public static List<string> ListAvailableLanguages()
        {
            var list = new List<string>();
            try { foreach (var tag in WinrtOcr.ListLanguages()) list.Add(tag); } catch { }
            return list;
        }

        public static string UsedLanguageTag()
        {
            try { return WinrtOcr.DefaultLanguageTag() ?? "用户语言"; } catch { return "用户语言"; }
        }

        public static void RecognizeRegion(Rectangle screenRegion, PresenterApplicationContext context)
        {
            // 异步：先截屏，后台识别，UI 显示"识别中"，完成后弹结果窗
            Bitmap capture = CaptureTool.CaptureScreen(screenRegion);
            Form wait = ShowWaitOverlay(screenRegion);
            string lang = UsedLanguageTag();
            Task.Run(() =>
            {
                string text = null;
                Exception err = null;
                try { text = Recognize(capture); }
                catch (Exception ex) { err = ex; }
                return new { text, err };
            }).ContinueWith(t =>
            {
                try { if (wait != null && !wait.IsDisposed) wait.Close(); } catch { }

                if (t.Exception != null || (t.Result != null && t.Result.err != null))
                {
                    if (capture != null) capture.Dispose();
                    context.Notify("OCR 识别失败", t.Result != null && t.Result.err != null ? t.Result.err.Message : (t.Exception != null ? t.Exception.Message : "未知错误"));
                    return;
                }
                string trimmed = (t.Result.text ?? "").Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    if (capture != null) capture.Dispose();
                    context.Notify("OCR 未识别到文字", "请确认区域内含可识别文字，且已安装对应语言 OCR 包。");
                    return;
                }
                // 克隆原图交给结果窗（支持切换语言重识别），原 capture 释放
                Bitmap forForm = null;
                if (capture != null) { forForm = (Bitmap)capture.Clone(); capture.Dispose(); }
                var form = new OcrResultForm(trimmed, context, lang, forForm);
                form.Show();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static Form ShowWaitOverlay(Rectangle region)
        {
            try
            {
                var f = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Bounds = region,
                    ShowInTaskbar = false,
                    TopMost = true,
                    BackColor = Color.FromArgb(40, 20, 20, 30),
                    TransparencyKey = Color.FromArgb(40, 20, 20, 30),
                };
                f.Paint += delegate(object s, PaintEventArgs e)
                {
                    using (var bg = new SolidBrush(Color.FromArgb(150, 20, 20, 30)))
                        e.Graphics.FillRectangle(bg, f.ClientRectangle);
                    using (var font = new Font(FontFamily.GenericSansSerif, 22, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var brush = new SolidBrush(Color.White))
                    {
                        string msg = "OCR 识别中…";
                        var sz = e.Graphics.MeasureString(msg, font);
                        e.Graphics.DrawString(msg, font, brush, (f.Width - sz.Width) / 2f, (f.Height - sz.Height) / 2f);
                    }
                };
                f.Show();
                Application.DoEvents();
                return f;
            }
            catch { return null; }
        }

        // ===== WinRT 直连（封装在独立类，避免在旧环境编译失败时影响其余代码） =====
        private static string RecognizeViaWinrt(string imagePath, string languageTag)
        {
            return WinrtOcr.RecognizeFile(imagePath, languageTag).GetAwaiter().GetResult();
        }

        // ===== PowerShell 兜底（保留原逻辑，兼容无 WinRT 元数据的环境） =====
        private static string RecognizeViaPowershell(string imagePath)
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
                string output = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(15000);
                if (!process.HasExited) { try { process.Kill(); } catch { } throw new Exception("OCR 执行超时（15秒）。"); }
                if (process.ExitCode != 0)
                {
                    string errMsg = !string.IsNullOrEmpty(stderr) ? stderr.Split('\n')[0].Trim() : "PowerShell 退出码 " + process.ExitCode;
                    throw new Exception("OCR 引擎错误: " + errMsg);
                }
                return output;
            }
        }
    }

    /// <summary>
    /// 直接调用 Windows.Media.Ocr WinRT API（不经 PowerShell，更快更可靠）。
    /// 用手动 Completed/GetResults 处理 WinRT 异步，不依赖 System.Runtime.WindowsRuntime 的 AsTask
    /// （后者要求单体 Windows.winmd，而系统通常只有按命名空间拆分的 winmd）。
    /// 所有调用阻塞式，由调用方在后台线程运行。
    /// </summary>
    internal static class WinrtOcr
    {
        private static T Await<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            var done = new System.Threading.ManualResetEventSlim(false);
            Windows.Foundation.AsyncStatus status = Windows.Foundation.AsyncStatus.Started;
            op.Completed = delegate(Windows.Foundation.IAsyncOperation<T> o, Windows.Foundation.AsyncStatus s)
            {
                status = s;
                done.Set();
            };
            done.Wait();
            if (status == Windows.Foundation.AsyncStatus.Error)
            {
                var code = op.ErrorCode;
                throw code ?? new Exception("WinRT 异步操作出错");
            }
            if (status == Windows.Foundation.AsyncStatus.Canceled)
                throw new System.OperationCanceledException();
            return op.GetResults();
        }

        public static string RecognizeFileSync(string imagePath, string languageTag)
        {
            var file = Await(Windows.Storage.StorageFile.GetFileFromPathAsync(imagePath));
            var stream = Await(file.OpenAsync(Windows.Storage.FileAccessMode.Read));
            try
            {
                var decoder = Await(Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream));
                var bmp = Await(decoder.GetSoftwareBitmapAsync(
                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied));
                Windows.Media.Ocr.OcrEngine engine;
                if (!string.IsNullOrEmpty(languageTag))
                {
                    Windows.Globalization.Language lang = null;
                    foreach (var l in Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages)
                    {
                        if (l.LanguageTag == languageTag) { lang = l; break; }
                    }
                    engine = lang != null ? Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(lang) : null;
                }
                else
                {
                    engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
                }
                if (engine == null)
                    throw new Exception("系统未安装 OCR 语言包。请在「Windows 设置 → 时间和语言 → 语言」添加语言并勾选 OCR。");
                var result = Await(engine.RecognizeAsync(bmp));
                return result.Text;
            }
            finally
            {
                try { stream.Dispose(); } catch { }
            }
        }

        public static Task<string> RecognizeFile(string imagePath, string languageTag)
        {
            return Task.Run(() => RecognizeFileSync(imagePath, languageTag));
        }

        public static List<string> ListLanguages()
        {
            var list = new List<string>();
            foreach (var l in Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages)
                list.Add(l.LanguageTag);
            return list;
        }

        public static string DefaultLanguageTag()
        {
            var langs = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages;
            return langs.Count > 0 ? langs[0].LanguageTag : null;
        }
    }
}

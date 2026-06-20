using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BoardBeam
{
    /// <summary>OCR 结果窗：可编辑 + 格式化（合并成段/表格）+ 切换语言重识别 + 复制。</summary>
    internal sealed class OcrResultForm : Form
    {
        private readonly PresenterApplicationContext context;
        private readonly Bitmap sourceImage;   // 持有原图，供切换语言重识别
        private string currentLang;
        private readonly TextBox box;
        private readonly ComboBox langCombo;
        private readonly Label statusLabel;

        public OcrResultForm(string text, PresenterApplicationContext context, string lang, Bitmap sourceImage = null)
        {
            this.context = context;
            this.sourceImage = sourceImage;
            currentLang = lang ?? "";
            Text = "OCR 识别结果（" + text.Length + " 字）";
            Width = 660;
            Height = 460;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 6, 8, 4), WrapContents = false };
            toolbar.Controls.Add(MakeButton("合并成段", delegate { box.Text = OcrFormatters.JoinParagraphs(box.Text); }));
            toolbar.Controls.Add(MakeButton("识别为表格", delegate { box.Text = OcrFormatters.ToTable(box.Text); }));
            toolbar.Controls.Add(MakeButton("复制", delegate
            {
                string e;
                if (ClipboardService.TrySetText(box.Text, out e)) context.Notify("已复制", box.Text.Length + " 字");
                else context.Notify("复制失败", e);
            }));
            toolbar.Controls.Add(new Label { Text = "语言：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 5, 0, 0) });
            langCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
            PopulateLanguages();
            langCombo.SelectedIndexChanged += delegate { ReRecognize(); };
            if (sourceImage == null) langCombo.Enabled = false;
            toolbar.Controls.Add(langCombo);
            Controls.Add(toolbar);

            statusLabel = new Label { Dock = DockStyle.Bottom, Height = 20, Text = "语言：" + currentLang, ForeColor = SystemColors.GrayText, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
            Controls.Add(statusLabel);

            box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Regular, GraphicsUnit.Pixel),
                Text = text ?? ""
            };
            Controls.Add(box);
            box.SelectionStart = 0;
            box.SelectionLength = 0;
        }

        private void PopulateLanguages()
        {
            langCombo.Items.Clear();
            var langs = OcrTool.ListAvailableLanguages();
            if (langs.Count == 0)
            {
                langCombo.Items.Add(currentLang.Length > 0 ? currentLang : "用户语言");
                langCombo.SelectedIndex = 0;
                return;
            }
            int sel = 0;
            for (int i = 0; i < langs.Count; i++)
            {
                langCombo.Items.Add(langs[i]);
                if (langs[i] == currentLang) sel = i;
            }
            langCombo.SelectedIndex = sel;
        }

        private void ReRecognize()
        {
            if (sourceImage == null || langCombo.SelectedItem == null) return;
            string tag = langCombo.SelectedItem.ToString();
            if (tag == currentLang) return;
            statusLabel.Text = "识别中（" + tag + "）…";
            UseWaitCursor = true;
            box.Enabled = false;
            string captured = tag;
            Task.Run(() =>
            {
                try { return OcrTool.Recognize(sourceImage, captured); }
                catch (Exception ex) { CrashLogger.Log("切换语言重识别", ex); return null; }
            }).ContinueWith(t =>
            {
                UseWaitCursor = false;
                box.Enabled = true;
                if (!string.IsNullOrEmpty(t.Result))
                {
                    box.Text = t.Result;
                    currentLang = captured;
                    statusLabel.Text = "语言：" + captured + "（" + t.Result.Length + " 字）";
                }
                else
                {
                    statusLabel.Text = "语言：" + currentLang + "（切换后未识别到文字）";
                    PopulateLanguages(); // 复位选中
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, Width = 96, Height = 28 };
            b.Click += onClick;
            return b;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && sourceImage != null) sourceImage.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>OCR 文本后处理：合并硬换行成段落、识别空格分隔的表格。</summary>
    internal static class OcrFormatters
    {
        /// <summary>去掉 OCR 常见的硬换行，合并成段落：行末连字符去换行，空行分段。</summary>
        public static string JoinParagraphs(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var lines = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var sb = new StringBuilder();
            var para = new StringBuilder();
            foreach (string line in lines)
            {
                string t = line.TrimEnd();
                if (string.IsNullOrWhiteSpace(t))
                {
                    if (para.Length > 0) { sb.Append(para.ToString().Trim()).Append("\n\n"); para.Clear(); }
                    continue;
                }
                if (para.Length > 0 && para[para.Length - 1] == '-')
                    para.Length--;
                else if (para.Length > 0)
                    para.Append(' ');
                para.Append(t.Trim());
            }
            if (para.Length > 0) sb.Append(para.ToString().Trim());
            return sb.ToString();
        }

        /// <summary>把含 2+ 连续空格的行识别为表格列，输出 Markdown 表格；不足一半行有列分隔则回退到合并成段。</summary>
        public static string ToTable(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var lines = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var rows = new System.Collections.Generic.List<string[]>();
            int maxCols = 0;
            int tableLineCount = 0, nonEmptyCount = 0;
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                nonEmptyCount++;
                var parts = line.Trim().Split(new[] { "  ", "\t" }, StringSplitOptions.None);
                var clean = new System.Collections.Generic.List<string>();
                foreach (var p in parts) { var c = p.Trim(); if (c.Length > 0) clean.Add(c); }
                if (clean.Count >= 2)
                {
                    tableLineCount++;
                    rows.Add(clean.ToArray());
                    if (clean.Count > maxCols) maxCols = clean.Count;
                }
            }
            if (rows.Count == 0 || maxCols < 2 || nonEmptyCount == 0 || tableLineCount * 2 < nonEmptyCount)
                return JoinParagraphs(raw);

            var sb = new StringBuilder();
            sb.Append("| " + string.Join(" | ", PadRow(rows[0], maxCols)) + " |");
            sb.Append("\n|" + Repeat("---|", maxCols));
            for (int i = 1; i < rows.Count; i++)
                sb.Append("\n| " + string.Join(" | ", PadRow(rows[i], maxCols)) + " |");
            return sb.ToString();
        }

        private static string[] PadRow(string[] row, int cols)
        {
            if (row.Length >= cols) return row;
            var r = new string[cols];
            for (int i = 0; i < row.Length; i++) r[i] = row[i];
            for (int i = row.Length; i < cols; i++) r[i] = "";
            return r;
        }

        private static string Repeat(string s, int n)
        {
            var sb = new StringBuilder(s.Length * n);
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }
    }
}

using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace BoardBeam
{
    /// <summary>OCR 结果窗：可编辑 + 一键格式化（保留原样/合并成段/识别为表格）+ 复制。</summary>
    internal sealed class OcrResultForm : Form
    {
        private readonly string originalText;
        private readonly TextBox box;

        public OcrResultForm(string text, PresenterApplicationContext context)
        {
            originalText = text ?? "";
            Text = "OCR 识别结果（" + text.Length + " 字）";
            Width = 620;
            Height = 440;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 6, 8, 4), WrapContents = false };
            toolbar.Controls.Add(MakeButton("保留原样", delegate { box.Text = originalText; }));
            toolbar.Controls.Add(MakeButton("合并成段", delegate { box.Text = OcrFormatters.JoinParagraphs(originalText); }));
            toolbar.Controls.Add(MakeButton("识别为表格", delegate { box.Text = OcrFormatters.ToTable(originalText); }));
            toolbar.Controls.Add(MakeButton("复制", delegate
            {
                string e;
                if (ClipboardService.TrySetText(box.Text, out e)) context.Notify("已复制", box.Text.Length + " 字");
                else context.Notify("复制失败", e);
            }));
            Controls.Add(toolbar);

            box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Regular, GraphicsUnit.Pixel),
                Text = originalText
            };
            Controls.Add(box);
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, Width = 96, Height = 28 };
            b.Click += onClick;
            return b;
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
                // 行末连字符（word-）去换行
                if (para.Length > 0 && para[para.Length - 1] == '-')
                    para.Length--;  // 去掉连字符直接接上
                else if (para.Length > 0)
                    para.Append(' ');
                para.Append(t.Trim());
            }
            if (para.Length > 0) sb.Append(para.ToString().Trim());
            return sb.ToString();
        }

        /// <summary>把含 2+ 连续空格的行识别为表格列，输出 Markdown 表格；无表格特征则原样返回。</summary>
        public static string ToTable(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var lines = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var rows = new System.Collections.Generic.List<string[]>();
            int maxCols = 0;
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                // 2+ 连续空格视为列分隔
                var parts = line.Trim().Split(new[] { "  ", "\t" }, StringSplitOptions.None);
                if (parts.Length < 2) return raw;  // 无表格特征
                var clean = new System.Collections.Generic.List<string>();
                foreach (var p in parts) { var c = p.Trim(); if (c.Length > 0) clean.Add(c); }
                if (clean.Count >= 2)
                {
                    rows.Add(clean.ToArray());
                    if (clean.Count > maxCols) maxCols = clean.Count;
                }
            }
            if (rows.Count == 0 || maxCols < 2) return raw;

            var sb = new StringBuilder();
            // 表头
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

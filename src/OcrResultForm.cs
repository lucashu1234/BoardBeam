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
            // 格式化基于当前文本框内容（保留用户编辑），而非丢弃编辑重置回原文
            toolbar.Controls.Add(MakeButton("合并成段", delegate { box.Text = OcrFormatters.JoinParagraphs(box.Text); }));
            toolbar.Controls.Add(MakeButton("识别为表格", delegate { box.Text = OcrFormatters.ToTable(box.Text); }));
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
            // 容错：仅当多数（>=50%）非空行有列分隔才视作表格，否则回退到合并成段
            if (rows.Count == 0 || maxCols < 2 || nonEmptyCount == 0 || tableLineCount * 2 < nonEmptyCount)
                return JoinParagraphs(raw);

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

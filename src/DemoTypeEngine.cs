using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BoardBeam
{
    internal sealed class DemoTypeEngine
    {
        private readonly List<string> snippets = new List<string>();
        private int index;
        private DateTime lastLoad = DateTime.MinValue;

        public string TypeNext()
        {
            EnsureLoaded();
            if (snippets.Count == 0)
            {
                return "没有可输入的 DemoType 文本。请在剪贴板放入文本，或创建 demotype.txt。";
            }

            if (index >= snippets.Count) index = 0;
            string text = snippets[index++];
            PasteText(text);
            return "已输入 DemoType 片段 " + index + " / " + snippets.Count;
        }

        public string TypePrevious()
        {
            EnsureLoaded();
            if (snippets.Count == 0)
            {
                return "没有可输入的 DemoType 文本。请在剪贴板放入文本，或创建 demotype.txt。";
            }

            index -= 2;
            while (index < 0)
            {
                index += snippets.Count;
            }

            string text = snippets[index];
            PasteText(text);
            index++;
            return "已输入上一段 DemoType 片段 " + index + " / " + snippets.Count;
        }

        private void EnsureLoaded()
        {
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "demotype.txt");
            if (File.Exists(file))
            {
                DateTime writeTime = File.GetLastWriteTimeUtc(file);
                if (writeTime != lastLoad)
                {
                    lastLoad = writeTime;
                    snippets.Clear();
                    snippets.AddRange(ParseSnippets(File.ReadAllText(file, Encoding.UTF8)));
                    index = 0;
                }
                return;
            }

            snippets.Clear();
            try
            {
                if (Clipboard.ContainsText())
                {
                    snippets.Add(Clipboard.GetText());
                }
            }
            catch
            {
            }
            index = 0;
        }

        private static IEnumerable<string> ParseSnippets(string text)
        {
            string normalized = text.Replace("\r\n", "\n");
            string[] parts = normalized.Split(new[] { "\n---\n" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim('\r', '\n');
                if (part.Length > 0) yield return part;
            }
        }

        private static void PasteText(string text)
        {
            string oldText = null;
            bool hadText = false;
            try
            {
                hadText = Clipboard.ContainsText();
                if (hadText) oldText = Clipboard.GetText();
            }
            catch
            {
            }

            string error;
            if (!ClipboardService.TrySetText(text, out error))
            {
                throw new InvalidOperationException(error);
            }
            Thread.Sleep(80);
            SendKeys.SendWait("^v");

            try
            {
                if (hadText && oldText != null)
                {
                    ClipboardService.TrySetText(oldText, out error);
                }
            }
            catch
            {
            }
        }
    }
}


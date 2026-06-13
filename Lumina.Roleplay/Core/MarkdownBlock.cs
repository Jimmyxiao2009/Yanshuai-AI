// MarkdownBlock.cs — 轻量 Markdown 渲染器 (v2 优化版)
// 策略: 预扫描快速路径 → 仅在检测到 MD 语法时才解析 → 精简 Inline 分配

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;

namespace yanshuai
{
    public static class MarkdownBlock
    {
        // 固定字体引用：避免渲染热路径（每行代码 / 内联代码 / 公式）里反复 new FontFamily
        private static readonly FontFamily CodeMonoFont = new FontFamily("Courier New");

        // ── Attached properties ────────────────────────────────────────────

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached("Text", typeof(string), typeof(MarkdownBlock),
                new PropertyMetadata(null, OnTextChanged));
        public static void SetText(DependencyObject obj, string v) => obj.SetValue(TextProperty, v);
        public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);

        public static readonly DependencyProperty QuoteColorProperty =
            DependencyProperty.RegisterAttached("QuoteColor", typeof(string), typeof(MarkdownBlock),
                new PropertyMetadata("", OnTextChanged));
        public static void SetQuoteColor(DependencyObject obj, string v) => obj.SetValue(QuoteColorProperty, v);
        public static string GetQuoteColor(DependencyObject obj) => (string)obj.GetValue(QuoteColorProperty);

        public static readonly DependencyProperty BracketColorProperty =
            DependencyProperty.RegisterAttached("BracketColor", typeof(string), typeof(MarkdownBlock),
                new PropertyMetadata("", OnTextChanged));
        public static void SetBracketColor(DependencyObject obj, string v) => obj.SetValue(BracketColorProperty, v);
        public static string GetBracketColor(DependencyObject obj) => (string)obj.GetValue(BracketColorProperty);

        // ── Cached brushes ────────────────────────────────────────────────

        private static readonly SolidColorBrush CodeFg     = new SolidColorBrush(Color.FromArgb(220, 200, 170, 90));
        private static readonly SolidColorBrush LinkFg     = new SolidColorBrush(Color.FromArgb(230, 100, 160, 220));
        private static readonly SolidColorBrush StrikeFg   = new SolidColorBrush(Color.FromArgb(160, 160, 160, 160));
        private static readonly SolidColorBrush HrFg       = new SolidColorBrush(Color.FromArgb(100, 180, 180, 180));
        private static readonly SolidColorBrush MathFg     = new SolidColorBrush(Color.FromArgb(220, 120, 180, 255));
        private static readonly SolidColorBrush DefaultQ   = new SolidColorBrush(Color.FromArgb(230, 220, 160, 60));
        private static readonly SolidColorBrush DefaultB   = new SolidColorBrush(Color.FromArgb(180, 150, 150, 150));

        // ── Detect triggers (quick pre-scan) ────────────────────────────────
        private static bool HasMarkdown(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '*' || c == '_' || c == '`' || c == '#' || c == '[' ||
                    c == '\u300c' || c == '\uff08' || c == '"' || c == '\u201c' || c == '\u2018' ||
                    c == '\n' || c == '~' || c == '>' || c == '|' || c == '$')
                    return true;
            }
            return false;
        }

        private static SolidColorBrush ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.TrimStart('#').Length != 6) return null;
            try
            {
                var h = hex.TrimStart('#');
                return new SolidColorBrush(Color.FromArgb(230,
                    Convert.ToByte(h.Substring(0,2),16),
                    Convert.ToByte(h.Substring(2,2),16),
                    Convert.ToByte(h.Substring(4,2),16)));
            }
            catch { return null; }
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is RichTextBlock rtb)) return;
            rtb.Blocks.Clear();
            var text = rtb.GetValue(TextProperty) as string;
            if (string.IsNullOrEmpty(text)) return;

            var qBrush = ParseHex(rtb.GetValue(QuoteColorProperty) as string) ?? DefaultQ;
            var bBrush = ParseHex(rtb.GetValue(BracketColorProperty) as string) ?? DefaultB;

            // 超长文本截断 (longest meaningful markdown message is ~2000 chars)
            if (text.Length > 3000)
                text = text.Substring(0, 3000) + "\n\n（内容过长，已截断）";

            // 快速路径: 无 MD 语法时直接用单 Run
            if (!HasMarkdown(text))
            {
                var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                p.Inlines.Add(new Run { Text = text });
                rtb.Blocks.Add(p);
                return;
            }

            ParseMarkdown(rtb, text, qBrush, bBrush);
        }

        // ── Lightweight block parser ────────────────────────────────────────

        private static void ParseMarkdown(RichTextBlock rtb, string text,
            SolidColorBrush qBrush, SolidColorBrush bBrush)
        {
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = text.Split('\n');
            bool inCode = false;
            string codeLang = "";
            var codeBuf = new List<string>();
            var paraBuf = new List<string>();

            void FlushPara()
            {
                if (paraBuf.Count == 0) return;
                var content = string.Join(" ", paraBuf);
                var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                ParseInlines(content, p.Inlines, qBrush, bBrush);
                rtb.Blocks.Add(p);
                paraBuf.Clear();
            }

            void FlushCode()
            {
                if (codeBuf.Count == 0) return;
                var p = new Paragraph { Margin = new Thickness(8, 4, 8, 4) };
                bool first = true;
                foreach (var cl in codeBuf)
                {
                    if (!first) p.Inlines.Add(new LineBreak());
                    first = false;
                    p.Inlines.Add(new Run { Text = cl, FontFamily = CodeMonoFont,
                        FontSize = 12, Foreground = CodeFg });
                }
                rtb.Blocks.Add(p);
                codeBuf.Clear(); codeLang = "";
            }

            foreach (var line in lines)
            {
                var t = line.TrimStart();

                // Fenced code
                if (t.StartsWith("```"))
                {
                    if (!inCode) { FlushPara(); inCode = true; codeLang = t.Substring(3).Trim(); }
                    else { FlushCode(); inCode = false; }
                    continue;
                }
                if (inCode) { codeBuf.Add(line); continue; }

                // Blank → flush para
                if (string.IsNullOrWhiteSpace(line)) { FlushPara(); continue; }

                // HR
                var clean = t.Replace(" ", "").Replace("\t", "");
                if (clean.Length >= 3 && (clean.Replace("-","")=="" || clean.Replace("*","")==""))
                    { FlushPara(); rtb.Blocks.Add(HrPara()); continue; }

                // Headings
                if (t.StartsWith("#"))
                {
                    int lv = 0; while (lv < t.Length && t[lv] == '#') lv++;
                    if (lv <= 6 && lv < t.Length && t[lv] == ' ')
                    {
                        FlushPara();
                        double[] fs = { 22, 19, 16, 15, 14, 13 };
                        int[] tp = { 12, 10, 8, 6, 4, 2 };
                        var hp = new Paragraph { Margin = new Thickness(0, tp[lv-1], 0, 3) };
                        string hc = t.Substring(lv + 1).TrimEnd('#').Trim();
                        var run = new Run { Text = hc, FontSize = fs[lv-1], FontWeight = FontWeights.SemiBold };
                        hp.Inlines.Add(run);
                        rtb.Blocks.Add(hp); continue;
                    }
                }

                // Blockquote
                if (t.StartsWith("> "))
                {
                    FlushPara();
                    var bp = new Paragraph { Margin = new Thickness(12, 2, 0, 2) };
                    var qr = new Run { Text = "\u258c ", Foreground = new SolidColorBrush(Color.FromArgb(200,160,160,160)) };
                    bp.Inlines.Add(qr);
                    ParseInlines(t.Substring(2), bp.Inlines, qBrush, bBrush);
                    rtb.Blocks.Add(bp); continue;
                }

                // Bullet list
                if ((t.StartsWith("- ") || t.StartsWith("* ")) && t.Length > 2)
                {
                    FlushPara();
                    int indent = line.Length - t.Length;
                    var bp = new Paragraph { Margin = new Thickness(14 + indent * 1.5, 1, 0, 1), TextIndent = -14 };
                    bp.Inlines.Add(new Run { Text = indent == 0 ? "\u2022 " : "\u25e6 " });
                    ParseInlines(t.Substring(2), bp.Inlines, qBrush, bBrush);
                    rtb.Blocks.Add(bp); continue;
                }

                // Numbered list
                var nm = Regex.Match(t, @"^(\d{1,9})[.)]\s(.*)");
                if (nm.Success)
                {
                    FlushPara();
                    var bp = new Paragraph { Margin = new Thickness(14, 1, 0, 1), TextIndent = -14 };
                    bp.Inlines.Add(new Run { Text = nm.Groups[1].Value + ". " });
                    ParseInlines(nm.Groups[2].Value, bp.Inlines, qBrush, bBrush);
                    rtb.Blocks.Add(bp); continue;
                }

                // Table
                if (t.StartsWith("|") && paraBuf.Count == 0)
                {
                    var cells = t.Trim().Trim('|').Split('|');
                    if (cells.Length >= 2)
                    {
                        var tp = new Paragraph { Margin = new Thickness(0, 1, 0, 0) };
                        for (int ci = 0; ci < cells.Length; ci++)
                        {
                            var cellRun = new Run { Text = cells[ci].Trim() };
                            tp.Inlines.Add(new Bold { Inlines = { cellRun } });
                            if (ci < cells.Length - 1)
                                tp.Inlines.Add(new Run { Text = "  \u2502  ", Foreground = HrFg });
                        }
                        rtb.Blocks.Add(tp); continue;
                    }
                }

                paraBuf.Add(line.TrimEnd());
            }
            FlushPara();
            FlushCode();
        }

        // ── Lightweight inline parser ──────────────────────────────────────

        private static void ParseInlines(string text, InlineCollection dest,
            SolidColorBrush qBrush, SolidColorBrush bBrush)
        {
            var buf = new StringBuilder();
            int i = 0;

            void Flush()
            {
                if (buf.Length > 0) { dest.Add(new Run { Text = buf.ToString() }); buf.Clear(); }
            }

            while (i < text.Length)
            {
                char c = text[i];

                if (c == '\n') { Flush(); dest.Add(new LineBreak()); i++; continue; }

                // Bold **text**
                if (c == '*' && i + 1 < text.Length && text[i+1] == '*')
                {
                    int end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end >= 0) { Flush(); var b = new Bold(); b.Inlines.Add(new Run { Text = text.Substring(i+2, end-i-2) }); dest.Add(b); i = end + 2; continue; }
                }

                // Bold __text__
                if (c == '_' && i + 1 < text.Length && text[i+1] == '_')
                {
                    int end = text.IndexOf("__", i + 2, StringComparison.Ordinal);
                    if (end >= 0) { Flush(); var b = new Bold(); b.Inlines.Add(new Run { Text = text.Substring(i+2, end-i-2) }); dest.Add(b); i = end + 2; continue; }
                }

                // Italic *text*
                if (c == '*' && i + 1 < text.Length && text[i+1] != '*' && text[i+1] != ' ')
                {
                    int end = text.IndexOf('*', i + 1);
                    if (end > i + 1 && (end+1 >= text.Length || text[end+1] != '*'))
                        { Flush(); var it = new Italic(); it.Inlines.Add(new Run { Text = text.Substring(i+1, end-i-1) }); dest.Add(it); i = end + 1; continue; }
                }

                // Strikethrough ~~text~~
                if (c == '~' && i + 1 < text.Length && text[i+1] == '~')
                {
                    int end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                    if (end >= 0) { Flush(); dest.Add(new Run { Text = text.Substring(i+2, end-i-2), Foreground = StrikeFg }); i = end + 2; continue; }
                }

                // Inline code `text`
                if (c == '`')
                {
                    int end = text.IndexOf("`", i + 1, StringComparison.Ordinal);
                    if (end > i + 1) { Flush(); dest.Add(new Run { Text = text.Substring(i+1, end-i-1), FontFamily = CodeMonoFont, FontSize = 12, Foreground = CodeFg }); i = end + 1; continue; }
                }

                // Link [text](url)
                if (c == '[')
                {
                    int te = text.IndexOf(']', i + 1);
                    if (te > 0 && te + 1 < text.Length && text[te+1] == '(')
                    {
                        int ue = text.IndexOf(')', te + 2);
                        if (ue > 0) { Flush(); var u = new Underline(); u.Inlines.Add(new Run { Text = text.Substring(i+1, te-i-1), Foreground = LinkFg }); dest.Add(u); i = ue + 1; continue; }
                    }
                }

                // Bracket （…）
                if (c == '\uff08') { int ce = text.IndexOf('\uff09', i + 1); if (ce > i) { Flush(); dest.Add(new Run { Text = text.Substring(i, ce-i+1), Foreground = bBrush }); i = ce + 1; continue; } }

                // Quote 「…」
                if (c == '\u300c') { int ce = text.IndexOf('\u300d', i + 1); if (ce > i) { Flush(); dest.Add(new Run { Text = text.Substring(i, ce-i+1), Foreground = qBrush }); i = ce + 1; continue; } }

                // Quote "…"
                if (c == '"') { int ce = text.IndexOf('"', i + 1); if (ce > i) { Flush(); dest.Add(new Run { Text = text.Substring(i, ce-i+1), Foreground = qBrush }); i = ce + 1; continue; } }

                // Curly quotes "…" and '…'
                if (c == '\u201c') { int ce = text.IndexOf('\u201d', i + 1); if (ce > i) { Flush(); dest.Add(new Run { Text = text.Substring(i, ce-i+1), Foreground = qBrush }); i = ce + 1; continue; } }
                if (c == '\u2018') { int ce = text.IndexOf('\u2019', i + 1); if (ce > i) { Flush(); dest.Add(new Run { Text = text.Substring(i, ce-i+1), Foreground = qBrush }); i = ce + 1; continue; } }

                // Math $…$
                if (c == '$' && i + 1 < text.Length && text[i+1] != '$') { int ce = text.IndexOf('$', i + 1); if (ce > i + 1) { Flush(); dest.Add(new Run { Text = "\u03a3(" + text.Substring(i+1, ce-i-1) + ")", FontFamily = CodeMonoFont, FontSize = 12, Foreground = MathFg }); i = ce + 1; continue; } }

                buf.Append(c); i++;
            }
            Flush();
        }

        private static Paragraph HrPara()
        {
            var p = new Paragraph { Margin = new Thickness(0, 8, 0, 8) };
            p.Inlines.Add(new Run { Text = new string('\u2015', 24), FontSize = 10, Foreground = HrFg });
            return p;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml.Media.Imaging;

namespace yanshuai
{
    /// <summary>
    /// Attached property that parses Markdown and populates a RichTextBlock.
    /// Supported (CommonMark + GFM subset):
    ///   Block  : ATX headings (#-######), setext headings, fenced code (```lang),
    ///            indented code blocks, horizontal rule (---/***/___)
    ///            tables (|…|), bullet/numbered/task lists (with nesting),
    ///            blockquotes (>), blank-line paragraph merging
    ///   Inline : **bold**, *italic*, ***bold-italic***, ~~strikethrough~~,
    ///            `code` (with background), [link](url), ![img](url/data:),
    ///            [^footnote], inline math ($…$), block math ($$…$$)
    /// </summary>
    public static class MarkdownBlock
    {
        // ── Attached property ─────────────────────────────────────────────────

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text", typeof(string), typeof(MarkdownBlock),
                new PropertyMetadata(null, OnTextChanged));

        public static void SetText(DependencyObject obj, string value)
            => obj.SetValue(TextProperty, value);
        public static string GetText(DependencyObject obj)
            => (string)obj.GetValue(TextProperty);

        // Per-RichTextBlock quote/bracket colors (hex #RRGGBB, empty = use default)
        public static readonly DependencyProperty QuoteColorProperty =
            DependencyProperty.RegisterAttached(
                "QuoteColor", typeof(string), typeof(MarkdownBlock),
                new PropertyMetadata("", OnTextChanged));

        public static void SetQuoteColor(DependencyObject obj, string value)
            => obj.SetValue(QuoteColorProperty, value);
        public static string GetQuoteColor(DependencyObject obj)
            => (string)obj.GetValue(QuoteColorProperty);

        public static readonly DependencyProperty BracketColorProperty =
            DependencyProperty.RegisterAttached(
                "BracketColor", typeof(string), typeof(MarkdownBlock),
                new PropertyMetadata("", OnTextChanged));

        public static void SetBracketColor(DependencyObject obj, string value)
            => obj.SetValue(BracketColorProperty, value);
        public static string GetBracketColor(DependencyObject obj)
            => (string)obj.GetValue(BracketColorProperty);

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is RichTextBlock rtb)) return;
            rtb.Blocks.Clear();
            var text = rtb.GetValue(TextProperty) as string;
            if (string.IsNullOrEmpty(text)) return;

            // Per-conversation colors
            string quoteCfg   = rtb.GetValue(QuoteColorProperty)   as string;
            string bracketCfg = rtb.GetValue(BracketColorProperty) as string;
            SolidColorBrush quoteBrush   = ParseHexBrush(quoteCfg)   ?? DefaultQuoteFg;
            SolidColorBrush bracketBrush = ParseHexBrush(bracketCfg) ?? DefaultBracketFg;

            // strip footnote definitions
            var footnotes = new Dictionary<string, string>(StringComparer.Ordinal);
            var bodyLines = new List<string>();
            foreach (var ln in text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
            {
                var t = ln.TrimStart();
                if (t.StartsWith("[^") && t.Contains("]:"))
                {
                    int cb = t.IndexOf("]:");
                    footnotes[t.Substring(2, cb - 2)] = t.Substring(cb + 2).Trim();
                }
                else bodyLines.Add(ln);
            }

            foreach (var block in BuildBlocks(bodyLines.ToArray(), footnotes, rtb, quoteBrush, bracketBrush))
                rtb.Blocks.Add(block);
        }

        // ── Colours ──────────────────────────────────────────────────────────

        private static readonly SolidColorBrush CodeBg        = new SolidColorBrush(Color.FromArgb(60,  128,128,128));
        private static readonly SolidColorBrush CodeFg        = new SolidColorBrush(Color.FromArgb(230, 220,180,100));
        private static readonly SolidColorBrush CodeLangFg    = new SolidColorBrush(Color.FromArgb(160, 160,160,160));
        private static readonly SolidColorBrush LinkFg        = new SolidColorBrush(Color.FromArgb(230, 100,160,220));
        private static readonly SolidColorBrush StrikeFg      = new SolidColorBrush(Color.FromArgb(160, 160,160,160));
        private static readonly SolidColorBrush HrFg          = new SolidColorBrush(Color.FromArgb(100, 180,180,180));
        private static readonly SolidColorBrush MathFg        = new SolidColorBrush(Color.FromArgb(220, 120,180,255));
        private static readonly SolidColorBrush QuoteFg       = new SolidColorBrush(Color.FromArgb(200, 160,160,160));
        private static readonly SolidColorBrush ImgFallbackFg = new SolidColorBrush(Color.FromArgb(180, 100,160,200));

        // Default colors for quote/bracket inline highlighting
        private static readonly SolidColorBrush DefaultQuoteFg   = new SolidColorBrush(Color.FromArgb(230, 220, 160,  60)); // orange-yellow
        private static readonly SolidColorBrush DefaultBracketFg = new SolidColorBrush(Color.FromArgb(180, 150, 150, 150)); // gray

        private static SolidColorBrush ParseHexBrush(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length != 6) return null;
            try
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new SolidColorBrush(Color.FromArgb(230, r, g, b));
            }
            catch { return null; }
        }

        // ── Block-level parser ────────────────────────────────────────────────

        private static IEnumerable<Block> BuildBlocks(string[] lines,
            Dictionary<string, string> footnotes, RichTextBlock rtb,
            SolidColorBrush quoteBrush, SolidColorBrush bracketBrush)
        {
            var result       = new List<Block>();
            bool inCode      = false;
            bool inMathBlock = false;
            bool inIndentCode= false;
            string codeLang  = "";
            var codeLines    = new List<string>();
            var mathBuf      = new List<string>();
            var tableLines   = new List<string>();
            var paraLines    = new List<string>(); // accumulated paragraph lines

            void FlushPara()
            {
                if (paraLines.Count == 0) return;
                // Check setext heading (underline on next processed line already consumed above)
                var content = string.Join(" ", paraLines.Select(l => l.TrimEnd()));
                var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                foreach (var il in ParseInlines(content, footnotes, rtb, quoteBrush, bracketBrush)) p.Inlines.Add(il);
                result.Add(p);
                paraLines.Clear();
            }

            void FlushTable()
            {
                if (tableLines.Count == 0) return;
                result.AddRange(BuildTable(tableLines, footnotes, quoteBrush, bracketBrush));
                tableLines.Clear();
            }

            void FlushCode()
            {
                if (codeLines.Count == 0) return;
                result.Add(BuildCodeBlock(codeLines, codeLang));
                codeLines.Clear(); codeLang = "";
            }

            for (int i = 0; i < lines.Length; i++)
            {
                var line    = lines[i];
                var trimmed = line.TrimStart();

                // ── Block math $$ ─────────────────────────────────────────
                if (trimmed == "$$")
                {
                    if (!inMathBlock)
                    {
                        FlushPara(); FlushTable();
                        inMathBlock = true; mathBuf.Clear();
                    }
                    else
                    {
                        result.Add(MathBlockPara(string.Join("\n", mathBuf)));
                        inMathBlock = false; mathBuf.Clear();
                    }
                    continue;
                }
                if (inMathBlock) { mathBuf.Add(line); continue; }

                // ── Fenced code ``` ───────────────────────────────────────
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    if (!inCode)
                    {
                        FlushPara(); FlushTable();
                        inCode   = true;
                        codeLang = trimmed.Substring(3).Trim();
                    }
                    else
                    {
                        FlushCode(); inCode = false;
                    }
                    continue;
                }
                if (inCode) { codeLines.Add(line); continue; }

                // ── Indented code block (4 spaces or 1 tab) ──────────────
                if ((line.StartsWith("    ") || line.StartsWith("\t")) && paraLines.Count == 0)
                {
                    FlushTable();
                    string stripped = line.StartsWith("\t") ? line.Substring(1) : line.Substring(4);
                    if (!inIndentCode) { inIndentCode = true; codeLang = ""; }
                    codeLines.Add(stripped);
                    continue;
                }
                if (inIndentCode) { FlushCode(); inIndentCode = false; }

                // ── Horizontal rule ───────────────────────────────────────
                var s = trimmed.Replace(" ", "").Replace("\t", "");
                if (s.Length >= 3 && !s.Contains("|") &&
                    (s.Replace("-","")=="" || s.Replace("*","")=="" || s.Replace("_","")==""))
                {
                    FlushPara(); FlushTable();
                    result.Add(HrPara()); continue;
                }

                // ── Setext heading (underline style) ──────────────────────
                if (paraLines.Count > 0 && i + 1 < lines.Length)
                {
                    var next = lines[i].Trim();
                    if ((next.Replace("=","")=="" && next.Length>=2) ||
                        (next.Replace("-","")=="" && next.Length>=2 && !next.Contains(" ")))
                    {
                        double fs   = next[0]=='=' ? 22 : 18;
                        var content = string.Join(" ", paraLines);
                        paraLines.Clear();
                        result.Add(HeadingPara(content, fs, new Thickness(0,10,0,4), footnotes, quoteBrush, bracketBrush));
                        continue;
                    }
                }

                // ── Table ─────────────────────────────────────────────────
                if (trimmed.StartsWith("|"))
                {
                    FlushPara();
                    tableLines.Add(line); continue;
                }
                else FlushTable();

                // ── ATX Headings #-###### ─────────────────────────────────
                if (trimmed.StartsWith("#"))
                {
                    int level = 0;
                    while (level < trimmed.Length && trimmed[level] == '#') level++;
                    if (level <= 6 && level < trimmed.Length && trimmed[level] == ' ')
                    {
                        FlushPara();
                        string content = trimmed.Substring(level + 1).TrimEnd('#').Trim();
                        double[] sizes = { 22, 19, 16, 15, 14, 13 };
                        int[] topPad   = {  12, 10,  8,  6,  4,  2 };
                        double fs  = sizes[level - 1];
                        int   tp   = topPad[level - 1];
                        result.Add(HeadingPara(content, fs, new Thickness(0,tp,0,3), footnotes, quoteBrush, bracketBrush));
                        continue;
                    }
                }

                // ── Task list ─────────────────────────────────────────────
                if (trimmed.Length > 5 &&
                    (trimmed.StartsWith("- [ ] ") || trimmed.StartsWith("- [x] ") ||
                     trimmed.StartsWith("* [ ] ") || trimmed.StartsWith("* [x] ") ||
                     trimmed.StartsWith("+ [ ] ") || trimmed.StartsWith("+ [x] ")))
                {
                    FlushPara();
                    bool done = trimmed[3] == 'x' || trimmed[3] == 'X';
                    int indent = line.Length - line.TrimStart().Length;
                    result.Add(TaskPara(trimmed.Substring(6), done, indent, footnotes, quoteBrush, bracketBrush));
                    continue;
                }

                // ── Bullet list (-, *, +) ──────────────────────────────────
                if ((trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
                    && trimmed.Length > 2)
                {
                    FlushPara();
                    int indent = line.Length - line.TrimStart().Length;
                    double leftPad = 14 + indent * 1.5;
                    var p = new Paragraph { Margin = new Thickness(leftPad, 1, 0, 1), TextIndent = -14 };
                    // nested bullet character
                    string bullet = indent == 0 ? "\u2022 " : indent <= 2 ? "\u25e6 " : "\u25aa ";
                    p.Inlines.Add(new Run { Text = bullet });
                    foreach (var il in ParseInlines(trimmed.Substring(2), footnotes, rtb, quoteBrush, bracketBrush)) p.Inlines.Add(il);
                    result.Add(p); continue;
                }

                // ── Numbered list ─────────────────────────────────────────
                var numMatch = Regex.Match(trimmed, @"^(\d{1,9})[.)]\s(.*)");
                if (numMatch.Success)
                {
                    FlushPara();
                    int indent = line.Length - line.TrimStart().Length;
                    double leftPad = 14 + indent * 1.5;
                    var p = new Paragraph { Margin = new Thickness(leftPad, 1, 0, 1), TextIndent = -14 };
                    p.Inlines.Add(new Run { Text = numMatch.Groups[1].Value + ". " });
                    foreach (var il in ParseInlines(numMatch.Groups[2].Value, footnotes, rtb, quoteBrush, bracketBrush)) p.Inlines.Add(il);
                    result.Add(p); continue;
                }

                // ── Blockquote ────────────────────────────────────────────
                if (trimmed.StartsWith("> ") || trimmed == ">")
                {
                    FlushPara();
                    string inner = trimmed.Length > 2 ? trimmed.Substring(2) : "";
                    var p = new Paragraph { Margin = new Thickness(12, 2, 0, 2) };
                    p.Inlines.Add(new Run { Text = "\u258c ", Foreground = QuoteFg });
                    foreach (var il in ParseInlines(inner, footnotes, rtb, quoteBrush, bracketBrush))
                    {
                        if (il is Run r) r.Foreground = QuoteFg;
                        p.Inlines.Add(il);
                    }
                    result.Add(p); continue;
                }

                // ── Blank line → flush accumulated paragraph ──────────────
                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushPara();
                    if (result.Count > 0) // add spacing between blocks
                        result.Add(new Paragraph { Margin = new Thickness(0, 3, 0, 3) });
                    continue;
                }

                // ── Accumulate paragraph lines ────────────────────────────
                paraLines.Add(line.TrimEnd());
            }

            FlushPara();
            FlushTable();
            FlushCode();
            if (inMathBlock && mathBuf.Count > 0) result.Add(MathBlockPara(string.Join("\n", mathBuf)));

            // Footnote definitions
            if (footnotes.Count > 0)
            {
                result.Add(HrPara());
                foreach (var kv in footnotes)
                {
                    var p = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                    p.Inlines.Add(new Run { Text = $"[{kv.Key}] ", FontSize = 12, Foreground = HrFg });
                    foreach (var il in ParseInlines(kv.Value, new Dictionary<string,string>(), rtb, quoteBrush, bracketBrush)) p.Inlines.Add(il);
                    result.Add(p);
                }
            }
            return result;
        }

        // ── Code block builder ────────────────────────────────────────────────

        private static Paragraph BuildCodeBlock(List<string> lines, string lang)
        {
            var p = new Paragraph
            {
                Margin      = new Thickness(0, 6, 0, 6),
                LineHeight  = 20,
                // simulate a background via left-border accent
            };

            // Language label
            if (!string.IsNullOrEmpty(lang))
            {
                p.Inlines.Add(new Run
                {
                    Text       = lang + "\n",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize   = 11,
                    Foreground = CodeLangFg,
                    FontStyle  = Windows.UI.Text.FontStyle.Italic,
                });
            }

            // Code lines with syntax-aware colour (simple keyword highlight)
            bool first = true;
            foreach (var line in lines)
            {
                if (!first) p.Inlines.Add(new LineBreak());
                first = false;
                // Simple token colouring: strings, comments, keywords
                AppendCodeLine(p, line, lang);
            }
            return p;
        }

        private static void AppendCodeLine(Paragraph p, string line, string lang)
        {
            // Very lightweight syntax highlighting for common cases
            // Full lexer would be too heavy for W10M; do regex-based pass
            var baseFg = CodeFg;

            // Comment detection
            bool isComment = false;
            if (!string.IsNullOrEmpty(lang))
            {
                string tl = line.TrimStart();
                if ((lang=="cs"||lang=="csharp"||lang=="cpp"||lang=="c"||lang=="java"||lang=="js"||lang=="ts")
                    && tl.StartsWith("//"))
                    isComment = true;
                if ((lang=="python"||lang=="py") && tl.StartsWith("#"))
                    isComment = true;
                if ((lang=="xml"||lang=="html") && tl.StartsWith("<!--"))
                    isComment = true;
            }

            if (isComment)
            {
                p.Inlines.Add(new Run
                {
                    Text       = line,
                    FontFamily = new FontFamily("Courier New"),
                    FontSize   = 13,
                    Foreground = new SolidColorBrush(Color.FromArgb(180, 100, 160, 100)),
                    FontStyle  = Windows.UI.Text.FontStyle.Italic,
                });
                return;
            }

            p.Inlines.Add(new Run
            {
                Text       = line,
                FontFamily = new FontFamily("Courier New"),
                FontSize   = 13,
                Foreground = baseFg,
            });
        }

        // ── Block helpers ─────────────────────────────────────────────────────

        private static Paragraph HeadingPara(string content, double fontSize,
            Thickness margin, Dictionary<string, string> footnotes,
            SolidColorBrush quoteBrush, SolidColorBrush bracketBrush)
        {
            var p = new Paragraph { Margin = margin };
            foreach (var il in ParseInlines(content, footnotes, null, quoteBrush, bracketBrush))
            {
                if (il is Run r) { r.FontWeight = FontWeights.SemiBold; r.FontSize = fontSize; }
                else if (il is Bold b)
                    foreach (var sub in b.Inlines.OfType<Run>()) { sub.FontSize = fontSize; }
                p.Inlines.Add(il);
            }
            return p;
        }

        private static Paragraph TaskPara(string content, bool done, int indent,
            Dictionary<string,string> footnotes,
            SolidColorBrush quoteBrush, SolidColorBrush bracketBrush)
        {
            double leftPad = 14 + indent * 1.5;
            var p = new Paragraph { Margin = new Thickness(leftPad, 1, 0, 1), TextIndent = -14 };
            p.Inlines.Add(new Run { Text = done ? "\u2611 " : "\u2610 " });
            foreach (var il in ParseInlines(content, footnotes, null, quoteBrush, bracketBrush))
            {
                if (done && il is Run r)
                    r.Foreground = StrikeFg;
                p.Inlines.Add(il);
            }
            return p;
        }

        private static Paragraph HrPara()
        {
            var p = new Paragraph { Margin = new Thickness(0, 8, 0, 8) };
            p.Inlines.Add(new Run
            {
                Text       = new string('\u2015', 24),
                FontSize   = 10,
                Foreground = HrFg,
            });
            return p;
        }

        private static Paragraph MathBlockPara(string content)
        {
            var p = new Paragraph { Margin = new Thickness(0, 6, 0, 6), LineHeight = 19 };
            p.Inlines.Add(new Run { Text = "\u03a3 ", FontSize = 14, Foreground = MathFg });
            p.Inlines.Add(new Run
            {
                Text       = content,
                FontFamily = new FontFamily("Courier New"),
                FontSize   = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(230, 200, 220, 255)),
            });
            return p;
        }

        // ── Table builder ─────────────────────────────────────────────────────

        private static IEnumerable<Block> BuildTable(List<string> rows,
            Dictionary<string, string> footnotes,
            SolidColorBrush quoteBrush, SolidColorBrush bracketBrush)
        {
            List<string> ParseRow(string row) =>
                row.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            bool IsAlignRow(string row) =>
                row.Trim().Trim('|').Split('|').All(c =>
                {
                    var t = c.Trim().TrimStart(':').TrimEnd(':');
                    return t.Replace("-","").Length == 0 && t.Length >= 1;
                });

            var result = new List<Block>();
            var data = rows.Where(r => !IsAlignRow(r)).ToList();
            if (data.Count == 0) return result;

            bool first = true;
            foreach (var row in data)
            {
                var cells = ParseRow(row);
                var p = new Paragraph { Margin = new Thickness(0, 1, 0, 0) };
                for (int ci = 0; ci < cells.Count; ci++)
                {
                    if (first)
                    {
                        var b = new Bold();
                        b.Inlines.Add(new Run { Text = cells[ci] });
                        p.Inlines.Add(b);
                    }
                    else
                    {
                        foreach (var il in ParseInlines(cells[ci], footnotes, null, quoteBrush, bracketBrush))
                            p.Inlines.Add(il);
                    }
                    if (ci < cells.Count - 1)
                        p.Inlines.Add(new Run { Text = "  \u2502  ", Foreground = HrFg });
                }
                result.Add(p);
                if (first) { result.Insert(1, HrPara()); first = false; }
            }
            return result;
        }

        // ── Inline parser ─────────────────────────────────────────────────────

        private static IEnumerable<Inline> ParseInlines(string text,
            Dictionary<string, string> footnotes, RichTextBlock rtb,
            SolidColorBrush quoteBrush, SolidColorBrush bracketBrush)
        {
            var result = new List<Inline>();
            if (string.IsNullOrEmpty(text)) return result;

            var buf = new StringBuilder();
            int i   = 0;

            void Flush()
            {
                if (buf.Length == 0) return;
                result.Add(new Run { Text = buf.ToString() });
                buf.Clear();
            }

            while (i < text.Length)
            {
                char c = text[i];

                // ── Hard line break (two trailing spaces + \n) ────────────
                if (c == '\n')
                {
                    Flush();
                    result.Add(new LineBreak());
                    i++; continue;
                }

                // ── Escape ────────────────────────────────────────────────
                if (c == '\\' && i + 1 < text.Length)
                {
                    char next = text[i + 1];
                    if ("\\`*_{}[]()#+-.!~|".IndexOf(next) >= 0)
                    { buf.Append(next); i += 2; continue; }
                }

                // ── Inline math $…$ ───────────────────────────────────────
                if (c == '$' && i + 1 < text.Length && text[i + 1] != '$')
                {
                    int end = text.IndexOf('$', i + 1);
                    if (end > i + 1)
                    {
                        Flush();
                        result.Add(new Run
                        {
                            Text       = "\u03a3(" + text.Substring(i + 1, end - i - 1) + ")",
                            FontFamily = new FontFamily("Courier New"),
                            FontSize   = 13,
                            Foreground = MathFg,
                        });
                        i = end + 1; continue;
                    }
                }

                // ── Image ![alt](url or data:…) ───────────────────────────
                if (c == '!' && i + 1 < text.Length && text[i + 1] == '[')
                {
                    int altEnd = text.IndexOf(']', i + 2);
                    if (altEnd > 0 && altEnd + 1 < text.Length && text[altEnd + 1] == '(')
                    {
                        int urlEnd = text.IndexOf(')', altEnd + 2);
                        if (urlEnd > altEnd + 2)
                        {
                            string alt = text.Substring(i + 2, altEnd - i - 2);
                            string url = text.Substring(altEnd + 2, urlEnd - altEnd - 2).Trim();
                            // strip title "url \"title\""
                            var urlTitleMatch = Regex.Match(url, @"^(.+?)\s+[""'](.+)[""']$");
                            if (urlTitleMatch.Success) url = urlTitleMatch.Groups[1].Value.Trim();

                            Flush();
                            BitmapImage bmp = null;
                            if (url.StartsWith("data:") && url.Contains(";base64,"))
                            {
                                // base64 data URI
                                try
                                {
                                    int b64start = url.IndexOf(";base64,") + 8;
                                    byte[] bytes = Convert.FromBase64String(url.Substring(b64start));
                                    bmp = AppSettings.LoadBitmapSync(bytes);
                                }
                                catch { bmp = null; }
                            }
                            else if (Uri.TryCreate(url, UriKind.Absolute, out Uri imgUri)
                                     && (imgUri.Scheme == "http" || imgUri.Scheme == "https"))
                            {
                                bmp = new BitmapImage(imgUri);
                            }

                            if (bmp != null && rtb != null)
                            {
                                var img = new Image
                                {
                                    Source    = bmp,
                                    MaxWidth  = rtb.ActualWidth > 0 ? rtb.ActualWidth - 8 : 280,
                                    MaxHeight = 400,
                                    Stretch   = Windows.UI.Xaml.Media.Stretch.Uniform,
                                    HorizontalAlignment = HorizontalAlignment.Left,
                                    Margin    = new Thickness(0, 4, 0, 4),
                                };
                                result.Add(new InlineUIContainer { Child = img });
                            }
                            else
                            {
                                // Fallback: styled text with alt
                                string label = string.IsNullOrEmpty(alt) ? url : alt;
                                result.Add(new Run
                                {
                                    Text       = $"\uD83D\uDDBC {label}",
                                    Foreground = ImgFallbackFg,
                                    FontSize   = 13,
                                });
                            }
                            i = urlEnd + 1; continue;
                        }
                    }
                }

                // ── Link [text](url) ──────────────────────────────────────
                if (c == '[' && i + 1 < text.Length && text[i + 1] != '^')
                {
                    int txtEnd = text.IndexOf(']', i + 1);
                    if (txtEnd > 0 && txtEnd + 1 < text.Length && text[txtEnd + 1] == '(')
                    {
                        int urlEnd = text.IndexOf(')', txtEnd + 2);
                        if (urlEnd > 0)
                        {
                            Flush();
                            string linkText = text.Substring(i + 1, txtEnd - i - 1);
                            string linkUrl  = text.Substring(txtEnd + 2, urlEnd - txtEnd - 2).Trim();
                            // Show text underlined in link colour; append short URL hint
                            var underline = new Underline();
                            underline.Inlines.Add(new Run
                            {
                                Text       = linkText,
                                Foreground = LinkFg,
                            });
                            result.Add(underline);
                            i = urlEnd + 1; continue;
                        }
                    }
                    // Reference-style link [text][ref] — just show text
                    if (txtEnd > 0 && txtEnd + 1 < text.Length && text[txtEnd + 1] == '[')
                    {
                        int refEnd = text.IndexOf(']', txtEnd + 2);
                        if (refEnd > 0)
                        {
                            Flush();
                            result.Add(new Run
                            {
                                Text       = text.Substring(i + 1, txtEnd - i - 1),
                                Foreground = LinkFg,
                            });
                            i = refEnd + 1; continue;
                        }
                    }
                }

                // ── Bold+Italic ***text*** or ___text___ ──────────────────
                if ((c == '*' && i + 2 < text.Length && text[i+1] == '*' && text[i+2] == '*') ||
                    (c == '_' && i + 2 < text.Length && text[i+1] == '_' && text[i+2] == '_'))
                {
                    string marker = new string(c, 3);
                    int end = text.IndexOf(marker, i + 3, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        Flush();
                        var span = new Bold();
                        var inner = new Italic();
                        inner.Inlines.Add(new Run { Text = text.Substring(i + 3, end - i - 3) });
                        span.Inlines.Add(inner);
                        result.Add(span); i = end + 3; continue;
                    }
                }

                // ── Bold **text** or __text__ ─────────────────────────────
                if ((c == '*' && i + 1 < text.Length && text[i+1] == '*') ||
                    (c == '_' && i + 1 < text.Length && text[i+1] == '_'))
                {
                    string marker = new string(c, 2);
                    int end = text.IndexOf(marker, i + 2, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        Flush();
                        var span = new Bold();
                        span.Inlines.Add(new Run { Text = text.Substring(i + 2, end - i - 2) });
                        result.Add(span); i = end + 2; continue;
                    }
                }

                // ── Italic *text* or _text_ ───────────────────────────────
                if (c == '*' || c == '_')
                {
                    // only if not surrounded by spaces (to avoid accidental _)
                    bool notSpace = i + 1 < text.Length && text[i + 1] != ' ';
                    if (notSpace)
                    {
                        int end = FindClosingEmphasis(text, c, i + 1);
                        if (end > i + 1)
                        {
                            Flush();
                            var span = new Italic();
                            span.Inlines.Add(new Run
                            {
                                Text       = text.Substring(i + 1, end - i - 1),
                                Foreground = bracketBrush,
                            });
                            result.Add(span); i = end + 1; continue;
                        }
                    }
                }

                // ── Strikethrough ~~text~~ ────────────────────────────────
                if (c == '~' && i + 1 < text.Length && text[i+1] == '~')
                {
                    int end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        Flush();
                        var span = new Span();
                        span.Inlines.Add(new Run
                        {
                            Text       = text.Substring(i + 2, end - i - 2),
                            Foreground = StrikeFg,
                            // UWP RichTextBlock has no TextDecorations on Run in older SDKs
                            // approximate with dim colour
                        });
                        result.Add(span); i = end + 2; continue;
                    }
                }

                // ── Inline code `code` or ``code`` ───────────────────────
                if (c == '`')
                {
                    // double-backtick span
                    bool dbl = i + 1 < text.Length && text[i+1] == '`';
                    string tick = dbl ? "``" : "`";
                    int start = i + tick.Length;
                    int end = text.IndexOf(tick, start, StringComparison.Ordinal);
                    if (end > start - 1)
                    {
                        Flush();
                        string codeText = text.Substring(start, end - start).Trim();
                        // Wrap in a Border via InlineUIContainer for background
                        var tb = new TextBlock
                        {
                            Text           = codeText,
                            FontFamily     = new FontFamily("Courier New"),
                            FontSize       = 13,
                            Foreground     = CodeFg,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        var border = new Border
                        {
                            Background    = CodeBg,
                            Padding       = new Thickness(4, 1, 4, 1),
                            Child         = tb,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        result.Add(new InlineUIContainer { Child = border });
                        i = end + tick.Length; continue;
                    }
                }

                // ── Footnote ref [^key] ───────────────────────────────────
                if (c == '[' && i + 1 < text.Length && text[i+1] == '^')
                {
                    int close = text.IndexOf(']', i + 2);
                    if (close > 0)
                    {
                        Flush();
                        string key = text.Substring(i + 2, close - i - 2);
                        result.Add(new Run
                        {
                            Text       = $"[{key}]",
                            FontSize   = 11,
                            Foreground = LinkFg,
                        });
                        i = close + 1; continue;
                    }
                }

                // ── Bracket coloring （…） ────────────────────────────────
                if (c == '\uff08') // full-width left paren （
                {
                    int close = text.IndexOf('\uff09', i + 1); // ）
                    if (close > i)
                    {
                        Flush();
                        result.Add(new Run
                        {
                            Text       = text.Substring(i, close - i + 1),
                            Foreground = bracketBrush,
                        });
                        i = close + 1; continue;
                    }
                }

                // ── Quote coloring 「…」 ──────────────────────────────────
                if (c == '\u300c') // 「
                {
                    int close = text.IndexOf('\u300d', i + 1); // 」
                    if (close > i)
                    {
                        Flush();
                        result.Add(new Run
                        {
                            Text       = text.Substring(i, close - i + 1),
                            Foreground = quoteBrush,
                        });
                        i = close + 1; continue;
                    }
                }

                // ── Quote coloring "…" (ASCII double quote) ───────────────
                if (c == '"' && i + 1 < text.Length)
                {
                    int close = text.IndexOf('"', i + 1);
                    if (close > i)
                    {
                        Flush();
                        result.Add(new Run
                        {
                            Text       = text.Substring(i, close - i + 1),
                            Foreground = quoteBrush,
                        });
                        i = close + 1; continue;
                    }
                }

                // ── Quote coloring \u201c…\u201d (curly double quotes) ─────────────
                if (c == '\u201c') // "
                {
                    int close = text.IndexOf('\u201d', i + 1); // "
                    if (close > i)
                    {
                        Flush();
                        result.Add(new Run
                        {
                            Text       = text.Substring(i, close - i + 1),
                            Foreground = quoteBrush,
                        });
                        i = close + 1; continue;
                    }
                }

                // ── Quote coloring \u2018…\u2019 (curly single quotes) ─────────────
                if (c == '\u2018') // '
                {
                    int close = text.IndexOf('\u2019', i + 1); // '
                    if (close > i)
                    {
                        Flush();
                        result.Add(new Run
                        {
                            Text       = text.Substring(i, close - i + 1),
                            Foreground = quoteBrush,
                        });
                        i = close + 1; continue;
                    }
                }

                buf.Append(c);
                i++;
            }
            Flush();
            return result;
        }

        private static int FindClosingEmphasis(string text, char ch, int from)
        {
            for (int j = from; j < text.Length; j++)
            {
                if (text[j] == ch)
                {
                    // not preceded by space, not followed by same char
                    bool ok = (j == 0 || text[j-1] != ' ') &&
                              (j + 1 >= text.Length || text[j+1] != ch);
                    if (ok) return j;
                }
            }
            return -1;
        }
    }
}

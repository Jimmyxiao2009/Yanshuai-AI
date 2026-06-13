using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.System;

namespace yanshuai
{
    /// <summary>
    /// Attached property that parses Markdown and populates a RichTextBlock.
    /// Rewritten for proper table alignment, clickable links, styled code blocks,
    /// and improved math formula rendering.
    /// </summary>
    public static class MarkdownBlock
    {
        // ── Attached properties ───────────────────────────────────────────────

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text", typeof(string), typeof(MarkdownBlock),
                new PropertyMetadata(null, OnTextChanged));

        public static void SetText(DependencyObject obj, string value)
            => obj.SetValue(TextProperty, value);
        public static string GetText(DependencyObject obj)
            => (string)obj.GetValue(TextProperty);

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

            string quoteCfg   = rtb.GetValue(QuoteColorProperty)   as string;
            string bracketCfg = rtb.GetValue(BracketColorProperty) as string;
            SolidColorBrush quoteBrush   = ParseHexBrush(quoteCfg)   ?? DefaultQuoteFg;
            SolidColorBrush bracketBrush = ParseHexBrush(bracketCfg) ?? DefaultBracketFg;

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

            var ctx = new RenderContext { Rtb = rtb, Footnotes = footnotes, QuoteBrush = quoteBrush, BracketBrush = bracketBrush };
            foreach (var block in BuildBlocks(bodyLines.ToArray(), ctx))
                rtb.Blocks.Add(block);
        }

        // ── Colours ──────────────────────────────────────────────────────────

        private static readonly SolidColorBrush CodeBg        = new SolidColorBrush(Color.FromArgb(40,  180,180,180));
        private static readonly SolidColorBrush CodeFg        = new SolidColorBrush(Color.FromArgb(230, 220,180,100));
        private static readonly SolidColorBrush CodeBlockBg   = new SolidColorBrush(Color.FromArgb(50,  100,100,100));
        private static readonly SolidColorBrush CodeLangFg    = new SolidColorBrush(Color.FromArgb(160, 160,160,160));
        private static readonly SolidColorBrush LinkFg        = new SolidColorBrush(Color.FromArgb(230, 80,160,230));
        private static readonly SolidColorBrush StrikeFg      = new SolidColorBrush(Color.FromArgb(160, 160,160,160));
        private static readonly SolidColorBrush HrFg          = new SolidColorBrush(Color.FromArgb(100, 180,180,180));
        private static readonly SolidColorBrush MathFg        = new SolidColorBrush(Color.FromArgb(220, 160,120,255));
        private static readonly SolidColorBrush QuoteFg       = new SolidColorBrush(Color.FromArgb(200, 160,160,160));
        private static readonly SolidColorBrush TableHeaderBg = new SolidColorBrush(Color.FromArgb(255,  45, 55,  75));
        private static readonly SolidColorBrush TableBorderFg = new SolidColorBrush(Color.FromArgb(120, 160,160,160));
        private static readonly SolidColorBrush TableRowBg    = new SolidColorBrush(Color.FromArgb(255,  28, 36,  52));
        private static readonly SolidColorBrush TableRowAltBg = new SolidColorBrush(Color.FromArgb(255,  32, 42,  60));
        private static readonly SolidColorBrush KeywordFg     = new SolidColorBrush(Color.FromArgb(230, 180,120,200));
        private static readonly SolidColorBrush StringFg      = new SolidColorBrush(Color.FromArgb(230, 120,190,120));
        private static readonly SolidColorBrush NumberFg      = new SolidColorBrush(Color.FromArgb(230, 130,170,230));
        private static readonly SolidColorBrush CommentFg     = new SolidColorBrush(Color.FromArgb(150, 100,160,100));

        // 通用 UI 字体回退链：保证中文/英文/emoji 都能找到合适字形，避免回退时字形宽度不一致导致重叠
        private static readonly FontFamily UiFontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI, Segoe UI Emoji, Segoe UI Symbol");
        private static readonly FontFamily MonoFontFamily = new FontFamily("Consolas, Courier New, Cascadia Mono");
        // 固定字体引用：避免在渲染热路径（每行代码 / 每个 emoji 段）里反复 new FontFamily
        private static readonly FontFamily CodeMonoFont = new FontFamily("Courier New");
        private static readonly FontFamily SegoeUiFont  = new FontFamily("Segoe UI");
        private static readonly FontFamily EmojiFont    = new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Microsoft YaHei UI, Segoe UI");

        private static readonly SolidColorBrush DefaultQuoteFg   = new SolidColorBrush(Color.FromArgb(230, 220, 160,  60));
        private static readonly SolidColorBrush DefaultBracketFg = new SolidColorBrush(Color.FromArgb(180, 150, 150, 150));

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

        private class RenderContext
        {
            public RichTextBlock Rtb;
            public Dictionary<string, string> Footnotes;
            public SolidColorBrush QuoteBrush;
            public SolidColorBrush BracketBrush;
        }

        // ── Block-level parser ────────────────────────────────────────────────

        private static IEnumerable<Block> BuildBlocks(string[] lines, RenderContext ctx)
        {
            var result       = new List<Block>();
            bool inCode      = false;
            bool inMathBlock = false;
            string codeLang  = "";
            var codeLines    = new List<string>();
            var mathBuf      = new List<string>();
            var tableLines   = new List<string>();
            var paraLines    = new List<string>();

            void FlushPara()
            {
                if (paraLines.Count == 0) return;
                var content = string.Join(" ", paraLines.Select(l => l.TrimEnd()));
                var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                foreach (var il in ParseInlines(content, ctx)) p.Inlines.Add(il);
                result.Add(p);
                paraLines.Clear();
            }

            void FlushTable()
            {
                if (tableLines.Count == 0) return;
                result.AddRange(BuildTable(tableLines, ctx));
                tableLines.Clear();
            }

            void FlushCode()
            {
                if (codeLines.Count == 0) return;
                result.Add(BuildCodeBlock(codeLines, codeLang, ctx));
                codeLines.Clear(); codeLang = "";
            }

            for (int i = 0; i < lines.Length; i++)
            {
                var line    = lines[i];
                var trimmed = line.TrimStart();

                // ── Block math $$ ─────────────────────────────────────────
                if (trimmed == "$$")
                {
                    if (!inMathBlock) { FlushPara(); FlushTable(); inMathBlock = true; mathBuf.Clear(); }
                    else { result.Add(MathBlockPara(string.Join("\n", mathBuf))); inMathBlock = false; }
                    continue;
                }
                if (inMathBlock) { mathBuf.Add(line); continue; }

                // ── Fenced code ``` ───────────────────────────────────────
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    if (!inCode) { FlushPara(); FlushTable(); inCode = true; codeLang = trimmed.Substring(3).Trim(); }
                    else { FlushCode(); inCode = false; }
                    continue;
                }
                if (inCode) { codeLines.Add(line); continue; }

                // ── Horizontal rule ───────────────────────────────────────
                var s = trimmed.Replace(" ", "").Replace("\t", "");
                if (s.Length >= 3 && !s.Contains("|") &&
                    (s.Replace("-","")=="" || s.Replace("*","")=="" || s.Replace("_","")==""))
                { FlushPara(); FlushTable(); result.Add(HrPara()); continue; }

                // ── Table ─────────────────────────────────────────────────
                if (trimmed.StartsWith("|"))
                { FlushPara(); tableLines.Add(line); continue; }
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
                        result.Add(HeadingPara(content, sizes[level - 1], new Thickness(0, topPad[level-1], 0, 3), ctx));
                        continue;
                    }
                }

                // ── Task list ─────────────────────────────────────────────
                if (trimmed.Length > 5 &&
                    (trimmed.StartsWith("- [ ] ") || trimmed.StartsWith("- [x] ") ||
                     trimmed.StartsWith("* [ ] ") || trimmed.StartsWith("* [x] ")))
                {
                    FlushPara();
                    bool done = trimmed[3] == 'x' || trimmed[3] == 'X';
                    int indent = line.Length - line.TrimStart().Length;
                    result.Add(TaskPara(trimmed.Substring(6), done, indent, ctx));
                    continue;
                }

                // ── Bullet list (-, *, +) ──────────────────────────────────
                if ((trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ ")) && trimmed.Length > 2)
                {
                    FlushPara();
                    int indent = line.Length - line.TrimStart().Length;
                    double leftPad = 14 + indent * 1.5;
                    var p = new Paragraph { Margin = new Thickness(leftPad, 1, 0, 1), TextIndent = -14 };
                    string bullet = indent == 0 ? "• " : indent <= 2 ? "◦ " : "▪ ";
                    p.Inlines.Add(new Run { Text = bullet });
                    foreach (var il in ParseInlines(trimmed.Substring(2), ctx)) p.Inlines.Add(il);
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
                    foreach (var il in ParseInlines(numMatch.Groups[2].Value, ctx)) p.Inlines.Add(il);
                    result.Add(p); continue;
                }

                // ── Blockquote ────────────────────────────────────────────
                if (trimmed.StartsWith("> ") || trimmed == ">")
                {
                    FlushPara();
                    string inner = trimmed.Length > 2 ? trimmed.Substring(2) : "";
                    var p = new Paragraph { Margin = new Thickness(12, 2, 0, 2) };
                    p.Inlines.Add(new Run { Text = "▌ ", Foreground = QuoteFg });
                    foreach (var il in ParseInlines(inner, ctx))
                    {
                        if (il is Run r) r.Foreground = QuoteFg;
                        p.Inlines.Add(il);
                    }
                    result.Add(p); continue;
                }

                // ── Blank line ────────────────────────────────────────────
                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushPara();
                    if (result.Count > 0) result.Add(new Paragraph { Margin = new Thickness(0, 3, 0, 3) });
                    continue;
                }

                // ── Accumulate paragraph ──────────────────────────────────
                paraLines.Add(line.TrimEnd());
            }

            FlushPara(); FlushTable(); FlushCode();
            if (inMathBlock && mathBuf.Count > 0) result.Add(MathBlockPara(string.Join("\n", mathBuf)));

            // Footnote definitions
            if (ctx.Footnotes.Count > 0)
            {
                result.Add(HrPara());
                foreach (var kv in ctx.Footnotes)
                {
                    var p = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                    p.Inlines.Add(new Run { Text = $"[{kv.Key}] ", FontSize = 12, Foreground = HrFg });
                    foreach (var il in ParseInlines(kv.Value, ctx)) p.Inlines.Add(il);
                    result.Add(p);
                }
            }
            return result;
        }

        // ── Code block builder (with header bar, copy/save buttons, syntax highlighting) ─────

        private static Paragraph BuildCodeBlock(List<string> lines, string lang, RenderContext ctx)
        {
            // Build the full code text (preserved for copy/save)
            string fullCodeText = string.Join("\n", lines);

            // Outer container: a Border with header + scrollable code area
            var outer = new Border
            {
                Background = CodeBlockBg,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 6, 0, 6),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 120, 120, 120)),
                BorderThickness = new Thickness(1),
            };
            var stack = new StackPanel();
            outer.Child = stack;

            // ── Header bar: language label + copy + save buttons
            var header = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(70, 60, 60, 60)),
                Padding = new Thickness(10, 4, 4, 4),
            };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var langLabel = new TextBlock
            {
                Text = string.IsNullOrEmpty(lang) ? "code" : lang,
                FontFamily = SegoeUiFont,
                FontSize = 11,
                Foreground = CodeLangFg,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(langLabel, 0);
            header.Children.Add(langLabel);

            var copyBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE8C8", FontSize = 12 },
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 2, 6, 2),
                MinWidth = 0,
                MinHeight = 0,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(copyBtn, "复制代码");
            copyBtn.Click += (s, e) =>
            {
                try
                {
                    var pkg = new DataPackage();
                    pkg.SetText(fullCodeText);
                    Clipboard.SetContent(pkg);
                    if (s is Button b && b.Content is FontIcon fi)
                    {
                        string prev = fi.Glyph;
                        fi.Glyph = "\uE73E";
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                        timer.Tick += (s2, e2) => { fi.Glyph = prev; timer.Stop(); };
                        timer.Start();
                    }
                }
                catch { }
            };
            Grid.SetColumn(copyBtn, 1);
            header.Children.Add(copyBtn);

            var saveBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74E", FontSize = 12 },
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 2, 6, 2),
                MinWidth = 0,
                MinHeight = 0,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
            };
            ToolTipService.SetToolTip(saveBtn, "保存为文件");
            string capturedLang = lang;
            saveBtn.Click += async (s, e) =>
            {
                try
                {
                    var picker = new FileSavePicker
                    {
                        SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                        SuggestedFileName = "code_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                    };
                    string ext = ExtensionForLanguage(capturedLang);
                    picker.FileTypeChoices.Add(string.IsNullOrEmpty(capturedLang) ? "Text" : capturedLang, new List<string> { ext });
                    var file = await picker.PickSaveFileAsync();
                    if (file != null) await FileIO.WriteTextAsync(file, fullCodeText);
                }
                catch { }
            };
            Grid.SetColumn(saveBtn, 2);
            header.Children.Add(saveBtn);

            stack.Children.Add(header);

            // ── Code area: horizontal scroll for long lines
            var codeRtb = new RichTextBlock
            {
                FontFamily = CodeMonoFont,
                FontSize = 13,
                Padding = new Thickness(10, 8, 10, 10),
                IsTextSelectionEnabled = true,
            };
            var codePara = new Paragraph { LineHeight = 18 };
            var keywords = GetKeywords(lang);
            bool first = true;
            foreach (var line in lines)
            {
                if (!first) codePara.Inlines.Add(new LineBreak());
                first = false;
                AppendHighlightedLine(codePara, line, lang, keywords);
            }
            codeRtb.Blocks.Add(codePara);

            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Auto,
                VerticalScrollMode = ScrollMode.Disabled,
                ZoomMode = ZoomMode.Disabled,
                Content = codeRtb,
            };
            stack.Children.Add(scroll);

            // Wrap into a Paragraph via InlineUIContainer so it lives inside the RichTextBlock
            var p = new Paragraph { Margin = new Thickness(0, 6, 0, 6) };
            p.Inlines.Add(new InlineUIContainer { Child = outer });
            return p;
        }

        private static string ExtensionForLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return ".txt";
            string l = lang.ToLower();
            switch (l)
            {
                case "cs": case "csharp": return ".cs";
                case "js": case "javascript": return ".js";
                case "ts": case "typescript": return ".ts";
                case "py": case "python": return ".py";
                case "java": return ".java";
                case "cpp": case "c++": return ".cpp";
                case "c": return ".c";
                case "xml": return ".xml";
                case "html": case "htm": return ".html";
                case "css": return ".css";
                case "json": return ".json";
                case "yaml": case "yml": return ".yaml";
                case "md": case "markdown": return ".md";
                case "sh": case "bash": return ".sh";
                case "ps1": case "powershell": return ".ps1";
                case "sql": return ".sql";
                case "go": return ".go";
                case "rs": case "rust": return ".rs";
                case "rb": case "ruby": return ".rb";
                case "php": return ".php";
                case "swift": return ".swift";
                case "kt": case "kotlin": return ".kt";
                default: return "." + l;
            }
        }

        private static HashSet<string> GetKeywords(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return null;
            string l = lang.ToLower();
            if (l == "cs" || l == "csharp")
                return new HashSet<string> { "using","namespace","class","struct","interface","enum","public","private","protected","internal","static","void","int","string","bool","var","new","return","if","else","for","foreach","while","do","switch","case","break","continue","try","catch","finally","throw","async","await","null","true","false","this","base","override","virtual","abstract","sealed","readonly","const","out","ref","in","typeof","sizeof","is","as","lock","yield" };
            if (l == "js" || l == "javascript" || l == "ts" || l == "typescript")
                return new HashSet<string> { "const","let","var","function","class","return","if","else","for","while","do","switch","case","break","continue","try","catch","finally","throw","new","this","import","export","from","default","async","await","null","undefined","true","false","typeof","instanceof" };
            if (l == "python" || l == "py")
                return new HashSet<string> { "def","class","return","if","elif","else","for","while","break","continue","try","except","finally","raise","import","from","as","with","lambda","yield","pass","None","True","False","and","or","not","in","is","self","async","await","global","nonlocal" };
            if (l == "java")
                return new HashSet<string> { "public","private","protected","class","interface","enum","static","void","int","long","double","float","boolean","char","String","new","return","if","else","for","while","do","switch","case","break","continue","try","catch","finally","throw","import","package","extends","implements","this","super","null","true","false","abstract","final","synchronized" };
            return null;
        }

        private static void AppendHighlightedLine(Paragraph p, string line, string lang, HashSet<string> keywords)
        {
            if (string.IsNullOrEmpty(line))
            {
                p.Inlines.Add(new Run { Text = " ", FontFamily = CodeMonoFont, FontSize = 13 });
                return;
            }

            string tl = line.TrimStart();
            // Comment detection
            bool isComment = false;
            if (!string.IsNullOrEmpty(lang))
            {
                string ll = lang.ToLower();
                if ((ll=="cs"||ll=="csharp"||ll=="cpp"||ll=="c"||ll=="java"||ll=="js"||ll=="ts"||ll=="javascript"||ll=="typescript") && tl.StartsWith("//"))
                    isComment = true;
                if ((ll=="python"||ll=="py") && tl.StartsWith("#"))
                    isComment = true;
                if ((ll=="xml"||ll=="html"||ll=="htm") && tl.StartsWith("<!--"))
                    isComment = true;
            }

            if (isComment)
            {
                p.Inlines.Add(new Run { Text = line, FontFamily = CodeMonoFont, FontSize = 13, Foreground = CommentFg, FontStyle = Windows.UI.Text.FontStyle.Italic });
                return;
            }

            if (keywords == null || keywords.Count == 0)
            {
                p.Inlines.Add(new Run { Text = line, FontFamily = CodeMonoFont, FontSize = 13, Foreground = CodeFg });
                return;
            }

            // Token-level highlighting
            int i = 0;
            var buf = new StringBuilder();
            var monoFont = CodeMonoFont;

            void FlushBuf()
            {
                if (buf.Length == 0) return;
                p.Inlines.Add(new Run { Text = buf.ToString(), FontFamily = monoFont, FontSize = 13, Foreground = CodeFg });
                buf.Clear();
            }

            while (i < line.Length)
            {
                char c = line[i];

                // String literal
                if (c == '"' || c == '\'')
                {
                    FlushBuf();
                    char quote = c;
                    var sb = new StringBuilder();
                    sb.Append(c); i++;
                    while (i < line.Length && line[i] != quote) { if (line[i] == '\\' && i+1 < line.Length) { sb.Append(line[i++]); } sb.Append(line[i++]); }
                    if (i < line.Length) { sb.Append(line[i++]); }
                    p.Inlines.Add(new Run { Text = sb.ToString(), FontFamily = monoFont, FontSize = 13, Foreground = StringFg });
                    continue;
                }

                // Number
                if (char.IsDigit(c) && (i == 0 || !char.IsLetterOrDigit(line[i-1])))
                {
                    FlushBuf();
                    var sb = new StringBuilder();
                    while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.' || line[i] == 'x' || line[i] == 'f')) sb.Append(line[i++]);
                    p.Inlines.Add(new Run { Text = sb.ToString(), FontFamily = monoFont, FontSize = 13, Foreground = NumberFg });
                    continue;
                }

                // Identifier / keyword
                if (char.IsLetter(c) || c == '_')
                {
                    var sb = new StringBuilder();
                    while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) sb.Append(line[i++]);
                    string word = sb.ToString();
                    if (keywords.Contains(word))
                    { FlushBuf(); p.Inlines.Add(new Run { Text = word, FontFamily = monoFont, FontSize = 13, Foreground = KeywordFg }); }
                    else
                    { buf.Append(word); }
                    continue;
                }

                buf.Append(c); i++;
            }
            FlushBuf();
        }

        // ── Table builder (Grid-based for proper alignment) ───────────────

        private static IEnumerable<Block> BuildTable(List<string> rows, RenderContext ctx)
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

            // Determine column count from first row
            int colCount = ParseRow(data[0]).Count;
            if (colCount == 0) return result;

            // Build table as Grid inside InlineUIContainer
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            for (int ci = 0; ci < colCount; ci++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            for (int ri = 0; ri < data.Count; ri++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var cells = ParseRow(data[ri]);
                for (int ci = 0; ci < colCount; ci++)
                {
                    string cellText = ci < cells.Count ? cells[ci] : "";

                    var tb = new TextBlock
                    {
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Padding = new Thickness(10, 6, 10, 6),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    // 解析单元格内的内联 Markdown（**粗体**、`代码`、链接等）
                    if (string.IsNullOrEmpty(cellText))
                    {
                        tb.Text = "";
                    }
                    else
                    {
                        try
                        {
                            foreach (var il in ParseInlines(cellText, ctx))
                                tb.Inlines.Add(il);
                        }
                        catch
                        {
                            tb.Text = cellText; // 兜底：解析失败显示原文
                        }
                    }
                    if (ri == 0) tb.FontWeight = FontWeights.SemiBold;

                    var border = new Border
                    {
                        Child = tb,
                        BorderBrush = TableBorderFg,
                        BorderThickness = new Thickness(0, 0, ci < colCount - 1 ? 1 : 0, 1),
                    };
                    if (ri == 0)          border.Background = TableHeaderBg;
                    else if (ri % 2 == 0) border.Background = TableRowAltBg;
                    else                  border.Background = TableRowBg;

                    Grid.SetRow(border, ri);
                    Grid.SetColumn(border, ci);
                    grid.Children.Add(border);
                }
            }

            // 给整个表格加一个背景框，让它在 AI 气泡上有明显边界
            var tableContainer = new Border
            {
                CornerRadius      = new CornerRadius(4),
                Background        = TableRowBg,
                BorderBrush       = TableBorderFg,
                BorderThickness  = new Thickness(1),
                Padding           = new Thickness(0),
                Child             = grid,
            };

            // Wrap in paragraph with InlineUIContainer + horizontal scroll for wide tables
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
                HorizontalScrollMode          = ScrollMode.Auto,
                VerticalScrollMode            = ScrollMode.Disabled,
                ZoomMode                      = ZoomMode.Disabled,
                Content                       = tableContainer,
            };
            var p = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
            p.Inlines.Add(new InlineUIContainer { Child = scroll });
            result.Add(p);
            return result;
        }

        // ── Block helpers ─────────────────────────────────────────────────────

        private static Paragraph HeadingPara(string content, double fontSize, Thickness margin, RenderContext ctx)
        {
            var p = new Paragraph { Margin = margin };
            foreach (var il in ParseInlines(content, ctx))
            {
                if (il is Run r) { r.FontWeight = FontWeights.SemiBold; r.FontSize = fontSize; }
                else if (il is Bold b)
                    foreach (var sub in b.Inlines.OfType<Run>()) sub.FontSize = fontSize;
                p.Inlines.Add(il);
            }
            return p;
        }

        private static Paragraph TaskPara(string content, bool done, int indent, RenderContext ctx)
        {
            double leftPad = 14 + indent * 1.5;
            var p = new Paragraph { Margin = new Thickness(leftPad, 1, 0, 1), TextIndent = -14 };
            p.Inlines.Add(new Run { Text = done ? "☑ " : "☐ " });
            foreach (var il in ParseInlines(content, ctx))
            {
                if (done && il is Run r) r.Foreground = StrikeFg;
                p.Inlines.Add(il);
            }
            return p;
        }

        private static Paragraph HrPara()
        {
            var p = new Paragraph { Margin = new Thickness(0, 8, 0, 8) };
            p.Inlines.Add(new Run { Text = new string('─', 30), FontSize = 10, Foreground = HrFg });
            return p;
        }

        private static Paragraph MathBlockPara(string content)
        {
            var p = new Paragraph { Margin = new Thickness(0, 6, 0, 6), TextAlignment = TextAlignment.Center };
            FrameworkElement math = LatexRenderer.Render(content, 16, MathFg, display: true);
            // 宽公式可横向滚动，避免撑破气泡（与表格/代码块一致的处理）
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
                HorizontalScrollMode          = ScrollMode.Auto,
                VerticalScrollMode            = ScrollMode.Disabled,
                ZoomMode                      = ZoomMode.Disabled,
                Content                       = math,
                Margin                        = new Thickness(0, 2, 0, 2),
            };
            p.Inlines.Add(new InlineUIContainer { Child = scroll });
            return p;
        }

        // ── Inline parser ─────────────────────────────────────────────────────

        private static IEnumerable<Inline> ParseInlines(string text, RenderContext ctx)
        {
            var result = new List<Inline>();
            if (string.IsNullOrEmpty(text)) return result;

            var buf = new StringBuilder();
            int i = 0;

            void Flush()
            {
                if (buf.Length == 0) return;
                string flushed = buf.ToString();
                // 检测是否以 emoji 开头：若 buf 首字符是高代理项（emoji 起始），
                // 把整段 emoji（含 ZWJ 复合、VS16 表现符、肤色修饰）拆出，单独 Run 渲染
                // 避免 emoji 走 Segoe UI Emoji 字体 + 中文走 Microsoft YaHei 字体，
                // 二者字形宽度/基线不一致导致视觉重叠
                if (flushed.Length >= 2 && char.IsHighSurrogate(flushed[0]))
                {
                    int cut = 2;
                    while (cut < flushed.Length)
                    {
                        char c = flushed[cut];
                        // ZWJ、VS16：归入 emoji 段
                        if (c == '\u200D' || c == '\uFE0F') { cut++; continue; }
                        // 肤色修饰 U+1F3FB..U+1F3FF：代理对
                        if (char.IsHighSurrogate(c) && cut + 1 < flushed.Length &&
                            flushed[cut] == '\uD83C' &&
                            char.IsLowSurrogate(flushed[cut + 1]) &&
                            flushed[cut + 1] >= '\uDFFB' && flushed[cut + 1] <= '\uDFFF')
                        {
                            cut += 2; continue;
                        }
                        break;
                    }
                    string emojiPart = flushed.Substring(0, cut);
                    string textPart  = cut < flushed.Length ? flushed.Substring(cut) : "";
                    // emoji 段：单独 Run，强制 Segoe UI Emoji 优先
                    result.Add(new Run
                    {
                        Text       = emojiPart,
                        FontFamily = EmojiFont,
                    });
                    // 后续文字：单独 Run，使用完整回退链
                    if (textPart.Length > 0)
                        result.Add(new Run { Text = textPart, FontFamily = UiFontFamily });
                }
                else
                {
                    result.Add(new Run { Text = flushed, FontFamily = UiFontFamily });
                }
                buf.Clear();
            }

            while (i < text.Length)
            {
                char c = text[i];

                // ── Hard line break ──────────────────────────────────────
                if (c == '\n') { Flush(); result.Add(new LineBreak()); i++; continue; }

                // ── Escape ───────────────────────────────────────────────
                if (c == '\\' && i + 1 < text.Length && "\\`*_{}[]()#+-.!~|$".IndexOf(text[i+1]) >= 0)
                { buf.Append(text[i+1]); i += 2; continue; }

                // ── Inline math $…$ ──────────────────────────────────────
                if (c == '$' && i + 1 < text.Length && text[i+1] != '$')
                {
                    int end = text.IndexOf('$', i + 1);
                    if (end > i + 1)
                    {
                        Flush();
                        string latex = text.Substring(i + 1, end - i - 1);
                        double baseline;
                        FrameworkElement math = LatexRenderer.Render(latex, 13, MathFg, false, out baseline);
                        // InlineUIContainer 默认把子元素底边对齐到文本基线；公式的数学基线
                        // 在其顶部 baseline 处、下方还有 descent，故整体下移 descent 让两条基线重合。
                        double descent = math.Height - baseline;
                        if (!double.IsNaN(descent) && descent > 0)
                            math.RenderTransform = new TranslateTransform { Y = descent };
                        result.Add(new InlineUIContainer { Child = math });
                        i = end + 1; continue;
                    }
                }

                // ── Image ![alt](url) ────────────────────────────────────
                if (c == '!' && i + 1 < text.Length && text[i+1] == '[')
                {
                    int altEnd = text.IndexOf(']', i + 2);
                    if (altEnd > 0 && altEnd + 1 < text.Length && text[altEnd+1] == '(')
                    {
                        int urlEnd = FindClosingParen(text, altEnd + 1);
                        if (urlEnd > altEnd + 2)
                        {
                            string alt = text.Substring(i + 2, altEnd - i - 2);
                            string url = text.Substring(altEnd + 2, urlEnd - altEnd - 2).Trim();
                            Flush();
                            BitmapImage bmp = null;
                            if (url.StartsWith("data:") && url.Contains(";base64,"))
                            {
                                try
                                {
                                    int b64s = url.IndexOf(";base64,") + 8;
                                    byte[] bytes = Convert.FromBase64String(url.Substring(b64s));
                                    bmp = AppSettings.LoadBitmapSync(bytes);
                                }
                                catch { }
                            }
                            else if (Uri.TryCreate(url, UriKind.Absolute, out Uri imgUri) && (imgUri.Scheme == "http" || imgUri.Scheme == "https"))
                                bmp = new BitmapImage(imgUri);

                            if (bmp != null && ctx.Rtb != null)
                            {
                                var img = new Image
                                {
                                    Source = bmp,
                                    MaxWidth = ctx.Rtb.ActualWidth > 0 ? ctx.Rtb.ActualWidth - 8 : 280,
                                    MaxHeight = 400,
                                    Stretch = Windows.UI.Xaml.Media.Stretch.Uniform,
                                    Margin = new Thickness(0, 4, 0, 4),
                                };
                                result.Add(new InlineUIContainer { Child = img });
                            }
                            else
                            {
                                result.Add(new Run { Text = $"🖼 {(string.IsNullOrEmpty(alt) ? url : alt)}", Foreground = LinkFg, FontSize = 13 });
                            }
                            i = urlEnd + 1; continue;
                        }
                    }
                }

                // ── Link [text](url) — clickable Hyperlink ───────────────
                if (c == '[' && i + 1 < text.Length && text[i+1] != '^')
                {
                    int txtEnd = text.IndexOf(']', i + 1);
                    if (txtEnd > 0 && txtEnd + 1 < text.Length && text[txtEnd+1] == '(')
                    {
                        int urlEnd = FindClosingParen(text, txtEnd + 1);
                        if (urlEnd > 0)
                        {
                            Flush();
                            string linkText = text.Substring(i + 1, txtEnd - i - 1);
                            string linkUrl  = text.Substring(txtEnd + 2, urlEnd - txtEnd - 2).Trim();
                            // strip title
                            var titleMatch = Regex.Match(linkUrl, @"^(.+?)\s+[""'](.+)[""']$");
                            if (titleMatch.Success) linkUrl = titleMatch.Groups[1].Value.Trim();

                            // Use Hyperlink for clickable links
                            Uri uri;
                            if (Uri.TryCreate(linkUrl, UriKind.Absolute, out uri))
                            {
                                var hyperlink = new Hyperlink { NavigateUri = uri };
                                hyperlink.Inlines.Add(new Run { Text = linkText, Foreground = LinkFg });
                                hyperlink.UnderlineStyle = UnderlineStyle.Single;
                                result.Add(hyperlink);
                            }
                            else
                            {
                                // Non-URL link: just style it
                                var underline = new Underline();
                                underline.Inlines.Add(new Run { Text = linkText, Foreground = LinkFg });
                                result.Add(underline);
                            }
                            i = urlEnd + 1; continue;
                        }
                    }
                    // Reference link [text][ref]
                    if (txtEnd > 0 && txtEnd + 1 < text.Length && text[txtEnd+1] == '[')
                    {
                        int refEnd = text.IndexOf(']', txtEnd + 2);
                        if (refEnd > 0)
                        {
                            Flush();
                            result.Add(new Run { Text = text.Substring(i + 1, txtEnd - i - 1), Foreground = LinkFg });
                            i = refEnd + 1; continue;
                        }
                    }
                }

                // ── Bold+Italic ***text*** ───────────────────────────────
                if ((c == '*' && i+2 < text.Length && text[i+1]=='*' && text[i+2]=='*') ||
                    (c == '_' && i+2 < text.Length && text[i+1]=='_' && text[i+2]=='_'))
                {
                    string marker = new string(c, 3);
                    int end = text.IndexOf(marker, i + 3, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        Flush();
                        var span = new Bold();
                        var inner = new Italic();
                        foreach (var il in ParseInlines(text.Substring(i + 3, end - i - 3), ctx))
                            inner.Inlines.Add(il);
                        span.Inlines.Add(inner);
                        result.Add(span); i = end + 3; continue;
                    }
                }

                // ── Bold **text** ────────────────────────────────────────
                if ((c == '*' && i+1 < text.Length && text[i+1]=='*') ||
                    (c == '_' && i+1 < text.Length && text[i+1]=='_'))
                {
                    string marker = new string(c, 2);
                    int end = text.IndexOf(marker, i + 2, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        Flush();
                        var span = new Bold();
                        foreach (var il in ParseInlines(text.Substring(i + 2, end - i - 2), ctx))
                            span.Inlines.Add(il);
                        result.Add(span); i = end + 2; continue;
                    }
                }

                // ── Italic *text* ────────────────────────────────────────
                if (c == '*' || c == '_')
                {
                    bool notSpace = i + 1 < text.Length && text[i+1] != ' ';
                    if (notSpace)
                    {
                        int end = FindClosingEmphasis(text, c, i + 1);
                        if (end > i + 1)
                        {
                            Flush();
                            var span = new Italic();
                            foreach (var il in ParseInlines(text.Substring(i + 1, end - i - 1), ctx))
                                span.Inlines.Add(il);
                            result.Add(span); i = end + 1; continue;
                        }
                    }
                }

                // ── Strikethrough ~~text~~ ───────────────────────────────
                if (c == '~' && i+1 < text.Length && text[i+1]=='~')
                {
                    int end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        Flush();
                        result.Add(new Run { Text = text.Substring(i + 2, end - i - 2), Foreground = StrikeFg });
                        i = end + 2; continue;
                    }
                }

                // ── Inline code `code` ───────────────────────────────────
                if (c == '`')
                {
                    bool dbl = i + 1 < text.Length && text[i+1] == '`';
                    string tick = dbl ? "``" : "`";
                    int start = i + tick.Length;
                    int end = text.IndexOf(tick, start, StringComparison.Ordinal);
                    if (end > start - 1)
                    {
                        Flush();
                        string codeText = text.Substring(start, end - start).Trim();
                        var tb = new TextBlock
                        {
                            Text = codeText,
                            FontFamily = CodeMonoFont,
                            FontSize = 13,
                            Foreground = CodeFg,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        var border = new Border
                        {
                            Background = CodeBg,
                            CornerRadius = new CornerRadius(3),
                            Padding = new Thickness(5, 2, 5, 2),
                            Child = tb,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        result.Add(new InlineUIContainer { Child = border });
                        i = end + tick.Length; continue;
                    }
                }

                // ── Footnote ref [^key] ──────────────────────────────────
                if (c == '[' && i+1 < text.Length && text[i+1]=='^')
                {
                    int close = text.IndexOf(']', i + 2);
                    if (close > 0)
                    {
                        Flush();
                        string key = text.Substring(i + 2, close - i - 2);
                        result.Add(new Run { Text = $"[{key}]", FontSize = 11, Foreground = LinkFg });
                        i = close + 1; continue;
                    }
                }

                // ── Quote/bracket coloring ───────────────────────────────
                if (c == '“') // "…"
                {
                    int close = text.IndexOf('”', i + 1);
                    if (close > i) { Flush(); result.Add(new Run { Text = text.Substring(i, close - i + 1), Foreground = ctx.QuoteBrush }); i = close + 1; continue; }
                }
                if (c == '「') // 「…」
                {
                    int close = text.IndexOf('」', i + 1);
                    if (close > i) { Flush(); result.Add(new Run { Text = text.Substring(i, close - i + 1), Foreground = ctx.QuoteBrush }); i = close + 1; continue; }
                }
                if (c == '（') // （…）
                {
                    int close = text.IndexOf('）', i + 1);
                    if (close > i) { Flush(); result.Add(new Run { Text = text.Substring(i, close - i + 1), Foreground = ctx.BracketBrush }); i = close + 1; continue; }
                }

                buf.Append(c);
                i++;
            }
            Flush();
            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static int FindClosingEmphasis(string text, char ch, int from)
        {
            for (int j = from; j < text.Length; j++)
            {
                if (text[j] == ch)
                {
                    bool ok = (j == 0 || text[j-1] != ' ') &&
                              (j + 1 >= text.Length || text[j+1] != ch);
                    if (ok) return j;
                }
            }
            return -1;
        }

        private static int FindClosingParen(string text, int openIdx)
        {
            // Handle nested parens in URLs
            int depth = 0;
            for (int j = openIdx; j < text.Length; j++)
            {
                if (text[j] == '(') depth++;
                else if (text[j] == ')') { depth--; if (depth == 0) return j; }
            }
            return -1;
        }
    }
}
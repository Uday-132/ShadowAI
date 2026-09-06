using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace OverlayApp.Views
{
    /// <summary>
    /// Attached property that renders markdown-style formatted text in a WPF TextBlock.
    /// Supports: headings (###), bold (**text**), inline code (`code`), code blocks (```),
    /// bullet points (* / -), horizontal rules (---), and status emoji prefixes.
    /// </summary>
    public static class TextBlockHelper
    {
        public static readonly DependencyProperty FormattedTextProperty =
            DependencyProperty.RegisterAttached(
                "FormattedText",
                typeof(string),
                typeof(TextBlockHelper),
                new PropertyMetadata(string.Empty, OnFormattedTextChanged));

        public static string GetFormattedText(DependencyObject obj) => (string)obj.GetValue(FormattedTextProperty);
        public static void SetFormattedText(DependencyObject obj, string value) => obj.SetValue(FormattedTextProperty, value);

        // Compiled regex patterns
        private static readonly Regex _boldRegex     = new Regex(@"\*\*(.+?)\*\*",   RegexOptions.Compiled);
        private static readonly Regex _italicRegex   = new Regex(@"\*(.+?)\*",        RegexOptions.Compiled);
        private static readonly Regex _inlineCode    = new Regex(@"`([^`]+)`",         RegexOptions.Compiled);
        private static readonly Regex _mathDollar    = new Regex(@"\$[^$]+\$",         RegexOptions.Compiled);

        private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock textBlock) return;
            textBlock.Inlines.Clear();
            if (e.NewValue is not string text || string.IsNullOrEmpty(text)) return;

            try
            {
                string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                bool inCodeBlock = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    bool addNewline = i < lines.Length - 1;

                    // ── Code block fence ────────────────────────────────────────
                    if (line.TrimStart().StartsWith("```"))
                    {
                        inCodeBlock = !inCodeBlock;
                        if (!addNewline) continue;
                        textBlock.Inlines.Add(new Run("\n"));
                        continue;
                    }

                    if (inCodeBlock)
                    {
                        var codeRun = new Run((addNewline ? line + "\n" : line))
                        {
                            Foreground  = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFCC80")), // Amber
                            FontFamily  = new FontFamily("Consolas, Courier New"),
                            FontSize    = 10
                        };
                        textBlock.Inlines.Add(codeRun);
                        continue;
                    }

                    string trimmed = line.TrimStart();

                    // ── Blank line ───────────────────────────────────────────────
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        textBlock.Inlines.Add(new Run("\n"));
                        continue;
                    }

                    // ── Horizontal rule ─────────────────────────────────────────
                    if (trimmed == "---" || trimmed == "***" || trimmed == "___")
                    {
                        var hrRun = new Run("────────────────────────────\n")
                        {
                            Foreground = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255))
                        };
                        textBlock.Inlines.Add(hrRun);
                        continue;
                    }

                    // ── Status / emoji prefixes (errors, success, warnings) ─────
                    if (trimmed.StartsWith("⚠️") || trimmed.StartsWith("❌") ||
                        trimmed.StartsWith("⏳ Rate limit") || trimmed.StartsWith("🔑") ||
                        trimmed.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("API Error", StringComparison.OrdinalIgnoreCase))
                    {
                        AppendInlineMarkdown(textBlock, line, addNewline,
                            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFF6B6B")),
                            FontWeights.SemiBold);
                        continue;
                    }

                    if (trimmed.StartsWith("✅") || trimmed.StartsWith("⭐") ||
                        trimmed.StartsWith("✨") ||
                        trimmed.Contains("Both models agree", StringComparison.OrdinalIgnoreCase))
                    {
                        AppendInlineMarkdown(textBlock, line, addNewline,
                            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF00E676")),
                            FontWeights.Bold);
                        continue;
                    }

                    // ── Markdown headings ────────────────────────────────────────
                    if (trimmed.StartsWith("### "))
                    {
                        string heading = trimmed.Substring(4);
                        AppendInlineMarkdown(textBlock, heading, addNewline,
                            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF00D2FF")),
                            FontWeights.Bold, fontSize: 12);
                        continue;
                    }
                    if (trimmed.StartsWith("## "))
                    {
                        string heading = trimmed.Substring(3);
                        AppendInlineMarkdown(textBlock, heading, addNewline,
                            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF00D2FF")),
                            FontWeights.Bold, fontSize: 13);
                        continue;
                    }
                    if (trimmed.StartsWith("# "))
                    {
                        string heading = trimmed.Substring(2);
                        AppendInlineMarkdown(textBlock, heading, addNewline,
                            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF00D2FF")),
                            FontWeights.Bold, fontSize: 14);
                        continue;
                    }

                    // ── Bullet points (* or -) ───────────────────────────────────
                    if (trimmed.StartsWith("* ") || trimmed.StartsWith("- ") ||
                        (trimmed.Length > 2 && trimmed[0] >= '1' && trimmed[0] <= '9' && trimmed[1] == '.'))
                    {
                        // Indent bullet
                        textBlock.Inlines.Add(new Run("  • ")
                        {
                            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#88AAAAAA"))
                        });
                        string bulletContent = trimmed.StartsWith("* ") || trimmed.StartsWith("- ")
                            ? trimmed.Substring(2)
                            : trimmed.Substring(trimmed.IndexOf('.') + 1).TrimStart();
                        AppendInlineMarkdown(textBlock, bulletContent, addNewline,
                            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE0E0E0")),
                            FontWeights.Normal);
                        continue;
                    }

                    // ── System/status lines ──────────────────────────────────────
                    if (trimmed.StartsWith("Transcribed Query:") || trimmed.StartsWith("👉 Follow-up Question:") ||
                        trimmed.StartsWith("Analyzing query") || trimmed.StartsWith("Live auto-answering active") ||
                        trimmed.StartsWith("Recording audio query") || trimmed.StartsWith("[System]") ||
                        trimmed.StartsWith("Thinking...") || trimmed.StartsWith("Listening..."))
                    {
                        AppendInlineMarkdown(textBlock, line, addNewline,
                            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA0A0A0")),
                            FontWeights.Bold);
                        continue;
                    }

                    // ── Quoted transcribed speech ────────────────────────────────
                    if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
                    {
                        AppendInlineMarkdown(textBlock, line, addNewline,
                            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF00E676")),
                            FontWeights.Medium, italic: true);
                        continue;
                    }

                    // ── Default: normal AI content with inline markdown ──────────
                    AppendInlineMarkdown(textBlock, line, addNewline,
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD0D0D0")),
                        FontWeights.Normal);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TextBlockHelper formatting failed: {ex.Message}");
                textBlock.Inlines.Clear();
                textBlock.Inlines.Add(new Run(e.NewValue as string ?? ""));
            }
        }

        /// <summary>
        /// Appends a line to the TextBlock, parsing inline **bold**, *italic*, and `code` spans.
        /// </summary>
        private static void AppendInlineMarkdown(TextBlock tb, string text, bool addNewline,
            Brush defaultBrush, FontWeight defaultWeight, double fontSize = 0, bool italic = false)
        {
            // Strip $...$ math notation (replace with plain text)
            text = _mathDollar.Replace(text, m => m.Value.Trim('$'));

            // Tokenise: split by bold, italic, inline-code markers in order
            int pos = 0;
            while (pos < text.Length)
            {
                // Try bold first
                var boldMatch  = _boldRegex.Match(text, pos);
                var codeMatch  = _inlineCode.Match(text, pos);
                var italicMatch = _italicRegex.Match(text, pos);

                // Pick the earliest match
                int boldIdx   = boldMatch.Success   ? boldMatch.Index   : int.MaxValue;
                int codeIdx   = codeMatch.Success   ? codeMatch.Index   : int.MaxValue;
                int italicIdx = italicMatch.Success ? italicMatch.Index : int.MaxValue;

                int earliest = Math.Min(boldIdx, Math.Min(codeIdx, italicIdx));

                if (earliest == int.MaxValue || earliest >= text.Length)
                {
                    // No more markers — append the rest
                    AppendRun(tb, text.Substring(pos), defaultBrush, defaultWeight, fontSize, italic);
                    pos = text.Length;
                    break;
                }

                // Text before the marker
                if (earliest > pos)
                    AppendRun(tb, text.Substring(pos, earliest - pos), defaultBrush, defaultWeight, fontSize, italic);

                if (earliest == boldIdx && boldIdx <= codeIdx && boldIdx <= italicIdx)
                {
                    AppendRun(tb, boldMatch.Groups[1].Value,
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFFFF")),
                        FontWeights.Bold, fontSize, italic);
                    pos = boldMatch.Index + boldMatch.Length;
                }
                else if (earliest == codeIdx)
                {
                    AppendRun(tb, codeMatch.Groups[1].Value,
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFCC80")),
                        FontWeights.Normal, fontSize == 0 ? 10 : fontSize, false,
                        fontFamily: new FontFamily("Consolas, Courier New"),
                        background: new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)));
                    pos = codeMatch.Index + codeMatch.Length;
                }
                else
                {
                    AppendRun(tb, italicMatch.Groups[1].Value,
                        defaultBrush, defaultWeight, fontSize, italic: true);
                    pos = italicMatch.Index + italicMatch.Length;
                }
            }

            if (addNewline)
                tb.Inlines.Add(new Run("\n"));
        }

        private static void AppendRun(TextBlock tb, string text, Brush foreground,
            FontWeight weight, double fontSize, bool italic,
            FontFamily? fontFamily = null, Brush? background = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            var run = new Run(text)
            {
                Foreground  = foreground,
                FontWeight  = weight,
                FontStyle   = italic ? FontStyles.Italic : FontStyles.Normal
            };
            if (fontSize > 0) run.FontSize = fontSize;
            if (fontFamily != null) run.FontFamily = fontFamily;
            if (background != null)
            {
                // Wrap in a bordered inline container via InlineUIContainer
                var border = new System.Windows.Controls.Border
                {
                    Background    = background,
                    CornerRadius  = new CornerRadius(2),
                    Padding       = new Thickness(2, 0, 2, 0),
                    Child         = new TextBlock(run)
                };
                tb.Inlines.Add(new InlineUIContainer(border));
                return;
            }
            tb.Inlines.Add(run);
        }
    }
}

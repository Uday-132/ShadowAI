using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace OverlayApp.Views
{
    /// <summary>
    /// Attached property to render multi-colored formatted text blocks in WPF,
    /// color-coding status indicators, transcribed questions, and AI answers differently.
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

        private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if (d is TextBlock textBlock)
                {
                    textBlock.Inlines.Clear();
                    string? text = e.NewValue as string;
                    if (string.IsNullOrEmpty(text)) return;

                    // Split text by lines to parse structure
                    string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            textBlock.Inlines.Add(new Run("\n"));
                            continue;
                        }

                        Run run = new Run(line + (i < lines.Length - 1 ? "\n" : ""));
                        string trimmed = line.Trim();

                        // 1. Theme Status indicators or header labels: Soft Gray
                        if (trimmed.StartsWith("Transcribed Query:") || 
                            trimmed.StartsWith("Transcribed Query (Live):") ||
                            trimmed.StartsWith("👉 Follow-up Question:") || 
                            trimmed.StartsWith("Analyzing query") || 
                            trimmed.StartsWith("Live auto-answering active") ||
                            trimmed.StartsWith("Recording audio query") ||
                            trimmed.StartsWith("[System]") ||
                            trimmed.StartsWith("Thinking...") ||
                            trimmed.StartsWith("Listening..."))
                        {
                            run.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA0A0A0")); // Soft Gray
                            run.FontWeight = FontWeights.Bold;
                        }
                        // 2. The transcribed spoken question: Highlight in bright Mint Green
                        else if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
                        {
                            run.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF00E676")); // Mint Green
                            run.FontStyle = FontStyles.Italic;
                            run.FontWeight = FontWeights.Medium;
                        }
                        // 3. AI Generated Solutions / content: Onyx Sky Blue / Cyan
                        else
                        {
                            run.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF00D2FF")); // Onyx Blue
                        }

                        textBlock.Inlines.Add(run);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TextBlockHelper formatting failed: {ex.Message}");
                if (d is TextBlock tb && e.NewValue is string plainText)
                {
                    tb.Inlines.Clear();
                    tb.Inlines.Add(new Run(plainText));
                }
            }
        }
    }
}

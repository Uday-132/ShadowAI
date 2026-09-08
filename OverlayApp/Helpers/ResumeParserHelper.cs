using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace OverlayApp.Helpers
{
    /// <summary>
    /// Extracts plain text from resume files (.txt, .md, .docx, .pdf, .rtf).
    /// Uses native .NET and Windows 10 WinRT OCR capabilities with zero external dependencies.
    /// </summary>
    public static class ResumeParserHelper
    {
        public static async Task<string> ExtractTextAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("Resume file does not exist.", filePath);
            }

            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            return extension switch
            {
                ".txt" or ".md" => await File.ReadAllTextAsync(filePath),
                ".docx" => await ExtractFromDocxAsync(filePath),
                ".pdf" => await ExtractFromPdfAsync(filePath),
                ".rtf" => await ExtractFromRtfAsync(filePath),
                _ => await File.ReadAllTextAsync(filePath)
            };
        }

        /// <summary>
        /// Native extraction from .docx by reading word/document.xml inside the zip package.
        /// </summary>
        private static async Task<string> ExtractFromDocxAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                var sb = new StringBuilder();
                using var archive = ZipFile.OpenRead(filePath);
                var docEntry = archive.GetEntry("word/document.xml");
                if (docEntry == null) return string.Empty;

                using var stream = docEntry.Open();
                var doc = XDocument.Load(stream);

                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

                foreach (var paragraph in doc.Descendants(w + "p"))
                {
                    var paragraphText = new StringBuilder();
                    foreach (var textNode in paragraph.Descendants(w + "t"))
                    {
                        paragraphText.Append(textNode.Value);
                    }

                    if (paragraphText.Length > 0)
                    {
                        sb.AppendLine(paragraphText.ToString());
                    }
                }

                return sb.ToString().Trim();
            });
        }

        /// <summary>
        /// Extracts text from a PDF file using stream parsing first, falling back to Windows 10 OCR.
        /// </summary>
        private static async Task<string> ExtractFromPdfAsync(string filePath)
        {
            // Try quick text stream extraction first
            string streamText = await ExtractPdfTextStreamsAsync(filePath);
            if (!string.IsNullOrWhiteSpace(streamText) && streamText.Length > 80)
            {
                return streamText;
            }

            // Fallback: Windows 10 Native PDF rendering + OcrEngine
            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath));
                PdfDocument pdfDoc = await PdfDocument.LoadFromFileAsync(file);

                if (pdfDoc != null && pdfDoc.PageCount > 0)
                {
                    var ocrEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
                                    ?? OcrEngine.TryCreateFromUserProfileLanguages();

                    if (ocrEngine != null)
                    {
                        var sb = new StringBuilder();
                        uint maxPages = Math.Min(pdfDoc.PageCount, 5); // typical resume is 1-3 pages
                        for (uint i = 0; i < maxPages; i++)
                        {
                            using var page = pdfDoc.GetPage(i);
                            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                            var renderOptions = new PdfPageRenderOptions { DestinationWidth = 1800 };
                            await page.RenderToStreamAsync(stream, renderOptions);

                            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                            SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                                BitmapPixelFormat.Bgra8,
                                BitmapAlphaMode.Premultiplied
                            );

                            var ocrResult = await ocrEngine.RecognizeAsync(softwareBitmap);
                            if (ocrResult != null && !string.IsNullOrWhiteSpace(ocrResult.Text))
                            {
                                sb.AppendLine(ocrResult.Text);
                                sb.AppendLine();
                            }
                        }

                        string ocrOutput = sb.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(ocrOutput))
                        {
                            return ocrOutput;
                        }
                    }
                }
            }
            catch
            {
                // If native PDF OCR fails, return whatever stream text was extracted
            }

            return !string.IsNullOrWhiteSpace(streamText) ? streamText : "Could not extract readable text from PDF. Please paste resume text directly.";
        }

        /// <summary>
        /// Reads uncompressed or raw literal text streams from standard PDF objects.
        /// </summary>
        private static async Task<string> ExtractPdfTextStreamsAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(filePath);
                    string raw = Encoding.Latin1.GetString(bytes);

                    var sb = new StringBuilder();

                    // Match text strings in PDF content: (Some text) Tj or [(Some) -20 (text)] TJ
                    var matches = Regex.Matches(raw, @"\(([^)]+)\)\s*(?:Tj|TJ)", RegexOptions.Compiled);
                    foreach (Match m in matches)
                    {
                        string val = m.Groups[1].Value;
                        // Clean basic PDF escapes
                        val = val.Replace(@"\n", "\n").Replace(@"\r", "").Replace(@"\t", " ").Replace(@"\(", "(").Replace(@"\)", ")");
                        sb.Append(val + " ");
                    }

                    return sb.ToString().Trim();
                }
                catch
                {
                    return string.Empty;
                }
            });
        }

        /// <summary>
        /// Strips RTF control codes to extract plain text.
        /// </summary>
        private static async Task<string> ExtractFromRtfAsync(string filePath)
        {
            return await Task.Run(async () =>
            {
                string rtf = await File.ReadAllTextAsync(filePath);
                // Strip RTF markup
                string plain = Regex.Replace(rtf, @"{\*?\\[^{}]+}|[{}]|\\\n?[A-Za-z]+\n?(?:-?\d+)?[ ]?", "");
                return plain.Trim();
            });
        }
    }
}

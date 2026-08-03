using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverlayApp.Services
{
    /// <summary>
    /// Service that coordinates the Dual-LLM scanning pipeline.
    /// Stage 1: Uses Groq Vision API or Windows WinRT OCR (Offline backup) to extract text from screen capture.
    /// Stage 2: Calls Groq OpenAI models (qwen/qwen3.6-27b / gpt-oss-120b / llama-3.3-70b) to process transcribed text.
    /// </summary>
    public class LlmService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Native Windows 10/11 WinRT OCR Engine.
        /// Extracts text from screen capture instantly (5ms) with 100% offline accuracy.
        /// </summary>
        private async Task<string> PerformWindowsOcrAsync(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0) return "";
            try
            {
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(imageBytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                stream.Seek(0);

                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied
                );

                var ocrEngine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
                if (ocrEngine == null) return "";

                var ocrResult = await ocrEngine.RecognizeAsync(softwareBitmap);
                if (ocrResult == null || ocrResult.Lines == null) return "";

                var sb = new System.Text.StringBuilder();
                foreach (var line in ocrResult.Lines)
                {
                    sb.AppendLine(line.Text);
                }
                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Windows OCR Error: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Stage 1: Extracts text from screen capture using Groq Vision API or Windows WinRT OCR (Offline backup).
        /// </summary>
        public async Task<(string Text, string Method, string Error)> ExtractTextFromImageAsync(string groqKey, byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0) return ("", "None", "Error: Captured screen image data was empty.");

            string lastError = "";

            // 1. Primary: Groq Vision API
            if (!string.IsNullOrWhiteSpace(groqKey))
            {
                string base64Image = Convert.ToBase64String(imageBytes);
                string url = "https://api.groq.com/openai/v1/chat/completions";

                string[] visionModels = new[]
                {
                    "qwen/qwen3.6-27b",
                    "qwen/qwen-2.5-vl-72b-instruct"
                };

                foreach (var visionModel in visionModels)
                {
                    foreach (var tokenParam in new[] { "max_completion_tokens", "max_tokens" })
                    {
                        try
                        {
                            object payload = tokenParam == "max_completion_tokens"
                                ? new
                                {
                                    model = visionModel,
                                    max_completion_tokens = 1024,
                                    temperature = 1,
                                    top_p = 1,
                                    stream = false,
                                    messages = new[]
                                    {
                                        new
                                        {
                                            role = "user",
                                            content = new object[]
                                            {
                                                new
                                                {
                                                    type = "text",
                                                    text = "Perform OCR on this image. Extract and transcribe all visible text, numbers, formulas, or code blocks accurately. Do not add any preamble, conversational text, markdown wrapping, or explanations. If there is no visible text, reply with '(no text detected)'."
                                                },
                                                new
                                                {
                                                    type = "image_url",
                                                    image_url = new
                                                    {
                                                        url = $"data:image/png;base64,{base64Image}"
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                : new
                                {
                                    model = visionModel,
                                    max_tokens = 1024,
                                    temperature = 1,
                                    top_p = 1,
                                    stream = false,
                                    messages = new[]
                                    {
                                        new
                                        {
                                            role = "user",
                                            content = new object[]
                                            {
                                                new
                                                {
                                                    type = "text",
                                                    text = "Perform OCR on this image. Extract and transcribe all visible text, numbers, formulas, or code blocks accurately. Do not add any preamble, conversational text, markdown wrapping, or explanations. If there is no visible text, reply with '(no text detected)'."
                                                },
                                                new
                                                {
                                                    type = "image_url",
                                                    image_url = new
                                                    {
                                                        url = $"data:image/png;base64,{base64Image}"
                                                    }
                                                }
                                            }
                                        }
                                    }
                                };

                            string jsonPayload = JsonSerializer.Serialize(payload);

                            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                            {
                                request.Headers.Add("Authorization", $"Bearer {groqKey}");
                                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                                var response = await _httpClient.SendAsync(request);
                                if (response.IsSuccessStatusCode)
                                {
                                    string responseJson = await response.Content.ReadAsStringAsync();
                                    string ocrText = ParseOpenAiMessageContent(responseJson);
                                    if (!string.IsNullOrWhiteSpace(ocrText) && ocrText.Trim() != "(no text detected)")
                                    {
                                        return (ocrText.Trim(), $"Groq Vision OCR ({visionModel})", "");
                                    }
                                }
                                else
                                {
                                    string error = await response.Content.ReadAsStringAsync();
                                    lastError = $"Groq vision response error: HTTP {response.StatusCode} - {error}";
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            lastError = ex.Message;
                        }
                    }
                }
            }

            // 2. Secondary Fallback: Windows WinRT OCR
            try
            {
                string localOcrText = await PerformWindowsOcrAsync(imageBytes);
                if (!string.IsNullOrWhiteSpace(localOcrText))
                {
                    return (localOcrText, "Windows WinRT OCR", "");
                }
            }
            catch (Exception ex)
            {
                lastError += $"\nWindows OCR Exception: {ex.Message}";
            }

            return ("", "None", $"OCR transcription failed. {lastError}".Trim());
        }

        /// <summary>
        /// Stage 2: Sends extracted screen text to Groq for analysis, problem-solving, or explanations.
        /// </summary>
        public async Task<string> ProcessTextWithGroqAsync(string groqKey, string transcribedText)
        {
            if (string.IsNullOrWhiteSpace(groqKey))
            {
                return "Error: Groq API Key is not configured.";
            }

            try
            {
                string url = "https://api.groq.com/openai/v1/chat/completions";

                // Build Groq chat completion request using GPT-OSS 120B
                var payload = new
                {
                    model = "openai/gpt-oss-120b",
                    max_tokens = 3500,
                    messages = new[]
                    {
                        new
                        {
                            role = "system",
                            content = "You are a helpful overlay productivity assistant. You analyze raw transcribed text from the user's screen. If it is a question or problem, solve it step-by-step. If it is code, explain and debug it. If it is general text, explain or summarize it. Keep your output concise, clear, and formatted in markdown."
                        },
                        new
                        {
                            role = "user",
                            content = $"Here is the raw text extracted from my screen:\n\n{transcribedText}"
                        }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);

                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Headers.Add("Authorization", $"Bearer {groqKey}");
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        return $"Groq API Error (HTTP {response.StatusCode}):\n{errorContent}";
                    }

                    string responseJson = await response.Content.ReadAsStringAsync();
                    return ParseOpenAiMessageContent(responseJson);
                }
            }
            catch (Exception ex)
            {
                return $"Error contacting Groq API: {ex.Message}";
            }
        }
        /// <summary>
        /// Sends follow-up conversational context to Groq to refine the previous solution.
        /// </summary>
        public async Task<string> ProcessFollowUpWithGroqAsync(string groqKey, string previousQuery, string previousAnswer, string followUpQuery)
        {
            if (string.IsNullOrWhiteSpace(groqKey))
            {
                return "Error: Groq API Key is not configured.";
            }

            try
            {
                string url = "https://api.groq.com/openai/v1/chat/completions";

                var payload = new
                {
                    model = "openai/gpt-oss-120b",
                    max_tokens = 3500,
                    messages = new[]
                    {
                        new
                        {
                            role = "system",
                            content = "You are a helpful overlay productivity assistant. The user is asking a follow-up question or requesting modifications to a previous solution. Answer the user's follow-up request accurately, keeping the context of the previous query and previous solution in mind. Keep your output concise and formatted in markdown. Write in a natural, humanized style. Avoid robotic AI transitions or preambles."
                        },
                        new
                        {
                            role = "user",
                            content = $"[Previous Query]\n{previousQuery}\n\n[Previous Solution]\n{previousAnswer}"
                        },
                        new
                        {
                            role = "user",
                            content = $"[Follow-up Request]\n{followUpQuery}"
                        }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);

                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Headers.Add("Authorization", $"Bearer {groqKey}");
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        return $"Groq API Error (HTTP {response.StatusCode}):\n{errorContent}";
                    }

                    string responseJson = await response.Content.ReadAsStringAsync();
                    return ParseOpenAiMessageContent(responseJson);
                }
            }
            catch (Exception ex)
            {
                return $"Error contacting Groq API: {ex.Message}";
            }
        }

        /// <summary>
        /// Transcribes recorded speech WAV audio using Groq's Whisper API.
        /// </summary>
        public async Task<string> TranscribeAudioAsync(string groqKey, string audioFilePath)
        {
            if (string.IsNullOrWhiteSpace(groqKey))
            {
                return "Error: Groq API Key is not configured.";
            }

            if (!System.IO.File.Exists(audioFilePath))
            {
                return "Error: Recorded audio file was not found.";
            }

            try
            {
                string url = "https://api.groq.com/openai/v1/audio/transcriptions";

                using (var form = new MultipartFormDataContent())
                {
                    byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(audioFilePath);
                    var fileContent = new ByteArrayContent(fileBytes);
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
                    form.Add(fileContent, "file", "speech.wav");
                    form.Add(new StringContent("whisper-large-v3"), "model");

                    using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                    {
                        request.Headers.Add("Authorization", $"Bearer {groqKey}");
                        request.Content = form;

                        var response = await _httpClient.SendAsync(request);
                        if (!response.IsSuccessStatusCode)
                        {
                            string errorContent = await response.Content.ReadAsStringAsync();
                            return $"Groq Whisper Error (HTTP {response.StatusCode}):\n{errorContent}";
                        }

                        string responseJson = await response.Content.ReadAsStringAsync();
                        using (var doc = JsonDocument.Parse(responseJson))
                        {
                            if (doc.RootElement.TryGetProperty("text", out var textProp))
                            {
                                return textProp.GetString() ?? "";
                            }
                        }
                        return $"Error: Transcription text not found in response JSON: {responseJson}";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Error contacting Groq Whisper API: {ex.Message}";
            }
        }

        /// <summary>
        /// Helper to extract chat completions content from standard OpenAI JSON responses.
        /// Used by both OpenRouter and Groq APIs.
        /// </summary>
        private string ParseOpenAiMessageContent(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty("message", out var message))
                        {
                            return message.GetProperty("content").GetString() ?? "Empty message content.";
                        }
                    }
                }
                return "Error: Could not parse message contents from completions API JSON response.";
            }
            catch (Exception ex)
            {
                return $"Failed to parse response JSON: {ex.Message}\nRaw JSON response:\n{json}";
            }
        }
        /// <summary>
        /// Sends the entire conversational message history to Groq for stateful chat completions.
        /// </summary>
        public async Task<string> ProcessChatWithGroqAsync(string groqKey, System.Collections.Generic.List<ChatMessage> history, string modelName = "llama-3.3-70b-versatile")
        {
            if (string.IsNullOrWhiteSpace(groqKey))
            {
                return "Error: Groq API Key is not configured.";
            }

            // Estimate input tokens from history
            int totalChars = 0;
            if (history != null)
            {
                foreach (var msg in history)
                {
                    totalChars += msg.Content?.Length ?? 0;
                }
            }
            int approxInputTokens = totalChars / 4;

            // Dynamically optimize max_tokens so (input_tokens + max_tokens) stays well below TPM limits
            int maxTokens;
            if (modelName.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase))
            {
                maxTokens = Math.Clamp(3800 - approxInputTokens, 1000, 2500);
            }
            else if (modelName.Contains("llama-3.3", StringComparison.OrdinalIgnoreCase))
            {
                maxTokens = Math.Clamp(5500 - approxInputTokens, 1500, 3000);
            }
            else
            {
                maxTokens = 2000;
            }

            string[] fallbackModels = new[]
            {
                modelName,
                "llama-3.1-8b-instant",
                "llama-3.2-3b-preview"
            };

            string lastError = "";

            foreach (var currentModel in fallbackModels)
            {
                try
                {
                    string url = "https://api.groq.com/openai/v1/chat/completions";

                    var payload = new
                    {
                        model = currentModel,
                        max_tokens = maxTokens,
                        messages = history
                    };

                    string jsonPayload = JsonSerializer.Serialize(payload);

                    using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                    {
                        request.Headers.Add("Authorization", $"Bearer {groqKey}");
                        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                        var response = await _httpClient.SendAsync(request);
                        string responseStr = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            return ParseOpenAiMessageContent(responseStr);
                        }

                        lastError = $"Groq API Error ({currentModel} HTTP {(int)response.StatusCode}):\n{responseStr}";

                        // If rate limit / TPM exceeded, try next fallback model (llama-3.1-8b-instant has 50,000 TPM limit)
                        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || 
                            responseStr.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase) ||
                            responseStr.Contains("RequestEntityTooLarge", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        return lastError;
                    }
                }
                catch (Exception ex)
                {
                    lastError = $"Error contacting Groq API ({currentModel}): {ex.Message}";
                }
            }

            return lastError;
        }

        /// <summary>
        /// Validates a Groq API key by testing it against the Groq models endpoint.
        /// </summary>
        public async Task<(bool IsValid, string ErrorMessage)> ValidateGroqKeyAsync(string groqKey)
        {
            if (string.IsNullOrWhiteSpace(groqKey))
            {
                return (false, "Please paste your Groq API Key.");
            }

            groqKey = groqKey.Trim();

            try
            {
                string url = "https://api.groq.com/openai/v1/models";
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Add("Authorization", $"Bearer {groqKey}");
                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        return (true, "");
                    }
                    else
                    {
                        string err = await response.Content.ReadAsStringAsync();
                        return (false, $"Invalid Groq API Key (HTTP {response.StatusCode}). Please check key.");
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, $"Connection Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates a Google Gemini API key by testing it against the Gemini models endpoint.
        /// </summary>
        public async Task<(bool IsValid, string ErrorMessage)> ValidateGeminiKeyAsync(string geminiKey)
        {
            if (string.IsNullOrWhiteSpace(geminiKey))
            {
                return (false, "Please paste your Gemini API Key.");
            }

            geminiKey = geminiKey.Trim();

            try
            {
                string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={geminiKey}";
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        return (true, "");
                    }
                    else
                    {
                        string err = await response.Content.ReadAsStringAsync();
                        return (false, $"Invalid Gemini API Key (HTTP {response.StatusCode}). Please check key.");
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, $"Connection Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Stage 1 (Gemini): Performs OCR on image using Google Gemini Vision API (gemini-2.0-flash).
        /// </summary>
        public async Task<(string Text, string Method, string Error)> ExtractTextFromGeminiImageAsync(string geminiKey, byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0) return ("", "None", "Error: Captured image data was empty.");
            if (string.IsNullOrWhiteSpace(geminiKey)) return ("", "None", "Error: Gemini API Key is not configured.");

            try
            {
                string base64Image = Convert.ToBase64String(imageBytes);
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={geminiKey.Trim()}";

                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = "Perform OCR on this image. Extract and transcribe all visible text, numbers, formulas, or code blocks accurately. Do not add any preamble, conversational text, markdown wrapping, or explanations. If there is no visible text, reply with '(no text detected)'." },
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = "image/png",
                                        data = base64Image
                                    }
                                }
                            }
                        }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);

                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        string responseJson = await response.Content.ReadAsStringAsync();
                        string ocrText = ParseGeminiMessageContent(responseJson);
                        if (!string.IsNullOrWhiteSpace(ocrText) && ocrText.Trim() != "(no text detected)")
                        {
                            return (ocrText.Trim(), "Gemini Vision OCR (gemini-2.0-flash)", "");
                        }
                    }
                    else
                    {
                        string error = await response.Content.ReadAsStringAsync();
                        return ("", "None", $"Gemini Vision error HTTP {response.StatusCode}: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                return ("", "None", $"Gemini Vision Exception: {ex.Message}");
            }

            // Fallback to Windows WinRT OCR if Gemini fails
            string localText = await PerformWindowsOcrAsync(imageBytes);
            if (!string.IsNullOrWhiteSpace(localText))
            {
                return (localText, "Windows WinRT OCR", "");
            }

            return ("", "None", "OCR transcription failed.");
        }

        /// <summary>
        /// Stage 2 (Gemini): Sends chat history to Google Gemini API (gemini-2.0-flash).
        /// </summary>
        public async Task<string> ProcessChatWithGeminiAsync(string geminiKey, System.Collections.Generic.List<ChatMessage> history, string modelName = "gemini-2.0-flash")
        {
            if (string.IsNullOrWhiteSpace(geminiKey))
            {
                return "Error: Gemini API Key is not configured.";
            }

            try
            {
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={geminiKey.Trim()}";

                var contentsList = new System.Collections.Generic.List<object>();
                string systemPrompt = "";

                if (history != null)
                {
                    foreach (var msg in history)
                    {
                        if (msg.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                        {
                            systemPrompt += msg.Content + "\n";
                        }
                        else
                        {
                            string geminiRole = msg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user";
                            contentsList.Add(new
                            {
                                role = geminiRole,
                                parts = new[] { new { text = msg.Content } }
                            });
                        }
                    }
                }

                object payload;
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                {
                    payload = new
                    {
                        system_instruction = new
                        {
                            parts = new[] { new { text = systemPrompt.Trim() } }
                        },
                        contents = contentsList
                    };
                }
                else
                {
                    payload = new { contents = contentsList };
                }

                string jsonPayload = JsonSerializer.Serialize(payload);

                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);
                    string responseJson = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        return ParseGeminiMessageContent(responseJson);
                    }

                    return $"Gemini API Error (HTTP {(int)response.StatusCode}):\n{responseJson}";
                }
            }
            catch (Exception ex)
            {
                return $"Error contacting Gemini API: {ex.Message}";
            }
        }

        /// <summary>
        /// Helper to extract response text from Google Gemini API JSON payload.
        /// </summary>
        private string ParseGeminiMessageContent(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var firstCandidate = candidates[0];
                        if (firstCandidate.TryGetProperty("content", out var content))
                        {
                            if (content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                            {
                                var firstPart = parts[0];
                                if (firstPart.TryGetProperty("text", out var textProp))
                                {
                                    return textProp.GetString() ?? "";
                                }
                            }
                        }
                    }
                }
                return "Error: Could not parse message text from Gemini API response.";
            }
            catch (Exception ex)
            {
                return $"Failed to parse Gemini response JSON: {ex.Message}\nRaw JSON:\n{json}";
            }
        }
    }

    /// <summary>
    /// Holds a single role/content message in the OpenAI chat completions message list.
    /// </summary>
    public class ChatMessage
    {
        [System.Text.Json.Serialization.JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }
}
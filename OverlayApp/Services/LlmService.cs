using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverlayApp.Services
{
    /// <summary>
    /// Service that coordinates the Dual-LLM scanning pipeline.
    /// Stage 1: Calls OpenRouter (amazon/nova-2-lite-v1:free) to extract text via OCR from the screen capture.
    /// Stage 2: Calls Groq (llama-3.1-8b-instant) to process the extracted text (solve, explain, summarize).
    /// </summary>
    public class LlmService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Stage 1: Uploads the base64 screen capture to OpenRouter to perform OCR/text extraction.
        /// </summary>
        public async Task<string> ExtractTextFromImageAsync(string groqKey, byte[] imageBytes)
        {
            if (string.IsNullOrWhiteSpace(groqKey))
            {
                return "Error: Groq API Key is not configured.";
            }

            try
            {
                string base64Image = Convert.ToBase64String(imageBytes);
                string url = "https://api.groq.com/openai/v1/chat/completions";

                // Build Groq multimodal payload using Llama 4 Scout
                var payload = new
                {
                    model = "meta-llama/llama-4-scout-17b-16e-instruct",
                    max_tokens = 1000,
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
                    if (!response.IsSuccessStatusCode)
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        return $"Groq OCR Error (HTTP {response.StatusCode}):\n{errorContent}";
                    }

                    string responseJson = await response.Content.ReadAsStringAsync();
                    return ParseOpenAiMessageContent(responseJson);
                }
            }
            catch (Exception ex)
            {
                return $"Error contacting Groq OCR API: {ex.Message}";
            }
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

                // Build Groq chat completion request using GPT-OSS 20B
                var payload = new
                {
                    model = "openai/gpt-oss-20b",
                    max_tokens = 1500,
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
                    model = "openai/gpt-oss-20b",
                    max_tokens = 1500,
                    messages = new[]
                    {
                        new
                        {
                            role = "system",
                            content = "You are a helpful overlay productivity assistant. The user is asking a follow-up question or requesting modifications to a previous solution. Answer the user's follow-up request accurately, keeping the context of the previous query and previous solution in mind. Keep your output concise and formatted in markdown."
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
        public async Task<string> ProcessChatWithGroqAsync(string groqKey, System.Collections.Generic.List<ChatMessage> history)
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
                    model = "openai/gpt-oss-20b",
                    max_tokens = 1500,
                    messages = history
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

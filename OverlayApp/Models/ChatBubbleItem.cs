using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace OverlayApp.Models
{
    /// <summary>
    /// Represents a single chat bubble in the ChatGPT/Gemini-style conversation UI.
    /// Each bubble is either a "user" message (showing captured screenshots) or an "assistant" message (showing AI response).
    /// </summary>
    public class ChatBubbleItem : INotifyPropertyChanged
    {
        private string _role = "";
        private string _content = "";
        private int _turnNumber;
        private bool _isLoading;
        private string _modelInfo = "";

        /// <summary>
        /// "user" or "assistant"
        /// </summary>
        public string Role
        {
            get => _role;
            set { if (_role != value) { _role = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsUser)); OnPropertyChanged(nameof(IsAssistant)); } }
        }

        /// <summary>
        /// The text content of this bubble (markdown for assistant, summary for user).
        /// </summary>
        public string Content
        {
            get => _content;
            set { if (_content != value) { _content = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Turn number in the conversation (1, 2, 3, ...).
        /// </summary>
        public int TurnNumber
        {
            get => _turnNumber;
            set { if (_turnNumber != value) { _turnNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(TurnLabel)); } }
        }

        /// <summary>
        /// Screenshot preview images for user bubbles.
        /// </summary>
        public List<ImageSource> ScreenshotPreviews { get; set; } = new List<ImageSource>();

        /// <summary>
        /// Number of screenshots in this user turn.
        /// </summary>
        public int ScreenshotCount => ScreenshotPreviews?.Count ?? 0;

        /// <summary>
        /// Whether this bubble is still waiting for a response (shows loading animation).
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set { if (_isLoading != value) { _isLoading = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Model info badge text (e.g., "Gemini 2.0 Flash + GPT-OSS").
        /// </summary>
        public string ModelInfo
        {
            get => _modelInfo;
            set { if (_modelInfo != value) { _modelInfo = value; OnPropertyChanged(); } }
        }

        // Convenience properties for XAML binding
        public bool IsUser => Role == "user";
        public bool IsAssistant => Role == "assistant";
        public string TurnLabel => $"Turn #{TurnNumber}";

        public bool HasScreenshots => ScreenshotPreviews != null && ScreenshotPreviews.Count > 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace OverlayApp.Models
{
    /// <summary>
    /// Represents a captured screen region item in the batch screenshot queue.
    /// </summary>
    public class CapturedScreenshotItem : INotifyPropertyChanged
    {
        private int _index;
        public int Index
        {
            get => _index;
            set
            {
                if (_index != value)
                {
                    _index = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public ImageSource PreviewImage { get; set; } = null!;
        public byte[] ImageBytes { get; set; } = Array.Empty<byte>();

        public string DisplayName => $"Screenshot {Index}";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

using System;
using System.Windows.Threading;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using OverlayApp.Models;
using OverlayApp.Services;
using OverlayApp.Helpers;

namespace OverlayApp.ViewModels
{
    /// <summary>
    /// The primary ViewModel of the overlay application, controlling themes, active widgets,
    /// settings properties, stopwatch states, and CPU/RAM usage notifications.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly WidgetSettings _settings;
        private readonly SystemMonitorService _monitorService;
        private readonly HotkeyService _hotkeyService;
        private readonly WindowStyleService _styleService;

        private bool _isSettingsOpen;
        private double _cpuUsage;
        private double _memoryUsage;

        // Stopwatch Timer Fields
        private readonly DispatcherTimer _stopwatchTimer;
        private TimeSpan _elapsedTime;
        private DateTime _timerStartTime;
        private bool _isTimerRunning;
        private string _timerDisplay = "00:00.0";

        // AI Scan Fields
        private readonly LlmService _llmService = new LlmService();
        private bool _isScanning;
        private System.Windows.Media.ImageSource? _capturedPreview;

        // Voice Scan Fields
        private readonly AudioRecorderService _audioRecorder = new AudioRecorderService();
        private bool _isRecording;
        private bool _isProcessingVoice;

        private readonly System.Collections.Generic.List<ChatMessage> _voiceChatHistory = new System.Collections.Generic.List<ChatMessage>();
        private readonly System.Collections.Generic.List<ChatMessage> _txtChatHistory = new System.Collections.Generic.List<ChatMessage>();
        private string _followUpText = "";
        private bool _isFollowUpRecording;
        private bool _wasLiveScanActiveBeforeFollowUp;

        // Authentication & Session Fields
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly DispatcherTimer _sessionTimer;
        
        private string _sessionTimerDisplay = "Please log in";
        private bool _isAdmin;
        private bool _isTrialActive;
        private bool _isPaidActive;
        private DateTime? _trialEndsAt;
        private DateTime? _paidUntil;
        private bool _isSessionActive;
        private string _systemGroqKey = "";
        
        private bool _isLoginOverlayVisible = true;
        private bool _isPaymentOverlayVisible = false;
        private bool _isPaymentCreditAvailable = false;
        private string _paymentQrUrl = "";
        
        private string _loginEmail = "";
        private string _loginPassword = "";
        private string _authErrorMessage = "";
        private bool _isAuthLoading;
        
        private string _paymentUtr = "";
        private string _paymentErrorMessage = "";
        private bool _isPaymentLoading;

        // Commands
        public ICommand ToggleSettingsCommand { get; }
        public ICommand ToggleClickThroughCommand { get; }
        public ICommand SelectWidgetCommand { get; }
        public ICommand ChangeThemeCommand { get; }
        public ICommand TimerStartPauseCommand { get; }
        public ICommand TimerResetCommand { get; }
        public ICommand CloseAppCommand { get; }
        public ICommand StartScanCommand { get; }
        public ICommand ToggleVoiceCommand { get; }
        public ICommand ClearTxtScanCommand { get; }
        public ICommand ClearVoiceScanCommand { get; }
        public ICommand SubmitFollowUpCommand { get; }
        public ICommand ToggleFollowUpVoiceCommand { get; }
        public ICommand NextOnboardingCommand { get; }
        public ICommand BackOnboardingCommand { get; }
        public ICommand SkipOnboardingCommand { get; }
        public ICommand FinishOnboardingCommand { get; }

        // Copy Commands
        public ICommand CopyTxtCommand { get; }
        public ICommand CopyVoiceCommand { get; }

        // Preset Follow-ups
        public System.Collections.ObjectModel.ObservableCollection<string> PresetFollowUps { get; } = new System.Collections.ObjectModel.ObservableCollection<string>();
        public ICommand AskFollowUpCommand { get; }

        // Authentication Commands
        public ICommand LoginCommand { get; }
        public ICommand SignupCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand SubmitPaymentCommand { get; }
        public ICommand StartPaidSessionCommand { get; }
        public ICommand RefreshSessionStatusCommand { get; }

        // Groq Key Validation & Free Trial Commands
        public ICommand ValidateGroqKeyCommand { get; }
        public ICommand OpenGroqConsoleCommand { get; }
        public ICommand StartFreeTrialCommand { get; }

        private readonly SettingsService _settingsService;

        public MainViewModel(
            SystemMonitorService monitorService,
            HotkeyService hotkeyService,
            WindowStyleService styleService)
        {
            _settingsService = new SettingsService();
            _settings = _settingsService.LoadSettings();

            // Migrate old Vercel URL instances to the new production server
            if (string.IsNullOrEmpty(_settings.ApiBaseUrl) || 
                _settings.ApiBaseUrl.Contains("shadow-ai-1vjz-six.vercel.app"))
            {
                _settings.ApiBaseUrl = "https://shadow-ai-iota.vercel.app";
            }
            
            // Always start scan outputs empty, bypassing settings load persistence
            _settings.ScanResponseText = "";
            _settings.VoiceScanResponseText = "";
            _monitorService = monitorService;
            _hotkeyService = hotkeyService;
            _styleService = styleService;

            GroqInputKey = _settings.GroqKey;
            ValidateGroqKeyCommand = new RelayCommand(async _ => await ValidateGroqKeyAsync());
            OpenGroqConsoleCommand = new RelayCommand(_ => OpenGroqConsole());
            StartFreeTrialCommand = new RelayCommand(_ => StartFreeTrial());
            AskFollowUpCommand = new RelayCommand(param => AskFollowUp(param as string));

            // Initialize presets based on default scan type
            UpdatePresetFollowUps();
            UpdateOverlayVisibilities();

            // Initialize ICommands
            ToggleSettingsCommand = new RelayCommand(_ => IsSettingsOpen = !IsSettingsOpen);
            
            ToggleClickThroughCommand = new RelayCommand(_ => IsClickThrough = !IsClickThrough);
            
            SelectWidgetCommand = new RelayCommand(param =>
            {
                if (param is WidgetType type)
                {
                    ActiveWidget = type;
                }
                else if (param is string str && Enum.TryParse(str, out WidgetType parsedType))
                {
                    ActiveWidget = parsedType;
                }
            });
            
            ChangeThemeCommand = new RelayCommand(param =>
            {
                if (param is string themeName)
                {
                    Theme = themeName;
                }
            });

            TimerStartPauseCommand = new RelayCommand(_ => ToggleTimer());
            TimerResetCommand = new RelayCommand(_ => ResetTimer());
            CloseAppCommand = new RelayCommand(_ => System.Windows.Application.Current.Shutdown());
            StartScanCommand = new RelayCommand(_ => TriggerScreenScan());
            ToggleVoiceCommand = new RelayCommand(_ => ToggleVoiceRecording());
            ClearTxtScanCommand = new RelayCommand(_ => { 
                ScanResponseText = ""; 
                CapturedPreview = null; 
                _txtChatHistory.Clear();
                OnPropertyChanged(nameof(IsFollowUpVisible));
            });
            ClearVoiceScanCommand = new RelayCommand(_ => { 
                VoiceScanResponseText = "";
                _voiceChatHistory.Clear();
                FollowUpText = "";
                OnPropertyChanged(nameof(IsFollowUpVisible));
            });

            SubmitFollowUpCommand = new RelayCommand(_ => SubmitFollowUpPrompt());
            ToggleFollowUpVoiceCommand = new RelayCommand(_ => ToggleFollowUpVoiceRecording());

            CopyTxtCommand = new RelayCommand(_ => { 
                if (!string.IsNullOrEmpty(ScanResponseText)) 
                    System.Windows.Clipboard.SetText(ScanResponseText); 
            });
            CopyVoiceCommand = new RelayCommand(_ => { 
                if (!string.IsNullOrEmpty(VoiceScanResponseText)) 
                    System.Windows.Clipboard.SetText(VoiceScanResponseText); 
            });

            NextOnboardingCommand = new RelayCommand(_ =>
            {
                if (CurrentOnboardingSlide < 3)
                {
                    CurrentOnboardingSlide++;
                }
                else
                {
                    IsFirstRun = false;
                }
            });
            BackOnboardingCommand = new RelayCommand(_ =>
            {
                if (CurrentOnboardingSlide > 0)
                {
                    CurrentOnboardingSlide--;
                }
            });
            SkipOnboardingCommand = new RelayCommand(_ => IsFirstRun = false);
            FinishOnboardingCommand = new RelayCommand(_ => IsFirstRun = false);

            // Wire up System Metrics Update
            _monitorService.MetricsUpdated += (cpu, ram) =>
            {
                CpuUsage = cpu;
                MemoryUsage = ram;
            };

            // Register Hotkey Hook callbacks
            _hotkeyService.HotkeyPressed += (id) =>
            {
                switch (id)
                {
                    case Services.HotkeyService.HOTKEY_SCAN_ID:
                        TriggerSilentScan();
                        break;
                    case Services.HotkeyService.HOTKEY_COPY_ID:
                        if (!string.IsNullOrEmpty(ScanResponseText))
                        {
                            System.Windows.Clipboard.SetText(ScanResponseText);
                        }
                        break;
                    case Services.HotkeyService.HOTKEY_CLEAR_ID:
                        ScanResponseText = "";
                        CapturedPreview = null;
                        _txtChatHistory.Clear();
                        OnPropertyChanged(nameof(IsFollowUpVisible));
                        break;
                }
            };

            // Set up Stopwatch stopwatch update interval
            _stopwatchTimer = new DispatcherTimer();
            _stopwatchTimer.Interval = TimeSpan.FromMilliseconds(100);
            _stopwatchTimer.Tick += StopwatchTimer_Tick;

            // Read local API base URL override if exists
            try
            {
                string localFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "api_url.txt");
                if (System.IO.File.Exists(localFile))
                {
                    string content = System.IO.File.ReadAllText(localFile).Trim();
                    if (!string.IsNullOrEmpty(content))
                    {
                        ApiBaseUrl = content;
                    }
                }
            }
            catch {}

            // Set up Session countdown & sync timer (runs every 1 second)
            _sessionTimer = new DispatcherTimer();
            _sessionTimer.Interval = TimeSpan.FromSeconds(1);
            _sessionTimer.Tick += SessionTimer_Tick;
            _sessionTimer.Start();

            // Setup new auth & session commands
            LoginCommand = new RelayCommand(async _ => await ExecuteLoginAsync());
            SignupCommand = new RelayCommand(async _ => await ExecuteSignupAsync());
            LogoutCommand = new RelayCommand(_ => ExecuteLogout());
            SubmitPaymentCommand = new RelayCommand(async _ => await ExecuteSubmitPaymentAsync());
            StartPaidSessionCommand = new RelayCommand(async _ => await ExecuteStartPaidSessionAsync());
            RefreshSessionStatusCommand = new RelayCommand(async _ => await CheckSessionStatusAsync(true));

            // Run initial check if we have a saved token
            if (IsLoggedIn)
            {
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () => {
                    await CheckSessionStatusAsync(false);
                }));
            }
            else
            {
                UpdateOverlayVisibilities();
            }

            // Auto-save settings on change
            this.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Theme) ||
                    e.PropertyName == nameof(WindowOpacity) ||
                    e.PropertyName == nameof(AlwaysOnTop) ||
                    e.PropertyName == nameof(IsClickThrough) ||
                    e.PropertyName == nameof(IsLocked) ||
                    e.PropertyName == nameof(NotesText) ||
                    e.PropertyName == nameof(GroqKey) ||
                    e.PropertyName == nameof(AppFontSize) ||
                    e.PropertyName == nameof(IsFirstRun) ||
                    e.PropertyName == nameof(IsSystemAudioSource) ||
                    e.PropertyName == nameof(IsLiveMode) ||
                    e.PropertyName == nameof(IsMcqScanMode) ||
                    e.PropertyName == nameof(IsCodingScanMode) ||
                    e.PropertyName == nameof(IsNormalScanMode) ||
                    e.PropertyName == nameof(SessionToken) ||
                    e.PropertyName == nameof(UserEmail) ||
                    e.PropertyName == nameof(ApiBaseUrl))
                {
                    _settingsService.SaveSettings(_settings);
                }
            };
        }

        /// <summary>
        /// Registers window-specific handlers and applies default configurations.
        /// Called from Window code-behind after the window completes source initialization.
        /// </summary>
        public void InitializeServices(System.Windows.Window window)
        {
            _styleService.Initialize(window);
            _hotkeyService.Register(window);

            // Apply default configurations
            _styleService.SetOpacity(_settings.Opacity);
            _styleService.SetAlwaysOnTop(_settings.AlwaysOnTop);
            _styleService.SetClickThrough(_settings.IsClickThrough);

            // Start hardware query service if monitor widget is initially selected
            if (ActiveWidget == WidgetType.SystemMonitor)
            {
                _monitorService.Start();
            }

            // Re-apply modal overlay state now that _styleService has a valid window reference.
            // The constructor's SyncStealthForModalOverlays() call had no effect because
            // _targetWindow was null at that point. This ensures Login/Groq overlays
            // properly disable stealth and activate the window for keyboard input.
            SyncStealthForModalOverlays();
        }

        public void Cleanup()
        {
            _monitorService.Stop();
            _hotkeyService.Unregister();
            _stopwatchTimer.Stop();
            try
            {
                _audioRecorder.StopRecording();
            }
            catch {}
        }

        #region Bound Properties

        public double WindowOpacity
        {
            get => _settings.Opacity;
            set
            {
                if (SetProperty(ref _settings.Opacity, value))
                {
                    _styleService.SetOpacity(value);
                }
            }
        }

        public bool AlwaysOnTop
        {
            get => _settings.AlwaysOnTop;
            set
            {
                if (SetProperty(ref _settings.AlwaysOnTop, value))
                {
                    _styleService.SetAlwaysOnTop(value);
                }
            }
        }

        public bool IsClickThrough
        {
            get => _settings.IsClickThrough;
            set
            {
                if (SetProperty(ref _settings.IsClickThrough, value))
                {
                    _styleService.SetClickThrough(value);
                    // Stealth mode stays ON always — never disable it when toggling click-through
                    
                    // Close settings panel when activating click-through for UI clarity
                    if (value)
                    {
                        IsSettingsOpen = false;
                    }
                }
            }
        }

        public bool IsLocked
        {
            get => _settings.IsLocked;
            set => SetProperty(ref _settings.IsLocked, value);
        }

        public WidgetType ActiveWidget
        {
            get => _settings.ActiveWidget;
            set
            {
                // Map legacy AiScan to TxtScan
                if (value == WidgetType.AiScan) value = WidgetType.TxtScan;

                if (SetProperty(ref _settings.ActiveWidget, value))
                {
                    OnPropertyChanged(nameof(IsNotesActive));
                    OnPropertyChanged(nameof(IsSystemActive));
                    OnPropertyChanged(nameof(IsTimerActive));
                    OnPropertyChanged(nameof(IsTxtScanActive));
                    OnPropertyChanged(nameof(IsVoiceScanActive));
                    OnPropertyChanged(nameof(IsProfileActive));
                    OnPropertyChanged(nameof(IsFollowUpVisible));

                    // Manage performance statistics updates (avoid querying background stats when hidden)
                    if (value == WidgetType.SystemMonitor)
                    {
                        _monitorService.Start();
                    }
                    else
                    {
                        _monitorService.Stop();
                    }

                    // Release recording device immediately if user leaves Voice tab
                    if (value != WidgetType.VoiceScan)
                    {
                        try
                        {
                            _audioRecorder.SilenceDetected -= OnLiveSilenceDetected;
                            _audioRecorder.StopRecording();
                            IsRecording = false;
                        }
                        catch {}
                    }
                }
            }
        }

        public bool IsNotesActive => ActiveWidget == WidgetType.Notes;
        public bool IsSystemActive => ActiveWidget == WidgetType.SystemMonitor;
        public bool IsTimerActive => ActiveWidget == WidgetType.Timer;
        public bool IsAiScanActive => ActiveWidget == WidgetType.TxtScan || ActiveWidget == WidgetType.VoiceScan;
        public bool IsTxtScanActive => ActiveWidget == WidgetType.TxtScan;
        public bool IsVoiceScanActive => ActiveWidget == WidgetType.VoiceScan;
        public bool IsProfileActive => ActiveWidget == WidgetType.Profile;

        public string ProfileName
        {
            get
            {
                if (string.IsNullOrEmpty(UserEmail)) return "User";
                int index = UserEmail.IndexOf('@');
                if (index > 0)
                {
                    return UserEmail.Substring(0, index);
                }
                return UserEmail;
            }
        }

        public string MaskedGroqKey
        {
            get
            {
                if (string.IsNullOrEmpty(GroqKey)) return "Not Configured";
                if (GroqKey.Length <= 10) return "****";
                return GroqKey.Substring(0, 7) + "..." + GroqKey.Substring(GroqKey.Length - 4);
            }
        }

        public string GroqKey
        {
            get => _settings.GroqKey;
            set
            {
                if (SetProperty(ref _settings.GroqKey, value))
                {
                    OnPropertyChanged(nameof(MaskedGroqKey));
                }
            }
        }

        public string ScanResponseText
        {
            get => _settings.ScanResponseText;
            set => SetProperty(ref _settings.ScanResponseText, value);
        }

        public string VoiceScanResponseText
        {
            get => _settings.VoiceScanResponseText;
            set => SetProperty(ref _settings.VoiceScanResponseText, value);
        }

        public bool IsScanning
        {
            get => _isScanning;
            set => SetProperty(ref _isScanning, value);
        }

        public System.Windows.Media.ImageSource? CapturedPreview
        {
            get => _capturedPreview;
            set => SetProperty(ref _capturedPreview, value);
        }

        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                if (SetProperty(ref _isRecording, value))
                {
                    OnPropertyChanged(nameof(VoiceBtnText));
                }
            }
        }

        public string VoiceBtnText
        {
            get
            {
                if (IsLiveMode)
                {
                    return IsRecording ? "STOP LIVE SCAN" : "START LIVE SCAN";
                }
                return IsRecording ? "STOP RECORDING" : "RECORD VOICE SCAN";
            }
        }

        public bool IsSystemAudioSource
        {
            get => _settings.IsSystemAudioSource;
            set
            {
                if (SetProperty(ref _settings.IsSystemAudioSource, value))
                {
                    OnPropertyChanged(nameof(IsMicrophoneSource));
                    if (IsRecording) RestartRecordingWithCurrentSettings();
                }
            }
        }

        public bool IsMicrophoneSource
        {
            get => !IsSystemAudioSource;
            set => IsSystemAudioSource = !value;
        }

        public bool IsLiveMode
        {
            get => _settings.IsLiveMode;
            set
            {
                if (SetProperty(ref _settings.IsLiveMode, value))
                {
                    OnPropertyChanged(nameof(IsManualMode));
                    OnPropertyChanged(nameof(VoiceBtnText));
                    if (IsRecording) RestartRecordingWithCurrentSettings();
                }
            }
        }

        public bool IsManualMode
        {
            get => !IsLiveMode;
            set => IsLiveMode = !value;
        }

        public bool IsMcqScanMode
        {
            get => _settings.TextScanType == "MCQ";
            set
            {
                if (value && _settings.TextScanType != "MCQ")
                {
                    _settings.TextScanType = "MCQ";
                    OnPropertyChanged(nameof(IsMcqScanMode));
                    OnPropertyChanged(nameof(IsCodingScanMode));
                    OnPropertyChanged(nameof(IsNormalScanMode));
                    UpdatePresetFollowUps();
                }
            }
        }

        public bool IsCodingScanMode
        {
            get => _settings.TextScanType == "Coding";
            set
            {
                if (value && _settings.TextScanType != "Coding")
                {
                    _settings.TextScanType = "Coding";
                    OnPropertyChanged(nameof(IsMcqScanMode));
                    OnPropertyChanged(nameof(IsCodingScanMode));
                    OnPropertyChanged(nameof(IsNormalScanMode));
                    UpdatePresetFollowUps();
                }
            }
        }

        public bool IsNormalScanMode
        {
            get => _settings.TextScanType == "Normal";
            set
            {
                if (value && _settings.TextScanType != "Normal")
                {
                    _settings.TextScanType = "Normal";
                    OnPropertyChanged(nameof(IsMcqScanMode));
                    OnPropertyChanged(nameof(IsCodingScanMode));
                    OnPropertyChanged(nameof(IsNormalScanMode));
                    UpdatePresetFollowUps();
                }
            }
        }

        public string FollowUpText
        {
            get => _followUpText;
            set => SetProperty(ref _followUpText, value);
        }

        public bool IsFollowUpRecording
        {
            get => _isFollowUpRecording;
            set
            {
                if (SetProperty(ref _isFollowUpRecording, value))
                {
                    OnPropertyChanged(nameof(FollowUpMicColor));
                }
            }
        }

        public string FollowUpMicColor => _isFollowUpRecording ? "#FFFF453A" : "#88FFFFFF";

        public bool IsFollowUpVisible
        {
            get
            {
                if (ActiveWidget == WidgetType.TxtScan)
                {
                    return _txtChatHistory.Count > 1;
                }
                return _voiceChatHistory.Count > 1;
            }
        }

        public string Theme
        {
            get => _settings.Theme;
            set => SetProperty(ref _settings.Theme, value);
        }

        public string NotesText
        {
            get => _settings.NotesText;
            set => SetProperty(ref _settings.NotesText, value);
        }

        public double AppFontSize
        {
            get => _settings.FontSize;
            set => SetProperty(ref _settings.FontSize, value);
        }

        public bool IsFirstRun
        {
            get => _settings.IsFirstRun;
            set
            {
                if (SetProperty(ref _settings.IsFirstRun, value))
                {
                    OnPropertyChanged(nameof(IsNotesActive));
                    OnPropertyChanged(nameof(IsSystemActive));
                    OnPropertyChanged(nameof(IsTimerActive));
                    OnPropertyChanged(nameof(IsTxtScanActive));
                    OnPropertyChanged(nameof(IsVoiceScanActive));
                }
            }
        }

        private int _currentOnboardingSlide = 0;
        public int CurrentOnboardingSlide
        {
            get => _currentOnboardingSlide;
            set
            {
                if (SetProperty(ref _currentOnboardingSlide, value))
                {
                    OnPropertyChanged(nameof(IsSlide0Active));
                    OnPropertyChanged(nameof(IsSlide1Active));
                    OnPropertyChanged(nameof(IsSlide2Active));
                    OnPropertyChanged(nameof(IsSlide3Active));
                }
            }
        }

        public bool IsSlide0Active => _currentOnboardingSlide == 0;
        public bool IsSlide1Active => _currentOnboardingSlide == 1;
        public bool IsSlide2Active => _currentOnboardingSlide == 2;
        public bool IsSlide3Active => _currentOnboardingSlide == 3;

        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set
            {
                if (SetProperty(ref _isSettingsOpen, value))
                {
                    if (!value)
                    {
                        // Save current Groq Key to database persistently in background when settings drawer closes
                        Task.Run(async () => await SaveGroqKeyToServerAsync(GroqKey));
                    }
                }
            }
        }

        public double CpuUsage
        {
            get => _cpuUsage;
            set => SetProperty(ref _cpuUsage, value);
        }

        public double MemoryUsage
        {
            get => _memoryUsage;
            set => SetProperty(ref _memoryUsage, value);
        }

        public bool IsTimerRunning
        {
            get => _isTimerRunning;
            private set => SetProperty(ref _isTimerRunning, value);
        }

        public string TimerDisplay
        {
            get => _timerDisplay;
            private set => SetProperty(ref _timerDisplay, value);
        }

        public bool IsAdmin
        {
            get => _isAdmin;
            set
            {
                if (SetProperty(ref _isAdmin, value))
                {
                    OnPropertyChanged(nameof(IsPaymentOverlayVisible));
                    OnPropertyChanged(nameof(IsLoginOverlayVisible));
                }
            }
        }

        #endregion

        #region Timer Core Logic

        private void ToggleTimer()
        {
            if (IsTimerRunning)
            {
                _stopwatchTimer.Stop();
                IsTimerRunning = false;
            }
            else
            {
                _timerStartTime = DateTime.Now - _elapsedTime;
                _stopwatchTimer.Start();
                IsTimerRunning = true;
            }
        }

        private void ResetTimer()
        {
            _stopwatchTimer.Stop();
            _elapsedTime = TimeSpan.Zero;
            IsTimerRunning = false;
            UpdateTimerDisplay();
        }

        private void StopwatchTimer_Tick(object? sender, EventArgs e)
        {
            _elapsedTime = DateTime.Now - _timerStartTime;
            UpdateTimerDisplay();
        }

        private void UpdateTimerDisplay()
        {
            // Format mm:ss.f
            TimerDisplay = $"{((int)_elapsedTime.TotalMinutes):D2}:{_elapsedTime.Seconds:D2}.{_elapsedTime.Milliseconds / 100:D1}";
        }

        #endregion

        #region AI Scan Core Logic

        private System.Windows.Int32Rect _lastSelectedRect = System.Windows.Int32Rect.Empty;

        private void TriggerScreenScan()
        {
            if (IsLoginOverlayVisible || IsPaymentOverlayVisible || IsFeatureLocked)
            {
                return;
            }

            var selectionWindow = new Views.SelectionWindow();
            selectionWindow.ShowActivated = false;
            selectionWindow.AreaSelected = async rect =>
            {
                _lastSelectedRect = rect;
                await ExecuteScanWithRectAsync(rect);
            };

            selectionWindow.Show();
        }

        private async void TriggerSilentScan()
        {
            if (IsLoginOverlayVisible || IsPaymentOverlayVisible || IsFeatureLocked)
            {
                return;
            }

            System.Windows.Int32Rect rectToScan;
            if (_lastSelectedRect.Width > 0 && _lastSelectedRect.Height > 0)
            {
                rectToScan = _lastSelectedRect;
            }
            else
            {
                // Capture primary screen completely in physical coordinates
                double scaleX = 1.0;
                double scaleY = 1.0;
                if (System.Windows.Application.Current.MainWindow != null)
                {
                    var source = System.Windows.PresentationSource.FromVisual(System.Windows.Application.Current.MainWindow);
                    if (source?.CompositionTarget != null)
                    {
                        scaleX = source.CompositionTarget.TransformToDevice.M11;
                        scaleY = source.CompositionTarget.TransformToDevice.M22;
                    }
                }
                int w = (int)Math.Round(System.Windows.SystemParameters.PrimaryScreenWidth * scaleX);
                int h = (int)Math.Round(System.Windows.SystemParameters.PrimaryScreenHeight * scaleY);
                rectToScan = new System.Windows.Int32Rect(0, 0, w, h);
            }

            await ExecuteScanWithRectAsync(rectToScan);
        }

        private async Task ExecuteScanWithRectAsync(System.Windows.Int32Rect rect)
        {
            IsScanning = true;
            ScanResponseText = "[LLM 1] Extracting text from screen area...";
            
            try
            {
                byte[] imageBytes;
                var previewSource = CaptureScreenArea(rect, out imageBytes);
                CapturedPreview = previewSource;

                if (imageBytes != null && imageBytes.Length > 0)
                {
                    string effectiveGroqKey = string.IsNullOrWhiteSpace(GroqKey) ? SystemGroqKey : GroqKey;

                    // Send cropped screenshot to Groq Vision
                    var ocrResult = await _llmService.ExtractTextFromImageAsync(effectiveGroqKey, imageBytes);
                    
                    bool hasText = !string.IsNullOrWhiteSpace(ocrResult.Text) && ocrResult.Text.Trim() != "(no text detected)";
                    string textExtractedStatus = hasText ? $"Yes ({ocrResult.Text.Length} characters)" : "No";

                    // Build scan details metadata header
                    string metadataHeader = $"**🔍 Scan Meta Information**\n" +
                                            $"* **OCR Method:** {ocrResult.Method}\n" +
                                            $"* **Text Extracted:** {textExtractedStatus}\n";

                    if (!string.IsNullOrWhiteSpace(ocrResult.Error))
                    {
                        metadataHeader += $"* **Error:** {ocrResult.Error}\n";
                    }
                    metadataHeader += "\n";

                    if (!hasText)
                    {
                        ScanResponseText = metadataHeader + "⚠️ No text detected in the captured area. Please try scanning again.";
                        return;
                    }

                    // Send extracted text to Groq for explanation/solving
                    string singleModel = "openai/gpt-oss-120b";
                    
                    _txtChatHistory.Clear();
                    if (IsMcqScanMode)
                    {
                        _txtChatHistory.Add(new ChatMessage {
                            Role = "system",
                            Content = "You are a strict multiple-choice question solver. Your task is to analyze the multiple-choice question (MCQ) for aptitude, reasoning, or technical content, and output ONLY the correct option letter (e.g., A, B, C, or D) or the exact correct answer choice. Do not provide any explanation, working out, preamble, or conversational text. Return only the single character or short answer choice."
                        });
                        _txtChatHistory.Add(new ChatMessage {
                            Role = "user",
                            Content = $"Here is the raw text from a multiple-choice question:\n\n{ocrResult.Text}"
                        });

                        string modelA = "openai/gpt-oss-120b";
                        string modelB = "llama-3.3-70b-versatile";

                        ScanResponseText = metadataHeader + $"[LLM] Verifying MCQ answer with dual models ({modelA} and {modelB})...";

                        // Run dual models concurrently to verify answers
                        var taskA = _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, _txtChatHistory, modelA);
                        var taskB = _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, _txtChatHistory, modelB);

                        await Task.WhenAll(taskA, taskB);
                        string answerA = await taskA;
                        string answerB = await taskB;

                        string cleanA = CleanMcqResponse(answerA);
                        string cleanB = CleanMcqResponse(answerB);
                        bool isMatch = !string.IsNullOrEmpty(cleanA) && !string.IsNullOrEmpty(cleanB) && cleanA == cleanB;

                        var sbVerify = new System.Text.StringBuilder();
                        sbVerify.AppendLine(metadataHeader);
                        sbVerify.AppendLine("### 🤖 MCQ Double-Model Verification");
                        sbVerify.AppendLine();
                        sbVerify.AppendLine($"* **Model A ({modelA}):** {answerA.Trim()}");
                        sbVerify.AppendLine($"* **Model B ({modelB}):** {answerB.Trim()}");
                        sbVerify.AppendLine();
                        sbVerify.AppendLine("---");
                        sbVerify.AppendLine();
                        if (isMatch)
                        {
                            sbVerify.AppendLine($"✅ **Match!** Both models agree on the option: **{cleanA.ToUpperInvariant()}**");
                        }
                        else
                        {
                            sbVerify.AppendLine("⚠️ **Mismatch!** The models returned different answers. You should probably **rescan** the question.");
                        }

                        string finalResult = sbVerify.ToString();
                        ScanResponseText = finalResult;

                        _txtChatHistory.Add(new ChatMessage {
                            Role = "assistant",
                            Content = finalResult
                        });
                    }
                    else if (IsCodingScanMode)
                    {
                        ScanResponseText = metadataHeader + $"[LLM] Analyzing coding problem with **{singleModel}**...";
                        _txtChatHistory.Add(new ChatMessage {
                            Role = "system",
                            Content = "You are a strict code generator. Solve the programming challenge. You must output ONLY the source code in Python language by default. Write the code in a humanized style as if written by a developer in a real coding interview (use natural variable names, standard spacing, and write clean logic without adding excessive comments on every line). Do not include any warnings, intro/outro text, or markdown code block formatting (no ```). Return ONLY the raw code."
                        });
                        _txtChatHistory.Add(new ChatMessage {
                            Role = "user",
                            Content = $"Here is the raw text from a coding problem:\n\n{ocrResult.Text}"
                        });

                        string responseBody = await _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, _txtChatHistory, singleModel);
                        string finalResult = metadataHeader + responseBody;
                        ScanResponseText = finalResult;

                        _txtChatHistory.Add(new ChatMessage {
                            Role = "assistant",
                            Content = finalResult
                        });
                    }
                    else
                    {
                        ScanResponseText = metadataHeader + $"[LLM] Explaining text with **{singleModel}**...";
                        // Normal Scan Mode: returns general explanation / summary
                        _txtChatHistory.Add(new ChatMessage {
                            Role = "system",
                            Content = "You are a helpful overlay productivity assistant. Your task is to analyze the extracted text from the user's screen and explain it clearly and comprehensively. If the text contains a question, problem, or concepts, explain the answers or concepts step-by-step. Keep your output concise, clear, and formatted in markdown. Write in a natural, conversational, humanized style. Avoid typical robotic AI transitions, templates, or preambles. Explain it casually like an experienced developer explaining to a peer. Do not mention you are an AI."
                        });
                        _txtChatHistory.Add(new ChatMessage {
                            Role = "user",
                            Content = $"Here is the raw text from my screen:\n\n{ocrResult.Text}"
                        });

                        string responseBody = await _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, _txtChatHistory, singleModel);
                        string finalResult = metadataHeader + responseBody;
                        ScanResponseText = finalResult;

                        _txtChatHistory.Add(new ChatMessage {
                            Role = "assistant",
                            Content = finalResult
                        });
                    }
                    OnPropertyChanged(nameof(IsFollowUpVisible));
                }
                else
                {
                    ScanResponseText = "Error: Captured screen image data was empty.";
                }
            }
            catch (Exception ex)
            {
                ScanResponseText = $"Pipeline error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }
        }

        private void RestartRecordingWithCurrentSettings()
        {
            try
            {
                _audioRecorder.StopRecording();
                _audioRecorder.SilenceDetected -= OnLiveSilenceDetected;

                _audioRecorder.StartRecording(IsSystemAudioSource, IsLiveMode);
                if (IsLiveMode)
                {
                    _audioRecorder.SilenceDetected += OnLiveSilenceDetected;
                }
            }
            catch (Exception ex)
            {
                IsRecording = false;
                VoiceScanResponseText = $"Recording failed: {ex.Message}";
            }
        }

        private async void ToggleVoiceRecording()
        {
            if (IsFeatureLocked)
            {
                VoiceScanResponseText = "Access Locked: Your free trial has ended. Please verify a paid session credit to use voice scanning features.";
                return;
            }

            if (string.IsNullOrWhiteSpace(GroqKey))
            {
                VoiceScanResponseText = "Error: Please set your Groq API Key in Settings first.";
                return;
            }

            if (!IsRecording)
            {
                try
                {
                    // If follow-up recording is running, stop it silently first
                    if (IsFollowUpRecording)
                    {
                        IsFollowUpRecording = false;
                        _audioRecorder.StopRecording();
                        FollowUpText = "";
                    }

                    _audioRecorder.SilenceDetected -= OnLiveSilenceDetected; // safety unbind
                    _audioRecorder.StartRecording(IsSystemAudioSource, IsLiveMode);
                    IsRecording = true;

                    if (IsLiveMode)
                    {
                        _audioRecorder.SilenceDetected += OnLiveSilenceDetected;
                        VoiceScanResponseText = "Live auto-answering active. Listening...\n\nSpeak or play sound now. The app will automatically transcribe and answer when you pause.";
                    }
                    else
                    {
                        VoiceScanResponseText = "Recording audio query... Speak/play now.\n\nClick STOP RECORDING to transcribe and analyze.";
                    }

                }
                catch (Exception ex)
                {
                    VoiceScanResponseText = $"Recording failed: {ex.Message}";
                }
            }
            else
            {
                IsRecording = false;
                _audioRecorder.SilenceDetected -= OnLiveSilenceDetected;
                _audioRecorder.StopRecording();

                await ProcessVoiceCaptureAsync();
            }
        }

        private async void OnLiveSilenceDetected()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
            if (!dispatcher.CheckAccess())
            {
                _ = dispatcher.BeginInvoke(new Action(() => OnLiveSilenceDetected()));
                return;
            }

            // Silence was detected in Live Auto-Answer mode!
            // First stop recording synchronously to release file locks
            _audioRecorder.SilenceDetected -= OnLiveSilenceDetected;
            _audioRecorder.StopRecording();
            IsRecording = false;

            // Transcribe and solve the question
            await ProcessVoiceCaptureAsync();

            // If the user hasn't switched away and is still in Live mode, resume listening!
            if (IsLiveMode && ActiveWidget == WidgetType.VoiceScan)
            {
                try
                {
                    // Brief delay so the user can read the start of the answer
                    await Task.Delay(1000);
                    
                    // Resume listening
                    _audioRecorder.StartRecording(IsSystemAudioSource, true);
                    _audioRecorder.SilenceDetected += OnLiveSilenceDetected;
                    IsRecording = true;
                    
                    VoiceScanResponseText += "\n\n---\n[System] Listening resumes... Speak or play next question.";
                }
                catch (Exception ex)
                {
                    VoiceScanResponseText += $"\n\n[System Error] Auto-listening failed to resume: {ex.Message}";
                }
            }
        }

        private async Task ProcessVoiceCaptureAsync()
        {
            if (IsLoginOverlayVisible || IsPaymentOverlayVisible) return;
            if (_isProcessingVoice) return;
            _isProcessingVoice = true;

            try
            {
                IsScanning = true;
                string sourceDesc = IsSystemAudioSource ? "system loopback audio" : "speech query";
                VoiceScanResponseText = $"Transcribing {sourceDesc} (Groq Whisper)...";

                string effectiveGroqKey = string.IsNullOrWhiteSpace(GroqKey) ? SystemGroqKey : GroqKey;

                string transcribedText = await _llmService.TranscribeAudioAsync(effectiveGroqKey, _audioRecorder.TempFilePath);

                if (transcribedText.StartsWith("Error"))
                {
                    VoiceScanResponseText = transcribedText;
                    return;
                }

                if (string.IsNullOrWhiteSpace(transcribedText))
                {
                    VoiceScanResponseText = "No clear audio or speech was detected. Please try again.";
                    return;
                }

                VoiceScanResponseText = $"Transcribed Query:\n\"{transcribedText}\"\n\nAnalyzing query (Groq Llama 3.3)...";

                _voiceChatHistory.Clear();
                _voiceChatHistory.Add(new ChatMessage {
                    Role = "system",
                    Content = "You are a helpful overlay productivity assistant. Solve or explain the user's transcribed question. Keep your output concise, clear, and formatted in markdown. Write in a natural, humanized style. Avoid robotic AI transitions, repetitive templates, or preambles. Speak like an experienced developer or colleague offering quick assistance. Do not say you are an AI."
                });
                _voiceChatHistory.Add(new ChatMessage {
                    Role = "user",
                    Content = transcribedText
                });

                string explanation = await _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, _voiceChatHistory);
                VoiceScanResponseText = $"Transcribed Query:\n\"{transcribedText}\"\n\n---\n\n{explanation}";

                _voiceChatHistory.Add(new ChatMessage {
                    Role = "assistant",
                    Content = explanation
                });
                OnPropertyChanged(nameof(IsFollowUpVisible));
            }
            catch (Exception ex)
            {
                VoiceScanResponseText = $"Voice processing failed: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                _isProcessingVoice = false;
            }
        }

        private string CleanMcqResponse(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            
            // Trim whitespaces, quotes, and punctuation
            string cleaned = input.Trim().Trim('"', '\'', '.', ':', ')', '(', '[', ']');
            
            // Convert to lower case for case-insensitive comparison
            cleaned = cleaned.ToLowerInvariant();
            
            // If it is long, just take the first word or first character if it starts with a/b/c/d/e
            if (cleaned.Length > 0)
            {
                char first = cleaned[0];
                if (first >= 'a' && first <= 'e')
                {
                    if (cleaned.Length == 1 || !char.IsLetter(cleaned[1]))
                    {
                        return first.ToString();
                    }
                }
            }
            return cleaned;
        }

        private System.Windows.Media.ImageSource? CaptureScreenArea(System.Windows.Int32Rect rect, out byte[] imageBytes)
        {
            imageBytes = Array.Empty<byte>();
            
            // Get desktop device context
            IntPtr hdcSrc = Win32.GetDC(IntPtr.Zero);
            if (hdcSrc == IntPtr.Zero) return null;

            // Create memory device context compatible with desktop DC
            IntPtr hdcDest = Win32.CreateCompatibleDC(hdcSrc);
            if (hdcDest == IntPtr.Zero)
            {
                Win32.ReleaseDC(IntPtr.Zero, hdcSrc);
                return null;
            }

            // Create compatible GDI bitmap
            IntPtr hBitmap = Win32.CreateCompatibleBitmap(hdcSrc, rect.Width, rect.Height);
            if (hBitmap == IntPtr.Zero)
            {
                Win32.DeleteDC(hdcDest);
                Win32.ReleaseDC(IntPtr.Zero, hdcSrc);
                return null;
            }

            // Select GDI bitmap object into destination DC
            IntPtr hOld = Win32.SelectObject(hdcDest, hBitmap);
            
            // Execute hardware-accelerated BitBlt screenshot transfer
            Win32.BitBlt(hdcDest, 0, 0, rect.Width, rect.Height, hdcSrc, rect.X, rect.Y, Win32.SRCCOPY);
            
            // Restore selection
            Win32.SelectObject(hdcDest, hOld);

            // Convert HBitmap handle into WPF visual BitmapSource
            System.Windows.Media.Imaging.BitmapSource bitmapSource = 
                System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, 
                    IntPtr.Zero, 
                    System.Windows.Int32Rect.Empty, 
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

            // Convert BitmapSource to PNG formatted byte array
            using (var ms = new System.IO.MemoryStream())
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
                encoder.Save(ms);
                imageBytes = ms.ToArray();
            }

            // Clean up GDI pointers to prevent leaks
            Win32.DeleteObject(hBitmap);
            Win32.DeleteDC(hdcDest);
            Win32.ReleaseDC(IntPtr.Zero, hdcSrc);

            return bitmapSource;
        }

        #region Groq Key Setup & Dashboard Overlay Properties

        private string _groqInputKey = "";
        private string _groqKeyValidationError = "";
        private bool _isValidatingGroqKey;

        public string GroqInputKey
        {
            get => _groqInputKey;
            set => SetProperty(ref _groqInputKey, value);
        }

        public string GroqKeyValidationError
        {
            get => _groqKeyValidationError;
            set => SetProperty(ref _groqKeyValidationError, value);
        }

        public bool IsValidatingGroqKey
        {
            get => _isValidatingGroqKey;
            set => SetProperty(ref _isValidatingGroqKey, value);
        }

        public bool IsGroqKeyValidated
        {
            get => _settings.IsGroqKeyValidated;
            set
            {
                if (SetProperty(ref _settings.IsGroqKeyValidated, value))
                {
                    OnPropertyChanged(nameof(IsGroqKeyOverlayVisible));
                    OnPropertyChanged(nameof(IsDashboardOverlayVisible));
                    SyncStealthForModalOverlays();
                }
            }
        }

        public bool IsTrialStarted
        {
            get => _settings.IsTrialStarted;
            set
            {
                if (SetProperty(ref _settings.IsTrialStarted, value))
                {
                    OnPropertyChanged(nameof(IsDashboardOverlayVisible));
                    SyncStealthForModalOverlays();
                }
            }
        }

        public bool IsGroqKeyOverlayVisible => !IsGroqKeyValidated;

        public bool IsDashboardOverlayVisible => IsGroqKeyValidated && !IsTrialStarted;

        private async Task ValidateGroqKeyAsync()
        {
            if (string.IsNullOrWhiteSpace(GroqInputKey))
            {
                GroqKeyValidationError = "Please paste your Groq API Key to continue.";
                return;
            }

            IsValidatingGroqKey = true;
            GroqKeyValidationError = "";

            try
            {
                var (isValid, errorMessage) = await _llmService.ValidateGroqKeyAsync(GroqInputKey);
                if (isValid)
                {
                    _settings.GroqKey = GroqInputKey.Trim();
                    GroqKey = _settings.GroqKey;
                    IsGroqKeyValidated = true;
                    _settingsService.SaveSettings(_settings);
                    
                    // Save key to user account database persistently
                    await SaveGroqKeyToServerAsync(GroqInputKey.Trim());
                }
                else
                {
                    GroqKeyValidationError = errorMessage;
                }
            }
            catch (Exception ex)
            {
                GroqKeyValidationError = $"Validation failed: {ex.Message}";
            }
            finally
            {
                IsValidatingGroqKey = false;
            }
        }

        private void OpenGroqConsole()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://console.groq.com/keys",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                GroqKeyValidationError = $"Could not open browser: {ex.Message}";
            }
        }

        private void StartFreeTrial()
        {
            IsTrialStarted = true;
            _settingsService.SaveSettings(_settings);
        }

        #endregion

        private void AskFollowUp(string? question)
        {
            if (string.IsNullOrWhiteSpace(question)) return;
            FollowUpText = question;
            SubmitFollowUpPrompt();
        }

        private void UpdatePresetFollowUps()
        {
            PresetFollowUps.Clear();
            if (IsMcqScanMode)
            {
                PresetFollowUps.Add("Why is this option correct?");
                PresetFollowUps.Add("Why are other options wrong?");
                PresetFollowUps.Add("Double check the question");
                PresetFollowUps.Add("Provide formula/theory used");
                PresetFollowUps.Add("Explain step-by-step");
                PresetFollowUps.Add("Show shortcut to solve");
                PresetFollowUps.Add("Verify Option A");
                PresetFollowUps.Add("Verify Option B");
                PresetFollowUps.Add("Verify Option C");
                PresetFollowUps.Add("Verify Option D");
            }
            else if (IsCodingScanMode)
            {
                PresetFollowUps.Add("Optimize code");
                PresetFollowUps.Add("Explain approach/logic");
                PresetFollowUps.Add("Add code comments");
                PresetFollowUps.Add("Dry run with example");
                PresetFollowUps.Add("Rewrite in Python");
                PresetFollowUps.Add("Rewrite in C++");
                PresetFollowUps.Add("Rewrite in Java");
                PresetFollowUps.Add("Rewrite in JS");
                PresetFollowUps.Add("Check boundary cases");
                PresetFollowUps.Add("Time complexity");
            }
            else // Normal Scan Mode
            {
                PresetFollowUps.Add("Explain simpler");
                PresetFollowUps.Add("Give examples");
                PresetFollowUps.Add("List key points");
                PresetFollowUps.Add("Summarize");
                PresetFollowUps.Add("Related concepts");
                PresetFollowUps.Add("Pros and cons");
                PresetFollowUps.Add("Simple English");
                PresetFollowUps.Add("Detailed breakdown");
                PresetFollowUps.Add("Explain to beginner");
                PresetFollowUps.Add("Background theory");
            }
        }

        private async void SubmitFollowUpPrompt()
        {
            if (IsLoginOverlayVisible || IsPaymentOverlayVisible)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(FollowUpText)) return;

            string effectiveGroqKey = string.IsNullOrWhiteSpace(GroqKey) ? SystemGroqKey : GroqKey;
            if (string.IsNullOrWhiteSpace(effectiveGroqKey))
            {
                if (ActiveWidget == WidgetType.TxtScan)
                    ScanResponseText = "Error: Please set your Groq API Key in Settings first.";
                else
                    VoiceScanResponseText = "Error: Please set your Groq API Key in Settings first.";
                return;
            }

            string question = FollowUpText.Trim();
            FollowUpText = ""; // Clear immediately for visual feedback

            IsScanning = true;
            if (ActiveWidget == WidgetType.TxtScan)
            {
                ScanResponseText += $"\n\n👉 Follow-up Question:\n\"{question}\"\n\nThinking...";
                string finalQuestion = question;
                try
                {
                    if (IsCodingScanMode)
                    {
                        finalQuestion = question + "\n\n(Reminder: Output ONLY the source code in a humanized developer style. Do not include markdown code block wrappers, descriptions, warnings, or explanations. Return ONLY the code.)";
                    }

                    _txtChatHistory.Add(new ChatMessage {
                        Role = "user",
                        Content = finalQuestion
                    });

                    string answer = await _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, _txtChatHistory);
                    
                    ScanResponseText = ScanResponseText.Replace("Thinking...", answer);

                    _txtChatHistory.Add(new ChatMessage {
                        Role = "assistant",
                        Content = answer
                    });
                }
                catch (Exception ex)
                {
                    ScanResponseText = ScanResponseText.Replace("Thinking...", $"Follow-up query failed: {ex.Message}");
                    if (_txtChatHistory.Count > 0 && (_txtChatHistory[_txtChatHistory.Count - 1].Content == question || _txtChatHistory[_txtChatHistory.Count - 1].Content == finalQuestion))
                    {
                        _txtChatHistory.RemoveAt(_txtChatHistory.Count - 1);
                    }
                }
                finally
                {
                    IsScanning = false;
                }
            }
            else
            {
                VoiceScanResponseText += $"\n\n👉 Follow-up Question:\n\"{question}\"\n\nThinking...";
                try
                {
                    _voiceChatHistory.Add(new ChatMessage {
                        Role = "user",
                        Content = question
                    });

                    string answer = await _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, _voiceChatHistory);
                    
                    VoiceScanResponseText = VoiceScanResponseText.Replace("Thinking...", answer);

                    _voiceChatHistory.Add(new ChatMessage {
                        Role = "assistant",
                        Content = answer
                    });
                }
                catch (Exception ex)
                {
                    VoiceScanResponseText = VoiceScanResponseText.Replace("Thinking...", $"Follow-up query failed: {ex.Message}");
                    if (_voiceChatHistory.Count > 0 && _voiceChatHistory[_voiceChatHistory.Count - 1].Content == question)
                    {
                        _voiceChatHistory.RemoveAt(_voiceChatHistory.Count - 1);
                    }
                }
                finally
                {
                    IsScanning = false;
                    ResumeLiveScanIfNeeded();
                }
            }
        }

        private async void ToggleFollowUpVoiceRecording()
        {
            string effectiveGroqKey = string.IsNullOrWhiteSpace(GroqKey) ? SystemGroqKey : GroqKey;
            if (string.IsNullOrWhiteSpace(effectiveGroqKey))
            {
                VoiceScanResponseText = "Error: Please set your Groq API Key in Settings first.";
                return;
            }

            if (!IsFollowUpRecording)
            {
                try
                {
                    // If regular recording is running, stop it silently to prevent race conditions on the WAV file
                    if (IsRecording)
                    {
                        _wasLiveScanActiveBeforeFollowUp = IsLiveMode;
                        IsRecording = false;
                        _audioRecorder.SilenceDetected -= OnLiveSilenceDetected;
                        _audioRecorder.StopRecording();
                    }
                    else
                    {
                        _wasLiveScanActiveBeforeFollowUp = false;
                    }

                    _audioRecorder.StartRecording(false, false); // Mic only, manual mode
                    IsFollowUpRecording = true;
                    FollowUpText = "Listening... Speak follow-up question now.";
                }
                catch (Exception ex)
                {
                    FollowUpText = $"Recording failed: {ex.Message}";
                }
            }
            else
            {
                IsFollowUpRecording = false;
                _audioRecorder.StopRecording();
                FollowUpText = "Transcribing voice...";

                try
                {
                    string transcribedText = await _llmService.TranscribeAudioAsync(effectiveGroqKey, _audioRecorder.TempFilePath);
                    
                    if (transcribedText.StartsWith("Error"))
                    {
                        FollowUpText = transcribedText;
                        ResumeLiveScanIfNeeded();
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(transcribedText))
                    {
                        FollowUpText = "No speech detected. Try again.";
                        ResumeLiveScanIfNeeded();
                        return;
                    }

                    FollowUpText = transcribedText;
                    
                    // Auto-submit the transcribed voice query
                    SubmitFollowUpPrompt();
                }
                catch (Exception ex)
                {
                    FollowUpText = $"Transcription failed: {ex.Message}";
                    ResumeLiveScanIfNeeded();
                }
            }
        }

        private void ResumeLiveScanIfNeeded()
        {
            if (!_wasLiveScanActiveBeforeFollowUp) return;
            _wasLiveScanActiveBeforeFollowUp = false;

            try
            {
                // Resume system audio live scan recording
                _audioRecorder.SilenceDetected -= OnLiveSilenceDetected; // safety unbind
                _audioRecorder.StartRecording(IsSystemAudioSource, true);
                _audioRecorder.SilenceDetected += OnLiveSilenceDetected;
                IsRecording = true;

                VoiceScanResponseText += "\n\n---\n[System] Live scan resumed. Listening for next question...";

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResumeLiveScan failed: {ex.Message}");
                VoiceScanResponseText += $"\n\n[System] Could not resume live scan: {ex.Message}";
            }
        }

        #endregion

        #region Authentication Properties
        public string SessionToken
        {
            get => _settings.SessionToken;
            set
            {
                if (SetProperty(ref _settings.SessionToken, value))
                {
                    OnPropertyChanged(nameof(IsLoggedIn));
                    UpdateOverlayVisibilities();
                }
            }
        }

        public string UserEmail
        {
            get => _settings.UserEmail;
            set => SetProperty(ref _settings.UserEmail, value);
        }

        public string ApiBaseUrl
        {
            get => _settings.ApiBaseUrl;
            set => SetProperty(ref _settings.ApiBaseUrl, value);
        }

        public bool IsLoggedIn => !string.IsNullOrEmpty(SessionToken);

        public string SessionTimerDisplay
        {
            get => _sessionTimerDisplay;
            set => SetProperty(ref _sessionTimerDisplay, value);
        }

        public bool IsTrialActive
        {
            get => _isTrialActive;
            set
            {
                if (SetProperty(ref _isTrialActive, value))
                {
                    UpdateOverlayVisibilities();
                }
            }
        }

        public bool IsPaidActive
        {
            get => _isPaidActive;
            set
            {
                if (SetProperty(ref _isPaidActive, value))
                {
                    UpdateOverlayVisibilities();
                    OnPropertyChanged(nameof(IsFeatureLocked));
                }
            }
        }

        public bool IsFeatureLocked => !IsAdmin && IsLoggedIn && !IsTrialActive && !IsPaidActive;

        public string SystemGroqKey
        {
            get => _systemGroqKey;
            set => SetProperty(ref _systemGroqKey, value);
        }

        public bool IsLoginOverlayVisible
        {
            get => _isLoginOverlayVisible;
            set
            {
                if (SetProperty(ref _isLoginOverlayVisible, value))
                {
                    SyncStealthForModalOverlays();
                }
            }
        }

        public bool IsPaymentOverlayVisible
        {
            get => _isPaymentOverlayVisible;
            set
            {
                if (SetProperty(ref _isPaymentOverlayVisible, value))
                {
                    SyncStealthForModalOverlays();
                }
            }
        }

        public bool IsPaymentCreditAvailable
        {
            get => _isPaymentCreditAvailable;
            set => SetProperty(ref _isPaymentCreditAvailable, value);
        }

        public string PaymentQrUrl
        {
            get => _paymentQrUrl;
            set => SetProperty(ref _paymentQrUrl, value);
        }

        public string LoginEmail
        {
            get => _loginEmail;
            set => SetProperty(ref _loginEmail, value);
        }

        public string LoginPassword
        {
            get => _loginPassword;
            set => SetProperty(ref _loginPassword, value);
        }

        public string AuthErrorMessage
        {
            get => _authErrorMessage;
            set => SetProperty(ref _authErrorMessage, value);
        }

        public bool IsAuthLoading
        {
            get => _isAuthLoading;
            set => SetProperty(ref _isAuthLoading, value);
        }

        public string PaymentUtr
        {
            get => _paymentUtr;
            set => SetProperty(ref _paymentUtr, value);
        }

        public string PaymentErrorMessage
        {
            get => _paymentErrorMessage;
            set => SetProperty(ref _paymentErrorMessage, value);
        }

        public bool IsPaymentLoading
        {
            get => _isPaymentLoading;
            set => SetProperty(ref _isPaymentLoading, value);
        }
        #endregion

        #region Session Management & API Calls

        private string GetApiEndpoint(string relativePath)
        {
            string baseUrl = (ApiBaseUrl ?? "").Trim().TrimEnd('/');
            string path = relativePath.StartsWith("/") ? relativePath : "/" + relativePath;
            return $"{baseUrl}{path}";
        }

        private bool TryParseJson<T>(string text, out T? result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.Trim();
            if (!trimmed.StartsWith("{") && !trimmed.StartsWith("[")) return false;
            try
            {
                result = JsonSerializer.Deserialize<T>(trimmed);
                return result != null;
            }
            catch
            {
                return false;
            }
        }

        private void UpdateOverlayVisibilities()
        {
            if (IsAdmin)
            {
                IsLoginOverlayVisible = false;
                IsPaymentOverlayVisible = false;
            }
            else if (!IsLoggedIn)
            {
                IsLoginOverlayVisible = true;
                IsPaymentOverlayVisible = false;
            }
            else
            {
                IsLoginOverlayVisible = false;
                IsPaymentOverlayVisible = !IsTrialActive && !IsPaidActive;
            }

            SyncStealthForModalOverlays();
        }

        public void SyncStealthForModalOverlays()
        {
            bool hasModalOverlay = IsLoginOverlayVisible || IsPaymentOverlayVisible || IsGroqKeyOverlayVisible || IsDashboardOverlayVisible;
            if (hasModalOverlay)
            {
                _styleService.SetClickThrough(false);
                _styleService.SetStealthMode(false);
                _styleService.ActivateWindow(); // Activate so TextBoxes can receive keyboard input
            }
            else
            {
                _styleService.SetClickThrough(_settings.IsClickThrough);
                _styleService.SetStealthMode(true); // ALWAYS keep stealth ON when no modal overlay
            }
        }

        private async Task ExecuteLoginAsync()
        {
            if (string.IsNullOrWhiteSpace(LoginEmail) || string.IsNullOrWhiteSpace(LoginPassword))
            {
                AuthErrorMessage = "Email and password are required.";
                return;
            }

            AuthErrorMessage = "";
            IsAuthLoading = true;

            // Clear local key state first so we don't inherit old keys from this PC
            _settings.GroqKey = "";
            GroqKey = "";
            GroqInputKey = "";
            IsGroqKeyValidated = false;
            _settingsService.SaveSettings(_settings);

            try
            {
                var payload = new { email = LoginEmail.Trim(), password = LoginPassword.Trim() };
                string json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(GetApiEndpoint("/api/auth/login"), content);
                string responseStr = await response.Content.ReadAsStringAsync();
                
                if (TryParseJson<AuthResponse>(responseStr, out var result) && result != null)
                {
                    if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(result.token))
                    {
                        UserEmail = result.email;
                        SessionToken = result.token;
                        
                        LoginEmail = "";
                        LoginPassword = "";
                        
                        await CheckSessionStatusAsync(true);
                    }
                    else
                    {
                        string errStr = !string.IsNullOrWhiteSpace(result.error) 
                            ? result.error 
                            : (!string.IsNullOrWhiteSpace(result.message) ? result.message : "Invalid email or password.");
                        AuthErrorMessage = errStr;
                    }
                }
                else
                {
                    AuthErrorMessage = $"Server Error ({(int)response.StatusCode}): {responseStr}";
                }
            }
            catch (Exception ex)
            {
                AuthErrorMessage = $"Connection error: {ex.Message}";
            }
            finally
            {
                IsAuthLoading = false;
            }
        }

        private async Task ExecuteSignupAsync()
        {
            if (string.IsNullOrWhiteSpace(LoginEmail) || string.IsNullOrWhiteSpace(LoginPassword))
            {
                AuthErrorMessage = "Email and password are required.";
                return;
            }

            if (LoginPassword.Length < 6)
            {
                AuthErrorMessage = "Password must be at least 6 characters.";
                return;
            }

            AuthErrorMessage = "";
            IsAuthLoading = true;

            // Clear local key state first so we don't inherit old keys from this PC
            _settings.GroqKey = "";
            GroqKey = "";
            GroqInputKey = "";
            IsGroqKeyValidated = false;
            _settingsService.SaveSettings(_settings);

            try
            {
                var payload = new { email = LoginEmail.Trim(), password = LoginPassword.Trim() };
                string json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(GetApiEndpoint("/api/auth/signup"), content);
                string responseStr = await response.Content.ReadAsStringAsync();
                
                if (TryParseJson<AuthResponse>(responseStr, out var result) && result != null)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        UserEmail = result.email;
                        SessionToken = result.token;
                        
                        LoginEmail = "";
                        LoginPassword = "";
                        
                        await CheckSessionStatusAsync(true);
                    }
                    else
                    {
                        AuthErrorMessage = result.error ?? "Sign up failed.";
                    }
                }
                else
                {
                    AuthErrorMessage = $"Server Error ({(int)response.StatusCode}): Invalid server endpoint URL or Vercel 404 response.";
                }
            }
            catch (Exception ex)
            {
                AuthErrorMessage = $"Connection error: {ex.Message}";
            }
            finally
            {
                IsAuthLoading = false;
            }
        }

        private void ExecuteLogout()
        {
            SessionToken = "";
            UserEmail = "";
            SystemGroqKey = "";
            IsAdmin = false;
            IsTrialActive = false;
            IsPaidActive = false;
            _trialEndsAt = null;
            _paidUntil = null;
            IsPaymentCreditAvailable = false;
            IsSettingsOpen = false;
            
            // Clear Groq key states to protect user privacy
            _settings.GroqKey = "";
            GroqKey = "";
            GroqInputKey = "";
            IsGroqKeyValidated = false;
            _settingsService.SaveSettings(_settings);

            UpdateOverlayVisibilities();
            OnPropertyChanged(nameof(IsFeatureLocked));
        }

        private async Task ExecuteSubmitPaymentAsync()
        {
            if (string.IsNullOrWhiteSpace(PaymentUtr) || !System.Text.RegularExpressions.Regex.IsMatch(PaymentUtr.Trim(), @"^\d{12}$"))
            {
                PaymentErrorMessage = "Invalid Ref No. UTR must be exactly 12 digits.";
                return;
            }

            PaymentErrorMessage = "";
            IsPaymentLoading = true;
            try
            {
                var payload = new { utr = PaymentUtr.Trim() };
                string json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var request = new HttpRequestMessage(HttpMethod.Post, GetApiEndpoint("/api/pay/verify"))
                {
                    Content = content
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SessionToken);

                var response = await _httpClient.SendAsync(request);
                string responseStr = await response.Content.ReadAsStringAsync();
                
                if (TryParseJson<PaymentVerifyResponse>(responseStr, out var result) && result != null)
                {
                    if (response.IsSuccessStatusCode && result.success)
                    {
                        PaymentUtr = "";
                        await CheckSessionStatusAsync(true);
                    }
                    else
                    {
                        PaymentErrorMessage = result.error ?? "Payment verification failed.";
                    }
                }
                else
                {
                    PaymentErrorMessage = $"Server Error ({(int)response.StatusCode}): Invalid server endpoint URL.";
                }
            }
            catch (Exception ex)
            {
                PaymentErrorMessage = $"Connection error: {ex.Message}";
            }
            finally
            {
                IsPaymentLoading = false;
            }
        }

        private async Task ExecuteStartPaidSessionAsync()
        {
            PaymentErrorMessage = "";
            IsPaymentLoading = true;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, GetApiEndpoint("/api/session/start"));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SessionToken);

                var response = await _httpClient.SendAsync(request);
                string responseStr = await response.Content.ReadAsStringAsync();
                
                if (TryParseJson<SessionStartResponse>(responseStr, out var result) && result != null)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        await CheckSessionStatusAsync(true);
                    }
                    else
                    {
                        PaymentErrorMessage = result.error ?? "Failed to start session.";
                    }
                }
                else
                {
                    PaymentErrorMessage = $"Server Error ({(int)response.StatusCode}): Invalid server endpoint URL.";
                }
            }
            catch (Exception ex)
            {
                PaymentErrorMessage = $"Connection error: {ex.Message}";
            }
            finally
            {
                IsPaymentLoading = false;
            }
        }

        private int _statusSyncCounter = 0;

        private async Task CheckSessionStatusAsync(bool forceUiUpdate)
        {
            if (string.IsNullOrEmpty(SessionToken)) return;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, GetApiEndpoint("/api/session/status"));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SessionToken);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string responseStr = await response.Content.ReadAsStringAsync();
                    if (TryParseJson<SessionStatusResponse>(responseStr, out var result) && result != null)
                    {
                        SystemGroqKey = result.system_groq_key;
                        IsAdmin = result.is_admin || (!string.IsNullOrEmpty(UserEmail) && (UserEmail.ToLower().Contains("admin") || UserEmail.ToLower() == "udayv@gmail.com"));

                        IsTrialActive = IsAdmin || result.isTrialActive;
                        IsPaidActive = IsAdmin || result.isPaidActive;
                        IsPaymentCreditAvailable = IsAdmin || result.payment_credit;

                        _trialEndsAt = result.trial_ends_at != null ? DateTime.Parse(result.trial_ends_at).ToUniversalTime() : null;
                        _paidUntil = result.paid_until != null ? DateTime.Parse(result.paid_until).ToUniversalTime() : null;
                        _isSessionActive = IsAdmin || result.is_session_active;

                        // Generate UPI QR Code URL
                        string upiLink = $"upi://pay?pa=udayv132@ybl&pn=ShadowAI&am=50&cu=INR&tn=ShadowAI_{UserEmail}";
                        PaymentQrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=180x180&data={Uri.EscapeDataString(upiLink)}";
                        
                        // Load saved custom Groq key if present on the server database
                        if (!string.IsNullOrEmpty(result.user_groq_key))
                        {
                            _settings.GroqKey = result.user_groq_key.Trim();
                            GroqKey = _settings.GroqKey;
                            GroqInputKey = _settings.GroqKey;
                            IsGroqKeyValidated = true;
                            _settingsService.SaveSettings(_settings);
                        }
                        else
                        {
                            // If there is no custom key on the database, check if we can fall back to the system key.
                            // If both are missing, we must ask the user for a key so that the app functionality works.
                            bool hasSystemKey = !string.IsNullOrEmpty(result.system_groq_key);
                            IsGroqKeyValidated = hasSystemKey;
                        }

                        OnPropertyChanged(nameof(IsFeatureLocked));
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    ExecuteLogout();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to sync session status: {ex.Message}");
            }
            finally
            {
                UpdateOverlayVisibilities();
            }
        }

        private async void SessionTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsLoggedIn)
            {
                SessionTimerDisplay = "Please log in";
                return;
            }

            if (IsAdmin)
            {
                IsTrialActive = true;
                IsPaidActive = true;
                SessionTimerDisplay = "Admin: Unlimited Access";
                return;
            }

            var now = DateTime.UtcNow;
            
            if (IsPaidActive && _paidUntil != null && _paidUntil > now)
            {
                var diff = _paidUntil.Value - now;
                SessionTimerDisplay = $"Session: {((int)diff.TotalHours):D2}h {diff.Minutes:D2}m {diff.Seconds:D2}s left";
            }
            else if (IsTrialActive && _trialEndsAt != null && _trialEndsAt > now)
            {
                var diff = _trialEndsAt.Value - now;
                SessionTimerDisplay = $"Free Trial: {diff.Minutes:D2}m {diff.Seconds:D2}s left";
            }
            else
            {
                bool stateChanged = IsTrialActive || IsPaidActive;
                IsTrialActive = false;
                IsPaidActive = false;
                SessionTimerDisplay = "Session Locked";
                if (stateChanged)
                {
                    UpdateOverlayVisibilities();
                    OnPropertyChanged(nameof(IsFeatureLocked));
                }
            }

            _statusSyncCounter++;
            if (_statusSyncCounter >= 30)
            {
                _statusSyncCounter = 0;
                await CheckSessionStatusAsync(true);
            }
        }

        private class AuthResponse
        {
            public string token { get; set; } = "";
            public string email { get; set; } = "";
            public string? trial_ends_at { get; set; }
            public string? paid_until { get; set; }
            public bool is_session_active { get; set; }
            public bool is_admin { get; set; }
            public string error { get; set; } = "";
            public string message { get; set; } = "";
        }

        private class SessionStatusResponse
        {
            public string email { get; set; } = "";
            public bool is_admin { get; set; }
            public bool isTrialActive { get; set; }
            public bool isPaidActive { get; set; }
            public string? trial_ends_at { get; set; }
            public string? paid_until { get; set; }
            public string? session_started_at { get; set; }
            public bool is_session_active { get; set; }
            public bool payment_credit { get; set; }
            public string system_groq_key { get; set; } = "";
            public string user_groq_key { get; set; } = "";
            public string error { get; set; } = "";
        }

        private async Task SaveGroqKeyToServerAsync(string key)
        {
            if (string.IsNullOrEmpty(SessionToken)) return;
            try
            {
                var payload = new { groq_key = key };
                string jsonPayload = JsonSerializer.Serialize(payload);
                var request = new HttpRequestMessage(HttpMethod.Post, GetApiEndpoint("/api/user/save-key"));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SessionToken);
                request.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                await _httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save Groq Key on server database: {ex.Message}");
            }
        }

        private class PaymentVerifyResponse
        {
            public bool success { get; set; }
            public string message { get; set; } = "";
            public string error { get; set; } = "";
        }

        private class SessionStartResponse
        {
            public string message { get; set; } = "";
            public string? paid_until { get; set; }
            public string? session_started_at { get; set; }
            public string error { get; set; } = "";
        }

        #endregion
    }
}

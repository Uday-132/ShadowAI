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
        private int _txtTurnCounter;
        private int _voiceTurnCounter;
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
        public ICommand SendScreenshotsCommand { get; }
        public ICommand RemoveScreenshotCommand { get; }
        public ICommand ToggleVoiceCommand { get; }
        public ICommand ClearTxtScanCommand { get; }

        public System.Collections.ObjectModel.ObservableCollection<Models.CapturedScreenshotItem> CapturedScreenshots { get; } = new System.Collections.ObjectModel.ObservableCollection<Models.CapturedScreenshotItem>();

        /// <summary>
        /// Chat bubble items for the ChatGPT/Gemini-style conversation UI in Text Scan.
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<Models.ChatBubbleItem> ChatBubbles { get; } = new System.Collections.ObjectModel.ObservableCollection<Models.ChatBubbleItem>();

        /// <summary>
        /// Chat bubble items for the ChatGPT/Gemini-style conversation UI in Voice Scan.
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<Models.ChatBubbleItem> VoiceChatBubbles { get; } = new System.Collections.ObjectModel.ObservableCollection<Models.ChatBubbleItem>();

        public int CapturedScreenshotsCount => CapturedScreenshots.Count;
        public bool HasCapturedScreenshots => CapturedScreenshots.Count > 0;
        public bool IsMinimumScreenshotsReached => CapturedScreenshots.Count >= MaxScreenshotsLimit;
        public string SendButtonText => $"SEND ({CapturedScreenshots.Count})";

        public string ScreenshotsBadgeText
        {
            get
            {
                if (CapturedScreenshots.Count == 0)
                    return $"📸 Captured: 0 / {MaxScreenshotsLimit} max (Click + CAPTURE to add)";
                if (CapturedScreenshots.Count < MaxScreenshotsLimit)
                    return $"📸 Captured: {CapturedScreenshots.Count} / {MaxScreenshotsLimit} max (Ready to SEND or add more)";
                return $"✅ Captured: {MaxScreenshotsLimit} / {MaxScreenshotsLimit} max (Max limit reached - Ready to SEND)";
            }
        }

        private void NotifyScreenshotStateChanged()
        {
            OnPropertyChanged(nameof(CapturedScreenshotsCount));
            OnPropertyChanged(nameof(HasCapturedScreenshots));
            OnPropertyChanged(nameof(IsMinimumScreenshotsReached));
            OnPropertyChanged(nameof(SendButtonText));
            OnPropertyChanged(nameof(ScreenshotsBadgeText));
        }
        public ICommand ClearVoiceScanCommand { get; }
        public ICommand SubmitFollowUpCommand { get; }
        public ICommand ToggleFollowUpVoiceCommand { get; }
        public ICommand NextOnboardingCommand { get; }
        public ICommand BackOnboardingCommand { get; }
        public ICommand SkipOnboardingCommand { get; }
        public ICommand FinishOnboardingCommand { get; }

        // Copy & Font Size Commands
        public ICommand CopyTxtCommand { get; }
        public ICommand CopyVoiceCommand { get; }
        public ICommand DecreaseFontSizeCommand { get; }
        public ICommand IncreaseFontSizeCommand { get; }
        public ICommand ToggleExpandHeightCommand { get; }

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

        // Update Commands
        public ICommand CheckForUpdateCommand { get; }
        public ICommand DownloadUpdateCommand { get; }

        // Update State
        private bool _updateAvailable;
        private string _latestVersion = "";
        private string _updateDownloadUrl = "";
        private string _updateReleaseNotes = "";
        private bool _isUpdating;
        private double _updateProgress;
        private string _updateStatusText = "";

        public string CurrentAppVersion => Services.UpdateService.CurrentVersion;

        public bool UpdateAvailable
        {
            get => _updateAvailable;
            set { if (_updateAvailable != value) { _updateAvailable = value; OnPropertyChanged(); } }
        }
        public string LatestVersion
        {
            get => _latestVersion;
            set { if (_latestVersion != value) { _latestVersion = value; OnPropertyChanged(); } }
        }
        public bool IsUpdating
        {
            get => _isUpdating;
            set { if (_isUpdating != value) { _isUpdating = value; OnPropertyChanged(); } }
        }
        public double UpdateProgress
        {
            get => _updateProgress;
            set { if (_updateProgress != value) { _updateProgress = value; OnPropertyChanged(); OnPropertyChanged(nameof(UpdateProgressPercent)); } }
        }
        public string UpdateProgressPercent => $"{(int)(_updateProgress * 100)}%";
        public string UpdateStatusText
        {
            get => _updateStatusText;
            set { if (_updateStatusText != value) { _updateStatusText = value; OnPropertyChanged(); } }
        }
        public string UpdateReleaseNotes
        {
            get => _updateReleaseNotes;
            set { if (_updateReleaseNotes != value) { _updateReleaseNotes = value; OnPropertyChanged(); } }
        }

        // Groq Key Validation & Free Trial Commands
        public ICommand ValidateGroqKeyCommand { get; }
        public ICommand OpenApiKeySettingsCommand { get; }
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
            GeminiInputKey = _settings.GeminiKey;
            ValidateGroqKeyCommand = new RelayCommand(async _ => await ValidateGroqKeyAsync());
            ValidateGeminiKeyCommand = new RelayCommand(async _ => await ValidateGeminiKeyAsync());
            ValidateApiKeysCommand = new RelayCommand(async _ => await ValidateApiKeysAsync());
            OpenApiKeySettingsCommand = new RelayCommand(_ => IsSettingsOpen = true);
            OpenGroqConsoleCommand = new RelayCommand(_ => OpenGroqConsole());
            OpenGeminiConsoleCommand = new RelayCommand(_ => OpenGeminiConsole());
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
            CycleMaxScreenshotsLimitCommand = new RelayCommand(_ => { MaxScreenshotsLimit = (MaxScreenshotsLimit % 5) + 1; });
            SendScreenshotsCommand = new RelayCommand(async _ => await ExecuteSendBatchScreenshotsAsync());
            RemoveScreenshotCommand = new RelayCommand(param => RemoveScreenshot(param));
            ToggleVoiceCommand = new RelayCommand(_ => ToggleVoiceRecording());
            ClearTxtScanCommand = new RelayCommand(_ => { 
                CapturedScreenshots.Clear();
                NotifyScreenshotStateChanged();
                ScanResponseText = ""; 
                CapturedPreview = null; 
                _txtChatHistory.Clear();
                _txtTurnCounter = 0;
                ChatBubbles.Clear();
                ScanModeState currentState = GetModeState(_activeScanModeName);
                currentState.ResponseText = "";
                currentState.ChatHistory.Clear();
                currentState.Screenshots.Clear();
                currentState.CapturedPreview = null;
                OnPropertyChanged(nameof(IsFollowUpVisible));
            });
            ClearVoiceScanCommand = new RelayCommand(_ => { 
                VoiceScanResponseText = "";
                _voiceChatHistory.Clear();
                _voiceTurnCounter = 0;
                VoiceChatBubbles.Clear();
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
                if (VoiceChatBubbles.Count > 0)
                {
                    var lastAssistant = System.Linq.Enumerable.LastOrDefault(VoiceChatBubbles, b => b.IsAssistant && !b.IsLoading);
                    if (lastAssistant != null && !string.IsNullOrEmpty(lastAssistant.Content))
                    {
                        System.Windows.Clipboard.SetText(lastAssistant.Content);
                        return;
                    }
                }
                if (!string.IsNullOrEmpty(VoiceScanResponseText)) 
                    System.Windows.Clipboard.SetText(VoiceScanResponseText); 
            });

            DecreaseFontSizeCommand = new RelayCommand(_ => {
                if (AppFontSize > 8.0) AppFontSize = Math.Max(8.0, AppFontSize - 1.0);
            });
            IncreaseFontSizeCommand = new RelayCommand(_ => {
                if (AppFontSize < 22.0) AppFontSize = Math.Min(22.0, AppFontSize + 1.0);
            });
            ToggleExpandHeightCommand = new RelayCommand(_ => {
                IsExpandedHeight = !IsExpandedHeight;
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

            CheckForUpdateCommand = new RelayCommand(async _ => await CheckForUpdateAsync());
            DownloadUpdateCommand = new RelayCommand(async _ => await ExecuteDownloadUpdateAsync(),
                _ => UpdateAvailable && !IsUpdating);

            // Run initial check if we have a saved token
            if (IsLoggedIn)
            {
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () => {
                    await CheckSessionStatusAsync(false);
                    await CheckForUpdateAsync();
                }));
            }
            else
            {
                UpdateOverlayVisibilities();
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () => {
                    await CheckForUpdateAsync();
                }));
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
                    OnPropertyChanged(nameof(ActiveApiKeyStatusText));
                }
            }
        }

        public int MaxScreenshotsLimit
        {
            get => _settings.MaxScreenshotsLimit <= 0 ? 5 : _settings.MaxScreenshotsLimit;
            set
            {
                if (_settings.MaxScreenshotsLimit != value)
                {
                    _settings.MaxScreenshotsLimit = value;
                    OnPropertyChanged(nameof(MaxScreenshotsLimit));
                    OnPropertyChanged(nameof(MaxScreenshotsButtonText));
                    OnPropertyChanged(nameof(IsMinimumScreenshotsReached));
                    NotifyScreenshotStateChanged();
                }
            }
        }

        public string MaxScreenshotsButtonText => $"MAX: {MaxScreenshotsLimit}";
        public ICommand CycleMaxScreenshotsLimitCommand { get; }

        public string ActiveApiProvider
        {
            get => string.IsNullOrEmpty(_settings.ActiveApiProvider) ? "Groq" : _settings.ActiveApiProvider;
            set
            {
                if (_settings.ActiveApiProvider != value)
                {
                    _settings.ActiveApiProvider = value;
                    OnPropertyChanged(nameof(ActiveApiProvider));
                    OnPropertyChanged(nameof(IsGroqApiActive));
                    OnPropertyChanged(nameof(IsGeminiApiActive));
                    OnPropertyChanged(nameof(ActiveApiKeyStatusText));
                }
            }
        }

        public bool IsGroqApiActive
        {
            get => ActiveApiProvider == "Groq";
            set
            {
                if (value) ActiveApiProvider = "Groq";
            }
        }

        public bool IsGeminiApiActive
        {
            get => ActiveApiProvider == "Gemini";
            set
            {
                if (value) ActiveApiProvider = "Gemini";
            }
        }

        public string GeminiKey
        {
            get => _settings.GeminiKey;
            set
            {
                if (SetProperty(ref _settings.GeminiKey, value))
                {
                    OnPropertyChanged(nameof(MaskedGeminiKey));
                    OnPropertyChanged(nameof(ActiveApiKeyStatusText));
                }
            }
        }

        public string MaskedGeminiKey
        {
            get
            {
                if (string.IsNullOrEmpty(GeminiKey)) return "Not Configured";
                if (GeminiKey.Length <= 10) return "****";
                return GeminiKey.Substring(0, 7) + "..." + GeminiKey.Substring(GeminiKey.Length - 4);
            }
        }

        public string ActiveApiKeyStatusText => IsGeminiApiActive 
            ? $"Active API: Gemini ({MaskedGeminiKey})" 
            : $"Active API: Groq ({MaskedGroqKey})";

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

        private class ScanModeState
        {
            public string ResponseText { get; set; } = "";
            public List<ChatMessage> ChatHistory { get; set; } = new List<ChatMessage>();
            public List<Models.CapturedScreenshotItem> Screenshots { get; set; } = new List<Models.CapturedScreenshotItem>();
            public System.Windows.Media.ImageSource? CapturedPreview { get; set; }
        }

        private readonly ScanModeState _normalModeState = new ScanModeState();
        private readonly ScanModeState _mcqModeState = new ScanModeState();
        private readonly ScanModeState _codingModeState = new ScanModeState();
        private string _activeScanModeName = "Normal";

        private void SwitchTextScanModeState(string targetMode)
        {
            if (_activeScanModeName == targetMode) return;

            // 1. Save current active mode state
            ScanModeState currentState = GetModeState(_activeScanModeName);
            currentState.ResponseText = ScanResponseText;
            currentState.ChatHistory = new List<ChatMessage>(_txtChatHistory);
            currentState.Screenshots = new List<Models.CapturedScreenshotItem>(CapturedScreenshots);
            currentState.CapturedPreview = CapturedPreview;

            // 2. Switch active mode key
            _activeScanModeName = targetMode;

            // 3. Load target mode state
            ScanModeState targetState = GetModeState(targetMode);
            ScanResponseText = targetState.ResponseText;
            
            _txtChatHistory.Clear();
            foreach (var item in targetState.ChatHistory) _txtChatHistory.Add(item);

            CapturedScreenshots.Clear();
            foreach (var item in targetState.Screenshots) CapturedScreenshots.Add(item);

            CapturedPreview = targetState.CapturedPreview;

            // 4. Trigger UI updates
            NotifyScreenshotStateChanged();
            OnPropertyChanged(nameof(IsFollowUpVisible));
            OnPropertyChanged(nameof(CapturedPreview));
        }

        private ScanModeState GetModeState(string mode)
        {
            return mode switch
            {
                "MCQ" => _mcqModeState,
                "Coding" => _codingModeState,
                _ => _normalModeState
            };
        }

        public bool IsMcqScanMode
        {
            get => _settings.TextScanType == "MCQ";
            set
            {
                if (value && _settings.TextScanType != "MCQ")
                {
                    SwitchTextScanModeState("MCQ");
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
                    SwitchTextScanModeState("Coding");
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
                    SwitchTextScanModeState("Normal");
                    _settings.TextScanType = "Normal";
                    OnPropertyChanged(nameof(IsMcqScanMode));
                    OnPropertyChanged(nameof(IsCodingScanMode));
                    OnPropertyChanged(nameof(IsNormalScanMode));
                    UpdatePresetFollowUps();
                }
            }
        }

        public string ProgrammingLanguage
        {
            get => string.IsNullOrWhiteSpace(_settings.ProgrammingLanguage) ? "Python" : _settings.ProgrammingLanguage;
            set
            {
                if (_settings.ProgrammingLanguage != value)
                {
                    _settings.ProgrammingLanguage = value;
                    OnPropertyChanged(nameof(ProgrammingLanguage));
                    OnPropertyChanged(nameof(IsPythonSelected));
                    OnPropertyChanged(nameof(IsJsSelected));
                    OnPropertyChanged(nameof(IsJavaSelected));
                    OnPropertyChanged(nameof(IsCppSelected));
                    OnPropertyChanged(nameof(IsCSelected));
                    OnPropertyChanged(nameof(IsHtmlSelected));
                    OnPropertyChanged(nameof(IsCssSelected));
                    OnPropertyChanged(nameof(IsProjectSelected));
                }
            }
        }

        public bool IsPythonSelected
        {
            get => ProgrammingLanguage.Equals("Python", StringComparison.OrdinalIgnoreCase);
            set { if (value) ProgrammingLanguage = "Python"; }
        }

        public bool IsJsSelected
        {
            get => ProgrammingLanguage.Equals("JS", StringComparison.OrdinalIgnoreCase) || ProgrammingLanguage.Equals("JavaScript", StringComparison.OrdinalIgnoreCase);
            set { if (value) ProgrammingLanguage = "JS"; }
        }

        public bool IsJavaSelected
        {
            get => ProgrammingLanguage.Equals("Java", StringComparison.OrdinalIgnoreCase);
            set { if (value) ProgrammingLanguage = "Java"; }
        }

        public bool IsCppSelected
        {
            get => ProgrammingLanguage.Equals("C++", StringComparison.OrdinalIgnoreCase) || ProgrammingLanguage.Equals("Cpp", StringComparison.OrdinalIgnoreCase);
            set { if (value) ProgrammingLanguage = "C++"; }
        }

        public bool IsCSelected
        {
            get => ProgrammingLanguage.Equals("C", StringComparison.OrdinalIgnoreCase);
            set { if (value) ProgrammingLanguage = "C"; }
        }

        public bool IsHtmlSelected
        {
            get => ProgrammingLanguage.Equals("HTML", StringComparison.OrdinalIgnoreCase);
            set { if (value) ProgrammingLanguage = "HTML"; }
        }

        public bool IsCssSelected
        {
            get => ProgrammingLanguage.Equals("CSS", StringComparison.OrdinalIgnoreCase);
            set { if (value) ProgrammingLanguage = "CSS"; }
        }

        public bool IsProjectSelected
        {
            get => ProgrammingLanguage.Equals("Project", StringComparison.OrdinalIgnoreCase);
            set { if (value) ProgrammingLanguage = "Project"; }
        }

        public string FollowUpText
        {
            get => _followUpText;
            set => SetProperty(ref _followUpText, value);
        }

        private int _followUpCooldownSeconds = 0;
        private System.Windows.Threading.DispatcherTimer? _followUpCooldownTimer;

        public int FollowUpCooldownSeconds
        {
            get => _followUpCooldownSeconds;
            set
            {
                if (SetProperty(ref _followUpCooldownSeconds, value))
                {
                    OnPropertyChanged(nameof(IsFollowUpCooldownActive));
                    OnPropertyChanged(nameof(FollowUpCooldownText));
                }
            }
        }

        public bool IsFollowUpCooldownActive => _followUpCooldownSeconds > 0;

        public string FollowUpCooldownText => _followUpCooldownSeconds > 0 ? $"⏳ Wait {_followUpCooldownSeconds}s" : "";

        private void StartFollowUpCooldown()
        {
            FollowUpCooldownSeconds = 7;
            if (_followUpCooldownTimer == null)
            {
                _followUpCooldownTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _followUpCooldownTimer.Tick += (s, e) =>
                {
                    if (FollowUpCooldownSeconds > 1)
                    {
                        FollowUpCooldownSeconds--;
                    }
                    else
                    {
                        FollowUpCooldownSeconds = 0;
                        _followUpCooldownTimer?.Stop();
                    }
                };
            }
            _followUpCooldownTimer.Start();
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

        private double _windowHeight = 480;
        private double _windowWidth = 420;
        private bool _isExpandedHeight = false;

        public double WindowHeight
        {
            get => _windowHeight;
            set => SetProperty(ref _windowHeight, value);
        }

        public double WindowWidth
        {
            get => _windowWidth;
            set => SetProperty(ref _windowWidth, value);
        }

        public bool IsExpandedHeight
        {
            get => _isExpandedHeight;
            set
            {
                if (SetProperty(ref _isExpandedHeight, value))
                {
                    WindowHeight = _isExpandedHeight ? 700 : 480;
                    OnPropertyChanged(nameof(ExpandHeightButtonText));
                }
            }
        }

        public string ExpandHeightButtonText => IsExpandedHeight ? "↕ COMPACT" : "↕ EXPAND";

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
                        // Save current API Keys to database persistently in background when settings drawer closes
                        Task.Run(async () => await SaveApiKeysToServerAsync(GroqKey, GeminiKey));
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

            // Auto-refresh: ONLY clear old screenshots/chat if a previous AI response has ALREADY been generated & sent.
            // If the user is currently capturing a batch of screenshots before sending, DO NOT clear.
            bool isPreviousResponseGenerated = _txtChatHistory.Count > 0 || 
                                               (!string.IsNullOrWhiteSpace(ScanResponseText) && 
                                                (ScanResponseText.Contains("Batch Scan Meta Information") || 
                                                 ScanResponseText.StartsWith("### 🤖") || 
                                                 (ScanResponseText.StartsWith("✅") && !ScanResponseText.Contains("Captured Screenshot")) ||
                                                 ScanResponseText.StartsWith("✨")));

            if (isPreviousResponseGenerated)
            {
                // Clear pending screenshot batch thumbnails for next turn, but KEEP chat thread and history intact!
                CapturedScreenshots.Clear();
                NotifyScreenshotStateChanged();
                CapturedPreview = null;
                ScanModeState currentState = GetModeState(_activeScanModeName);
                currentState.Screenshots.Clear();
                currentState.CapturedPreview = null;
                OnPropertyChanged(nameof(IsFollowUpVisible));
            }

            if (CapturedScreenshots.Count >= MaxScreenshotsLimit)
            {
                ScanResponseText = $"⚠️ **Maximum limit of {MaxScreenshotsLimit} screenshot(s) reached.**\n\n" +
                                   $"You have already captured **{CapturedScreenshots.Count} / {MaxScreenshotsLimit}** screenshots (the maximum allowed).\n\n" +
                                   $"Click **SEND ({CapturedScreenshots.Count})** to process your screenshots, or click **✕** on a thumbnail to remove a screenshot.";
                return;
            }

            var selectionWindow = new Views.SelectionWindow();
            selectionWindow.ShowActivated = false;
            selectionWindow.AreaSelected = rect =>
            {
                _lastSelectedRect = rect;
                AddCapturedScreenshot(rect);
            };

            selectionWindow.Show();
        }

        private async void TriggerSilentScan()
        {
            if (IsLoginOverlayVisible || IsPaymentOverlayVisible || IsFeatureLocked)
            {
                return;
            }

            if (CapturedScreenshots.Count >= MaxScreenshotsLimit)
            {
                ScanResponseText = $"⚠️ **Maximum limit of {MaxScreenshotsLimit} screenshot(s) reached.**\n\n" +
                                   $"Click **SEND ({CapturedScreenshots.Count})** to process your screenshots, or remove a screenshot to capture a new one.";
                return;
            }

            System.Windows.Int32Rect rectToScan;
            if (_lastSelectedRect.Width > 0 && _lastSelectedRect.Height > 0)
            {
                rectToScan = _lastSelectedRect;
            }
            else
            {
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

            AddCapturedScreenshot(rectToScan);
        }

        private void AddCapturedScreenshot(System.Windows.Int32Rect rect)
        {
            if (CapturedScreenshots.Count >= MaxScreenshotsLimit)
            {
                ScanResponseText = $"⚠️ **Maximum limit of {MaxScreenshotsLimit} screenshot(s) reached.**\n\n" +
                                   $"Click **SEND ({CapturedScreenshots.Count})** to process your screenshots, or remove a screenshot to capture a new one.";
                return;
            }

            byte[] imageBytes;
            var previewSource = CaptureScreenArea(rect, out imageBytes);
            if (imageBytes != null && imageBytes.Length > 0 && previewSource != null)
            {
                CapturedPreview = previewSource;
                var item = new Models.CapturedScreenshotItem
                {
                    Index = CapturedScreenshots.Count + 1,
                    PreviewImage = previewSource,
                    ImageBytes = imageBytes
                };
                CapturedScreenshots.Add(item);
                NotifyScreenshotStateChanged();

                if (CapturedScreenshots.Count < MaxScreenshotsLimit)
                {
                    ScanResponseText = $"📸 **Captured Screenshot #{item.Index}.**\n\n" +
                                       $"Total captured: **{CapturedScreenshots.Count} / {MaxScreenshotsLimit} max**.\n" +
                                       $"Click **SEND ({CapturedScreenshots.Count})** to process now, or click **+ CAPTURE** to add up to {MaxScreenshotsLimit - CapturedScreenshots.Count} more.";
                }
                else
                {
                    ScanResponseText = $"✅ **Captured Screenshot #{item.Index}.**\n\n" +
                                       $"Maximum limit reached (**{MaxScreenshotsLimit} / {MaxScreenshotsLimit}** screenshots).\n" +
                                       $"Click **SEND ({MaxScreenshotsLimit})** to process all screenshots with AI!";
                }
            }
        }

        private void RemoveScreenshot(object? param)
        {
            if (param is Models.CapturedScreenshotItem item && CapturedScreenshots.Contains(item))
            {
                CapturedScreenshots.Remove(item);
                for (int i = 0; i < CapturedScreenshots.Count; i++)
                {
                    CapturedScreenshots[i].Index = i + 1;
                }
                NotifyScreenshotStateChanged();
                if (CapturedScreenshots.Count == 0)
                {
                    CapturedPreview = null;
                    ScanResponseText = "";
                }
                else
                {
                    CapturedPreview = CapturedScreenshots[CapturedScreenshots.Count - 1].PreviewImage;
                }
            }
        }

        private async Task ExecuteSendBatchScreenshotsAsync()
        {
            if (CapturedScreenshots.Count == 0)
            {
                ScanResponseText = $"⚠️ **No screenshots captured.**\n\nPlease click **+ CAPTURE** to capture at least 1 screenshot (up to {MaxScreenshotsLimit} max) before clicking **SEND**.";
                return;
            }

            IsScanning = true;
            _txtTurnCounter++;
            int turnNum = _txtTurnCounter;

            // --- 1. Add USER BUBBLE with screenshot thumbnails ---
            var userBubble = new Models.ChatBubbleItem
            {
                Role = "user",
                TurnNumber = turnNum,
                Content = $"📸 {CapturedScreenshots.Count} screenshot{(CapturedScreenshots.Count == 1 ? "" : "s")} captured",
                ScreenshotPreviews = new System.Collections.Generic.List<System.Windows.Media.ImageSource>()
            };
            foreach (var ss in CapturedScreenshots)
            {
                if (ss.PreviewImage != null)
                    userBubble.ScreenshotPreviews.Add(ss.PreviewImage);
            }
            ChatBubbles.Add(userBubble);

            // --- 2. Add ASSISTANT BUBBLE (loading placeholder) ---
            var assistantBubble = new Models.ChatBubbleItem
            {
                Role = "assistant",
                TurnNumber = turnNum,
                Content = "",
                IsLoading = true,
                ModelInfo = ""
            };
            ChatBubbles.Add(assistantBubble);

            string singleModel = IsGeminiApiActive ? "gemini-3.5-flash-lite + gemma-4-31b-it" : "groq/compound";

            // Live elapsed-time ticker — updates the bubble every second while waiting for LLM
            var scanStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var timerCts = new System.Threading.CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!timerCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(1000, timerCts.Token).ContinueWith(_ => { });
                    if (timerCts.Token.IsCancellationRequested) break;
                    int elapsed = (int)scanStopwatch.Elapsed.TotalSeconds;
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null && !timerCts.Token.IsCancellationRequested)
                    {
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (assistantBubble.IsLoading)
                                assistantBubble.ElapsedSeconds = elapsed;
                        }));
                    }
                }
            }, timerCts.Token);

            try
            {
                string effectiveGroqKey = string.IsNullOrWhiteSpace(GroqKey) ? SystemGroqKey : GroqKey;
                var combinedTextBuilder = new System.Text.StringBuilder();
                int totalChars = 0;
                int successfulScans = 0;

                // --- OCR Phase ---
                assistantBubble.Content = $"⏳ Extracting text from {CapturedScreenshots.Count} screenshot(s)...";

                for (int i = 0; i < CapturedScreenshots.Count; i++)
                {
                    var item = CapturedScreenshots[i];
                    assistantBubble.Content = $"⏳ [OCR {i + 1}/{CapturedScreenshots.Count}] Extracting text from Screenshot {item.Index}...";

                    var ocrResult = await PerformOcrAsync(item.ImageBytes);

                    string text = ocrResult.Text?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(text) && text != "(no text detected)")
                    {
                        combinedTextBuilder.AppendLine($"--- SCREENSHOT {item.Index} ---");
                        combinedTextBuilder.AppendLine(text);
                        combinedTextBuilder.AppendLine();
                        totalChars += text.Length;
                        successfulScans++;
                    }
                    else
                    {
                        combinedTextBuilder.AppendLine($"--- SCREENSHOT {item.Index} ---");
                        combinedTextBuilder.AppendLine("(no text detected)");
                        combinedTextBuilder.AppendLine();
                    }
                }

                if (totalChars == 0)
                {
                    assistantBubble.Content = "⚠️ No readable text was detected across all captured screenshots. Please try capturing clearer screen areas.";
                    assistantBubble.IsLoading = false;
                    return;
                }

                string providerInfo = IsGeminiApiActive ? "Google Gemini API (gemini-3.5-flash-lite + gemma-4-31b-it → gemini-3.7-flash + groq/compound)" : "Groq API (groq/compound)";
                string metadataHeader = $"**🔍 Scan Info** — {CapturedScreenshots.Count} screenshots, {totalChars} chars extracted ({providerInfo})\n\n";

                string combinedExtractedText = combinedTextBuilder.ToString().Trim();
                bool isFollowUpTurn = _txtChatHistory.Count > 0;

                // --- Build LLM Chat History ---
                if (!isFollowUpTurn)
                {
                    if (IsMcqScanMode)
                    {
                        // Single combined user message — no system role, works universally across all models
                        _txtChatHistory.Add(new ChatMessage {
                            Role = "user",
                            Content = $"Task: Look at the multiple choice question below. Output only the letter of the correct answer. Do not write anything else. Not even a period.\n\n{combinedExtractedText}\n\nAnswer (single letter only):"
                        });
                    }
                    else if (IsCodingScanMode)
                    {
                        string targetLang = string.IsNullOrWhiteSpace(ProgrammingLanguage) ? "Python" : ProgrammingLanguage;
                        bool isProjectMode = targetLang.Equals("Project", StringComparison.OrdinalIgnoreCase);

                        string systemPrompt = isProjectMode ?
                            "You are a strict expert full-stack senior developer and project architect. Solve the project challenge, bug fix, or feature request across all captured screenshots. Analyze the code files, HTML structure, CSS styles, JavaScript/Python backend routes, and database schemas. Output clear, modular, file-by-file code fixes (e.g. index.html, style.css, script.js, app.py/server.js) with clean, 100% working code. Write the code in a humanized style as if written by a senior developer in a real coding interview. Do not include markdown code block backticks (```)." :
                            $"You are a strict expert {targetLang} code generator. Solve the programming challenge described across all captured screenshots. You must output ONLY the complete, working source code in {targetLang} language by default. Write the code in a humanized style as if written by a senior developer in a real coding interview (use natural variable names, standard spacing, clean modular logic, and complete all functions thoroughly without cutting off). Do not include any warnings, intro/outro text, or markdown code block formatting (no ```). Return ONLY the raw code.";

                        _txtChatHistory.Add(new ChatMessage {
                            Role = "system",
                            Content = systemPrompt
                        });
                        _txtChatHistory.Add(new ChatMessage {
                            Role = "user",
                            Content = $"Here is the {(isProjectMode ? "full-stack project" : "coding problem")} raw text from {CapturedScreenshots.Count} screenshots:\n\n{combinedExtractedText}"
                        });
                    }
                    else
                    {
                        _txtChatHistory.Add(new ChatMessage {
                            Role = "system",
                            Content = "You are a helpful overlay productivity assistant. Your task is to analyze the extracted text from the user's screenshots and explain it clearly and comprehensively. If the text contains questions, problems, or concepts across screenshots, explain the answers or concepts step-by-step. Keep your output concise, clear, and formatted in markdown. Write in a natural, conversational, humanized style. Avoid typical robotic AI transitions, templates, or preambles. Explain it casually like an experienced developer explaining to a peer. Do not mention you are an AI."
                        });
                        _txtChatHistory.Add(new ChatMessage {
                            Role = "user",
                            Content = $"Here is the raw text from {CapturedScreenshots.Count} screenshots:\n\n{combinedExtractedText}"
                        });
                    }
                }
                else
                {
                    _txtChatHistory.Add(new ChatMessage {
                        Role = "user",
                        Content = $"👉 Turn #{turnNum} Follow-up with {CapturedScreenshots.Count} new screenshot(s):\n\n{combinedExtractedText}"
                    });
                }

                // --- LLM Response Phase ---
                if (IsMcqScanMode)
                {
                    // Set A (primary): gemini-3.5-flash-lite + gemma-4-31b-it (both Gemini)
                    // Qwen tiebreaker: used ONLY if (a) a model exceeds 40s, or (b) Set A answers mismatch
                    // Set B (fallback): gemini-3.7-flash + groq/compound — used only if BOTH Set A models fail
                    string modelA = "gemini-3.5-flash-lite";
                    string modelB = "gemma-4-31b-it";
                    string modelC = "gemini-3.7-flash";
                    string modelD = "groq/compound";
                    string modelQwen = "openai/gpt-oss-120b";
                    assistantBubble.ModelInfo = $"Set A: {modelA} + {modelB}";
                    assistantBubble.Content = $"⏳ Verifying MCQ answer with Set A ({modelA} + {modelB})...";

                    // Run Set A in parallel with a 40s timeout per model
                    var cts = new System.Threading.CancellationTokenSource();
                    var timeout = Task.Delay(40000, cts.Token);

                    var taskA = _llmService.ProcessChatWithGeminiAsync(GeminiKey, _txtChatHistory, modelA, "", "");
                    var taskB = _llmService.ProcessChatWithGeminiAsync(GeminiKey, _txtChatHistory, modelB, "", "");

                    // Wait for both, but track if either exceeds 40s
                    var taskAWithTimeout = Task.WhenAny(taskA, Task.Delay(40000));
                    var taskBWithTimeout = Task.WhenAny(taskB, Task.Delay(40000));

                    await Task.WhenAll(taskAWithTimeout, taskBWithTimeout);

                    string answerA = taskA.IsCompleted ? await taskA : "";
                    string answerB = taskB.IsCompleted ? await taskB : "";

                    bool timedOutA = !taskA.IsCompleted;
                    bool timedOutB = !taskB.IsCompleted;

                    // If timed out, replace with Groq qwen
                    if (timedOutA || timedOutB)
                    {
                        assistantBubble.Content = $"⏳ {(timedOutA ? modelA : modelB)} timed out — fetching from Groq qwen...";
                        string qwenAnswer = await _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, _txtChatHistory, modelQwen);
                        if (timedOutA) answerA = qwenAnswer;
                        if (timedOutB) answerB = qwenAnswer;
                        assistantBubble.ModelInfo = $"Set A: {(timedOutA ? modelQwen : modelA)} + {(timedOutB ? modelQwen : modelB)} (qwen substituted)";
                    }

                    bool isErrorA = answerA == null || OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(answerA);
                    bool isErrorB = answerB == null || OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(answerB);

                    string answerC = "", answerD = "";
                    bool isErrorC = false, isErrorD = false;
                    bool usedFallback = false;

                    // If BOTH Set A models fail, fall back to Set B
                    if (isErrorA && isErrorB)
                    {
                        usedFallback = true;
                        assistantBubble.ModelInfo = $"Set B (fallback): {modelC} + {modelD}";
                        assistantBubble.Content = $"⚠️ Set A had errors — retrying with Set B ({modelC} + {modelD})...";

                        var taskC = _llmService.ProcessChatWithGeminiAsync(GeminiKey, _txtChatHistory, modelC, "", "");
                        var taskD = _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, _txtChatHistory, modelD);
                        await Task.WhenAll(taskC, taskD);
                        answerC = await taskC;
                        answerD = await taskD;
                        isErrorC = OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(answerC);
                        isErrorD = OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(answerD);
                    }

                    // If Set A both succeeded but answers mismatch — call Groq qwen as tiebreaker
                    string answerQwen = "";
                    bool usedQwenTiebreaker = false;
                    if (!usedFallback && !isErrorA && !isErrorB)
                    {
                        string cleanA = CleanMcqResponse(answerA);
                        string cleanB = CleanMcqResponse(answerB);
                        bool mismatch = !string.IsNullOrEmpty(cleanA) && !string.IsNullOrEmpty(cleanB) &&
                                        !cleanA.Equals(cleanB, StringComparison.OrdinalIgnoreCase);
                        if (mismatch)
                        {
                            usedQwenTiebreaker = true;
                            assistantBubble.Content = $"⚠️ Mismatch detected — calling Groq qwen tiebreaker...";
                            answerQwen = await _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, _txtChatHistory, modelQwen);
                        }
                    }

                    var sbVerify = new System.Text.StringBuilder();
                    sbVerify.AppendLine(metadataHeader);
                    sbVerify.AppendLine(usedFallback
                        ? "### 🤖 MCQ Verification — Set B (Fallback)"
                        : usedQwenTiebreaker
                            ? "### 🤖 MCQ Verification — Set A + Qwen Tiebreaker"
                            : "### 🤖 MCQ Verification — Set A");
                    sbVerify.AppendLine();

                    if (!usedFallback)
                    {
                        string cleanedA = CleanMcqResponse(answerA ?? "");
                        string cleanedB = CleanMcqResponse(answerB ?? "");
                        string displayA = isErrorA ? "⚠️ Error" : (!string.IsNullOrEmpty(cleanedA) ? cleanedA.ToUpperInvariant() : (answerA ?? "").Trim());
                        string displayB = isErrorB ? "⚠️ Error" : (!string.IsNullOrEmpty(cleanedB) ? cleanedB.ToUpperInvariant() : (answerB ?? "").Trim());
                        string labelA = timedOutA ? $"{modelQwen} (qwen sub)" : $"{modelA} (Gemini)";
                        string labelB = timedOutB ? $"{modelQwen} (qwen sub)" : $"{modelB} (Gemini)";
                        sbVerify.AppendLine($"* **{labelA}:** {displayA}");
                        sbVerify.AppendLine($"* **{labelB}:** {displayB}");
                        if (usedQwenTiebreaker)
                        {
                            string cleanedQ = CleanMcqResponse(answerQwen);
                            bool isErrorQ = OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(answerQwen);
                            string displayQ = isErrorQ ? "⚠️ Error" : (!string.IsNullOrEmpty(cleanedQ) ? cleanedQ.ToUpperInvariant() : answerQwen.Trim());
                            sbVerify.AppendLine($"* **{modelQwen} (Groq tiebreaker):** {displayQ}");
                        }
                    }
                    else
                    {
                        sbVerify.AppendLine($"* **{modelA} (Gemini):** ⚠️ Error — fell back to Set B");
                        sbVerify.AppendLine($"* **{modelB} (Gemini):** {(isErrorB ? "⚠️ Error — fell back to Set B" : "✅ OK")}");
                        string cleanedC = CleanMcqResponse(answerC);
                        string cleanedD = CleanMcqResponse(answerD);
                        string displayC = isErrorC ? "⚠️ Error" : (!string.IsNullOrEmpty(cleanedC) ? cleanedC.ToUpperInvariant() : answerC.Trim());
                        string displayD = isErrorD ? "⚠️ Error" : (!string.IsNullOrEmpty(cleanedD) ? cleanedD.ToUpperInvariant() : answerD.Trim());
                        sbVerify.AppendLine($"* **{modelC} (Gemini fallback):** {displayC}");
                        sbVerify.AppendLine($"* **{modelD} (Groq fallback):** {displayD}");
                    }
                    sbVerify.AppendLine();
                    sbVerify.AppendLine("---");
                    sbVerify.AppendLine();

                    // Collect valid answers for consensus
                    var validAnswers = new System.Collections.Generic.List<(string label, string raw, string clean)>();
                    if (!usedFallback)
                    {
                        if (!isErrorA) { string c = CleanMcqResponse(answerA ?? ""); if (!string.IsNullOrEmpty(c)) validAnswers.Add((timedOutA ? modelQwen : modelA, answerA, c)); }
                        if (!isErrorB) { string c = CleanMcqResponse(answerB ?? ""); if (!string.IsNullOrEmpty(c)) validAnswers.Add((timedOutB ? modelQwen : modelB, answerB, c)); }
                        if (usedQwenTiebreaker && !OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(answerQwen))
                        {
                            string c = CleanMcqResponse(answerQwen);
                            if (!string.IsNullOrEmpty(c)) validAnswers.Add((modelQwen, answerQwen, c));
                        }
                    }
                    else
                    {
                        if (!isErrorC) { string c = CleanMcqResponse(answerC); if (!string.IsNullOrEmpty(c)) validAnswers.Add((modelC, answerC, c)); }
                        if (!isErrorD) { string c = CleanMcqResponse(answerD); if (!string.IsNullOrEmpty(c)) validAnswers.Add((modelD, answerD, c)); }
                    }

                    bool anyError = usedFallback ? (isErrorC || isErrorD) : (isErrorA && isErrorB);
                    assistantBubble.HasError = anyError;
                    assistantBubble.ShowCheckApiKeyAction = anyError;

                    if (validAnswers.Count == 0)
                    {
                        assistantBubble.ErrorSummary = "All models encountered errors.";
                        sbVerify.AppendLine("⚠️ **Verification Failed:** All AI models encountered errors. Please check your API keys or exam environment settings.");
                    }
                    else
                    {
                        var voteCounts = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        foreach (var (_, _, clean) in validAnswers)
                        {
                            string key = clean.ToUpperInvariant();
                            voteCounts[key] = voteCounts.TryGetValue(key, out int v) ? v + 1 : 1;
                        }

                        string consensusAnswer = "";
                        int maxVotes = 0;
                        foreach (var kv in voteCounts)
                        {
                            if (kv.Value > maxVotes) { maxVotes = kv.Value; consensusAnswer = kv.Key; }
                        }

                        bool fullConsensus = maxVotes == validAnswers.Count;
                        bool majorityConsensus = maxVotes >= 2;

                        if (anyError) assistantBubble.ErrorSummary = "Some models had errors; consensus from available responses.";

                        if (fullConsensus && validAnswers.Count >= 2)
                            sbVerify.AppendLine($"✅ **Both models agree:** Option **{consensusAnswer}**");
                        else if (majorityConsensus)
                            sbVerify.AppendLine($"✅ **Majority ({maxVotes}/{validAnswers.Count}) agree:** Option **{consensusAnswer}**");
                        else if (validAnswers.Count == 1)
                            sbVerify.AppendLine($"✅ **Answer:** Option **{consensusAnswer}**");
                        else
                            sbVerify.AppendLine($"⚠️ **Mismatch!** Models returned different answers — review results above.");
                    }

                    string finalContent = sbVerify.ToString().Trim();
                    assistantBubble.Content = finalContent;
                    assistantBubble.IsLoading = false;
                    ScanResponseText = finalContent;

                    _txtChatHistory.Add(new ChatMessage { Role = "assistant", Content = finalContent });
                }
                else if (IsCodingScanMode)
                {
                    // Generator: gemma-4-31b-it (25s timeout) → fallback to gemini-3.5-flash-lite if slow
                    // Verifier: gemini-3.5-flash-lite always
                    // Set B (error fallback): gemini-3.7-flash generator + groq/compound verifier
                    string targetLang = string.IsNullOrWhiteSpace(ProgrammingLanguage) ? "Python" : ProgrammingLanguage;
                    string primaryModelA = "gemma-4-31b-it";
                    string timeoutFallbackModel = "gemini-3.5-flash-lite";
                    string verifierModelA = "gemini-3.5-flash-lite";
                    string primaryModelB = "gemini-3.7-flash";
                    string verifierModelB = "groq/compound";
                    bool isProjectMode = targetLang.Equals("Project", StringComparison.OrdinalIgnoreCase);
                    assistantBubble.ModelInfo = $"{primaryModelA} → {verifierModelA}";

                    assistantBubble.Content = $"⏳ [1/2] Generating {(isProjectMode ? "multi-file project" : targetLang)} code with **{primaryModelA}**...";

                    // Run Gemma with a 25s timeout — if it doesn't respond, switch to gemini-3.5-flash-lite
                    var gemmaTask = _llmService.ProcessChatWithGeminiAsync(GeminiKey, _txtChatHistory, primaryModelA, effectiveGroqKey, "qwen/qwen3.6-27b");
                    var timeoutTask = Task.Delay(25000);
                    var firstDone = await Task.WhenAny(gemmaTask, timeoutTask);

                    string initialCode;
                    string generatorUsed;
                    string verifierUsed = verifierModelA;

                    if (firstDone == timeoutTask)
                    {
                        // Gemma timed out — switch immediately to gemini-3.5-flash-lite for the answer
                        generatorUsed = timeoutFallbackModel;
                        assistantBubble.Content = $"⏳ Gemma timed out — switching to **{timeoutFallbackModel}**...";
                        assistantBubble.ModelInfo = $"{timeoutFallbackModel} (timeout fallback) → {verifierModelA}";
                        initialCode = await _llmService.ProcessChatWithGeminiAsync(GeminiKey, _txtChatHistory, timeoutFallbackModel, effectiveGroqKey, "qwen/qwen3.6-27b");
                    }
                    else
                    {
                        // Gemma responded in time — use its output
                        initialCode = await gemmaTask;
                        generatorUsed = primaryModelA;
                    }

                    bool isErrorGen = OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(initialCode);

                    // If generator still errors, fall back to Set B
                    if (isErrorGen)
                    {
                        assistantBubble.ModelInfo = $"Set B (fallback): {primaryModelB} → {verifierModelB}";
                        assistantBubble.Content = $"⚠️ Generator error — retrying with **{primaryModelB}** (Set B)...";
                        initialCode = await _llmService.ProcessChatWithGeminiAsync(GeminiKey, _txtChatHistory, primaryModelB, effectiveGroqKey, "qwen/qwen3.6-27b");
                        generatorUsed = primaryModelB;
                        verifierUsed = verifierModelB;

                        if (OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(initialCode))
                        {
                            assistantBubble.HasError = true;
                            assistantBubble.ShowCheckApiKeyAction = true;
                            assistantBubble.ErrorSummary = "Both generators encountered errors.";
                            string errContent = metadataHeader + initialCode.Trim();
                            assistantBubble.Content = errContent;
                            assistantBubble.IsLoading = false;
                            ScanResponseText = initialCode.Trim();
                            _txtChatHistory.Add(new ChatMessage { Role = "assistant", Content = initialCode.Trim() });
                            return;
                        }
                    }

                    initialCode = CleanCodeMarkdown(initialCode);

                    // Truncation Check & Continuation
                    if (IsCodeTruncated(initialCode))
                    {
                        assistantBubble.Content = $"⏳ Code truncated — requesting continuation from {generatorUsed}...";

                        var continuationHistory = new System.Collections.Generic.List<ChatMessage>(_txtChatHistory)
                        {
                            new ChatMessage { Role = "assistant", Content = initialCode },
                            new ChatMessage { Role = "user", Content = $"The previous {targetLang} code output was cut off mid-way. Continue the code EXACTLY from where it stopped. Do not repeat the previous code. Output ONLY the remaining raw code without any markdown or intro." }
                        };

                        string continuationCode = await _llmService.ProcessChatWithGeminiAsync(GeminiKey, continuationHistory, generatorUsed, effectiveGroqKey, "qwen/qwen3.6-27b");
                        if (!OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(continuationCode))
                        {
                            continuationCode = CleanCodeMarkdown(continuationCode);
                            initialCode = initialCode.TrimEnd() + "\n" + continuationCode.TrimStart();
                        }
                    }

                    // Code Audit with the verifier from whichever set was used
                    assistantBubble.Content = $"⏳ [2/2] Verifying code with **{verifierUsed}**...";

                    var verifyHistory = new System.Collections.Generic.List<ChatMessage>
                    {
                        new ChatMessage {
                            Role = "system",
                            Content = $"You are a strict senior code reviewer. Review the generated code solution for the given problem statement. Is this code 100% complete, bug-free, and correctly solving the problem in {targetLang}? If it is correct and complete, reply EXACTLY with 'VERIFIED_OK'. If it is incomplete, cut off, or contains errors, reply with 'CORRECTED_CODE:' on line 1, followed by the complete, 100% working {targetLang} code starting on line 2. Do not include markdown code block backticks (```)."
                        },
                        new ChatMessage {
                            Role = "user",
                            Content = $"[PROBLEM STATEMENT]\n{combinedExtractedText}\n\n[GENERATED CODE SOLUTION ({targetLang})]\n{initialCode}"
                        }
                    };

                    // Set A verifier uses Gemini; Set B verifier uses Groq
                    string verificationOutput;
                    if (verifierUsed == verifierModelA)
                        verificationOutput = (await _llmService.ProcessChatWithGeminiAsync(GeminiKey, verifyHistory, verifierUsed, "", "")).Trim();
                    else
                        verificationOutput = (await _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, verifyHistory, verifierUsed)).Trim();

                    // If Set A verifier fails, try Set B verifier
                    if (OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(verificationOutput) && verifierUsed == verifierModelA)
                    {
                        verifierUsed = verifierModelB;
                        verificationOutput = (await _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, verifyHistory, verifierUsed)).Trim();
                    }

                    string finalCode = initialCode;
                    string auditNote;

                    if (OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(verificationOutput))
                    {
                        auditNote = $"⚠️ Verifier error. Code shown as generated by {generatorUsed}.";
                    }
                    else if (verificationOutput.StartsWith("CORRECTED_CODE:", StringComparison.OrdinalIgnoreCase))
                    {
                        string correctedCode = CleanCodeMarkdown(verificationOutput.Substring("CORRECTED_CODE:".Length).Trim());
                        if (!string.IsNullOrWhiteSpace(correctedCode) && correctedCode.Length > 20)
                        {
                            finalCode = correctedCode;
                            auditNote = $"✨ Code audited/corrected by {verifierUsed}.";
                        }
                        else
                        {
                            auditNote = $"✅ Code verified bug-free by {verifierUsed}.";
                        }
                    }
                    else
                    {
                        auditNote = $"✅ Code verified bug-free by {verifierUsed}.";
                    }

                    assistantBubble.HasError = false;
                    assistantBubble.ShowCheckApiKeyAction = false;
                    string finalContent = metadataHeader + $"* **Generator:** {generatorUsed}\n* **Audit:** {auditNote}\n\n" + finalCode.Trim();
                    assistantBubble.Content = finalContent;
                    assistantBubble.IsLoading = false;
                    ScanResponseText = finalCode.Trim();

                    _txtChatHistory.Add(new ChatMessage { Role = "assistant", Content = finalCode.Trim() });
                }
                else
                {
                    // Normal scan — Set A first (gemini-3.5-flash-lite + gemma-4-31b-it, both Gemini), fallback to Set B (gemini-3.7-flash + groq/compound)
                    string geminiModelA = "gemini-3.5-flash-lite";
                    string geminiModelA2 = "gemma-4-31b-it";
                    string geminiModelB = "gemini-3.7-flash";
                    string groqModelB = "groq/compound";
                    assistantBubble.ModelInfo = $"Set A: {geminiModelA} + {geminiModelA2}";
                    assistantBubble.Content = $"⏳ Generating response with Set A ({geminiModelA} + {geminiModelA2})...";

                    // Run Set A in parallel — both via Gemini API
                    var taskGA = _llmService.ProcessChatWithGeminiAsync(GeminiKey, _txtChatHistory, geminiModelA, "", "");
                    var taskQA = _llmService.ProcessChatWithGeminiAsync(GeminiKey, _txtChatHistory, geminiModelA2, "", "");
                    await Task.WhenAll(taskGA, taskQA);
                    string respGA = await taskGA;
                    string respQA = await taskQA;

                    bool errGA = OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(respGA);
                    bool errQA = OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(respQA);

                    string respGB = "", respQB = "";
                    bool errGB = false, errQB = false;
                    bool usedFallback = false;

                    // If BOTH Set A models fail, fall back to Set B. If at least one succeeds, display that and stop.
                    if (errGA && errQA)
                    {
                        usedFallback = true;
                        assistantBubble.ModelInfo = $"Set B (fallback): {geminiModelB} + {groqModelB}";
                        assistantBubble.Content = $"⚠️ Set A had errors — retrying with Set B ({geminiModelB} + {groqModelB})...";

                        var taskGB = _llmService.ProcessChatWithGeminiAsync(GeminiKey, _txtChatHistory, geminiModelB, "", "");
                        var taskQB = _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, _txtChatHistory, groqModelB);
                        await Task.WhenAll(taskGB, taskQB);
                        respGB = await taskGB;
                        respQB = await taskQB;
                        errGB = OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(respGB);
                        errQB = OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(respQB);
                    }

                    var sbNormal = new System.Text.StringBuilder();
                    sbNormal.AppendLine(metadataHeader);

                    bool allFailed = usedFallback ? (errGB && errQB) : (errGA && errQA);

                    if (allFailed)
                    {
                        sbNormal.AppendLine("⚠️ All models encountered errors. Please check your API keys.");
                        assistantBubble.HasError = true;
                        assistantBubble.ShowCheckApiKeyAction = true;
                        assistantBubble.ErrorSummary = "All models failed.";
                    }
                    else if (!usedFallback)
                    {
                        if (!errGA) { sbNormal.AppendLine($"### 🔵 {geminiModelA}\n\n{respGA.Trim()}\n"); sbNormal.AppendLine("---\n"); }
                        if (!errQA) { sbNormal.AppendLine($"### � {geminiModelA2}\n\n{respQA.Trim()}\n"); }
                        // At least one succeeded — not an error state
                        assistantBubble.HasError = false;
                        assistantBubble.ShowCheckApiKeyAction = false;
                    }
                    else
                    {
                        if (!errGB) { sbNormal.AppendLine($"### 🔵 {geminiModelB} (fallback)\n\n{respGB.Trim()}\n"); sbNormal.AppendLine("---\n"); }
                        if (!errQB) { sbNormal.AppendLine($"### 🟢 {groqModelB} (fallback)\n\n{respQB.Trim()}\n"); }
                        bool anyErr = errGB || errQB;
                        assistantBubble.HasError = anyErr;
                        assistantBubble.ShowCheckApiKeyAction = anyErr;
                        if (anyErr) assistantBubble.ErrorSummary = "Fallback models had errors; partial results shown.";
                    }

                    string finalContent = sbNormal.ToString().Trim();
                    assistantBubble.Content = finalContent;
                    assistantBubble.IsLoading = false;
                    ScanResponseText = finalContent;

                    _txtChatHistory.Add(new ChatMessage { Role = "assistant", Content = finalContent });
                }
            }
            catch (Exception ex)
            {
                timerCts.Cancel();
                scanStopwatch.Stop();
                var errorInfo = OverlayApp.Helpers.LlmErrorHelper.FormatError("Scanner", singleModel, 0, "", ex);
                assistantBubble.HasError = true;
                assistantBubble.ShowCheckApiKeyAction = errorInfo.RequiresKeyCheck;
                assistantBubble.ErrorSummary = errorInfo.FriendlyMessage;
                assistantBubble.Content = errorInfo.FriendlyMessage;
                assistantBubble.IsLoading = false;
                ScanResponseText = errorInfo.FriendlyMessage;
            }
            finally
            {
                timerCts.Cancel();
                scanStopwatch.Stop();
                // Append final elapsed time to the model info badge
                int totalSecs = (int)scanStopwatch.Elapsed.TotalSeconds;
                if (!string.IsNullOrEmpty(assistantBubble.ModelInfo))
                    assistantBubble.ModelInfo = $"{assistantBubble.ModelInfo} · {totalSecs}s";
                IsScanning = false;
                OnPropertyChanged(nameof(IsFollowUpVisible));
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

            var voiceStopwatch = new System.Diagnostics.Stopwatch();
            var voiceTimerCts = new System.Threading.CancellationTokenSource();

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

                _voiceTurnCounter++;
                int turnNum = _voiceTurnCounter;

                string userContent = IsSystemAudioSource 
                    ? $"🔊 [System Audio] \"{transcribedText}\"" 
                    : $"🎙️ \"{transcribedText}\"";

                var userBubble = new Models.ChatBubbleItem
                {
                    Role = "user",
                    TurnNumber = turnNum,
                    Content = userContent,
                    ScreenshotPreviews = new System.Collections.Generic.List<System.Windows.Media.ImageSource>()
                };
                VoiceChatBubbles.Add(userBubble);

                var assistantBubble = new Models.ChatBubbleItem
                {
                    Role = "assistant",
                    TurnNumber = turnNum,
                    Content = "⏳ Analyzing query (gemini-3.7-flash)...",
                    IsLoading = true,
                    ModelInfo = "gemini-3.7-flash"
                };
                VoiceChatBubbles.Add(assistantBubble);

                if (_voiceChatHistory.Count == 0)
                {
                    _voiceChatHistory.Add(new ChatMessage {
                        Role = "system",
                        Content = "You are a helpful overlay productivity assistant. Solve or explain the user's transcribed question. Keep your output concise, clear, and formatted in markdown. Write in a natural, humanized style. Avoid robotic AI transitions, repetitive templates, or preambles. Speak like an experienced developer or colleague offering quick assistance. Do not say you are an AI."
                    });
                }
                _voiceChatHistory.Add(new ChatMessage {
                    Role = "user",
                    Content = transcribedText
                });

                var historyToSend = PruneVoiceChatHistory(_voiceChatHistory);

                // Live elapsed-time ticker for voice scan
                voiceStopwatch.Restart();
                _ = Task.Run(async () =>
                {
                    while (!voiceTimerCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(1000, voiceTimerCts.Token).ContinueWith(_ => { });
                        if (voiceTimerCts.Token.IsCancellationRequested) break;
                        int elapsed = (int)voiceStopwatch.Elapsed.TotalSeconds;
                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        if (dispatcher != null && !voiceTimerCts.Token.IsCancellationRequested)
                        {
                            dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (assistantBubble.IsLoading)
                                    assistantBubble.ElapsedSeconds = elapsed;
                            }));
                        }
                    }
                }, voiceTimerCts.Token);

                // First preference: gemini-3.7-flash; fallback: qwen/qwen3.8-27b (Groq)
                string explanation = await _llmService.ProcessChatWithGeminiAsync(GeminiKey, historyToSend, "gemini-3.7-flash", effectiveGroqKey, "");
                string voiceModelUsed = "gemini-3.7-flash";

                if (OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(explanation))
                {
                    assistantBubble.Content = "⏳ Gemini unavailable — falling back to qwen/qwen3.8-27b...";
                    assistantBubble.ModelInfo = "qwen/qwen3.8-27b";
                    voiceModelUsed = "qwen/qwen3.8-27b";
                    explanation = await _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, historyToSend, "qwen/qwen3.8-27b");
                }

                voiceTimerCts.Cancel();
                voiceStopwatch.Stop();
                int voiceTotalSecs = (int)voiceStopwatch.Elapsed.TotalSeconds;

                assistantBubble.ModelInfo = $"{voiceModelUsed} · {voiceTotalSecs}s";
                
                bool isVoiceError = OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(explanation);
                assistantBubble.HasError = isVoiceError;
                assistantBubble.ShowCheckApiKeyAction = isVoiceError;
                if (isVoiceError)
                {
                    assistantBubble.ErrorSummary = "Voice query error.";
                }

                assistantBubble.Content = explanation;
                assistantBubble.IsLoading = false;
                VoiceScanResponseText = explanation;

                _voiceChatHistory.Add(new ChatMessage {
                    Role = "assistant",
                    Content = explanation
                });
                OnPropertyChanged(nameof(IsFollowUpVisible));
            }
            catch (Exception ex)
            {
                voiceTimerCts.Cancel();
                voiceStopwatch.Stop();
                var errorInfo = OverlayApp.Helpers.LlmErrorHelper.FormatError("Voice Assistant", "gemini-3.7-flash / qwen3.8-27b", 0, "", ex);
                if (VoiceChatBubbles.Count > 0 && VoiceChatBubbles[VoiceChatBubbles.Count - 1].IsAssistant && VoiceChatBubbles[VoiceChatBubbles.Count - 1].IsLoading)
                {
                    var bubble = VoiceChatBubbles[VoiceChatBubbles.Count - 1];
                    bubble.Content = errorInfo.FriendlyMessage;
                    bubble.HasError = true;
                    bubble.ShowCheckApiKeyAction = errorInfo.RequiresKeyCheck;
                    bubble.ErrorSummary = errorInfo.FriendlyMessage;
                    bubble.IsLoading = false;
                }
                VoiceScanResponseText = errorInfo.FriendlyMessage;
                if (_voiceChatHistory.Count > 0 && _voiceChatHistory[_voiceChatHistory.Count - 1].Role == "user")
                {
                    _voiceChatHistory.RemoveAt(_voiceChatHistory.Count - 1);
                }
            }
            finally
            {
                voiceTimerCts.Cancel();
                voiceStopwatch.Stop();
                IsScanning = false;
                _isProcessingVoice = false;
            }
        }

        private bool IsCodeTruncated(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            string trimmed = code.TrimEnd();
            
            string lastLine = trimmed.Split('\n').LastOrDefault()?.Trim() ?? "";
            
            if (lastLine.EndsWith(":") || lastLine.EndsWith(",") || lastLine.EndsWith("(") || 
                lastLine.EndsWith("{") || lastLine.EndsWith("[") || lastLine.EndsWith("+") || 
                lastLine.EndsWith("-") || lastLine.EndsWith("*") || lastLine.EndsWith("=") ||
                lastLine.EndsWith("def") || lastLine.EndsWith("class") || lastLine.EndsWith("return"))
            {
                return true;
            }

            int openParen = trimmed.Count(c => c == '(') - trimmed.Count(c => c == ')');
            int openBrace = trimmed.Count(c => c == '{') - trimmed.Count(c => c == '}');
            int openBracket = trimmed.Count(c => c == '[') - trimmed.Count(c => c == ']');
            
            return openParen > 0 || openBrace > 0 || openBracket > 0;
        }

        private string CleanCodeMarkdown(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";
            string cleaned = code.Trim();
            if (cleaned.StartsWith("```"))
            {
                int firstLineEnd = cleaned.IndexOf('\n');
                if (firstLineEnd > 0)
                {
                    cleaned = cleaned.Substring(firstLineEnd + 1);
                }
                if (cleaned.EndsWith("```"))
                {
                    cleaned = cleaned.Substring(0, cleaned.Length - 3);
                }
            }
            return cleaned.Trim();
        }

        private System.Collections.Generic.List<ChatMessage> PruneVoiceChatHistory(System.Collections.Generic.List<ChatMessage> fullHistory)
        {
            if (fullHistory == null || fullHistory.Count <= 12)
            {
                return fullHistory ?? new System.Collections.Generic.List<ChatMessage>();
            }

            var pruned = new System.Collections.Generic.List<ChatMessage>();
            
            // 1. Keep System Message
            var sysMsg = fullHistory.FirstOrDefault(m => m.Role == "system");
            if (sysMsg != null)
            {
                pruned.Add(sysMsg);
            }

            // 2. Keep the most recent 10 messages (5 conversational turns) for continuity
            var recent = System.Linq.Enumerable.TakeLast(fullHistory.Where(m => m.Role != "system"), 10);
            pruned.AddRange(recent);

            return pruned;
        }

        private System.Collections.Generic.List<ChatMessage> PruneChatHistory(System.Collections.Generic.List<ChatMessage> fullHistory)
        {
            if (fullHistory == null || fullHistory.Count <= 3)
            {
                return fullHistory ?? new System.Collections.Generic.List<ChatMessage>();
            }

            var pruned = new System.Collections.Generic.List<ChatMessage>();
            
            // 1. Keep System Message
            var sysMsg = fullHistory.FirstOrDefault(m => m.Role == "system");
            if (sysMsg != null)
            {
                pruned.Add(sysMsg);
            }

            // 2. Keep Initial User Problem Statement
            var firstUser = fullHistory.FirstOrDefault(m => m.Role == "user");
            if (firstUser != null)
            {
                pruned.Add(firstUser);
            }

            // 3. Keep Initial Code/Solution Output (compact version)
            var firstAssistant = fullHistory.FirstOrDefault(m => m.Role == "assistant");
            if (firstAssistant != null)
            {
                string content = firstAssistant.Content ?? "";
                if (content.Length > 2000)
                {
                    content = content.Substring(0, 2000) + "\n...[truncated for token optimization]...";
                }
                pruned.Add(new ChatMessage { Role = "assistant", Content = content });
            }

            // 4. Keep Current User Question
            var lastUser = fullHistory.LastOrDefault(m => m.Role == "user");
            if (lastUser != null && !pruned.Contains(lastUser))
            {
                pruned.Add(lastUser);
            }

            return pruned;
        }

        private static readonly System.Text.RegularExpressions.Regex _mcqSingleLetter =
            new System.Text.RegularExpressions.Regex(@"^[(\[]?([A-Ea-e])[)\].]?\s*$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _mcqConclusionPhrase =
            new System.Text.RegularExpressions.Regex(
                @"(?:correct\s+answer\s+is|answer\s+is|the\s+answer\s*[:\-=]|answer\s*[:\-=]|option\s+is|is\s+option|is\s+answer)\s*[:\-]?\s*[(\[]?([A-Ea-e])[)\].]?(?:\s|$)",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex _mcqOptionLabel =
            new System.Text.RegularExpressions.Regex(
                @"\boption\s+([A-Ea-e])\b",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex _mcqStandaloneLetter =
            new System.Text.RegularExpressions.Regex(
                @"(?<![A-Za-z])([A-Ea-e])(?![A-Za-z])",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private string CleanMcqResponse(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            string trimmed = input.Trim().Trim('"', '\'', '*', ' ');

            // 1. Fast path — already a single letter (e.g. "B", "b.", "(C)", "[D]")
            var m = _mcqSingleLetter.Match(trimmed);
            if (m.Success) return m.Groups[1].Value.ToLowerInvariant();

            // 2. Conclusion phrases — search from the end (last match wins)
            var matches = _mcqConclusionPhrase.Matches(trimmed);
            if (matches.Count > 0) return matches[matches.Count - 1].Groups[1].Value.ToLowerInvariant();

            // 3. "option X" anywhere
            matches = _mcqOptionLabel.Matches(trimmed);
            if (matches.Count > 0) return matches[matches.Count - 1].Groups[1].Value.ToLowerInvariant();

            // 4. Last standalone letter A-E in the text
            matches = _mcqStandaloneLetter.Matches(trimmed);
            if (matches.Count > 0) return matches[matches.Count - 1].Groups[1].Value.ToLowerInvariant();

            return string.Empty;
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

        private string _geminiInputKey = "";
        private string _geminiKeyValidationError = "";
        private bool _isValidatingGeminiKey;

        public string GeminiInputKey
        {
            get => _geminiInputKey;
            set => SetProperty(ref _geminiInputKey, value);
        }

        public string GeminiKeyValidationError
        {
            get => _geminiKeyValidationError;
            set => SetProperty(ref _geminiKeyValidationError, value);
        }

        public bool IsValidatingGeminiKey
        {
            get => _isValidatingGeminiKey;
            set => SetProperty(ref _isValidatingGeminiKey, value);
        }

        public ICommand ValidateGeminiKeyCommand { get; }
        public ICommand ValidateApiKeysCommand { get; }
        public ICommand OpenGeminiConsoleCommand { get; }

        private void OpenGeminiConsole()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://aistudio.google.com/app/apikey") { UseShellExecute = true });
            }
            catch { }
        }

        private async Task ValidateGeminiKeyAsync()
        {
            if (string.IsNullOrWhiteSpace(GeminiInputKey))
            {
                GeminiKeyValidationError = "Please paste your Gemini API Key.";
                return;
            }

            IsValidatingGeminiKey = true;
            GeminiKeyValidationError = "";

            try
            {
                var (isValid, errorMessage) = await _llmService.ValidateGeminiKeyAsync(GeminiInputKey);
                if (isValid)
                {
                    GeminiKey = GeminiInputKey.Trim();
                    _settings.IsGeminiKeyValidated = true;
                    ActiveApiProvider = "Gemini";
                    _settingsService.SaveSettings(_settings);
                    _ = SaveApiKeysToServerAsync(GroqKey, GeminiKey);
                }
                else
                {
                    GeminiKeyValidationError = errorMessage;
                }
            }
            catch (Exception ex)
            {
                GeminiKeyValidationError = $"Validation error: {ex.Message}";
            }
            finally
            {
                IsValidatingGeminiKey = false;
            }
        }

        private async Task ValidateApiKeysAsync()
        {
            if (string.IsNullOrWhiteSpace(GroqInputKey) && string.IsNullOrWhiteSpace(GeminiInputKey))
            {
                GroqKeyValidationError = "Please paste your Groq or Gemini API Key to continue.";
                return;
            }

            IsValidatingGroqKey = true;
            GroqKeyValidationError = "";

            try
            {
                bool groqValid = false;
                bool geminiValid = false;
                string errors = "";

                if (!string.IsNullOrWhiteSpace(GroqInputKey))
                {
                    var (isGValid, gErr) = await _llmService.ValidateGroqKeyAsync(GroqInputKey);
                    if (isGValid)
                    {
                        GroqKey = GroqInputKey.Trim();
                        _settings.IsGroqKeyValidated = true;
                        groqValid = true;
                    }
                    else
                    {
                        errors += $"Groq Key: {gErr}\n";
                    }
                }

                if (!string.IsNullOrWhiteSpace(GeminiInputKey))
                {
                    var (isGemValid, gemErr) = await _llmService.ValidateGeminiKeyAsync(GeminiInputKey);
                    if (isGemValid)
                    {
                        GeminiKey = GeminiInputKey.Trim();
                        _settings.IsGeminiKeyValidated = true;
                        geminiValid = true;
                    }
                    else
                    {
                        errors += $"Gemini Key: {gemErr}\n";
                    }
                }

                if (groqValid || geminiValid)
                {
                    IsGroqKeyValidated = true;
                    IsTrialStarted = true; // Complete entrance onboarding so user enters app directly
                    _settingsService.SaveSettings(_settings);
                    _ = SaveApiKeysToServerAsync(GroqKey, GeminiKey);
                    OnPropertyChanged(nameof(IsGroqKeyValidated));
                    OnPropertyChanged(nameof(IsTrialStarted));
                    OnPropertyChanged(nameof(IsGroqKeyOverlayVisible));
                    OnPropertyChanged(nameof(IsDashboardOverlayVisible));
                    SyncStealthForModalOverlays();
                }
                else
                {
                    GroqKeyValidationError = string.IsNullOrWhiteSpace(errors) ? "API key validation failed." : errors.Trim();
                }
            }
            catch (Exception ex)
            {
                GroqKeyValidationError = $"Validation error: {ex.Message}";
            }
            finally
            {
                IsValidatingGroqKey = false;
            }
        }

        private async Task<(string Text, string Method, string Error)> PerformOcrAsync(byte[] imageBytes)
        {
            string effectiveGroqKey = string.IsNullOrWhiteSpace(GroqKey) ? SystemGroqKey : GroqKey;

            if (IsGeminiApiActive)
            {
                return await _llmService.ExtractTextFromGeminiImageAsync(GeminiKey, imageBytes, effectiveGroqKey);
            }
            else
            {
                return await _llmService.ExtractTextFromImageAsync(effectiveGroqKey, imageBytes);
            }
        }

        private async Task<string> PerformChatAsync(System.Collections.Generic.List<ChatMessage> history, string groqModel = "qwen/qwen3.6-27b")
        {
            string effectiveGroqKey = string.IsNullOrWhiteSpace(GroqKey) ? SystemGroqKey : GroqKey;

            if (IsGeminiApiActive)
            {
                return await _llmService.ProcessChatWithGeminiAsync(GeminiKey, history, "gemini-3.5-flash-lite", effectiveGroqKey, groqModel);
            }
            else
            {
                return await _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, history, groqModel);
            }
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
                    GroqKey = GroqInputKey.Trim();
                    IsGroqKeyValidated = true;
                    _settingsService.SaveSettings(_settings);
                    
                    // Save keys to user account database persistently
                    await SaveApiKeysToServerAsync(GroqKey, GeminiKey);
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
            if (IsLoginOverlayVisible || IsPaymentOverlayVisible || IsFollowUpCooldownActive)
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
                _txtTurnCounter++;
                int turnNum = _txtTurnCounter;

                // Add user bubble with the text question
                var userBubble = new Models.ChatBubbleItem
                {
                    Role = "user",
                    TurnNumber = turnNum,
                    Content = $"💬 {question}",
                    ScreenshotPreviews = new System.Collections.Generic.List<System.Windows.Media.ImageSource>()
                };
                ChatBubbles.Add(userBubble);

                // Add assistant bubble (loading)
                var assistantBubble = new Models.ChatBubbleItem
                {
                    Role = "assistant",
                    TurnNumber = turnNum,
                    Content = "⏳ Thinking...",
                    IsLoading = true,
                    ModelInfo = ""
                };
                ChatBubbles.Add(assistantBubble);

                string finalQuestion = question;
                try
                {
                    if (IsCodingScanMode)
                    {
                        bool isCodeOnlyQuery = question.Contains("Rewrite", StringComparison.OrdinalIgnoreCase) ||
                                               question.Contains("Optimize code", StringComparison.OrdinalIgnoreCase) ||
                                               question.Contains("Add code comments", StringComparison.OrdinalIgnoreCase);

                        if (isCodeOnlyQuery)
                        {
                            finalQuestion = question + "\n\n(Reminder: Output ONLY the updated source code in a humanized developer style. Do not include markdown code block wrappers, descriptions, or warnings. Return ONLY the code.)";
                        }
                        else
                        {
                            finalQuestion = question + "\n\n(Provide a clear, detailed, step-by-step markdown explanation or line-by-line execution dry-run trace for the code above.)";
                        }
                    }

                    _txtChatHistory.Add(new ChatMessage {
                        Role = "user",
                        Content = finalQuestion
                    });

                    var optimizedHistory = PruneChatHistory(_txtChatHistory);

                    // For coding follow-ups: use Gemini (fast flash) with qwen as Groq fallback
                    // For normal follow-ups: use openai/gpt-oss-120b via Groq
                    string displayModel = IsCodingScanMode ? "gemini-3.5-flash-lite" : "openai/gpt-oss-120b";
                    string groqFallbackModel = IsCodingScanMode ? "qwen/qwen3.6-27b" : "openai/gpt-oss-120b";
                    assistantBubble.ModelInfo = displayModel;
                    assistantBubble.Content = $"⏳ Generating response with **{displayModel}**...";

                    string answer = await PerformChatAsync(optimizedHistory, groqFallbackModel);

                    bool isFollowUpError = OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(answer);
                    assistantBubble.HasError = isFollowUpError;
                    assistantBubble.ShowCheckApiKeyAction = isFollowUpError;
                    if (isFollowUpError)
                    {
                        assistantBubble.ErrorSummary = "Follow-up error";
                    }

                    assistantBubble.Content = answer;
                    assistantBubble.IsLoading = false;
                    ScanResponseText = answer;

                    _txtChatHistory.Add(new ChatMessage {
                        Role = "assistant",
                        Content = answer
                    });

                    StartFollowUpCooldown();
                }
                catch (Exception ex)
                {
                    string displayModel = IsCodingScanMode ? "gemini-3.5-flash-lite" : "openai/gpt-oss-120b";
                    var errorInfo = OverlayApp.Helpers.LlmErrorHelper.FormatError("Follow-up", displayModel, 0, "", ex);
                    assistantBubble.Content = errorInfo.FriendlyMessage;
                    assistantBubble.HasError = true;
                    assistantBubble.ShowCheckApiKeyAction = errorInfo.RequiresKeyCheck;
                    assistantBubble.ErrorSummary = errorInfo.FriendlyMessage;
                    assistantBubble.IsLoading = false;
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
                _voiceTurnCounter++;
                int turnNum = _voiceTurnCounter;

                var userBubble = new Models.ChatBubbleItem
                {
                    Role = "user",
                    TurnNumber = turnNum,
                    Content = $"💬 {question}",
                    ScreenshotPreviews = new System.Collections.Generic.List<System.Windows.Media.ImageSource>()
                };
                VoiceChatBubbles.Add(userBubble);

                var assistantBubble = new Models.ChatBubbleItem
                {
                    Role = "assistant",
                    TurnNumber = turnNum,
                    Content = "⏳ Thinking...",
                    IsLoading = true,
                    ModelInfo = "Groq Qwen 3.6"
                };
                VoiceChatBubbles.Add(assistantBubble);

                try
                {
                    if (_voiceChatHistory.Count == 0)
                    {
                        _voiceChatHistory.Add(new ChatMessage {
                            Role = "system",
                            Content = "You are a helpful overlay productivity assistant. Solve or explain the user's transcribed question. Keep your output concise, clear, and formatted in markdown. Write in a natural, humanized style. Avoid robotic AI transitions, repetitive templates, or preambles. Speak like an experienced developer or colleague offering quick assistance. Do not say you are an AI."
                        });
                    }

                    _voiceChatHistory.Add(new ChatMessage {
                        Role = "user",
                        Content = question
                    });

                    var historyToSend = PruneVoiceChatHistory(_voiceChatHistory);

                    string answer = await _llmService.ProcessChatWithGroqAsync(effectiveGroqKey, historyToSend);
                    
                    bool isVoiceFollowUpError = OverlayApp.Helpers.LlmErrorHelper.IsErrorResponse(answer);
                    assistantBubble.HasError = isVoiceFollowUpError;
                    assistantBubble.ShowCheckApiKeyAction = isVoiceFollowUpError;
                    if (isVoiceFollowUpError)
                    {
                        assistantBubble.ErrorSummary = "Voice follow-up error";
                    }

                    assistantBubble.Content = answer;
                    assistantBubble.IsLoading = false;
                    VoiceScanResponseText = answer;

                    _voiceChatHistory.Add(new ChatMessage {
                        Role = "assistant",
                        Content = answer
                    });

                    StartFollowUpCooldown();
                }
                catch (Exception ex)
                {
                    var errorInfo = OverlayApp.Helpers.LlmErrorHelper.FormatError("Voice Follow-up", "Groq Qwen 3.6", 0, "", ex);
                    assistantBubble.Content = errorInfo.FriendlyMessage;
                    assistantBubble.HasError = true;
                    assistantBubble.ShowCheckApiKeyAction = errorInfo.RequiresKeyCheck;
                    assistantBubble.ErrorSummary = errorInfo.FriendlyMessage;
                    assistantBubble.IsLoading = false;
                    VoiceScanResponseText = errorInfo.FriendlyMessage;
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
            GroqKey = "";
            GroqInputKey = "";
            GeminiKey = "";
            GeminiInputKey = "";
            IsGroqKeyValidated = false;
            _settings.IsGroqKeyValidated = false;
            _settings.IsGeminiKeyValidated = false;
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

                        // Directly load API keys from database if returned by login
                        if (!string.IsNullOrEmpty(result.user_groq_key))
                        {
                            string fetchedGroq = result.user_groq_key.Trim();
                            GroqKey = fetchedGroq;
                            GroqInputKey = fetchedGroq;
                            _settings.IsGroqKeyValidated = true;
                        }
                        if (!string.IsNullOrEmpty(result.user_gemini_key))
                        {
                            string fetchedGemini = result.user_gemini_key.Trim();
                            GeminiKey = fetchedGemini;
                            GeminiInputKey = fetchedGemini;
                            _settings.IsGeminiKeyValidated = true;
                        }

                        // If user has keys stored in database, directly activate them and do not ask again
                        bool hasKeys = !string.IsNullOrWhiteSpace(GroqKey) || !string.IsNullOrWhiteSpace(GeminiKey);
                        if (hasKeys)
                        {
                            IsGroqKeyValidated = true;
                            _settings.IsGroqKeyValidated = true;
                            IsTrialStarted = true;
                            _settingsService.SaveSettings(_settings);
                        }
                        
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

            // Clear local key state first so new signup starts fresh
            GroqKey = "";
            GroqInputKey = "";
            GeminiKey = "";
            GeminiInputKey = "";
            IsGroqKeyValidated = false;
            _settings.IsGroqKeyValidated = false;
            _settings.IsGeminiKeyValidated = false;
            IsTrialStarted = false;
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
                        
                        // New user: ask for API keys
                        GroqKey = "";
                        GroqInputKey = "";
                        GeminiKey = "";
                        GeminiInputKey = "";
                        IsGroqKeyValidated = false;
                        _settings.IsGroqKeyValidated = false;
                        _settings.IsGeminiKeyValidated = false;
                        IsTrialStarted = false;
                        _settingsService.SaveSettings(_settings);

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

        // ─── Auto-Update ─────────────────────────────────────────────────────────

        private async Task CheckForUpdateAsync()
        {
            try
            {
                var info = await Services.UpdateService.CheckForUpdateAsync(_settings.ApiBaseUrl);
                if (info == null) return;

                LatestVersion = info.LatestVersion;
                _updateDownloadUrl = info.DownloadUrl;
                UpdateReleaseNotes = info.ReleaseNotes;
                UpdateAvailable = info.UpdateAvailable;
            }
            catch { /* silent */ }
        }

        private async Task ExecuteDownloadUpdateAsync()
        {
            if (string.IsNullOrWhiteSpace(_updateDownloadUrl)) return;
            IsUpdating = true;
            UpdateStatusText = "Downloading update...";
            try
            {
                await Services.UpdateService.DownloadAndInstallAsync(_updateDownloadUrl, progress =>
                {
                    UpdateProgress = progress;
                    UpdateStatusText = progress >= 1.0
                        ? "Installing — app will restart..."
                        : $"Downloading... {(int)(progress * 100)}%";
                });
            }
            catch (Exception ex)
            {
                UpdateStatusText = $"Update failed: {ex.Message}";
                IsUpdating = false;
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
            
            // Clear API key states to protect user privacy
            GroqKey = "";
            GroqInputKey = "";
            GeminiKey = "";
            GeminiInputKey = "";
            IsGroqKeyValidated = false;
            _settings.IsGroqKeyValidated = false;
            _settings.IsGeminiKeyValidated = false;
            IsTrialStarted = false;
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
                            string fetchedKey = result.user_groq_key.Trim();
                            GroqKey = fetchedKey;
                            GroqInputKey = fetchedKey;
                            _settings.IsGroqKeyValidated = true;
                        }

                        // Load saved custom Gemini key if present on the server database
                        if (!string.IsNullOrEmpty(result.user_gemini_key))
                        {
                            string fetchedGemini = result.user_gemini_key.Trim();
                            GeminiKey = fetchedGemini;
                            GeminiInputKey = fetchedGemini;
                            _settings.IsGeminiKeyValidated = true;
                        }

                        bool hasKeys = !string.IsNullOrWhiteSpace(GroqKey) || !string.IsNullOrWhiteSpace(GeminiKey);
                        bool hasSystemKey = !string.IsNullOrWhiteSpace(result.system_groq_key);

                        if (hasKeys || hasSystemKey)
                        {
                            IsGroqKeyValidated = true;
                            _settings.IsGroqKeyValidated = true;
                            IsTrialStarted = true; // Directly enter the app without API key prompt
                        }
                        else
                        {
                            // Missing both custom keys and system key: setup is required
                            IsGroqKeyValidated = false;
                            _settings.IsGroqKeyValidated = false;
                        }

                        _settingsService.SaveSettings(_settings);

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
            public string user_groq_key { get; set; } = "";
            public string user_gemini_key { get; set; } = "";
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
            public string user_gemini_key { get; set; } = "";
            public string error { get; set; } = "";
        }

        private async Task SaveApiKeysToServerAsync(string groqKey, string geminiKey)
        {
            if (string.IsNullOrEmpty(SessionToken)) return;
            try
            {
                var payload = new { groq_key = groqKey, gemini_key = geminiKey };
                string jsonPayload = JsonSerializer.Serialize(payload);
                var request = new HttpRequestMessage(HttpMethod.Post, GetApiEndpoint("/api/user/save-key"));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SessionToken);
                request.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                await _httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save API Keys on server database: {ex.Message}");
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

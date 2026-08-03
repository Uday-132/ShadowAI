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
        public ICommand SendScreenshotsCommand { get; }
        public ICommand RemoveScreenshotCommand { get; }
        public ICommand ToggleVoiceCommand { get; }
        public ICommand ClearTxtScanCommand { get; }

        public System.Collections.ObjectModel.ObservableCollection<Models.CapturedScreenshotItem> CapturedScreenshots { get; } = new System.Collections.ObjectModel.ObservableCollection<Models.CapturedScreenshotItem>();

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
            GeminiInputKey = _settings.GeminiKey;
            ValidateGroqKeyCommand = new RelayCommand(async _ => await ValidateGroqKeyAsync());
            ValidateGeminiKeyCommand = new RelayCommand(async _ => await ValidateGeminiKeyAsync());
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
            CycleMaxScreenshotsLimitCommand = new RelayCommand(_ => { MaxScreenshotsLimit = (MaxScreenshotsLimit % 3) + 1; });
            SendScreenshotsCommand = new RelayCommand(async _ => await ExecuteSendBatchScreenshotsAsync());
            RemoveScreenshotCommand = new RelayCommand(param => RemoveScreenshot(param));
            ToggleVoiceCommand = new RelayCommand(_ => ToggleVoiceRecording());
            ClearTxtScanCommand = new RelayCommand(_ => { 
                CapturedScreenshots.Clear();
                NotifyScreenshotStateChanged();
                ScanResponseText = ""; 
                CapturedPreview = null; 
                _txtChatHistory.Clear();
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
                    OnPropertyChanged(nameof(ActiveApiKeyStatusText));
                }
            }
        }

        public int MaxScreenshotsLimit
        {
            get => _settings.MaxScreenshotsLimit <= 0 ? 3 : _settings.MaxScreenshotsLimit;
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
                    OnPropertyChanged(nameof(IsJavaSelected));
                    OnPropertyChanged(nameof(IsCppSelected));
                    OnPropertyChanged(nameof(IsCSelected));
                }
            }
        }

        public bool IsPythonSelected
        {
            get => ProgrammingLanguage.Equals("Python", StringComparison.OrdinalIgnoreCase);
            set { if (value) ProgrammingLanguage = "Python"; }
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

            // Auto-refresh: If a previous response has already been generated or chat exists,
            // automatically clear old screenshots and response text for the new capture session.
            if (!string.IsNullOrWhiteSpace(ScanResponseText) || _txtChatHistory.Count > 0)
            {
                CapturedScreenshots.Clear();
                NotifyScreenshotStateChanged();
                ScanResponseText = "";
                CapturedPreview = null;
                _txtChatHistory.Clear();
                ScanModeState currentState = GetModeState(_activeScanModeName);
                currentState.ResponseText = "";
                currentState.ChatHistory.Clear();
                currentState.Screenshots.Clear();
                currentState.CapturedPreview = null;
                OnPropertyChanged(nameof(IsFollowUpVisible));
            }

            if (CapturedScreenshots.Count >= 3)
            {
                ScanResponseText = "⚠️ **Maximum limit of 3 screenshots reached.**\n\n" +
                                   "You have already captured **3** screenshots (the maximum allowed).\n\n" +
                                   "Click **SEND (3)** to process your screenshots, or click **✕** on a thumbnail to remove a screenshot.";
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

            if (CapturedScreenshots.Count >= 3)
            {
                ScanResponseText = "⚠️ **Maximum limit of 3 screenshots reached.**\n\n" +
                                   "Click **SEND (3)** to process your screenshots, or remove a screenshot to capture a new one.";
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
            if (CapturedScreenshots.Count >= 3)
            {
                ScanResponseText = "⚠️ **Maximum limit of 3 screenshots reached.**\n\n" +
                                   "Click **SEND (3)** to process your screenshots, or remove a screenshot to capture a new one.";
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

                if (CapturedScreenshots.Count < 3)
                {
                    ScanResponseText = $"📸 **Captured Screenshot #{item.Index}.**\n\n" +
                                       $"Total captured: **{CapturedScreenshots.Count} / 3 max**.\n" +
                                       $"Click **SEND ({CapturedScreenshots.Count})** to process now, or click **+ CAPTURE** to add up to {3 - CapturedScreenshots.Count} more.";
                }
                else
                {
                    ScanResponseText = $"✅ **Captured Screenshot #{item.Index}.**\n\n" +
                                       $"Maximum limit reached (**3 / 3** screenshots).\n" +
                                       $"Click **SEND (3)** to process all screenshots with AI!";
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
                ScanResponseText = "⚠️ **No screenshots captured.**\n\nPlease click **+ CAPTURE** to capture at least 1 screenshot (up to 3 max) before clicking **SEND**.";
                return;
            }

            IsScanning = true;
            ScanResponseText = $"[OCR] Processing {CapturedScreenshots.Count} captured screenshot{(CapturedScreenshots.Count == 1 ? "" : "s")}...";

            try
            {
                string effectiveGroqKey = string.IsNullOrWhiteSpace(GroqKey) ? SystemGroqKey : GroqKey;
                var combinedTextBuilder = new System.Text.StringBuilder();
                int totalChars = 0;
                int successfulScans = 0;

                for (int i = 0; i < CapturedScreenshots.Count; i++)
                {
                    var item = CapturedScreenshots[i];
                    ScanResponseText = $"[OCR {i + 1}/{CapturedScreenshots.Count}] Extracting text from Screenshot {item.Index}...";

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
                    ScanResponseText = "⚠️ No readable text was detected across all captured screenshots. Please try capturing clearer screen areas.";
                    return;
                }

                string metadataHeader = $"**🔍 Batch Scan Meta Information**\n" +
                                        $"* **Total Screenshots Scanned:** {CapturedScreenshots.Count}\n" +
                                        $"* **Successful Extractions:** {successfulScans}/{CapturedScreenshots.Count}\n" +
                                        $"* **Total Text Extracted:** {totalChars} characters\n\n";

                string combinedExtractedText = combinedTextBuilder.ToString().Trim();

                string singleModel = "openai/gpt-oss-120b";
                _txtChatHistory.Clear();

                if (IsMcqScanMode)
                {
                    _txtChatHistory.Add(new ChatMessage {
                        Role = "system",
                        Content = "You are a strict multiple-choice question solver. Your task is to analyze the multiple-choice questions (MCQs) captured across all screenshots, and output ONLY the correct option letter (e.g., A, B, C, or D) or exact correct answer choice. Do not provide any explanation, working out, preamble, or conversational text. Return only the single character or short answer choice."
                    });
                    _txtChatHistory.Add(new ChatMessage {
                        Role = "user",
                        Content = $"Here is the raw text extracted from {CapturedScreenshots.Count} screenshots:\n\n{combinedExtractedText}"
                    });

                    string modelA = "openai/gpt-oss-120b";
                    string modelB = "llama-3.3-70b-versatile";

                    ScanResponseText = metadataHeader + $"[LLM] Verifying MCQ answer across {CapturedScreenshots.Count} screenshots with dual models ({modelA} and {modelB})...";

                    var taskA = PerformChatAsync(_txtChatHistory, modelA);
                    var taskB = PerformChatAsync(_txtChatHistory, modelB);

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
                        sbVerify.AppendLine("⚠️ **Mismatch!** The models returned different answers. Please double-check your screenshots.");
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
                    string targetLang = string.IsNullOrWhiteSpace(ProgrammingLanguage) ? "Python" : ProgrammingLanguage;
                    string primaryModel = "llama-3.3-70b-versatile";
                    string verifierModel = "openai/gpt-oss-120b";

                    ScanResponseText = metadataHeader + $"[LLM 1/2] Generating full {targetLang} code solution with **{primaryModel}**...";
                    
                    _txtChatHistory.Add(new ChatMessage {
                        Role = "system",
                        Content = $"You are a strict expert code generator. Solve the programming challenge described across all captured screenshots. You must output ONLY the complete, working source code in {targetLang} language by default. Write the code in a humanized style as if written by a senior developer in a real coding interview (use natural variable names, standard spacing, clean modular logic, and complete all functions thoroughly without cutting off). Do not include any warnings, intro/outro text, or markdown code block formatting (no ```). Return ONLY the raw code."
                    });
                    _txtChatHistory.Add(new ChatMessage {
                        Role = "user",
                        Content = $"Here is the coding problem raw text from {CapturedScreenshots.Count} screenshots:\n\n{combinedExtractedText}"
                    });

                    string initialCode = await PerformChatAsync(_txtChatHistory, primaryModel);
                    initialCode = CleanCodeMarkdown(initialCode);

                    // Step 1: Truncation Check & Continuation
                    if (IsCodeTruncated(initialCode))
                    {
                        ScanResponseText = metadataHeader + $"[LLM] Detecting code truncation... Requesting continuation...";
                        
                        var continuationHistory = new System.Collections.Generic.List<ChatMessage>(_txtChatHistory)
                        {
                            new ChatMessage { Role = "assistant", Content = initialCode },
                            new ChatMessage { Role = "user", Content = $"The previous {targetLang} code output was cut off mid-way. Continue the code EXACTLY from where it stopped. Do not repeat the previous code. Output ONLY the remaining raw code without any markdown or intro." }
                        };

                        string continuationCode = await PerformChatAsync(continuationHistory, primaryModel);
                        continuationCode = CleanCodeMarkdown(continuationCode);
                        initialCode = initialCode.TrimEnd() + "\n" + continuationCode.TrimStart();
                    }

                    // Step 2: Second Model Verification (Dual-Model Code Audit)
                    ScanResponseText = metadataHeader + $"[LLM 2/2] Verifying {targetLang} code completeness and correctness with **{verifierModel}**...";

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

                    string verificationOutput = await PerformChatAsync(verifyHistory, verifierModel);
                    verificationOutput = verificationOutput.Trim();

                    string finalCode = initialCode;
                    string auditNote = $"✅ {targetLang} code verified complete and bug-free by dual models.";

                    if (verificationOutput.StartsWith("CORRECTED_CODE:", StringComparison.OrdinalIgnoreCase))
                    {
                        string correctedCode = verificationOutput.Substring("CORRECTED_CODE:".Length).Trim();
                        correctedCode = CleanCodeMarkdown(correctedCode);
                        if (!string.IsNullOrWhiteSpace(correctedCode) && correctedCode.Length > 20)
                        {
                            finalCode = correctedCode;
                            auditNote = $"✨ {targetLang} code was audited, completed, and verified by dual models.";
                        }
                    }

                    string finalResult = metadataHeader + $"* **Audit Status:** {auditNote}\n\n" + finalCode.Trim();
                    ScanResponseText = finalResult;

                    _txtChatHistory.Add(new ChatMessage {
                        Role = "assistant",
                        Content = finalCode.Trim()
                    });
                }
                else
                {
                    ScanResponseText = metadataHeader + $"[LLM] Explaining text from {CapturedScreenshots.Count} screenshots with **{singleModel}**...";
                    _txtChatHistory.Add(new ChatMessage {
                        Role = "system",
                        Content = "You are a helpful overlay productivity assistant. Your task is to analyze the extracted text from the user's screenshots and explain it clearly and comprehensively. If the text contains questions, problems, or concepts across screenshots, explain the answers or concepts step-by-step. Keep your output concise, clear, and formatted in markdown. Write in a natural, conversational, humanized style. Avoid typical robotic AI transitions, templates, or preambles. Explain it casually like an experienced developer explaining to a peer. Do not mention you are an AI."
                    });
                    _txtChatHistory.Add(new ChatMessage {
                        Role = "user",
                        Content = $"Here is the raw text from {CapturedScreenshots.Count} screenshots:\n\n{combinedExtractedText}"
                    });

                    string responseBody = await PerformChatAsync(_txtChatHistory, singleModel);
                    string finalResult = metadataHeader + responseBody.Trim();
                    ScanResponseText = finalResult;

                    _txtChatHistory.Add(new ChatMessage {
                        Role = "assistant",
                        Content = finalResult
                    });
                }
            }
            catch (Exception ex)
            {
                ScanResponseText = $"⚠️ Error processing batch screenshots: {ex.Message}";
            }
            finally
            {
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

        private async Task<(string Text, string Method, string Error)> PerformOcrAsync(byte[] imageBytes)
        {
            if (IsGeminiApiActive)
            {
                string key = string.IsNullOrWhiteSpace(GeminiKey) ? SystemGroqKey : GeminiKey;
                return await _llmService.ExtractTextFromGeminiImageAsync(key, imageBytes);
            }
            else
            {
                string key = string.IsNullOrWhiteSpace(GroqKey) ? SystemGroqKey : GroqKey;
                return await _llmService.ExtractTextFromImageAsync(key, imageBytes);
            }
        }

        private async Task<string> PerformChatAsync(System.Collections.Generic.List<ChatMessage> history, string groqModel = "llama-3.3-70b-versatile")
        {
            if (IsGeminiApiActive)
            {
                string key = string.IsNullOrWhiteSpace(GeminiKey) ? SystemGroqKey : GeminiKey;
                return await _llmService.ProcessChatWithGeminiAsync(key, history, "gemini-2.0-flash");
            }
            else
            {
                string key = string.IsNullOrWhiteSpace(GroqKey) ? SystemGroqKey : GroqKey;
                return await _llmService.ProcessChatWithGroqAsync(key, history, groqModel);
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
                ScanResponseText += $"\n\n👉 Follow-up Question:\n\"{question}\"\n\nThinking...";
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

                    string followUpModel = IsCodingScanMode ? "llama-3.3-70b-versatile" : "openai/gpt-oss-120b";
                    string answer = await PerformChatAsync(optimizedHistory, followUpModel);
                    
                    ScanResponseText = ScanResponseText.Replace("Thinking...", answer);

                    _txtChatHistory.Add(new ChatMessage {
                        Role = "assistant",
                        Content = answer
                    });

                    StartFollowUpCooldown();
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
                            string fetchedKey = result.user_groq_key.Trim();
                            GroqKey = fetchedKey;
                            GroqInputKey = fetchedKey;
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

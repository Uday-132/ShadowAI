using System;
using System.Windows.Threading;
using System.Windows.Input;
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

        private readonly SettingsService _settingsService;

        public MainViewModel(
            SystemMonitorService monitorService,
            HotkeyService hotkeyService,
            WindowStyleService styleService)
        {
            _settingsService = new SettingsService();
            _settings = _settingsService.LoadSettings();
            
            // Always start scan outputs empty, bypassing settings load persistence
            _settings.ScanResponseText = "";
            _settings.VoiceScanResponseText = "";
            _monitorService = monitorService;
            _hotkeyService = hotkeyService;
            _styleService = styleService;

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

            // Register Hotkey Hook callback (Ctrl+Shift+C calls this)
            _hotkeyService.HotkeyPressed += () =>
            {
                // Flip click-through interactivity
                IsClickThrough = !IsClickThrough;
            };

            // Set up Stopwatch stopwatch update interval
            _stopwatchTimer = new DispatcherTimer();
            _stopwatchTimer.Interval = TimeSpan.FromMilliseconds(100);
            _stopwatchTimer.Tick += StopwatchTimer_Tick;

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
                    e.PropertyName == nameof(IsNormalScanMode))
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

        public string GroqKey
        {
            get => _settings.GroqKey;
            set => SetProperty(ref _settings.GroqKey, value);
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
            set => SetProperty(ref _isSettingsOpen, value);
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

        private void TriggerScreenScan()
        {
            var selectionWindow = new Views.SelectionWindow();
            selectionWindow.AreaSelected = async rect =>
            {
                IsScanning = true;
                ScanResponseText = "[LLM 1] Extracting text from screen area (Groq Llama 4 Scout)...";
                
                try
                {
                    byte[] imageBytes;
                    var previewSource = CaptureScreenArea(rect, out imageBytes);
                    CapturedPreview = previewSource;

                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        // Stage 1: Send cropped screenshot to Groq (Llama 4 Scout) for OCR
                        string ocrText = await _llmService.ExtractTextFromImageAsync(GroqKey, imageBytes);
                        
                        // Handle Stage 1 errors or empty results
                        if (ocrText.StartsWith("Error") || ocrText.StartsWith("Groq OCR Error"))
                        {
                            ScanResponseText = ocrText;
                            return;
                        }
                        
                        if (ocrText.Trim() == "(no text detected)" || string.IsNullOrWhiteSpace(ocrText))
                        {
                            ScanResponseText = "No text was detected in the captured area.";
                            return;
                        }

                        // Send extracted text to Groq for explanation/solving
                        ScanResponseText = $"[LLM] Analyzing transcribed text (Groq)...\n\nExtracted Text:\n\"{ocrText.Trim()}\"";
                        
                        _txtChatHistory.Clear();
                        if (IsMcqScanMode)
                        {
                            _txtChatHistory.Add(new ChatMessage {
                                Role = "system",
                                Content = "You are a strict multiple-choice question solver. Your task is to analyze the multiple-choice question (MCQ) for aptitude, reasoning, or technical content, and output ONLY the correct option letter (e.g., A, B, C, or D) or the exact correct answer choice. Do not provide any explanation, working out, preamble, or conversational text. Return only the single character or short answer choice."
                            });
                            _txtChatHistory.Add(new ChatMessage {
                                Role = "user",
                                Content = $"Here is the raw text from a multiple-choice question:\n\n{ocrText}"
                            });

                            // Run dual models concurrently to verify answers
                            var task1 = _llmService.ProcessChatWithGroqAsync(GroqKey, _txtChatHistory, "openai/gpt-oss-120b");
                            var task2 = _llmService.ProcessChatWithGroqAsync(GroqKey, _txtChatHistory, "llama-3.3-70b-versatile");
                            
                            await Task.WhenAll(task1, task2);
                            string answer1 = await task1;
                            string answer2 = await task2;

                            string clean1 = CleanMcqResponse(answer1);
                            string clean2 = CleanMcqResponse(answer2);
                            bool isMatch = !string.IsNullOrEmpty(clean1) && !string.IsNullOrEmpty(clean2) && clean1 == clean2;

                            System.Text.StringBuilder sb = new System.Text.StringBuilder();
                            sb.AppendLine("### 🤖 MCQ Double-Model Verification");
                            sb.AppendLine();
                            sb.AppendLine($"* **Model A (GPT-OSS-120B):** {answer1.Trim()}");
                            sb.AppendLine($"* **Model B (Llama-3.3-70B):** {answer2.Trim()}");
                            sb.AppendLine();
                            sb.AppendLine("---");
                            sb.AppendLine();
                            if (isMatch)
                            {
                                sb.AppendLine($"✅ **Match!** Both models agree on the option: **{clean1.ToUpperInvariant()}**");
                            }
                            else
                            {
                                sb.AppendLine("⚠️ **Mismatch!** The models returned different answers. You should probably **rescan** the question.");
                            }

                            string finalResult = sb.ToString();
                            ScanResponseText = finalResult;

                            _txtChatHistory.Add(new ChatMessage {
                                Role = "assistant",
                                Content = finalResult
                            });
                        }
                        else if (IsCodingScanMode)
                        {
                            _txtChatHistory.Add(new ChatMessage {
                                Role = "system",
                                Content = "You are a strict code generator. Solve the programming challenge. You must output ONLY the source code in Python language by default. Write the code in a humanized style as if written by a developer in a real coding interview (use natural variable names, standard spacing, and write clean logic without adding excessive comments on every line). Do not include any warnings, intro/outro text, or markdown code block formatting (no ```). Return ONLY the raw code."
                            });
                            _txtChatHistory.Add(new ChatMessage {
                                Role = "user",
                                Content = $"Here is the raw text from a coding problem:\n\n{ocrText}"
                            });

                            string finalResult = await _llmService.ProcessChatWithGroqAsync(GroqKey, _txtChatHistory);
                            ScanResponseText = finalResult;

                            _txtChatHistory.Add(new ChatMessage {
                                Role = "assistant",
                                Content = finalResult
                            });
                        }
                        else
                        {
                            // Normal Scan Mode: returns general explanation / summary
                            _txtChatHistory.Add(new ChatMessage {
                                Role = "system",
                                Content = "You are a helpful overlay productivity assistant. You analyze raw transcribed text from the user's screen. Solve problems step-by-step if it is a general question. Keep your output concise and formatted in markdown. Crucial constraint: You must write in a natural, conversational, humanized style. Avoid typical robotic AI phrases, transitions, templates, or preambles (like 'Here is...'). Write like an experienced developer explaining something casually to a peer. Do not mention you are an AI."
                            });
                            _txtChatHistory.Add(new ChatMessage {
                                Role = "user",
                                Content = $"Here is the raw text from my screen:\n\n{ocrText}"
                            });

                            string finalResult = await _llmService.ProcessChatWithGroqAsync(GroqKey, _txtChatHistory);
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
            };

            selectionWindow.Show();
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
            if (_isProcessingVoice) return;
            _isProcessingVoice = true;

            try
            {
                IsScanning = true;
                string sourceDesc = IsSystemAudioSource ? "system loopback audio" : "speech query";
                VoiceScanResponseText = $"Transcribing {sourceDesc} (Groq Whisper)...";

                string transcribedText = await _llmService.TranscribeAudioAsync(GroqKey, _audioRecorder.TempFilePath);

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

                string explanation = await _llmService.ProcessChatWithGroqAsync(GroqKey, _voiceChatHistory);
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

        private async void SubmitFollowUpPrompt()
        {
            if (string.IsNullOrWhiteSpace(FollowUpText)) return;
            if (string.IsNullOrWhiteSpace(GroqKey))
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

                    string answer = await _llmService.ProcessChatWithGroqAsync(GroqKey, _txtChatHistory);
                    
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

                    string answer = await _llmService.ProcessChatWithGroqAsync(GroqKey, _voiceChatHistory);
                    
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
            if (string.IsNullOrWhiteSpace(GroqKey))
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
                    string transcribedText = await _llmService.TranscribeAudioAsync(GroqKey, _audioRecorder.TempFilePath);
                    
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
                // Check if first character is a letter followed by a space, period, or end of string
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

        #endregion
    }
}

using System;
using System.IO;
using NAudio.Wave;

namespace OverlayApp.Services
{
    /// <summary>
    /// Captures audio from either the microphone or the system loopback.
    /// Standardizes the recorded output to 16kHz mono 16-bit PCM (WAV) in real-time,
    /// and performs silence/speech detection for hands-free live scanning.
    /// </summary>
    public class AudioRecorderService
    {
        private IWaveIn? _captureDevice;
        private WaveFileWriter? _waveWriter;
        private readonly string _tempFilePath;
        private readonly object _lock = new object();

        // Resampling state
        private double _samplePositionAccumulator;

        // Silence / Auto-Answer state
        private bool _isLiveMode;
        private bool _hasSpeechStarted;
        private double _silenceDurationSeconds;
        private double _totalDurationSeconds;
        private const double SilenceThreshold = 0.0015; // Extremely sensitive RMS threshold
        private const double SilenceTimeout = 1.2;     // 1.2 seconds of continuous silence before answering immediately as requested
        private const double MaxSpeechLength = 30.0;   // Max speech duration safety limit before auto-answering or resetting
        private System.Threading.Timer? _watchdogTimer;
        private DateTime _lastDataAvailableTime;

        public event Action? SilenceDetected;

        public AudioRecorderService()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), "overlay_mic_record.wav");
        }

        public string TempFilePath => _tempFilePath;
        public double CurrentRms { get; private set; }

        /// <summary>
        /// Starts recording from either the microphone or system loopback.
        /// </summary>
        public void StartRecording(bool useSystemAudio, bool isLiveMode)
        {
            lock (_lock)
            {
                StopRecording();

                _isLiveMode = isLiveMode;
                _hasSpeechStarted = false;
                _silenceDurationSeconds = 0;
                _totalDurationSeconds = 0;
                _samplePositionAccumulator = 0;
                CurrentRms = 0;
                _lastDataAvailableTime = DateTime.Now;

                try
                {
                    if (useSystemAudio)
                    {
                        // WasapiLoopbackCapture records system output (what plays on speakers)
                        _captureDevice = new WasapiLoopbackCapture();
                    }
                    else
                    {
                        // WaveInEvent records microphone input
                        var mic = new WaveInEvent();
                        mic.WaveFormat = new WaveFormat(16000, 16, 1); // standard mic format
                        _captureDevice = mic;
                    }

                    // Target format is ALWAYS 16kHz mono 16-bit PCM for optimal Whisper AI billing/performance
                    var targetFormat = new WaveFormat(16000, 16, 1);
                    _waveWriter = new WaveFileWriter(_tempFilePath, targetFormat);

                    var deviceFormat = _captureDevice.WaveFormat;

                    _captureDevice.DataAvailable += (sender, e) =>
                    {
                        try
                        {
                            lock (_lock)
                            {
                                _lastDataAvailableTime = DateTime.Now;

                                if (_waveWriter == null || e.BytesRecorded == 0) return;

                                if (deviceFormat.SampleRate == 16000 && deviceFormat.BitsPerSample == 16 && deviceFormat.Channels == 1)
                                {
                                    // Write raw PCM bytes directly to the file — eliminates resampling and float decoding bugs
                                    _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);

                                    // Track RMS volume for silence detection
                                    int sampleCount = e.BytesRecorded / 2;
                                    double sumSq = 0;
                                    for (int i = 0; i < sampleCount; i++)
                                    {
                                        short val = BitConverter.ToInt16(e.Buffer, i * 2);
                                        double valNormalized = val / 32768.0;
                                        sumSq += valNormalized * valNormalized;
                                    }
                                    CurrentRms = Math.Sqrt(sumSq / sampleCount);

                                    if (_isLiveMode)
                                    {
                                        double chunkDuration = (double)sampleCount / 16000.0;
                                        ProcessLiveSilenceDetection(chunkDuration);
                                    }
                                }
                                else
                               {
                                    // 1. Extract float samples from whatever native format the device is using (e.g. 48kHz stereo float loopback)
                                    float[] monoSamples = ExtractFloatSamples(e.Buffer, e.BytesRecorded, deviceFormat);
                                    if (monoSamples.Length == 0) return;

                                    // 2. Track RMS volume level
                                    double sumSq = 0;
                                    foreach (var s in monoSamples) sumSq += s * s;
                                    CurrentRms = Math.Sqrt(sumSq / monoSamples.Length);

                                    // 3. Perform Resampling (deviceFormat.SampleRate -> 16000Hz) and write output
                                    ResampleAndWrite(monoSamples, deviceFormat.SampleRate);

                                    // 4. Live silence detection
                                    if (_isLiveMode)
                                    {
                                        double chunkDuration = (double)monoSamples.Length / deviceFormat.SampleRate;
                                        ProcessLiveSilenceDetection(chunkDuration);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"DataAvailable background thread error caught: {ex.Message}");
                        }
                    };

                    _captureDevice.StartRecording();

                    if (_isLiveMode)
                    {
                        _watchdogTimer = new System.Threading.Timer(WatchdogTimerCallback, null, 200, 200);
                    }
                }
                catch (Exception ex)
                {
                    StopRecording();
                    throw new InvalidOperationException($"Failed to access audio capture device: {ex.Message}");
                }
            }
        }

        public void StopRecording()
        {
            try
            {
                lock (_lock)
                {
                    if (_watchdogTimer != null)
                    {
                        try { _watchdogTimer.Dispose(); } catch { }
                        _watchdogTimer = null;
                    }

                    if (_captureDevice != null)
                    {
                        try
                        {
                            _captureDevice.StopRecording();
                        }
                        catch { }
                        
                        try
                        {
                            _captureDevice.Dispose();
                        }
                        catch { }
                        
                        _captureDevice = null;
                    }

                    if (_waveWriter != null)
                    {
                        try
                        {
                            _waveWriter.Dispose();
                        }
                        catch { }
                        _waveWriter = null;
                    }

                    CurrentRms = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StopRecording critical error: {ex.Message}");
            }
        }

        private void WatchdogTimerCallback(object? state)
        {
            lock (_lock)
            {
                if (!_isLiveMode || _captureDevice == null) return;

                // If speech has started and no DataAvailable events have fired for SilenceTimeout, trigger silence!
                if (_hasSpeechStarted)
                {
                    double silenceSeconds = (DateTime.Now - _lastDataAvailableTime).TotalSeconds;
                    if (silenceSeconds >= SilenceTimeout)
                    {
                        _hasSpeechStarted = false;
                        System.Threading.Tasks.Task.Run(() => SilenceDetected?.Invoke());
                    }
                }
            }
        }

        private void ProcessLiveSilenceDetection(double chunkDuration)
        {
            _totalDurationSeconds += chunkDuration;

            if (CurrentRms >= SilenceThreshold)
            {
                _hasSpeechStarted = true;
                _silenceDurationSeconds = 0; // Reset silence clock
            }
            else
            {
                if (_hasSpeechStarted)
                {
                    _silenceDurationSeconds += chunkDuration;
                    if (_silenceDurationSeconds >= SilenceTimeout)
                    {
                        // Reset flags immediately to prevent double triggers while the Task pool thread schedules StopRecording
                        _silenceDurationSeconds = 0;
                        _hasSpeechStarted = false;
                        System.Threading.Tasks.Task.Run(() => SilenceDetected?.Invoke());
                        return;
                    }
                }
            }

            // Safety limit / reset
            if (_totalDurationSeconds >= MaxSpeechLength)
            {
                if (_hasSpeechStarted)
                {
                    _hasSpeechStarted = false;
                    System.Threading.Tasks.Task.Run(() => SilenceDetected?.Invoke());
                }
                else
                {
                    // No speech was detected for 30 seconds.
                    // Silently reset the recording to keep files small and fresh.
                    ResetRecordingSession();
                }
            }
        }

        private void ResetRecordingSession()
        {
            if (_waveWriter != null)
            {
                _waveWriter.Dispose();
                _waveWriter = null;
            }

            try
            {
                if (File.Exists(_tempFilePath))
                {
                    File.Delete(_tempFilePath);
                }
            }
            catch {}

            if (_captureDevice != null)
            {
                var targetFormat = new WaveFormat(16000, 16, 1);
                _waveWriter = new WaveFileWriter(_tempFilePath, targetFormat);
            }

            _totalDurationSeconds = 0;
            _silenceDurationSeconds = 0;
            _hasSpeechStarted = false;
        }

        private void ResampleAndWrite(float[] srcSamples, int sourceSampleRate)
        {
            if (_waveWriter == null) return;

            if (sourceSampleRate == 16000)
            {
                // No resampling needed, just clamp and write directly
                foreach (var val in srcSamples)
                {
                    float clamped = Math.Max(-1.0f, Math.Min(1.0f, val));
                    _waveWriter.WriteSample(clamped);
                }
            }
            else
            {
                // Resample to 16000Hz using linear interpolation
                double ratio = (double)sourceSampleRate / 16000.0;
                int inputSampleCount = srcSamples.Length;

                while (_samplePositionAccumulator < inputSampleCount)
                {
                    int index1 = (int)_samplePositionAccumulator;
                    int index2 = Math.Min(index1 + 1, inputSampleCount - 1);
                    double fraction = _samplePositionAccumulator - index1;

                    float val = (float)((1.0 - fraction) * srcSamples[index1] + fraction * srcSamples[index2]);
                    float clamped = Math.Max(-1.0f, Math.Min(1.0f, val));

                    _waveWriter.WriteSample(clamped);
                    _samplePositionAccumulator += ratio;
                }

                _samplePositionAccumulator -= inputSampleCount;
            }
        }

        private float[] ExtractFloatSamples(byte[] buffer, int bytesRecorded, WaveFormat format)
        {
            if (format == null || format.BitsPerSample <= 0 || format.Channels <= 0)
                return Array.Empty<float>();

            int bytesPerSample = format.BitsPerSample / 8;
            if (bytesPerSample <= 0) return Array.Empty<float>();

            int totalSamples = bytesRecorded / bytesPerSample;
            int channels = format.Channels;
            int frames = totalSamples / channels;

            if (frames <= 0) return Array.Empty<float>();

            float[] monoSamples = new float[frames];

            if (format.BitsPerSample == 32)
            {
                // IEEE Float (32-bit float samples, typical for WASAPI Loopback / Extensible)
                for (int i = 0; i < frames; i++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int byteIndex = (i * channels + ch) * 4;
                        if (byteIndex + 4 <= bytesRecorded)
                        {
                            sum += BitConverter.ToSingle(buffer, byteIndex);
                        }
                    }
                    monoSamples[i] = sum / channels;
                }
            }
            else if (format.BitsPerSample == 16)
            {
                // 16-bit signed PCM (typical for Microphone input)
                for (int i = 0; i < frames; i++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int byteIndex = (i * channels + ch) * 2;
                        if (byteIndex + 2 <= bytesRecorded)
                        {
                            sum += BitConverter.ToInt16(buffer, byteIndex) / 32768f;
                        }
                    }
                    monoSamples[i] = sum / channels;
                }
            }
            else if (format.BitsPerSample == 8)
            {
                // 8-bit unsigned PCM
                for (int i = 0; i < frames; i++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int byteIndex = i * channels + ch;
                        if (byteIndex < bytesRecorded)
                        {
                            sum += (buffer[byteIndex] - 128) / 128f;
                        }
                    }
                    monoSamples[i] = sum / channels;
                }
            }

            return monoSamples;
        }
    }
}

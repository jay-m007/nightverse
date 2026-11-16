using System;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Nightverse.Services
{
    /// <summary>
    /// Listens to whatever the PC is playing (WASAPI loopback, so it works with Spotify, YT Music and anything
    /// else) and hands the audio to <see cref="BeatDetector"/>. Nothing is recorded or stored.
    /// </summary>
    public sealed class AudioAnalyzer : IDisposable
    {
        private readonly BeatDetector _det = new();
        private WasapiLoopbackCapture? _cap;
        private double[] _mono = new double[4096];
        private bool _disposed;

        /// <summary>Low-end energy, 0..1, volume-independent and punchy.</summary>
        public double Bass => _det.Bass;
        /// <summary>Overall loudness, 0..1.</summary>
        public double Level => _det.Level;
        /// <summary>Goes up by one on every beat.</summary>
        public long BeatCount => _det.BeatCount;
        /// <summary>How hard the last beat hit, 0.45..1.</summary>
        public double BeatStrength => _det.BeatStrength;
        /// <summary>Estimated tempo, or 0 when nothing steady has been found.</summary>
        public double Bpm => _det.Bpm;
        /// <summary>True while audio is actually arriving.</summary>
        public bool HasSignal => Environment.TickCount64 - _lastDataMs < 400 && _det.HasSignal;

        private long _lastDataMs;
        private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

        public void Start()
        {
            if (_disposed) return;
            try
            {
                var cap = new WasapiLoopbackCapture();
                cap.DataAvailable += OnData;
                cap.RecordingStopped += (_, _) => Restart();
                _cap = cap;
                cap.StartRecording();
            }
            catch { Restart(); }
        }

        private void Restart()
        {
            if (_disposed) return;
            try { _cap?.Dispose(); } catch { }
            _cap = null;
            // the output device changed or went away: try again shortly
            Task.Delay(2000).ContinueWith(_ => { if (!_disposed) Start(); });
        }

        private void OnData(object? sender, WaveInEventArgs e)
        {
            try
            {
                var cap = _cap;
                if (cap == null || e.BytesRecorded <= 0) return;
                var fmt = cap.WaveFormat;
                int ch = Math.Max(1, fmt.Channels);
                int bps = Math.Max(1, fmt.BitsPerSample / 8);
                int need = e.BytesRecorded / (bps * ch) + 1;
                if (_mono.Length < need) _mono = new double[need * 2];
                bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat;
                if (fmt.Encoding == WaveFormatEncoding.Extensible && fmt is WaveFormatExtensible wfe)
                    isFloat = wfe.SubFormat == FloatSubFormat;
                int frames = SampleConverter.ToMono(e.Buffer, e.BytesRecorded, ch, fmt.BitsPerSample, isFloat, _mono);
                if (frames <= 0) return;
                _lastDataMs = Environment.TickCount64;
                _det.Push(_mono, frames, fmt.SampleRate);
            }
            catch { /* never let an audio hiccup crash the app */ }
        }

        public void Dispose()
        {
            _disposed = true;
            try { _cap?.StopRecording(); } catch { }
            try { _cap?.Dispose(); } catch { }
        }
    }
}

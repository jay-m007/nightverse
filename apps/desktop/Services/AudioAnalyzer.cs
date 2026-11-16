using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Nightverse.Services
{
    /// <summary>
    /// Listens to whatever the PC is playing (WASAPI loopback, so it works with Spotify, YT Music and anything else)
    /// and exposes a smooth bass level plus a beat counter. Nothing is recorded or stored.
    /// </summary>
    public sealed class AudioAnalyzer : IDisposable
    {
        private WasapiLoopbackCapture? _cap;
        private bool _disposed;
        private double _lp, _env, _avg, _level;
        private long _beats;
        private double _beatStrength;
        private long _lastBeatMs;
        private long _lastDataMs;

        /// <summary>Smoothed bass energy, 0..1 (punchy, follows the kick).</summary>
        public double Bass { get; private set; }
        /// <summary>Overall loudness, 0..1.</summary>
        public double Level => _level;
        /// <summary>Increments on every detected beat.</summary>
        public long BeatCount => Interlocked.Read(ref _beats);
        /// <summary>How strong the last beat was, 0.4..1.</summary>
        public double BeatStrength => _beatStrength;
        /// <summary>True while audio is actually arriving.</summary>
        public bool HasSignal => Environment.TickCount64 - _lastDataMs < 400 && _level > 0.002;

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
            // output device changed or went away: try again shortly
            Task.Delay(2000).ContinueWith(_ => { if (!_disposed) Start(); });
        }

        private void OnData(object? sender, WaveInEventArgs e)
        {
            try { Analyse(e); } catch { /* never let an audio hiccup crash the app */ }
        }

        private void Analyse(WaveInEventArgs e)
        {
            var cap = _cap;
            if (cap == null || e.BytesRecorded <= 0) return;
            var fmt = cap.WaveFormat;
            int ch = Math.Max(1, fmt.Channels);
            int bytesPer = fmt.BitsPerSample / 8;
            if (bytesPer != 4 && bytesPer != 2) return;
            bool isFloat = bytesPer == 4;
            int frames = e.BytesRecorded / (bytesPer * ch);
            if (frames <= 0) return;

            double a = 1 - Math.Exp(-2 * Math.PI * 140.0 / fmt.SampleRate); // ~140 Hz low-pass
            double bassSq = 0, levelSq = 0;
            var buf = e.Buffer;
            int pos = 0;
            for (int f = 0; f < frames; f++)
            {
                double mono = 0;
                for (int c = 0; c < ch; c++)
                {
                    double v = isFloat ? BitConverter.ToSingle(buf, pos) : BitConverter.ToInt16(buf, pos) / 32768.0;
                    mono += v;
                    pos += bytesPer;
                }
                mono /= ch;
                _lp += a * (mono - _lp);
                bassSq += _lp * _lp;
                levelSq += mono * mono;
            }
            double bass = Math.Sqrt(bassSq / frames);
            double level = Math.Sqrt(levelSq / frames);
            _lastDataMs = Environment.TickCount64;
            _level += (Math.Min(1, level * 3.2) - _level) * 0.25;

            // envelope follower: fast attack, slower release
            _env += (bass - _env) * (bass > _env ? 0.65 : 0.10);
            // slow running average = "what is normal right now"
            _avg += (bass - _avg) * 0.004;
            double norm = _env / (_avg * 2.0 + 0.015);
            Bass = Math.Clamp(norm, 0, 1);

            long now = Environment.TickCount64;
            if (bass > _avg * 1.32 + 0.008 && now - _lastBeatMs > 150 && _level > 0.01)
            {
                _lastBeatMs = now;
                _beatStrength = Math.Clamp(0.45 + (bass - _avg) / (_avg + 0.02) * 0.5, 0.45, 1.0);
                Interlocked.Increment(ref _beats);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            try { _cap?.StopRecording(); } catch { }
            try { _cap?.Dispose(); } catch { }
        }
    }
}

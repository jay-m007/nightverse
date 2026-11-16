using System;

namespace Nightverse.Services
{
    /// <summary>
    /// Finds the beat in whatever is playing, from a stream of mono samples.
    /// Short-time FFT → spectral flux onset detection with an adaptive threshold, plus a tempo lock so the
    /// light keeps a steady pulse through quiet bars instead of twitching. Pure maths and a sample clock,
    /// no device code, so it can be tested offline against known click tracks.
    /// </summary>
    public sealed class BeatDetector
    {
        public const int FftSize = 2048;      // ~43 ms window at 48 kHz: enough resolution to see the kick
        public const int Hop = 512;           // ~11 ms between frames
        private const int Bins = FftSize / 2;
        private const int HistLen = 48;       // ~0.5 s of onset history for the adaptive threshold
        private const int IoiLen = 24;        // inter-onset gaps kept for the tempo estimate

        private readonly double[] _win = new double[FftSize];
        private readonly double[] _re = new double[FftSize], _im = new double[FftSize];
        private readonly double[] _buf = new double[FftSize];
        private const int Bands = 26;                 // log-spaced bands: averaging inside a band cancels hiss,
        private readonly int[] _bandLo = new int[Bands + 1];   // so only a real, coherent jump reads as an onset
        private readonly double[] _band = new double[Bands], _prevBand = new double[Bands];
        private readonly double[] _hist = new double[HistLen];
        private readonly double[] _ioi = new double[IoiLen];
        private int _fill, _histN, _histAt, _ioiN, _ioiAt;
        private int _rate = 48000;
        private long _samples;
        private double _o1, _o2;              // the two previous onset values (peak picking runs one frame late)

        private int _bass0, _bass1, _body0, _body1, _flux1;
        private double _bassRef = 1e-4, _levelRef = 1e-4;
        private double _bassNow, _level;
        private double _lastBeatMs = -1e9, _lastOnsetMs = -1e9, _periodMs, _conf;
        private long _beats;
        private double _strength, _loudAvg, _peakAvg;
        private int _offGrid;

        public BeatDetector() { for (int i = 0; i < FftSize; i++) _win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)); SetRate(48000); }

        /// <summary>Low-end energy, 0..1, volume-independent and punchy.</summary>
        public double Bass { get; private set; }
        /// <summary>Overall loudness, 0..1.</summary>
        public double Level => _level;
        /// <summary>Goes up by one on every beat.</summary>
        public long BeatCount => System.Threading.Interlocked.Read(ref _beats);
        /// <summary>How hard the last beat hit, 0.45..1.</summary>
        public double BeatStrength => _strength;
        /// <summary>Estimated tempo, or 0 when nothing steady has been found.</summary>
        public double Bpm => _conf >= 0.4 && _periodMs > 0 ? 60000.0 / _periodMs : 0;
        /// <summary>True when the tempo estimate is solid enough to keep time on its own.</summary>
        public bool Locked => _conf >= 0.5 && _periodMs > 0;
        /// <summary>Milliseconds of audio seen so far (the detector's own clock).</summary>
        public double ClockMs => _samples * 1000.0 / _rate;
        /// <summary>Diagnostics for the offline tests.</summary>
        public double DbgOnset { get; private set; }
        public double DbgLoud { get; private set; }
        /// <summary>True while audio is actually arriving.</summary>
        public bool HasSignal => _level > 0.0015;

        private void SetRate(int rate)
        {
            _rate = rate <= 0 ? 48000 : rate;
            double hz = (double)_rate / FftSize;
            int Bin(double f) => Math.Clamp((int)Math.Round(f / hz), 1, Bins - 1);
            _bass0 = Bin(30); _bass1 = Bin(145);          // the kick lives here
            _body0 = Bin(145); _body1 = Bin(700);
            _flux1 = Bin(9000);                            // snares and hats matter too, so look well up the range
            for (int i = 0; i <= Bands; i++)
                _bandLo[i] = Bin(30 * Math.Pow(9000.0 / 30.0, i / (double)Bands));
            for (int i = 1; i <= Bands; i++) if (_bandLo[i] <= _bandLo[i - 1]) _bandLo[i] = _bandLo[i - 1] + 1;
            Array.Clear(_prevBand);
        }

        /// <summary>Feed mono samples. Safe to call with any buffer size.</summary>
        public void Push(double[] mono, int count, int sampleRate)
        {
            if (count <= 0) return;
            if (sampleRate != _rate) { SetRate(sampleRate); }
            for (int i = 0; i < count; i++)
            {
                _buf[_fill++] = mono[i];
                _samples++;
                if (_fill == FftSize)
                {
                    Frame();
                    Array.Copy(_buf, Hop, _buf, 0, FftSize - Hop);
                    _fill = FftSize - Hop;
                }
            }
        }

        private void Frame()
        {
            for (int i = 0; i < FftSize; i++) { _re[i] = _buf[i] * _win[i]; _im[i] = 0; }
            Fft(_re, _im);

            double bass = 0, body = 0, all = 0;
            for (int k = 1; k < Bins; k++)
            {
                double m = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
                _re[k] = m;                                   // reuse the buffer to hold magnitudes
                if (k <= _flux1) all += m;
                if (k >= _bass0 && k <= _bass1) bass += m * m;
                else if (k >= _body0 && k <= _body1) body += m * m;
            }

            // band energies, then flux = how much each band grew since the last frame
            double flux = 0, bassFlux = 0;
            for (int i = 0; i < Bands; i++)
            {
                int lo = Math.Min(_bandLo[i], Bins - 1), hi = Math.Min(_bandLo[i + 1], Bins);
                double acc = 0;
                for (int k = lo; k < hi; k++) acc += _re[k];
                double v = hi > lo ? acc / (hi - lo) : 0;
                double d = v - _prevBand[i];
                if (d > 0) { flux += d; if (_bandLo[i] <= _bass1) bassFlux += d; }
                _band[i] = v;
            }
            Array.Copy(_band, _prevBand, Bands);

            double norm = 2.0 / FftSize;
            bass = Math.Sqrt(bass) * norm; body = Math.Sqrt(body) * norm;
            flux *= norm * 8; bassFlux *= norm * 8;      // bands are averages, so scale back to a comparable size
            double loud = all * norm;

            // --- loudness and a volume-independent bass level for the glow ---
            _levelRef = Math.Max(_levelRef * 0.9995, loud);
            _level += (Math.Min(1, loud / (_levelRef + 1e-6)) * Math.Min(1, loud * 8) - _level) * 0.2;
            _bassRef = Math.Max(_bassRef * 0.9993, bass);                       // slowly sagging peak = automatic gain
            double b = Math.Min(1, bass / (_bassRef * 0.85 + 1e-6));
            _bassNow += (b - _bassNow) * (b > _bassNow ? 0.55 : 0.12);          // fast attack, slower release
            Bass = Math.Clamp(_bassNow, 0, 1);

            // --- onset strength: broadband flux with the kick band counted twice ---
            double onset = flux + bassFlux * 1.6;
            double now = ClockMs;
            _loudAvg += (loud - _loudAvg) * 0.02;

            // adaptive threshold from the recent past (mean + spread), so it follows the song's own dynamics
            double sum = 0, sum2 = 0;
            for (int i = 0; i < _histN; i++) { sum += _hist[i]; sum2 += _hist[i] * _hist[i]; }
            double mean = _histN > 0 ? sum / _histN : 0;
            double sd = _histN > 1 ? Math.Sqrt(Math.Max(0, sum2 / _histN - mean * mean)) : 0;
            // the last term is the important one: a steady sound (a held chord, room noise) has plenty of tiny
            // wobbles above its own average, so a beat must also be a real jump next to how loud the music is
            double thresh = Math.Max(Math.Max(mean + 2.2 * sd, mean * 1.6), _loudAvg * 0.8) + 1e-4;

            // peak picking runs one frame behind, so we can confirm the middle frame really was the top
            DbgOnset = onset; DbgLoud = _loudAvg;
            double cand = _o1;
            bool peak = _histN >= 8 && cand > _o2 && cand >= onset && cand > thresh && _level > 0.01;
            double candMs = now - Hop * 1000.0 / _rate;

            _hist[_histAt] = onset; _histAt = (_histAt + 1) % HistLen; if (_histN < HistLen) _histN++;
            _o2 = _o1; _o1 = onset;

            if (peak && candMs - _lastOnsetMs > 90)
            {
                double ioi = candMs - _lastOnsetMs;
                _lastOnsetMs = candMs;
                if (ioi > 110 && ioi < 1400) AddIoi(ioi);

                double gap = candMs - _lastBeatMs;
                double expect = _lastBeatMs + _periodMs;
                // Once the tempo is known, only onsets near the expected beat count — otherwise every hi-hat
                // makes the light twitch. A much stronger off-grid hit, or a run of them after a tempo or
                // track change, wins and moves the beat back onto the music.
                bool onGrid = !Locked || Math.Abs(candMs - expect) <= _periodMs * 0.3;
                bool stronger = cand > _peakAvg * 1.35;
                if (gap > 120 && (onGrid || stronger || _offGrid >= 2))
                {
                    if (!onGrid) { _conf = 0; _ioiN = 0; _ioiAt = 0; }     // lost the thread: learn the new tempo
                    _offGrid = 0;
                    _peakAvg = _peakAvg <= 0 ? cand : _peakAvg + (cand - _peakAvg) * 0.3;
                    Fire(candMs, Math.Clamp(0.5 + (cand - thresh) / (thresh * 2 + 1e-6) * 0.5, 0.5, 1.0), false);
                }
                else if (!onGrid) _offGrid++;
            }
            else if (Locked && _level > 0.02 && now - _lastOnsetMs > _periodMs * 1.4 && now - _lastBeatMs >= _periodMs * 0.99)
            {
                // the tempo is known and the music is still playing: keep time through a quiet bar
                Fire(_lastBeatMs + _periodMs, 0.55, true);
            }
        }

        private void Fire(double ms, double strength, bool _)
        {
            _lastBeatMs = ms; _strength = strength;
            System.Threading.Interlocked.Increment(ref _beats);
        }

        /// <summary>Remember a gap between onsets and re-estimate the tempo from the most common one.</summary>
        private void AddIoi(double ms)
        {
            _ioi[_ioiAt] = ms; _ioiAt = (_ioiAt + 1) % IoiLen; if (_ioiN < IoiLen) _ioiN++;
            if (_ioiN < 4) return;

            // the winner is the gap with the most near-equal neighbours; halves and doubles count towards it,
            // so a track detected on eighth notes still locks to the right beat
            double best = 0, bestScore = 0;
            for (int i = 0; i < _ioiN; i++)
            {
                double p = _ioi[i], score = 0, sum = 0, n = 0;
                for (int j = 0; j < _ioiN; j++)
                {
                    foreach (double mul in new[] { 1.0, 2.0, 0.5 })
                    {
                        double q = _ioi[j] * mul;
                        if (q > p * 0.88 && q < p * 1.12) { score += mul == 1 ? 1 : 0.6; if (mul == 1) { sum += _ioi[j]; n++; } break; }
                    }
                }
                if (score > bestScore) { bestScore = score; best = n > 0 ? sum / n : p; }
            }
            if (best <= 0) return;
            while (best < 300) best *= 2;                 // keep the reported tempo in a musical range
            while (best > 1200) best /= 2;
            _periodMs = _periodMs > 0 && Math.Abs(best - _periodMs) < _periodMs * 0.12
                ? _periodMs + (best - _periodMs) * 0.3    // same tempo: settle onto it
                : best;                                   // new tempo: jump
            _conf = Math.Clamp(bestScore / _ioiN, 0, 1);
        }

        /// <summary>In-place iterative radix-2 FFT.</summary>
        internal static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        double ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr; cr = ncr;
                    }
                }
            }
        }
    }
}

using Nightverse.Services;

const int RATE = 48000;
var rnd = new Random(7);

// ---- synthetic audio helpers ----
static void Kick(double[] b, double at, double amp, int rate) {       // low thump with a short click
    int s = (int)(at * rate);
    for (int i = 0; i < rate * 0.22 && s + i < b.Length; i++) {
        double t = i / (double)rate;
        double f = 110 * Math.Exp(-t * 28) + 48;
        b[s + i] += amp * Math.Exp(-t * 16) * Math.Sin(2 * Math.PI * f * t);
    }
}
static void Snare(double[] b, double at, double amp, int rate, Random r) {
    int s = (int)(at * rate);
    for (int i = 0; i < rate * 0.16 && s + i < b.Length; i++) {
        double t = i / (double)rate;
        b[s + i] += amp * Math.Exp(-t * 24) * ((r.NextDouble() * 2 - 1) * 0.8 + Math.Sin(2 * Math.PI * 190 * t) * 0.4);
    }
}
static void Hat(double[] b, double at, double amp, int rate, Random r) {
    int s = (int)(at * rate);
    for (int i = 0; i < rate * 0.05 && s + i < b.Length; i++) {
        double t = i / (double)rate;
        b[s + i] += amp * Math.Exp(-t * 90) * (r.NextDouble() * 2 - 1);
    }
}
static void Pad(double[] b, int rate, double amp) {                    // sustained chord, no rhythm at all
    for (int i = 0; i < b.Length; i++) {
        double t = i / (double)rate;
        b[i] += amp * (Math.Sin(2 * Math.PI * 220 * t) + Math.Sin(2 * Math.PI * 277 * t) + Math.Sin(2 * Math.PI * 330 * t)) / 3;
    }
}

// ---- run the detector over a buffer in realistic chunks ----
static (List<double> beats, double bpm, double maxBass, double minBassDuringMusic) Run(double[] buf, int rate, Random r) {
    var d = new BeatDetector();
    var beats = new List<double>();
    long seen = 0; int pos = 0; double maxBass = 0, minBass = 1;
    while (pos < buf.Length) {
        int n = Math.Min(rnd2(r), buf.Length - pos);                   // jittery buffer sizes, like WASAPI
        var chunk = new double[n];
        Array.Copy(buf, pos, chunk, 0, n);
        d.Push(chunk, n, rate);
        pos += n;
        if (d.BeatCount != seen) { for (long k = seen; k < d.BeatCount; k++) beats.Add(d.ClockMs); seen = d.BeatCount; }
        if (d.ClockMs > 2000 && d.Level > 0.05) { maxBass = Math.Max(maxBass, d.Bass); minBass = Math.Min(minBass, d.Bass); }
    }
    return (beats, d.Bpm, maxBass, minBass);
    static int rnd2(Random r) => 240 + r.Next(0, 900);
}

static (double recall, double prec, double err) Score(List<double> got, List<double> want, double tolMs) {
    int hit = 0; double errSum = 0;
    var used = new bool[got.Count];
    foreach (var w in want) {
        int best = -1; double bd = tolMs;
        for (int i = 0; i < got.Count; i++) if (!used[i] && Math.Abs(got[i] - w) <= bd) { bd = Math.Abs(got[i] - w); best = i; }
        if (best >= 0) { used[best] = true; hit++; errSum += bd; }
    }
    return (want.Count == 0 ? 1 : hit / (double)want.Count, got.Count == 0 ? 1 : hit / (double)got.Count, hit == 0 ? 0 : errSum / hit);
}

int fails = 0;
void Check(string name, bool ok, string detail) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-34} {detail}"); if (!ok) fails++; }

// ===== 1. four-on-the-floor, 120 BPM =====
{
    double bpm = 120, sec = 20; var buf = new double[(int)(RATE * sec)]; var want = new List<double>();
    Pad(buf, RATE, 0.10);
    for (double t = 0.5; t < sec - 0.3; t += 60 / bpm) { Kick(buf, t, 0.9, RATE); want.Add(t * 1000); }
    for (double t = 0.5; t < sec - 0.3; t += 30 / bpm) Hat(buf, t, 0.18, RATE, rnd);
    var (beats, got, maxB, minB) = Run(buf, RATE, rnd);
    var (rec, pre, err) = Score(beats, want, 90);
    Check("120 BPM kick: recall", rec >= 0.95, $"recall={rec:P0} precision={pre:P0} err={err:F0}ms");
    Check("120 BPM kick: tempo", Math.Abs(got - 120) < 3, $"detected {got:F1} BPM");
    Check("120 BPM kick: bass pumps", maxB > 0.8 && minB < 0.45, $"bass {minB:F2}..{maxB:F2}");
}

// ===== 2. kick + snare backbeat, 100 BPM, with a quiet bar in the middle =====
{
    double bpm = 100, sec = 24, sp = 60 / bpm; var buf = new double[(int)(RATE * sec)]; var want = new List<double>();
    Pad(buf, RATE, 0.08);
    int i = 0;
    for (double t = 0.5; t < sec - 0.3; t += sp, i++) {
        bool quiet = t > 12 && t < 14.5;                         // two bars where the drums drop out
        want.Add(t * 1000);
        if (quiet) continue;
        if (i % 2 == 0) Kick(buf, t, 0.9, RATE); else Snare(buf, t, 0.5, RATE, rnd);
    }
    var (beats, got, _, _) = Run(buf, RATE, rnd);
    var (rec, pre, err) = Score(beats, want, 110);
    Check("100 BPM kick+snare", rec >= 0.9 && pre >= 0.75, $"recall={rec:P0} precision={pre:P0} err={err:F0}ms");
    Check("100 BPM tempo", Math.Abs(got - 100) < 4, $"detected {got:F1} BPM");
}

// ===== 3. silence =====
{
    var buf = new double[RATE * 6];
    var (beats, _, _, _) = Run(buf, RATE, rnd);
    Check("silence: no beats", beats.Count == 0, $"{beats.Count} beats");
}

// ===== 4. steady pad, no rhythm =====
{
    var buf = new double[RATE * 12]; Pad(buf, RATE, 0.35);
    for (int i = 0; i < buf.Length; i++) buf[i] += (rnd.NextDouble() * 2 - 1) * 0.01;
    var (beats, _, _, _) = Run(buf, RATE, rnd);
    Check("no rhythm: few false beats", beats.Count <= 6, $"{beats.Count} beats in 12s");
}

// ===== 5. tempo change 90 -> 140 =====
{
    double sec = 30; var buf = new double[(int)(RATE * sec)]; var late = new List<double>();
    Pad(buf, RATE, 0.08);
    for (double t = 0.5; t < 14; t += 60 / 90.0) Kick(buf, t, 0.9, RATE);
    for (double t = 15; t < sec - 0.3; t += 60 / 140.0) { Kick(buf, t, 0.9, RATE); if (t > 20) late.Add(t * 1000); }
    var (beats, got, _, _) = Run(buf, RATE, rnd);
    var (rec, pre, err) = Score(beats.FindAll(b => b > 20000), late, 90);
    Check("tempo change re-locks", rec >= 0.9, $"recall={rec:P0} final {got:F0} BPM");
}

// ===== 6. busy mix: offbeat hats, bass line, 128 BPM =====
{
    double bpm = 128, sec = 20, sp = 60 / bpm; var buf = new double[(int)(RATE * sec)]; var want = new List<double>();
    Pad(buf, RATE, 0.12);
    for (double t = 0.5; t < sec - 0.3; t += sp) { Kick(buf, t, 0.85, RATE); want.Add(t * 1000); }
    for (double t = 0.5 + sp / 2; t < sec - 0.3; t += sp) Hat(buf, t, 0.3, RATE, rnd);
    for (double t = 0.5 + sp; t < sec - 0.3; t += sp * 2) Snare(buf, t, 0.45, RATE, rnd);
    var (beats, got, _, _) = Run(buf, RATE, rnd);
    var (rec, pre, err) = Score(beats, want, 90);
    Check("128 BPM busy mix", rec >= 0.9, $"recall={rec:P0} precision={pre:P0} {got:F0} BPM");
}

// ===== 7. quiet track (low volume) still detected =====
{
    double bpm = 110, sec = 16; var buf = new double[(int)(RATE * sec)]; var want = new List<double>();
    for (double t = 0.5; t < sec - 0.3; t += 60 / bpm) { Kick(buf, t, 0.08, RATE); want.Add(t * 1000); }
    var (beats, got, _, _) = Run(buf, RATE, rnd);
    var (rec, _, _) = Score(beats, want, 90);
    Check("quiet track still beats", rec >= 0.85, $"recall={rec:P0} {got:F0} BPM");
}

// ===== 8. 44.1 kHz works too =====
{
    int r44 = 44100; double bpm = 120, sec = 16; var buf = new double[(int)(r44 * sec)]; var want = new List<double>();
    Pad(buf, r44, 0.1);
    for (double t = 0.5; t < sec - 0.3; t += 60 / bpm) { Kick(buf, t, 0.9, r44); want.Add(t * 1000); }
    var (beats, got, _, _) = Run(buf, r44, rnd);
    var (rec, _, err) = Score(beats, want, 90);
    Check("44.1 kHz", rec >= 0.95 && Math.Abs(got - 120) < 3, $"recall={rec:P0} {got:F1} BPM err={err:F0}ms");
}

{
  var pad = new double[RATE*12]; Pad(pad, RATE, 0.35);
  for (int i=0;i<pad.Length;i++) pad[i] += (rnd.NextDouble()*2-1)*0.01;
  Diag.Run(pad, RATE, "steady pad");
  var kb = new double[RATE*12]; Pad(kb, RATE, 0.10);
  for (double t=0.5;t<11.7;t+=0.5) Kick(kb, t, 0.9, RATE);
  Diag.Run(kb, RATE, "120bpm kick");
  var q = new double[RATE*12];
  for (double t=0.5;t<11.7;t+=60/110.0) Kick(q, t, 0.08, RATE);
  Diag.Run(q, RATE, "quiet kick");
}

// ===== 9. every WASAPI buffer format Windows might hand us =====
{
    double[] want = { 0.0, 0.5, -0.5, 0.999, -0.999 };
    var dst = new double[16];

    // 32-bit float, stereo
    var f32 = new byte[want.Length * 2 * 4];
    for (int i = 0; i < want.Length; i++) { BitConverter.GetBytes((float)want[i]).CopyTo(f32, i*8); BitConverter.GetBytes((float)want[i]).CopyTo(f32, i*8+4); }
    int n = SampleConverter.ToMono(f32, f32.Length, 2, 32, true, dst);
    bool ok32 = n == want.Length; for (int i = 0; i < n; i++) ok32 &= Math.Abs(dst[i] - want[i]) < 1e-5;
    Check("format: 32-bit float stereo", ok32, $"{n} frames");

    // 16-bit PCM, stereo
    var p16 = new byte[want.Length * 2 * 2];
    for (int i = 0; i < want.Length; i++) { var b = BitConverter.GetBytes((short)(want[i]*32767)); b.CopyTo(p16, i*4); b.CopyTo(p16, i*4+2); }
    n = SampleConverter.ToMono(p16, p16.Length, 2, 16, false, dst);
    bool ok16 = n == want.Length; for (int i = 0; i < n; i++) ok16 &= Math.Abs(dst[i] - want[i]) < 1e-3;
    Check("format: 16-bit PCM stereo", ok16, $"{n} frames");

    // 24-bit PCM, stereo  (the old code threw this away entirely)
    var p24 = new byte[want.Length * 2 * 3];
    for (int i = 0; i < want.Length; i++) { int v = (int)(want[i]*8388607);
        for (int c = 0; c < 2; c++) { int o = i*6 + c*3; p24[o] = (byte)v; p24[o+1] = (byte)(v>>8); p24[o+2] = (byte)(v>>16); } }
    n = SampleConverter.ToMono(p24, p24.Length, 2, 24, false, dst);
    bool ok24 = n == want.Length; for (int i = 0; i < n; i++) ok24 &= Math.Abs(dst[i] - want[i]) < 1e-5;
    Check("format: 24-bit PCM stereo", ok24, $"{n} frames, got {string.Join(",", dst.Take(n).Select(x=>x.ToString("F3")))}");

    // 32-bit PCM, 6 channels (surround): all channels mixed down
    var p32 = new byte[want.Length * 6 * 4];
    for (int i = 0; i < want.Length; i++) for (int c = 0; c < 6; c++) BitConverter.GetBytes((int)(want[i]*2147483647.0)).CopyTo(p32, i*24+c*4);
    n = SampleConverter.ToMono(p32, p32.Length, 6, 32, false, dst);
    bool ok6 = n == want.Length; for (int i = 0; i < n; i++) ok6 &= Math.Abs(dst[i] - want[i]) < 1e-5;
    Check("format: 32-bit PCM 5.1", ok6, $"{n} frames");

    // junk in, no crash out
    Check("format: ragged buffer safe", SampleConverter.ToMono(new byte[7], 7, 2, 32, true, dst) == 0, "handled");
    Check("format: null safe", SampleConverter.ToMono(null!, 10, 2, 32, true, dst) == 0, "handled");
}

// ===== 10. a 24-bit source still produces beats end to end =====
{
    double bpm = 120, sec = 14; var src = new double[(int)(RATE*sec)]; var want = new List<double>();
    Pad(src, RATE, 0.1);
    for (double t = 0.5; t < sec-0.3; t += 60/bpm) { Kick(src, t, 0.9, RATE); want.Add(t*1000); }
    var det = new BeatDetector(); var got = new List<double>(); long seen = 0;
    var mono = new double[2048];
    for (int pos = 0; pos < src.Length; ) {
        int frames = Math.Min(700, src.Length - pos);
        var raw = new byte[frames*2*3];
        for (int i = 0; i < frames; i++) { int v = (int)(Math.Clamp(src[pos+i],-1,1)*8388607);
            for (int c = 0; c < 2; c++) { int o = i*6+c*3; raw[o]=(byte)v; raw[o+1]=(byte)(v>>8); raw[o+2]=(byte)(v>>16); } }
        int nf = SampleConverter.ToMono(raw, raw.Length, 2, 24, false, mono);
        det.Push(mono, nf, RATE);
        if (det.BeatCount != seen) { for (long k = seen; k < det.BeatCount; k++) got.Add(det.ClockMs); seen = det.BeatCount; }
        pos += frames;
    }
    var (rec, pre, _) = Score(got, want, 90);
    Check("24-bit end to end", rec >= 0.95, $"recall={rec:P0} precision={pre:P0} {det.Bpm:F0} BPM");
}

Console.WriteLine(fails == 0 ? "\nALL PASS" : $"\n{fails} FAILED");
return fails == 0 ? 0 : 1;

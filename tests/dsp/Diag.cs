using Nightverse.Services;
public static class Diag {
  public static void Run(double[] buf, int rate, string name) {
    var d = new BeatDetector(); int pos = 0; var r = new List<double>();
    while (pos < buf.Length) { int n = Math.Min(512, buf.Length - pos); var c = new double[n]; Array.Copy(buf,pos,c,0,n); d.Push(c,n,rate); pos += n;
      if (d.ClockMs > 3000 && d.DbgLoud > 1e-9) r.Add(d.DbgOnset / d.DbgLoud); }
    r.Sort();
    double P(double q) => r.Count==0?0:r[(int)Math.Min(r.Count-1, q*r.Count)];
    Console.WriteLine($"{name,-18} onset/loud p50={P(.5):F3} p90={P(.9):F3} p99={P(.99):F3} max={P(1):F3}");
  }
}

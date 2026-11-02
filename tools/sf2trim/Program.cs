// sf2trim - extract GM presets (bank 0) from a big SF2 into a small SF2. Deterministic, no dependencies (.NET 10).
//
// Input : MuseScore_General.sf2 (MIT), https://ftp.osuosl.org/pub/musescore/soundfont/MuseScore_General/MuseScore_General.sf2
//         SHA-256 ee51d2c4b1525e70f19a45909c4fd7a2e26d91d115fa89dbf5a6bc413d8b9bf3 (version 0.6 / 206 MB; tool warns if different)
//
// Usage : dotnet run -c Release --project sf2trim -- <in.sf2> <out.sf2> --programs 0,4,19,... [options]
//   --rate N                 default max sample rate (default 22050); samples already <= N are untouched
//   --rate-prog P=N,P=N      per-program max rate override
//   --mono                   stereo pairs: keep the left sample only, drop right zones, pan reset to 0
//   --maxlen S               truncate UNLOOPED samples to S seconds (300 ms raised-cosine fade-out at the cut)
//   --maxlen-prog P=lo:hi,.. per-program length: lo seconds at key 21 (A0) scaling linearly to hi seconds at key 108 (or P=S)
//   --thin-prog P=N,..       keep one sample zone per N zones (middle one) and widen its key range over the gap
//   --vel V                  keep only the velocity layer containing V (preset- and instrument-level), range widened to 0-127
// Looped samples are kept up to loop end + 8 points. Each sample is followed by 46 zero points (SF2 spec). 24-bit (sm24) is dropped.
// Example (final): see run-final.sh next to this project.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

const string ExpectedSha = "ee51d2c4b1525e70f19a45909c4fd7a2e26d91d115fa89dbf5a6bc413d8b9bf3";
if (args.Length < 3) { Console.Error.WriteLine("usage: sf2trim in.sf2 out.sf2 --programs 0,4,.. [--rate N] [--rate-prog P=N,..] [--mono] [--maxlen S] [--maxlen-prog P=lo:hi,..] [--thin-prog P=N,..] [--vel V]"); return 1; }
var opt = new Dictionary<string, string>();
for (int i = 2; i < args.Length; i++)
{
    if (!args[i].StartsWith("--")) continue;
    opt[args[i][2..]] = (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[++i] : "true";
}
Dictionary<int, string> PerProg(string key)
{
    var r = new Dictionary<int, string>();
    if (opt.TryGetValue(key, out var v)) foreach (var kv in v.Split(',')) { var s = kv.Split('='); r[int.Parse(s[0])] = s[1]; }
    return r;
}
double Dbl(string s) => double.Parse(s, CultureInfo.InvariantCulture);
int defRate = opt.TryGetValue("rate", out var rs) ? int.Parse(rs) : 22050;
var cutLoop = (opt.TryGetValue("cut-looped", out var cl) ? cl : "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();
var rateProg = PerProg("rate-prog"); var lenProg = PerProg("maxlen-prog"); var thinProg = PerProg("thin-prog");
double defLen = opt.TryGetValue("maxlen", out var ml) ? Dbl(ml) : 0;
bool mono = opt.ContainsKey("mono");
int vel = opt.TryGetValue("vel", out var vs) ? int.Parse(vs) : 0;
var progs = opt["programs"].Split(',').Select(int.Parse).ToList();
int RateFor(int p) => rateProg.TryGetValue(p, out var s) ? int.Parse(s) : defRate;
(double lo, double hi) LenFor(int p)
{
    if (lenProg.TryGetValue(p, out var s)) { var a = s.Split(':'); return (Dbl(a[0]), Dbl(a[a.Length > 1 ? 1 : 0])); }
    return (defLen, defLen);
}
int ThinFor(int p) => thinProg.TryGetValue(p, out var s) ? int.Parse(s) : 1;

byte[] d = File.ReadAllBytes(args[0]);
string sha = Convert.ToHexString(SHA256.HashData(d)).ToLowerInvariant();
Console.WriteLine($"input sha256 {sha}" + (sha == ExpectedSha ? " (matches expected)" : " WARNING: differs from expected"));

var chunks = new Dictionary<string, (int off, int len)>();
var info = new List<(string id, byte[] data)>();
int pos = 12;
while (pos < d.Length)
{
    string id = Encoding.ASCII.GetString(d, pos, 4); int len = BitConverter.ToInt32(d, pos + 4);
    if (id == "LIST")
    {
        string t = Encoding.ASCII.GetString(d, pos + 8, 4);
        int p = pos + 12, end = pos + 8 + len;
        while (p < end)
        {
            string sid = Encoding.ASCII.GetString(d, p, 4); int sl = BitConverter.ToInt32(d, p + 4);
            if (t == "INFO") info.Add((sid, d[(p + 8)..(p + 8 + sl)])); else chunks[sid] = (p + 8, sl);
            p += 8 + sl + (sl & 1);
        }
    }
    pos += 8 + len + (len & 1);
}
ushort U16(int o) => BitConverter.ToUInt16(d, o);
uint U32(int o) => BitConverter.ToUInt32(d, o);
var (ph, phl) = chunks["phdr"]; var (pb, _) = chunks["pbag"]; var (pm, _) = chunks["pmod"]; var (pg, _) = chunks["pgen"];
var (ih, _) = chunks["inst"]; var (ib, _) = chunks["ibag"]; var (im, _) = chunks["imod"]; var (ig, _) = chunks["igen"];
var (sh, _) = chunks["shdr"]; var (sm, _) = chunks["smpl"];
int nPre = phl / 38;

List<Zone> ReadZones(int bagBase, int genBase, int modBase, int b0, int b1, bool inst)
{
    var zs = new List<Zone>();
    for (int b = b0; b < b1; b++)
    {
        int g0 = U16(bagBase + b * 4), g1 = U16(bagBase + (b + 1) * 4), m0 = U16(bagBase + b * 4 + 2), m1 = U16(bagBase + (b + 1) * 4 + 2);
        var z = new Zone();
        for (int g = g0; g < g1; g++) z.G.Add(new Gen { Op = U16(genBase + g * 4), Lo = d[genBase + g * 4 + 2], Hi = d[genBase + g * 4 + 3] });
        for (int m = m0; m < m1; m++) z.M.Add(d[(modBase + m * 10)..(modBase + m * 10 + 10)]);
        // velocity layer filter
        if (vel > 0) { var vg = z.Find(44); if (vg != null && !(vg.Lo <= vel && vel <= vg.Hi)) continue; }
        zs.Add(z);
    }
    return zs;
}
int SType(int s) => U16(sh + s * 46 + 44);

// ---- build model
var presets = new List<PresetM>();
var insts = new List<InstM>();
foreach (int prog in progs)
{
    int p = -1;
    for (int i = 0; i < nPre - 1; i++) if (U16(ph + i * 38 + 22) == 0 && U16(ph + i * 38 + 20) == prog) { p = i; break; }
    if (p < 0) { Console.WriteLine($"WARNING: program {prog} not found"); continue; }
    var pm_ = new PresetM { Prog = prog, Hdr = d[(ph + p * 38)..(ph + p * 38 + 24)] };
    pm_.Zones = ReadZones(pb, pg, pm, U16(ph + p * 38 + 24), U16(ph + (p + 1) * 38 + 24), false);
    foreach (var z in pm_.Zones)
    {
        var g41 = z.Find(41); if (g41 == null) continue;
        int ii = g41.U;
        var im_ = new InstM { Prog = prog, Name = d[(ih + ii * 22)..(ih + ii * 22 + 20)] };
        im_.Zones = ReadZones(ib, ig, im, U16(ih + ii * 22 + 20), U16(ih + (ii + 1) * 22 + 20), true);
        Zone glob = im_.Zones.Count > 0 && im_.Zones[0].Find(53) == null ? im_.Zones[0] : null;
        var real = im_.Zones.Where(x => x.Find(53) != null).ToList();
        foreach (var r in real)
        {
            var sg = r.Find(54) ?? glob?.Find(54); int mode = sg == null ? 0 : sg.U & 3;
            r.Looped = mode == 1 || mode == 3;
        }
        if (mono)
        {
            foreach (var r in real.ToList())
            {
                int ty = SType(r.Find(53).U);
                if (ty == 2) { im_.Zones.Remove(r); real.Remove(r); }
                else if (ty == 4) { r.G.RemoveAll(x => x.Op == 17); r.G.Insert(r.G.Count - 1, new Gen { Op = 17, Lo = 0, Hi = 0 }); }
            }
        }
        int thin = ThinFor(prog);
        if (thin > 1)
        {
            var groups = real.GroupBy(x => { var k = x.Find(43); return k == null ? (0, 127) : ((int)k.Lo, (int)k.Hi); }).OrderBy(x => x.Key.Item1).ToList();
            for (int j = 0; j < groups.Count; j += thin)
            {
                int cnt = Math.Min(thin, groups.Count - j), keep = j + cnt / 2;
                int lo = groups[j].Key.Item1, hi = groups[j + cnt - 1].Key.Item2;
                for (int q = j; q < j + cnt; q++)
                    foreach (var z2 in groups[q])
                    {
                        if (q != keep) im_.Zones.Remove(z2);
                        else { var k = z2.Find(43); if (k != null) { k.Lo = (byte)lo; k.Hi = (byte)hi; } }
                    }
            }
        }
        z.NewInst = insts.Count; insts.Add(im_);
    }
    presets.Add(pm_);
}
// ---- samples
var smp = new SortedDictionary<int, SInfo>();
foreach (var im_ in insts)
    foreach (var z in im_.Zones)
    {
        var g = z.Find(53); if (g == null) continue;
        int s = g.U; if (!smp.TryGetValue(s, out var si)) smp[s] = si = new SInfo { Rate = 0, Lo = -1, Hi = -1 };
        si.Looped |= z.Looped; si.CutLoop &= cutLoop.Contains(im_.Prog); si.Rate = Math.Max(si.Rate, RateFor(im_.Prog));
        var (lo, hi) = LenFor(im_.Prog);
        if (lo <= 0) { si.Lo = si.Hi = 0; si.Unl = true; } else if (!si.Unl) { si.Lo = Math.Max(si.Lo, lo); si.Hi = Math.Max(si.Hi, hi); }
    }
if (!mono)
{
    bool added;
    do
    {
        added = false;
        foreach (int s in smp.Keys.ToList())
        {
            int ty = SType(s); int lk = U16(sh + s * 46 + 42);
            if ((ty == 2 || ty == 4 || ty == 8) && !smp.ContainsKey(lk)) { smp[lk] = new SInfo { Rate = smp[s].Rate, Looped = smp[s].Looped, Lo = smp[s].Lo, Hi = smp[s].Hi, Unl = smp[s].Unl }; added = true; }
        }
    } while (added);
}
var smpMap = new Dictionary<int, int>(); int kk = 0; foreach (int s in smp.Keys) smpMap[s] = kk++;
Console.WriteLine($"presets {presets.Count}, instruments {insts.Count}, samples {smp.Count}");

var newSmpl = new MemoryStream(); var newSh = new MemoryStream();
void W16(Stream s, int v) => s.Write(BitConverter.GetBytes((ushort)v));
void W32(Stream s, long v) => s.Write(BitConverter.GetBytes((uint)v));
long origPts = 0, truncated = 0;
foreach (var (s, si) in smp)
{
    int o = sh + s * 46; uint st = U32(o + 20), en = U32(o + 24), ls = U32(o + 28), le = U32(o + 32), rate = U32(o + 36);
    int pitch = d[o + 40];
    origPts += en - st;
    if (Environment.GetEnvironmentVariable("SF2DUMP") != null) Console.WriteLine($"smp {s} pitch {pitch} rate {rate} len {(en - st) / (double)rate:F2}s loop {(ls - st) / (double)rate:F2}-{(le - st) / (double)rate:F2}s looped={si.Looped}");
    long end = en; bool fade = false;
    if (!si.Unl && si.Lo > 0 && (!si.Looped || si.CutLoop))
    {
        double t = Math.Clamp((pitch - 21) / 87.0, 0, 1);
        long lim = (long)((si.Lo + (si.Hi - si.Lo) * t) * rate);
        if (end - st > lim) { end = st + lim; fade = true; si.Cut = si.Looped; }
    }
    if (si.Looped && !si.Cut) end = Math.Min(end, (long)le + 8);
    int n = (int)(end - st); truncated += en - st - n;
    var src = new short[n]; Buffer.BlockCopy(d, sm + (int)st * 2, src, 0, n * 2);
    if (fade)
    {
        int fn = Math.Min(n, (int)(0.3 * rate));
        for (int i = 0; i < fn; i++) src[n - fn + i] = (short)Math.Round(src[n - fn + i] * 0.5 * (1 + Math.Cos(Math.PI * (i + 1) / fn)));
    }
    double r = 1; short[] outp = src;
    if (si.Rate > 0 && rate > si.Rate)
    {
        r = (double)si.Rate / rate; int m = (int)Math.Round(n * r); outp = new short[m];
        int half = 24; double fc = r;
        for (int i = 0; i < m; i++)
        {
            double c = i / r; int k0 = (int)Math.Floor(c) - half, k1 = (int)Math.Floor(c) + half; double acc = 0, ws = 0;
            for (int j = k0; j <= k1; j++)
            {
                double x = c - j; double h = x == 0 ? fc : Math.Sin(Math.PI * fc * x) / (Math.PI * x);
                h *= 0.5 + 0.5 * Math.Cos(Math.PI * x / (half + 1)); ws += h; if (j >= 0 && j < n) acc += h * src[j];
            }
            outp[i] = (short)Math.Clamp(Math.Round(acc / ws), -32768, 32767);
        }
    }
    long ns = newSmpl.Length / 2; var bytes = new byte[outp.Length * 2]; Buffer.BlockCopy(outp, 0, bytes, 0, bytes.Length);
    newSmpl.Write(bytes); newSmpl.Write(new byte[92]);
    int m2 = outp.Length;
    newSh.Write(d, o, 20); W32(newSh, ns); W32(newSh, ns + m2);
    W32(newSh, ns + Math.Clamp((long)Math.Round(((long)ls - st) * r), 0, m2)); W32(newSh, ns + Math.Clamp((long)Math.Round(((long)le - st) * r), 0, m2));
    W32(newSh, r < 1 ? si.Rate : rate); newSh.Write(d, o + 40, 2);
    int ty = SType(s), lk = U16(o + 42);
    if (mono) { W16(newSh, 0); W16(newSh, 1); }
    else { W16(newSh, smpMap.TryGetValue(lk, out var nl) ? nl : 0); W16(newSh, ty); }
}
{ var eos = new byte[46]; Encoding.ASCII.GetBytes("EOS").CopyTo(eos, 0); newSh.Write(eos); }

var nPhdr = new MemoryStream(); var nPbag = new MemoryStream(); var nPmod = new MemoryStream(); var nPgen = new MemoryStream();
var nInst = new MemoryStream(); var nIbag = new MemoryStream(); var nImod = new MemoryStream(); var nIgen = new MemoryStream();
int pbagN = 0, pmodN = 0, pgenN = 0, ibagN = 0, imodN = 0, igenN = 0;
void WriteGens(Stream gs, Zone z, ref int gn, Func<Gen, Gen> map) { foreach (var g in z.G) { var m = map(g); W16(gs, m.Op); gs.WriteByte(m.Lo); gs.WriteByte(m.Hi); gn++; } }
foreach (var pr in presets.OrderBy(x => x.Prog))
{
    nPhdr.Write(pr.Hdr); W16(nPhdr, pbagN); nPhdr.Write(new byte[12]);
    foreach (var z in pr.Zones)
    {
        W16(nPbag, pgenN); W16(nPbag, pmodN); pbagN++;
        WriteGens(nPgen, z, ref pgenN, g => g.Op == 44 && vel > 0 ? new Gen { Op = 44, Lo = 0, Hi = 127 } : g.Op == 41 ? new Gen { Op = 41, Lo = (byte)(z.NewInst & 255), Hi = (byte)(z.NewInst >> 8) } : g);
        foreach (var m in z.M) { nPmod.Write(m); pmodN++; }
    }
}
{ var e = new byte[20]; Encoding.ASCII.GetBytes("EOP").CopyTo(e, 0); nPhdr.Write(e); W16(nPhdr, 0); W16(nPhdr, 0); W16(nPhdr, pbagN); nPhdr.Write(new byte[12]); }
W16(nPbag, pgenN); W16(nPbag, pmodN); nPgen.Write(new byte[4]); nPmod.Write(new byte[10]);
foreach (var im_ in insts)
{
    nInst.Write(im_.Name); W16(nInst, ibagN);
    foreach (var z in im_.Zones)
    {
        W16(nIbag, igenN); W16(nIbag, imodN); ibagN++;
        var zs_ = z.Find(53); if (zs_ != null && smp[zs_.U].Cut) { z.G.RemoveAll(x => x.Op == 54); z.G.Insert(z.G.Count - 1, new Gen { Op = 54, Lo = 0, Hi = 0 }); }
        WriteGens(nIgen, z, ref igenN, g => g.Op == 44 && vel > 0 ? new Gen { Op = 44, Lo = 0, Hi = 127 } : g.Op == 53 ? new Gen { Op = 53, Lo = (byte)(smpMap[g.U] & 255), Hi = (byte)(smpMap[g.U] >> 8) } : g);
        foreach (var m in z.M) { nImod.Write(m); imodN++; }
    }
}
{ var e = new byte[20]; Encoding.ASCII.GetBytes("EOI").CopyTo(e, 0); nInst.Write(e); W16(nInst, ibagN); }
W16(nIbag, igenN); W16(nIbag, imodN); nIgen.Write(new byte[4]); nImod.Write(new byte[10]);

byte[] Ck(string id, byte[] data) { var ms = new MemoryStream(); ms.Write(Encoding.ASCII.GetBytes(id)); W32(ms, data.Length); ms.Write(data); if ((data.Length & 1) == 1) ms.WriteByte(0); return ms.ToArray(); }
byte[] List(string t, params byte[][] parts) { var ms = new MemoryStream(); ms.Write(Encoding.ASCII.GetBytes(t)); foreach (var p in parts) ms.Write(p); return Ck("LIST", ms.ToArray()); }
var infoMs = new MemoryStream();
foreach (var c in info) if (c.id != "ICMT") infoMs.Write(Ck(c.id, c.data));
infoMs.Write(Ck("ICMT", Encoding.ASCII.GetBytes("Subset of MuseScore_General (MIT; Frank Wen, S. Christian Collins et al.), trimmed by sf2trim\0")));
var body = new MemoryStream(); body.Write(Encoding.ASCII.GetBytes("sfbk"));
body.Write(List("INFO", infoMs.ToArray())); body.Write(List("sdta", Ck("smpl", newSmpl.ToArray())));
body.Write(List("pdta", Ck("phdr", nPhdr.ToArray()), Ck("pbag", nPbag.ToArray()), Ck("pmod", nPmod.ToArray()), Ck("pgen", nPgen.ToArray()),
    Ck("inst", nInst.ToArray()), Ck("ibag", nIbag.ToArray()), Ck("imod", nImod.ToArray()), Ck("igen", nIgen.ToArray()), Ck("shdr", newSh.ToArray())));
using (var f = File.Create(args[1])) { f.Write(Encoding.ASCII.GetBytes("RIFF")); W32(f, body.Length); f.Write(body.ToArray()); }
Console.WriteLine($"sample points {origPts} -> {newSmpl.Length / 2} (truncated {truncated}); wrote {new FileInfo(args[1]).Length / 1048576.0:F2} MB");
return 0;

class Gen { public int Op; public byte Lo, Hi; public int U => Lo | (Hi << 8); }
class Zone
{
    public List<Gen> G = new(); public List<byte[]> M = new(); public bool Looped; public int NewInst;
    public Gen Find(int op) => G.FirstOrDefault(x => x.Op == op);
}
class PresetM { public int Prog; public byte[] Hdr; public List<Zone> Zones; }
class InstM { public int Prog; public byte[] Name; public List<Zone> Zones; }
class SInfo { public int Rate; public bool Looped, Unl, CutLoop = true, Cut; public double Lo, Hi; }

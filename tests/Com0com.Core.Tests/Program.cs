using Com0com.Core;

int failed = 0, passed = 0;

void Check(string name, bool ok, string? detail = null)
{
    if (ok) { passed++; return; }
    failed++;
    Console.WriteLine($"FAIL: {name}{(detail != null ? " -- " + detail : "")}");
}

void Eq<T>(string name, T expected, T actual) =>
    Check(name, EqualityComparer<T>.Default.Equals(expected, actual), $"expected '{expected}', got '{actual}'");

PortParameters Apply(string s)
{
    var p = new PortParameters("CNCA0");
    if (!p.TryApply(s, out var e)) throw new Exception($"apply '{s}' failed: {e}");
    return p;
}

// ---- defaults / formatting
var d = new PortParameters("CNCA0");
Eq("default format", "PortName=-", d.Format(false));
Eq("default detail", "PortName=CNCA0,EmuBR=no,EmuOverrun=no,PlugInMode=no,ExclusiveMode=no,HiddenMode=no,AllDataBits=no,cts=rrts,dsr=rdtr,dcd=rdtr,ri=!on,EmuNoise=0,AddRTTO=0,AddRITO=0", d.Format(true));
Eq("default port name", "CNCA0", d.PortName);
Check("default not changed", !d.Changed);

// ---- parsing
var p1 = Apply("PortName=com5,EmuBR=yes,cts=!lopen,EmuNoise=0.0015,AddRTTO=100");
Eq("parse format", "PortName=COM5,EmuBR=yes,cts=!lopen,EmuNoise=0.0015,AddRTTO=100", p1.Format(false));
Check("parse changed", p1.Changed);
Eq("port name upper", "COM5", p1.PortName);
Eq("pin value", "!lopen", p1.GetValue("cts"));
Eq("explicit", true, p1.IsExplicit("EmuBR"));
Eq("not explicit", false, p1.IsExplicit("dsr"));
Eq("case-insensitive keys", "yes", Apply("emubr=YES").GetValue("EmuBR"));
Eq("spaces trimmed", "COM7", Apply(" PortName = COM7 , EmuBR=no").PortName);

// value "no" equal to default still becomes explicit? Original: only if changed. Default 0 == no -> not changed -> not explicit.
Eq("same-as-default not explicit", "PortName=-", Apply("EmuBR=no").Format(false));

// ---- probability
Eq("noise 0", "0", Apply("EmuNoise=0").GetValue("EmuNoise"));
Eq("noise max", "0.99999999", Apply("EmuNoise=0.99999999").GetValue("EmuNoise"));
Eq("noise trailing zeros", "0.5", Apply("EmuNoise=0.50000000000").GetValue("EmuNoise"));
Eq("noise small", "0.00000001", Apply("EmuNoise=0.00000001").GetValue("EmuNoise"));
Check("noise too long", !new PortParameters("X").TryApply("EmuNoise=0.123456789", out _));
Check("noise > 1", !new PortParameters("X").TryApply("EmuNoise=1", out _));
Check("noise junk", !new PortParameters("X").TryApply("EmuNoise=0.1a", out _));

// ---- errors are atomic
var atomic = Apply("EmuBR=yes");
Check("bad key rejected", !atomic.TryApply("EmuOverrun=yes,Bogus=1", out var err1) && err1!.Contains("Bogus"));
Eq("atomic: EmuOverrun untouched", "no", atomic.GetValue("EmuOverrun"));
Check("missing value", !new PortParameters("X").TryApply("EmuBR", out _));
Check("bad flag", !new PortParameters("X").TryApply("EmuBR=maybe", out _));
Check("bad pin", !new PortParameters("X").TryApply("cts=foo", out _));
Check("bad unsigned", !new PortParameters("X").TryApply("AddRTTO=-1", out _));

// ---- special values
var sp = Apply("EmuBR=yes,PortName=COM9");
sp.TryApply("EmuBR=-", out _);
Eq("reset single", "PortName=COM9", sp.Format(false));
sp.TryApply("*", out _);
Eq("star keeps", "PortName=COM9", sp.Format(false));
sp.TryApply("-", out _);
Eq("dash resets all", "PortName=-", sp.Format(false));
Eq("dash resets name", "CNCA0", sp.PortName);

// ---- RealPortName handling
var rp = Apply("PortName=COM#");
rp.InitRealPortName("com3");
Check("init real not changed", !rp.Changed || rp.IsExplicit("PortName"));
Eq("real port name", "COM3", rp.RealPortName);
rp.TryApply("RealPortName=COM12", out _);
Eq("real changed", "COM12", rp.RealPortName);
rp.InitRealPortName();
Eq("real cleared", "", rp.RealPortName);

// ---- pins
foreach (var (name, _) in PortParameters.PinSources)
{
    Check($"pin roundtrip {name}", PortParameters.TryParsePin(name, out var v) && PortParameters.FormatPin(v) == name);
    Check($"pin roundtrip !{name}", PortParameters.TryParsePin("!" + name, out var v2) && PortParameters.FormatPin(v2) == "!" + name);
}

// ---- ComDB names
Eq("COM1", 1, ComDb.NameToNum("COM1"));
Eq("com4096", 4096, ComDb.NameToNum("com4096"));
Eq("COM4097", 0, ComDb.NameToNum("COM4097"));
Eq("COM0", 0, ComDb.NameToNum("COM0"));
Eq("COM05", 0, ComDb.NameToNum("COM05"));
Eq("CNCA0", 0, ComDb.NameToNum("CNCA0"));
Eq("COM#", 0, ComDb.NameToNum("COM#"));

// ---- port ids
Check("parse CNCA12", C0C.TryParsePortId("CNCA12", out var isA, out var num) && isA && num == 12);
Check("parse cncb0", C0C.TryParsePortId("cncb0", out isA, out num) && !isA && num == 0);
Check("reject CNCC0", !C0C.TryParsePortId("CNCC0", out _, out _));
Check("reject CNCA", !C0C.TryParsePortId("CNCA", out _, out _));
Check("reject CNCA-1", !C0C.TryParsePortId("CNCA-1", out _, out _));

// ---- pattern / sort
Check("match COM?*", Com0comSetup.MatchPattern("COM?*", "COM12"));
Check("no match COM?* on COM", !Com0comSetup.MatchPattern("COM?*", "COM"));
Check("match *", Com0comSetup.MatchPattern("*", ""));
Check("match C*1", Com0comSetup.MatchPattern("C*1", "CNCA11"));
Check("no match C*1", !Com0comSetup.MatchPattern("C*1", "CNCA10"));
var sorted = new List<string> { "COM10", "COM2", "CNCA0", "COM1" };
sorted.Sort(Com0comSetup.NaturalCompare);
Eq("natural sort", "CNCA0,COM1,COM2,COM10", string.Join(',', sorted));

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

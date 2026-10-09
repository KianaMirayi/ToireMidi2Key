using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ToireMidi2Key;

/// <summary>程序配置，直接对应 config.json。字段名用 camelCase 序列化。</summary>
public sealed class ToireMidi2KeyConfig
{
    /// <summary>MIDI 输入设备序号（--list 可以看）。</summary>
    public int Device { get; set; }

    /// <summary>按名字匹配设备（包含匹配，忽略大小写）；非空时优先于 Device。</summary>
    public string DeviceName { get; set; } = "";

    /// <summary>scancode（推荐，游戏认）或 vk。</summary>
    public string Mode { get; set; } = KeySender.ModeScancode;

    /// <summary>音名按 scientific（60=C4）还是 yamaha（60=C3）解释。</summary>
    public string NoteNaming { get; set; } = NoteName.NamingScientific;

    /// <summary>整体移调（半音），临时改变八度很方便。</summary>
    public int Transpose { get; set; }

    /// <summary>同一个音「还没松开又按下」时的最小重触发间隔（毫秒），太快会被游戏吞掉。</summary>
    public int MinRetriggerMs { get; set; } = 30;

    /// <summary>极短音符的最小按下时长（毫秒）。</summary>
    public int MinPulseMs { get; set; } = 15;

    /// <summary>和弦错峰（毫秒）：同一瞬间的音依次错开，防止游戏只吃到第一个。0 = 关闭。</summary>
    public int ChordSpreadMs { get; set; }

    /// <summary>小于这个力度的音符直接忽略（防误触）。</summary>
    public int VelocityThreshold { get; set; } = 1;

    /// <summary>未映射的音怎么办：ignore（丢掉）或 nearest（就近折叠，原神推荐）。</summary>
    public string Unmapped { get; set; } = Mapping.ModeNearest;

    /// <summary>踩下这个 CC（默认 66）可以暂停/恢复全部注入。设成 -1 关闭。</summary>
    public int ToggleCc { get; set; } = 66;

    /// <summary>延音踏板 CC 号。</summary>
    public int SustainCc { get; set; } = 64;

    /// <summary>是否启用延音踏板行为。</summary>
    public bool SustainEnabled { get; set; }

    /// <summary>启动时自动以管理员身份重启（调试器附加时自动跳过）。</summary>
    public bool AutoElevate { get; set; }

    /// <summary>MIDI 音号（或音名；科学音名 C4=60、Yamaha C3=60，推荐直接写音号）→ 电脑按键。</summary>
    public Dictionary<string, string> Map { get; set; } = new();

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ToireMidi2KeyConfig CreateDefault() => new() { Map = GenshinPreset.Build() };

    public static ToireMidi2KeyConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            ToireMidi2KeyConfig fresh = CreateDefault();
            fresh.Save(path);
            return fresh;
        }
        return FromJson(File.ReadAllText(path));
    }

    public static ToireMidi2KeyConfig FromJson(string json)
    {
        ToireMidi2KeyConfig cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<ToireMidi2KeyConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"config.json 格式不对：{ex.Message}");
        }
        cfg ??= CreateDefault();
        cfg.Map ??= new Dictionary<string, string>();
        if (cfg.Map.Count == 0) cfg.Map = GenshinPreset.Build();
        return cfg;
    }

    public void Save(string path)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public ToireMidi2KeyConfig Clone() => FromJson(ToJson());
}

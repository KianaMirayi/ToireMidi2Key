using System.Text.RegularExpressions;

namespace Midi2Key;

/// <summary>
/// MIDI 音号 &lt;-&gt; 音名。
///
/// 这里有个所有 MIDI 工具都会踩的坑：中央 C（MIDI 60）到底叫 C4 还是 C3？
///   - scientific（科学音名，Yamaha 之外的多数软件/DAW）：60 = C4
///   - yamaha（很多硬件键盘面板上的标注）：60 = C3
/// 所以 --learn 会同时打印两种写法，配置项 noteNaming 决定按哪种解释你写的音名。
/// 拿不准就写 MIDI 音号（0~127），永远不会有歧义。
/// </summary>
public static class NoteName
{
    public const string NamingScientific = "scientific";
    public const string NamingYamaha = "yamaha";

    private static readonly string[] Pitch = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static string Scientific(int note) => Pitch[Mod12(note)] + (note / 12 - 1);

    public static string Yamaha(int note) => Pitch[Mod12(note)] + (note / 12 - 2);

    public static string Describe(int note) => $"{Scientific(note)} / {Yamaha(note)}(Yamaha)";

    public static bool IsYamaha(string naming) =>
        string.Equals(naming, NamingYamaha, StringComparison.OrdinalIgnoreCase);

    public static int Parse(string text, string naming)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("音名/音号为空");
        string t = text.Trim();

        if (int.TryParse(t, out int numeric))
        {
            if (numeric < 0 || numeric > 127) throw new FormatException($"MIDI 音号必须在 0~127 之间：{text}");
            return numeric;
        }

        Match m = Regex.Match(t, @"^([A-Ga-g])([#b]?)(-?\d+)$");
        if (!m.Success) throw new FormatException($"看不懂的音名：\"{text}\"（正确写法：C3 / C#3 / Db4 / 60）");

        char letter = char.ToUpperInvariant(m.Groups[1].Value[0]);
        int semitone = letter switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11, _ => 0
        };
        string accidental = m.Groups[2].Value;
        if (accidental == "#") semitone++;
        else if (accidental == "b") semitone--;

        int octave = int.Parse(m.Groups[3].Value);
        int note = IsYamaha(naming) ? (octave + 2) * 12 + semitone : (octave + 1) * 12 + semitone;
        if (note < 0 || note > 127) throw new FormatException($"\"{text}\" 算出来是 MIDI {note}，超出 0~127");
        return note;
    }

    private static int Mod12(int note) => ((note % 12) + 12) % 12;
}

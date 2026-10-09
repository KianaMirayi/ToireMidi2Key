namespace Midi2Key;

/// <summary>原神「风物之诗琴 / 镜花之琴」的社区通用键盘布局。</summary>
public static class GenshinPreset
{
    /// <summary>三排键：低八度在 Z 排，中八度在 A 排，高八度在 Q 排。</summary>
    public static readonly string[] Rows = { "ZXCVBNM", "ASDFGHJ", "QWERTYU" };

    /// <summary>一个八度内的白键半音偏移（原神乐器只有白键）。</summary>
    private static readonly int[] WhiteSteps = { 0, 2, 4, 5, 7, 9, 11 };

    /// <summary>默认最低音 = MIDI 48（科学音名 C3 / Yamaha 音名 C2）。</summary>
    public const int DefaultBaseNote = 48;

    /// <summary>
    /// 生成 21 个音的映射：连续三个八度的白键，依次落到 Z 排 / A 排 / Q 排。
    /// 键用 MIDI 音号字符串，避免 C3/C4 命名歧义。
    /// </summary>
    public static Dictionary<string, string> Build(int baseNote = DefaultBaseNote)
    {
        var map = new Dictionary<string, string>();
        for (int row = 0; row < Rows.Length; row++)
        {
            for (int i = 0; i < WhiteSteps.Length; i++)
            {
                int note = baseNote + row * 12 + WhiteSteps[i];
                if (note < 0 || note > 127) continue;
                map[note.ToString()] = Rows[row][i].ToString();
            }
        }
        return map;
    }
}

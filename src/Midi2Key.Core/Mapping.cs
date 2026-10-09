namespace Midi2Key;

/// <summary>一张"MIDI 音 → 电脑按键"的查找表，负责把配置翻译成可用的键码。</summary>
public sealed class Mapping
{
    public const string ModeIgnore = "ignore";
    public const string ModeNearest = "nearest";

    private readonly Dictionary<int, string> _noteToKey = new();
    private readonly Dictionary<int, ushort> _noteToVk = new();
    private readonly List<int> _sortedNotes = new();

    /// <summary>配置里写错的条目（不影响其它映射能用的部分）。</summary>
    public IReadOnlyList<string> Warnings { get; }

    public Mapping(Midi2KeyConfig config)
    {
        var warnings = new List<string>();

        foreach (KeyValuePair<string, string> entry in config.Map ?? new Dictionary<string, string>())
        {
            try
            {
                int note = NoteName.Parse(entry.Key, config.NoteNaming);
                string key = Keys.Canonical(entry.Value);
                ushort vk = Keys.Resolve(key);
                if (_noteToKey.ContainsKey(note))
                    warnings.Add($"音 {NoteName.Describe(note)} 被映射了多次，后面的覆盖前面的");
                _noteToKey[note] = key;
                _noteToVk[note] = vk;
            }
            catch (Exception ex)
            {
                warnings.Add($"配置条目 \"{entry.Key}\" → \"{entry.Value}\" 无效：{ex.Message}");
            }
        }

        _sortedNotes = _noteToKey.Keys.OrderBy(n => n).ToList();
        Warnings = warnings;
    }

    public int Count => _sortedNotes.Count;

    /// <summary>已映射的音，升序。</summary>
    public IReadOnlyList<int> Notes => _sortedNotes;

    public bool IsMapped(int note) => _noteToKey.ContainsKey(note);

    /// <summary>
    /// 解析一个音应该按哪个键。
    /// unmapped = nearest 时会做"就近折叠"：黑键折到最近白键、超范围折到最近的在范围内音。
    /// </summary>
    public bool TryResolve(int note, string unmappedMode, out string keyName, out ushort vk, out int mappedNote)
    {
        if (_noteToKey.TryGetValue(note, out keyName))
        {
            vk = _noteToVk[note];
            mappedNote = note;
            return true;
        }

        if (string.Equals(unmappedMode, ModeNearest, StringComparison.OrdinalIgnoreCase) && _sortedNotes.Count > 0)
        {
            int best = _sortedNotes[0];
            int bestDistance = Math.Abs(best - note);
            foreach (int candidate in _sortedNotes)
            {
                int distance = Math.Abs(candidate - note);
                if (distance < bestDistance)   // 平的时保留更低的音（听感上相当于降调）
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
            keyName = _noteToKey[best];
            vk = _noteToVk[best];
            mappedNote = best;
            return true;
        }

        keyName = null;
        vk = 0;
        mappedNote = -1;
        return false;
    }
}

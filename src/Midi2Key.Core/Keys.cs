namespace Midi2Key;

/// <summary>把配置里的按键名（"Z" / "SPACE" / "F24" …）翻译成 Windows 虚拟键码。</summary>
public static class Keys
{
    private static readonly Dictionary<string, ushort> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SPACE"] = 0x20,
        ["ENTER"] = 0x0D, ["RETURN"] = 0x0D,
        ["TAB"] = 0x09,
        ["ESC"] = 0x1B, ["ESCAPE"] = 0x1B,
        ["BACKSPACE"] = 0x08, ["BKSP"] = 0x08,
        ["SHIFT"] = 0x10, ["LSHIFT"] = 0xA0, ["RSHIFT"] = 0xA1,
        ["CTRL"] = 0x11, ["CONTROL"] = 0x11, ["LCTRL"] = 0xA2, ["RCTRL"] = 0xA3,
        ["ALT"] = 0x12, ["LALT"] = 0xA4, ["RALT"] = 0xA5, ["MENU"] = 0x5D,
        ["CAPSLOCK"] = 0x14,
        ["WIN"] = 0x5B, ["LWIN"] = 0x5B, ["RWIN"] = 0x5C,
        ["UP"] = 0x26, ["DOWN"] = 0x28, ["LEFT"] = 0x25, ["RIGHT"] = 0x27,
        ["HOME"] = 0x24, ["END"] = 0x23,
        ["PGUP"] = 0x21, ["PRIOR"] = 0x21, ["PGDN"] = 0x22, ["PAGEDOWN"] = 0x22, ["NEXT"] = 0x22,
        ["INSERT"] = 0x2D, ["INS"] = 0x2D,
        ["DELETE"] = 0x2E, ["DEL"] = 0x2E,
        ["PRINTSCREEN"] = 0x2C, ["SNAPSHOT"] = 0x2C,
        ["NUMLOCK"] = 0x90, ["SCROLLLOCK"] = 0x91, ["PAUSE"] = 0x13,

        ["NUM0"] = 0x60, ["NUM1"] = 0x61, ["NUM2"] = 0x62, ["NUM3"] = 0x63, ["NUM4"] = 0x64,
        ["NUM5"] = 0x65, ["NUM6"] = 0x66, ["NUM7"] = 0x67, ["NUM8"] = 0x68, ["NUM9"] = 0x69,
        ["NUMPAD0"] = 0x60, ["NUMPAD1"] = 0x61, ["NUMPAD2"] = 0x62, ["NUMPAD3"] = 0x63, ["NUMPAD4"] = 0x64,
        ["NUMPAD5"] = 0x65, ["NUMPAD6"] = 0x66, ["NUMPAD7"] = 0x67, ["NUMPAD8"] = 0x68, ["NUMPAD9"] = 0x69,
        ["NUMMUL"] = 0x6A, ["NUMPAD*"] = 0x6A, ["NUMADD"] = 0x6B, ["NUMPAD+"] = 0x6B,
        ["NUMSUB"] = 0x6D, ["NUMPAD-"] = 0x6D, ["NUMDEC"] = 0x6E, ["NUMPAD."] = 0x6E,
        ["NUMDIV"] = 0x6F, ["NUMPAD/"] = 0x6F,

        ["MINUS"] = 0xBD, ["-"] = 0xBD, ["EQUAL"] = 0xBB, ["="] = 0xBB,
        ["COMMA"] = 0xBC, [","] = 0xBC, ["PERIOD"] = 0xBE, ["."] = 0xBE,
        ["SLASH"] = 0xBF, ["/"] = 0xBF, ["SEMICOLON"] = 0xBA, [";"] = 0xBA,
        ["QUOTE"] = 0xDE, ["'"] = 0xDE, ["LBRACKET"] = 0xDB, ["["] = 0xDB,
        ["RBRACKET"] = 0xDD, ["]"] = 0xDD, ["BACKSLASH"] = 0xDC, ["\\"] = 0xDC,
        ["GRAVE"] = 0xC0, ["`"] = 0xC0,
    };

    static Keys()
    {
        for (int i = 1; i <= 24; i++) Table["F" + i] = (ushort)(0x6F + i);   // F1=0x70 … F24=0x87
    }

    public static ushort Resolve(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
            throw new ArgumentException("按键名不能为空");

        string name = keyName.Trim().ToUpperInvariant();
        if (name.Length == 1)
        {
            char c = name[0];
            if (c >= 'A' && c <= 'Z') return c;
            if (c >= '0' && c <= '9') return c;
        }
        if (Table.TryGetValue(name, out ushort vk)) return vk;
        throw new ArgumentException($"不认识的按键名：\"{keyName}\"（可用：A-Z / 0-9 / SPACE / ENTER / F1-F24 / 方向键 / NUMPAD0-9 …）");
    }

    /// <summary>统一大小写，方便配置和日志比较。</summary>
    public static string Canonical(string keyName) => (keyName ?? string.Empty).Trim().ToUpperInvariant();

    public static bool IsKnown(string keyName)
    {
        try { Resolve(keyName); return true; } catch { return false; }
    }
}

using System;
using System.Globalization;

namespace QuickApp.Core.Services;

/// <summary>热键修饰键。数值与 Win32 MOD_* 常量对齐，平台层可以直接映射。</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8
}

/// <summary>
/// 全局热键组合：修饰键 + Win32 虚拟键码。
/// 解析放 Core 是为了让键位规则可单测，平台层只做数值映射。
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    /// <summary>解析 "Ctrl+Alt+Space" 这类描述。修饰键与键名大小写不敏感。</summary>
    public static bool TryParse(string? gesture, out HotkeyGesture result, out string? error)
    {
        result = default;
        error = null;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            error = "热键描述为空。";
            return false;
        }

        string[] tokens = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            error = "热键至少要有一个修饰键（Ctrl/Alt/Shift/Win）加一个键。";
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= HotkeyModifiers.Ctrl;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "win" or "windows" or "meta":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    error = "「" + tokens[i] + "」不是修饰键，修饰键只支持 Ctrl/Alt/Shift/Win。";
                    return false;
            }
        }

        if (!TryMapKey(tokens[^1], out int virtualKey))
        {
            error = "无法识别的键名：" + tokens[^1] + "。";
            return false;
        }

        result = new HotkeyGesture(modifiers, virtualKey);
        return true;
    }

    /// <summary>键名 → Win32 虚拟键码。字母、数字与 F1~F24 直接写，其余用标准 VK 值。</summary>
    private static bool TryMapKey(string token, out int virtualKey)
    {
        string key = token.ToUpperInvariant();

        if (key.Length == 1)
        {
            char c = key[0];
            if (c is >= 'A' and <= 'Z')
            {
                virtualKey = c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }
        }

        if (key.Length is 2 or 3 && key[0] == 'F' &&
            int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int fn) &&
            fn is >= 1 and <= 24)
        {
            virtualKey = 0x70 + fn - 1;
            return true;
        }

        virtualKey = key switch
        {
            "SPACE" or "SPACEBAR" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "BACKSPACE" or "BACK" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "+" or "PLUS" or "OEMPLUS" => 0xBB,
            "-" or "MINUS" or "OEMMINUS" => 0xBD,
            "," or "COMMA" or "OEMCOMMA" => 0xBC,
            "." or "PERIOD" or "OEMPERIOD" => 0xBE,
            "/" or "SLASH" => 0xBF,
            ";" or "OEMSEMICOLON" => 0xBA,
            "[" or "OEM_4" => 0xDB,
            "]" or "OEM_6" => 0xDD,
            "'" or "OEM_7" => 0xDE,
            "\\" or "OEM_5" => 0xDC,
            _ => 0
        };

        return virtualKey != 0;
    }
}

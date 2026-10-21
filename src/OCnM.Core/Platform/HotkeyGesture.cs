namespace OCnM.Core.Platform;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Super = 8,
}

/// <summary>
/// A parsed shortcut such as "Ctrl+Alt+Space". <see cref="Key"/> is a normalized key name:
/// "A"-"Z", "0"-"9", "F1"-"F24", or one of <see cref="NamedKeys"/>.
/// </summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public static readonly IReadOnlyList<string> NamedKeys =
    [
        "Space", "Enter", "Tab", "Escape", "Backspace", "Insert", "Delete", "Home", "End",
        "PageUp", "PageDown", "Up", "Down", "Left", "Right",
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Return"] = "Enter",
        ["Esc"] = "Escape",
        ["Del"] = "Delete",
        ["Ins"] = "Insert",
        ["PgUp"] = "PageUp",
        ["PgDn"] = "PageDown",
        ["ArrowUp"] = "Up",
        ["ArrowDown"] = "Down",
        ["ArrowLeft"] = "Left",
        ["ArrowRight"] = "Right",
    };

    public static bool TryParse(string? text, out HotkeyGesture? gesture, out string? error)
    {
        gesture = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The shortcut is empty.";
            return false;
        }

        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0)
            {
                error = $"\"{text}\" is not a valid shortcut.";
                return false;
            }

            var mod = raw.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Ctrl,
                "alt" or "option" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "super" or "win" or "meta" or "cmd" or "mod4" => HotkeyModifiers.Super,
                _ => HotkeyModifiers.None,
            };
            if (mod != HotkeyModifiers.None)
            {
                mods |= mod;
                continue;
            }

            if (key is not null)
            {
                error = $"\"{text}\" has more than one key.";
                return false;
            }
            key = NormalizeKey(raw);
            if (key is null)
            {
                error = $"Unknown key \"{raw}\".";
                return false;
            }
        }

        if (key is null)
        {
            error = "The shortcut needs a key besides the modifiers.";
            return false;
        }
        if (mods == HotkeyModifiers.None)
        {
            error = "Add at least one modifier (Ctrl, Alt, Shift or Super).";
            return false;
        }

        gesture = new HotkeyGesture(mods, key);
        return true;
    }

    private static string? NormalizeKey(string raw)
    {
        if (raw.Length == 1 && char.IsAsciiLetterOrDigit(raw[0]))
            return raw.ToUpperInvariant();
        if ((raw[0] is 'F' or 'f') && int.TryParse(raw.AsSpan(1), out var n) && n is >= 1 and <= 24 && raw[1] != '0')
            return "F" + n;
        if (Aliases.TryGetValue(raw, out var alias))
            return alias;
        return NamedKeys.FirstOrDefault(k => string.Equals(k, raw, StringComparison.OrdinalIgnoreCase));
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
            parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Super))
            parts.Add("Super");
        parts.Add(Key);
        return string.Join('+', parts);
    }
}

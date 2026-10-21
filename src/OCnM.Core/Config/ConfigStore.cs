using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OCnM.Core.Models;

namespace OCnM.Core.Config;

/// <summary>Result of loading the config file.</summary>
/// <param name="Config">The usable config (defaults when the file was missing or corrupt).</param>
/// <param name="Status">What happened while loading.</param>
/// <param name="BackupPath">Where a corrupt file was moved, if it was.</param>
public sealed record ConfigLoadResult(OCnMConfig Config, ConfigLoadStatus Status, string? BackupPath = null, string? Error = null);

public enum ConfigLoadStatus
{
    Loaded,
    CreatedNew,
    RecoveredFromCorruption,
}

/// <summary>
/// Reads and writes config.json. Reading is tolerant: unknown fields are ignored and every
/// missing or invalid field falls back to its default. Only an unparseable file counts as corrupt.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        // Keeps "Ctrl+Alt+Space" and non-ASCII paths readable; the file is never embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private readonly object _saveLock = new();

    public ConfigStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public string BackupPath => FilePath + ".bak";

    public static ConfigStore CreateDefault() => new(AppPaths.ConfigFile);

    public ConfigLoadResult Load()
    {
        if (!File.Exists(FilePath))
        {
            var fresh = OCnMConfig.CreateDefault();
            TrySave(fresh, out _);
            return new ConfigLoadResult(fresh, ConfigLoadStatus.CreatedNew);
        }

        string text;
        try
        {
            text = File.ReadAllText(FilePath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable (permissions, locked): use defaults but leave the file alone.
            return new ConfigLoadResult(OCnMConfig.CreateDefault(), ConfigLoadStatus.Loaded, Error: ex.Message);
        }

        if (TryParse(text, out var config, out var parseError))
            return new ConfigLoadResult(config, ConfigLoadStatus.Loaded);

        string? backup = null;
        try
        {
            File.Copy(FilePath, BackupPath, overwrite: true);
            backup = BackupPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without a backup we must not overwrite the original; run on defaults in memory.
            return new ConfigLoadResult(OCnMConfig.CreateDefault(), ConfigLoadStatus.RecoveredFromCorruption, null, parseError);
        }

        var recreated = OCnMConfig.CreateDefault();
        TrySave(recreated, out _);
        return new ConfigLoadResult(recreated, ConfigLoadStatus.RecoveredFromCorruption, backup, parseError);
    }

    public bool TrySave(OCnMConfig config, out string? error)
    {
        try
        {
            Save(config);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Writes atomically: a temp file next to the target is renamed over it.</summary>
    public void Save(OCnMConfig config)
    {
        var json = Serialize(config);
        lock (_saveLock)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var temp = FilePath + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, FilePath, overwrite: true);
        }
    }

    public static byte[] Serialize(OCnMConfig config)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteNumber("version", OCnMConfig.CurrentVersion);

            var s = config.Settings;
            w.WriteStartObject("settings");
            w.WriteString("defaultAction", EnumToJson(s.DefaultAction));
            w.WriteString("conflictPolicy", EnumToJson(s.ConflictPolicy));
            w.WriteString("hotkey", s.Hotkey);
            w.WriteString("theme", EnumToJson(s.Theme));
            w.WriteBoolean("launchAtLogin", s.LaunchAtLogin);
            w.WriteBoolean("hideOnFocusLoss", s.HideOnFocusLoss);
            w.WriteNumber("hideAfterDropMs", s.HideAfterDropMs);
            w.WriteBoolean("pinned", s.Pinned);
            w.WriteBoolean("showTrayIcon", s.ShowTrayIcon);
            w.WriteBoolean("reduceMotion", s.ReduceMotion);
            w.WriteString("linuxBackend", EnumToJson(s.LinuxBackend));
            w.WriteEndObject();

            w.WriteStartArray("slices");
            foreach (var slice in Normalize(config.Slices))
            {
                w.WriteStartObject();
                w.WriteNumber("index", slice.Index);
                WriteNullableString(w, "label", slice.Label);
                WriteNullableString(w, "path", slice.Path);
                WriteNullableString(w, "color", slice.Color);
                WriteNullableString(w, "icon", slice.Icon is { } icon ? EnumToJson(icon) : null);
                w.WriteString("action", EnumToJson(slice.Action));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }

    /// <summary>Parses config JSON. Returns false only when the text is not a JSON object.</summary>
    public static bool TryParse(string text, out OCnMConfig config, out string? error)
    {
        config = OCnMConfig.CreateDefault();
        error = null;
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "The config root is not a JSON object.";
                return false;
            }

            var version = ReadInt(root, "version") ?? OCnMConfig.CurrentVersion;
            var settings = root.TryGetProperty("settings", out var se) && se.ValueKind == JsonValueKind.Object
                ? ReadSettings(se)
                : new AppSettings();
            var slices = root.TryGetProperty("slices", out var sl) && sl.ValueKind == JsonValueKind.Array
                ? ReadSlices(sl)
                : OCnMConfig.CreateEmptySlices();

            config = new OCnMConfig { Version = version, Settings = settings, Slices = slices };
            return true;
        }
    }

    private static AppSettings ReadSettings(JsonElement e)
    {
        var d = new AppSettings();
        return new AppSettings
        {
            DefaultAction = ReadEnum(e, "defaultAction", d.DefaultAction),
            ConflictPolicy = ReadEnum(e, "conflictPolicy", d.ConflictPolicy),
            Hotkey = ReadString(e, "hotkey") ?? d.Hotkey,
            Theme = ReadEnum(e, "theme", d.Theme),
            LaunchAtLogin = ReadBool(e, "launchAtLogin") ?? d.LaunchAtLogin,
            HideOnFocusLoss = ReadBool(e, "hideOnFocusLoss") ?? d.HideOnFocusLoss,
            HideAfterDropMs = Math.Clamp(ReadInt(e, "hideAfterDropMs") ?? d.HideAfterDropMs, 0, 60_000),
            Pinned = ReadBool(e, "pinned") ?? d.Pinned,
            ShowTrayIcon = ReadBool(e, "showTrayIcon") ?? d.ShowTrayIcon,
            ReduceMotion = ReadBool(e, "reduceMotion") ?? d.ReduceMotion,
            LinuxBackend = ReadEnum(e, "linuxBackend", d.LinuxBackend),
        };
    }

    private static ImmutableArray<SliceConfig> ReadSlices(JsonElement array)
    {
        var result = new SliceConfig?[OCnMConfig.SliceCount];
        var unplaced = new List<SliceConfig>();
        var position = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                position++;
                continue;
            }

            var slice = new SliceConfig
            {
                Label = NullIfBlank(ReadString(item, "label")),
                Path = NullIfBlank(ReadString(item, "path")),
                Color = NormalizeColor(ReadString(item, "color")),
                Icon = ReadNullableEnum<SliceIcon>(item, "icon"),
                Action = ReadEnum(item, "action", SliceAction.Inherit),
            };

            // Prefer the explicit index; fall back to array position when it is missing, invalid or taken.
            var index = ReadInt(item, "index") ?? position;
            if (index is >= 0 and < OCnMConfig.SliceCount && result[index] is null)
                result[index] = slice with { Index = index };
            else
                unplaced.Add(slice);
            position++;
        }

        foreach (var slice in unplaced)
        {
            var free = Array.FindIndex(result, s => s is null);
            if (free < 0)
                break;
            result[free] = slice with { Index = free };
        }

        return result.Select((s, i) => s ?? SliceConfig.Empty(i)).ToImmutableArray();
    }

    private static IEnumerable<SliceConfig> Normalize(ImmutableArray<SliceConfig> slices)
    {
        for (var i = 0; i < OCnMConfig.SliceCount; i++)
        {
            var slice = !slices.IsDefault && i < slices.Length ? slices[i] : SliceConfig.Empty(i);
            yield return slice with { Index = i };
        }
    }

    /// <summary>Accepts "#RGB" and "#RRGGBB" (with or without '#'); returns "#RRGGBB" or null.</summary>
    public static string? NormalizeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var hex = value.Trim().TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        if (hex.Length != 6 || !hex.All(Uri.IsHexDigit))
            return null;
        return "#" + hex.ToUpperInvariant();
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? ReadString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? ReadBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static int? ReadInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static T ReadEnum<T>(JsonElement e, string name, T fallback) where T : struct, Enum =>
        ReadNullableEnum<T>(e, name) ?? fallback;

    private static T? ReadNullableEnum<T>(JsonElement e, string name) where T : struct, Enum
    {
        var s = ReadString(e, name);
        // Numeric strings are rejected so that "7" cannot become an undefined enum value.
        if (s is null || s.Length == 0 || char.IsDigit(s[0]) || s[0] == '-')
            return null;
        return Enum.TryParse<T>(s, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : null;
    }

    /// <summary>Enum names are stored in camelCase ("autoRename").</summary>
    public static string EnumToJson<T>(T value) where T : struct, Enum
    {
        var name = value.ToString();
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static void WriteNullableString(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null)
            w.WriteNull(name);
        else
            w.WriteString(name, value);
    }
}

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumin.Models;

namespace Lumin.Configuration;

/// <summary>Versioned, data-only JSON persistence for local Lumin profiles.</summary>
public sealed class ConfigurationStore
{
    public const int CurrentSchemaVersion = 2;
    private const int MaxConfigurationBytes = 1_048_576;
    private static readonly HashSet<string> SupportedThemes = new(StringComparer.OrdinalIgnoreCase) { "Lumin", "Rain", "Synth" };
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public ConfigurationStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumin", "configuration.json");
    }

    public ClientConfiguration Load()
    {
        if (!File.Exists(_path))
        {
            var initial = CreateDefault();
            Save(initial);
            return initial;
        }

        var json = ReadLimited(_path);
        var document = Parse(json);
        Validate(document);
        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            document.SchemaVersion = CurrentSchemaVersion;
            Save(document);
        }
        return document;
    }

    public void Save(ClientConfiguration document)
    {
        Validate(document);
        document.SchemaVersion = CurrentSchemaVersion;
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Configuration path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, _jsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    public ClientConfiguration Import(string path)
    {
        var imported = Parse(ReadLimited(path));
        Validate(imported);
        return imported;
    }

    public void Export(ClientConfiguration document, string path)
    {
        Validate(document);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(document, _jsonOptions));
    }

    public static ClientConfiguration CreateDefault()
    {
        var profile = new ConfigurationProfile();
        return new ClientConfiguration { ActiveProfileId = profile.Id, Profiles = [profile] };
    }

    private ClientConfiguration Parse(string json)
    {
        using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Configuration root must be a JSON object.");

        var version = parsed.RootElement.TryGetProperty("SchemaVersion", out var versionElement) && versionElement.TryGetInt32(out var value) ? value : 1;
        return version switch
        {
            1 => MigrateV1(parsed.RootElement),
            CurrentSchemaVersion => JsonSerializer.Deserialize<ClientConfiguration>(json, _jsonOptions) ?? throw new InvalidDataException("Configuration was empty."),
            _ => throw new InvalidDataException($"Configuration schema version {version} is not supported.")
        };
    }

    private static ClientConfiguration MigrateV1(JsonElement root)
    {
        // V1 stored one flat active profile; migrate it into the named-profile schema.
        var legacy = JsonSerializer.Deserialize<LegacyV1>(root.GetRawText()) ?? throw new InvalidDataException("Configuration was empty.");
        var profile = new ConfigurationProfile
        {
            Id = string.IsNullOrWhiteSpace(legacy.ActiveProfileId) ? Guid.NewGuid().ToString("N") : legacy.ActiveProfileId,
            Name = "Lumin Default",
            Modules = legacy.Modules ?? new(StringComparer.Ordinal)
        };
        return new ClientConfiguration { SchemaVersion = CurrentSchemaVersion, ActiveProfileId = profile.Id, Profiles = [profile] };
    }

    private static void Validate(ClientConfiguration document)
    {
        if (document.Profiles is null || document.Profiles.Count is < 1 or > 50)
            throw new InvalidDataException("A configuration must contain between 1 and 50 profiles.");
        if (document.SchemaVersion is < 1 or > CurrentSchemaVersion)
            throw new InvalidDataException($"Configuration schema version {document.SchemaVersion} is not supported.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in document.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 64 || !ids.Add(profile.Id))
                throw new InvalidDataException("Profile identifiers must be unique and no longer than 64 characters.");
            if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 40 || !names.Add(profile.Name.Trim()))
                throw new InvalidDataException("Profile names must be unique and contain 1 to 40 characters.");
            if (profile.Modules is null || profile.Modules.Count > 500)
                throw new InvalidDataException($"Profile '{profile.Name}' has an invalid module list.");
            if (profile.Modules.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 80))
                throw new InvalidDataException($"Profile '{profile.Name}' contains an invalid module identifier.");
            if (profile.InterfaceScale is < 0.75 or > 1.5 || double.IsNaN(profile.InterfaceScale) || double.IsInfinity(profile.InterfaceScale))
                throw new InvalidDataException($"Profile '{profile.Name}' has an invalid interface scale.");
            if (string.IsNullOrWhiteSpace(profile.Theme) || !SupportedThemes.Contains(profile.Theme))
                throw new InvalidDataException($"Theme '{profile.Theme}' is not supported by this build.");
        }
        if (!ids.Contains(document.ActiveProfileId))
            throw new InvalidDataException("The active profile does not exist in this configuration.");
    }

    private static string ReadLimited(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Configuration file was not found.", path);
        if (info.Length > MaxConfigurationBytes) throw new InvalidDataException("Configuration files must be 1 MB or smaller.");
        return File.ReadAllText(path);
    }

    private sealed class LegacyV1
    {
        public int SchemaVersion { get; set; }
        public string? ActiveProfileId { get; set; }
        public Dictionary<string, bool>? Modules { get; set; }
    }
}

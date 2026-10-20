namespace Lumin.Configuration;

public sealed class ClientConfiguration
{
    public int SchemaVersion { get; set; } = ConfigurationStore.CurrentSchemaVersion;
    public string ActiveProfileId { get; set; } = string.Empty;
    public List<ConfigurationProfile> Profiles { get; set; } = [];
}

public sealed class ConfigurationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Lumin Default";
    public Dictionary<string, bool> Modules { get; set; } = new(StringComparer.Ordinal);
    public double InterfaceScale { get; set; } = 1.0;
    public string Theme { get; set; } = "Lumin";
}

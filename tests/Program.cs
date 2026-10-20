using System.IO;
using Lumin.Configuration;
using Lumin.Diagnostics;
using Lumin.Models;
using Lumin.Modules;

var tests = new (string Name, Action Run)[]
{
    ("registry has unique stable identifiers", RegistryHasUniqueIds),
    ("module state raises change notifications", ModuleStateTransitions),
    ("configuration persists profiles", ConfigurationRoundTrip),
    ("version 1 configuration migrates", MigratesV1Configuration),
    ("unsupported schema is rejected", RejectsUnsupportedSchema),
    ("invalid profile scale is rejected", RejectsInvalidScale),
    ("duplicate profile names are rejected", RejectsDuplicateProfileNames),
    ("oversized imports are rejected", RejectsOversizedImport),
    ("activity log records and exports local events", ActivityLogRoundTrip)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} checks passed.");
return failures == 0 ? 0 : 1;

static void RegistryHasUniqueIds()
{
    var modules = new ModuleRegistry().All;
    Assert(modules.Count == 19, "Expected the initial 19-module catalog.");
    Assert(modules.Select(module => module.Id).Distinct(StringComparer.Ordinal).Count() == modules.Count, "Module IDs must be unique.");
    Assert(modules.Where(module => !module.CanEnable).All(module => !module.IsEnabled), "Unavailable modules start disabled.");
}

static void ModuleStateTransitions()
{
    var module = new ModuleItem { Id = "test", Name = "Test", Description = "Test module", Category = "Utility", Availability = "Local test", CanEnable = true };
    var changes = new List<string?>();
    module.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
    module.IsEnabled = true;
    Assert(module.StateLabel == "ENABLED", "Enabled state label did not update.");
    Assert(changes.Contains(nameof(ModuleItem.IsEnabled)) && changes.Contains(nameof(ModuleItem.StateLabel)), "Expected state and label notifications.");
    module.IsEnabled = false;
    Assert(module.StateLabel == "DISABLED", "Disabled state label did not update.");
}

static void ConfigurationRoundTrip()
{
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "configuration.json");
    var store = new ConfigurationStore(path);
    var document = ConfigurationStore.CreateDefault();
    document.Profiles[0].Modules["crosshair"] = true;
    document.Profiles[0].InterfaceScale = 1.25;
    document.Profiles.Add(new ConfigurationProfile { Name = "Practice", Modules = new() { ["hud"] = true } });
    store.Save(document);
    var loaded = store.Load();
    Assert(loaded.Profiles.Count == 2, "Both profiles should persist.");
    Assert(loaded.Profiles[0].Modules["crosshair"], "Module state should persist.");
    Assert(Math.Abs(loaded.Profiles[0].InterfaceScale - 1.25) < 0.001, "Scale should persist.");
}

static void MigratesV1Configuration()
{
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "legacy.json");
    File.WriteAllText(path, "{\"SchemaVersion\":1,\"ActiveProfileId\":\"legacy\",\"Modules\":{\"crosshair\":true}}");
    var migrated = new ConfigurationStore(path).Import(path);
    Assert(migrated.SchemaVersion == ConfigurationStore.CurrentSchemaVersion, "Migration should produce the current schema.");
    Assert(migrated.Profiles.Single().Name == "Lumin Default", "Migrated profile should receive the default name.");
    Assert(migrated.Profiles.Single().Modules["crosshair"], "Migrated module state should be preserved.");
}

static void RejectsUnsupportedSchema()
{
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "future.json");
    File.WriteAllText(path, "{\"SchemaVersion\":99,\"ActiveProfileId\":\"x\",\"Profiles\":[]}");
    AssertThrows<InvalidDataException>(() => new ConfigurationStore(path).Import(path));
}

static void RejectsInvalidScale()
{
    var document = ConfigurationStore.CreateDefault();
    document.Profiles[0].InterfaceScale = 4;
    using var temp = new TemporaryDirectory();
    var store = new ConfigurationStore(Path.Combine(temp.Path, "configuration.json"));
    AssertThrows<InvalidDataException>(() => store.Save(document));
}

static void RejectsDuplicateProfileNames()
{
    var document = ConfigurationStore.CreateDefault();
    document.Profiles.Add(new ConfigurationProfile { Name = "lumin default" });
    using var temp = new TemporaryDirectory();
    var store = new ConfigurationStore(Path.Combine(temp.Path, "configuration.json"));
    AssertThrows<InvalidDataException>(() => store.Save(document));
}

static void RejectsOversizedImport()
{
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "oversized.json");
    File.WriteAllText(path, new string(' ', 1_048_577));
    AssertThrows<InvalidDataException>(() => new ConfigurationStore(path).Import(path));
}

static void ActivityLogRoundTrip()
{
    using var temp = new TemporaryDirectory();
    var logger = new ActivityLogger(temp.Path);
    logger.Record("ignored_while_disabled");
    Assert(!Directory.GetFiles(temp.Path).Any(), "Disabled logger should not create files.");
    logger.IsEnabled = true;
    logger.Record("module_state_changed", "profile-1", "crosshair", "enabled");
    var export = Path.Combine(temp.Path, "export.jsonl");
    Assert(logger.ExportTo(export) == 1, "Expected one daily log file.");
    var line = File.ReadAllText(export);
    Assert(line.Contains("module_state_changed", StringComparison.Ordinal), "Expected the module event in the export.");
    Assert(line.Contains("Disconnected", StringComparison.Ordinal), "Log must clearly report that no game integration was connected.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "test-data", $"lumin-test-{Guid.NewGuid():N}");
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        var fullPath = System.IO.Path.GetFullPath(Path);
        var testRoot = System.IO.Path.GetFullPath(AppContext.BaseDirectory);
        if (!fullPath.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(fullPath).StartsWith("lumin-test-", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to clean a temporary directory outside the test temp root.");
        Directory.Delete(fullPath, recursive: true);
    }
}

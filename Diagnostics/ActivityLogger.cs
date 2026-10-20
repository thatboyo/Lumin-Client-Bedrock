using System.IO;
using System.Text.Json;

namespace Lumin.Diagnostics;

/// <summary>Records explicit Lumin UI/configuration events only; it does not inspect Minecraft.</summary>
public sealed class ActivityLogger
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _jsonOptions = new() { MaxDepth = 8 };

    public bool IsEnabled { get; set; }
    public string LogDirectory => _directory;

    public ActivityLogger(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumin", "logs");
    }

    public void Record(string eventType, string? profileId = null, string? moduleId = null, string? value = null)
    {
        if (!IsEnabled) return;
        Directory.CreateDirectory(_directory);
        var record = new ActivityRecord(DateTimeOffset.UtcNow, eventType, profileId, moduleId, value, "Disconnected");
        var line = JsonSerializer.Serialize(record, _jsonOptions);
        File.AppendAllText(Path.Combine(_directory, $"activity-{DateTimeOffset.UtcNow:yyyy-MM-dd}.jsonl"), line + Environment.NewLine);
    }

    public int ExportTo(string destination)
    {
        var files = Directory.Exists(_directory)
            ? Directory.GetFiles(_directory, "activity-*.jsonl", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        if (files.Length == 0) throw new FileNotFoundException("There are no activity logs to export yet.");
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using var output = new StreamWriter(destination, append: false);
        foreach (var file in files)
            foreach (var line in File.ReadLines(file))
                output.WriteLine(line);
        return files.Length;
    }
}

public sealed record ActivityRecord(
    DateTimeOffset TimestampUtc,
    string EventType,
    string? ProfileId,
    string? ModuleId,
    string? Value,
    string IntegrationState);

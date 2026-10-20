using Lumin.Models;

namespace Lumin.Modules;

/// <summary>Single source of truth for the module catalog and its declared capabilities.</summary>
public sealed class ModuleRegistry
{
    private readonly IReadOnlyList<ModuleItem> _modules;

    public ModuleRegistry()
    {
        _modules = new List<ModuleItem>
        {
            Item("hud", "HUD", "A compact on-screen status display.", "Visuals", "Local preview only", true),
            Item("crosshair", "Crosshair", "Tune a minimal reticle for a local scene.", "Visuals", "Local preview only", true),
            Item("waypoints", "Waypoints", "Save and organize locations in a local profile.", "Visuals", "Standalone utility", true),
            Item("breadcrumbs", "Breadcrumbs", "Review recent positions in a supported test scene.", "Visuals", "Simulation only", false, true),
            Item("entity-labels", "Entity Labels", "Show names and details for visible entities.", "Visuals", "Integration unavailable", false, true),
            Item("esp", "ESP", "Configurable entity highlighting for authorized test scenes.", "Visuals", "Integration unavailable", false, true),
            Item("tracers", "Tracers", "Draw lines from a screen anchor to selected entities.", "Visuals", "Integration unavailable", false, true),
            Item("item-esp", "Item ESP", "Highlight selected item entities when available.", "Visuals", "Integration unavailable", false, true),
            Item("storage-esp", "Storage ESP", "Visualize selected storage objects in test environments.", "Visuals", "Integration unavailable", false, true),
            Item("sprint", "Sprint", "Sprint assistance for a supported simulation.", "Movement", "Simulation only", false, true),
            Item("movement-recorder", "Movement Recorder", "Record local movement paths for review.", "Movement", "Simulation only", false, true),
            Item("position-history", "Position History", "Track position changes for diagnostics.", "Movement", "Simulation only", false, true),
            Item("safe-landing", "Safe Landing", "Show local fall-distance and landing indicators.", "Movement", "Simulation only", false, true),
            Item("reach-visualizer", "Reach Visualizer", "Display interaction distance in a local test scene.", "Combat", "Integration unavailable", false, true),
            Item("aim-training", "Aim Training", "Standalone target tracking practice scene.", "Combat", "Planned", false, true),
            Item("configuration-manager", "Configuration Manager", "Manage local settings and named profiles.", "Utility", "Standalone utility", true),
            Item("hide-gui", "Hide GUI", "Minimize or restore Lumin with Ctrl+Shift+H. The window remains available from the taskbar.", "Utility", "Standalone utility", true),
            Item("activity-log", "Activity Log", "Record local Lumin setting changes to an exportable log file.", "Utility", "Standalone utility · no game data", true),
            Item("keybind-manager", "Keybind Manager", "Inspect and configure module shortcuts.", "Utility", "Standalone preview", true),
            Item("diagnostic-report", "Diagnostic Report", "Summarize local application and integration status.", "Utility", "Standalone utility", true)
        };

        var duplicateId = _modules.GroupBy(module => module.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
            throw new InvalidOperationException($"Duplicate module identifier '{duplicateId.Key}'.");
    }

    public IReadOnlyList<ModuleItem> All => _modules;

    public ModuleItem? Find(string id) => _modules.FirstOrDefault(module => module.Id == id);

    private static ModuleItem Item(string id, string name, string description, string category, string availability, bool canEnable, bool experimental = false) =>
        new() { Id = id, Name = name, Description = description, Category = category, Availability = availability, CanEnable = canEnable, IsExperimental = experimental };
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuadBrowserLite;

public sealed record LoadedPaneSettings(
    List<AiPreset> Presets,
    List<PaneConfig> InitialPanes);

public static class PaneConfigLoader
{
    private static readonly string[] PaneKeys = { "pane0", "pane1", "pane2", "pane3" };

    public static string GetDefaultListPath()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            var dir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            var name = Path.GetFileNameWithoutExtension(exePath);
            return Path.Combine(dir, name + ".lst");
        }

        return Path.Combine(AppContext.BaseDirectory, "QuadBrowserLite.lst");
    }

    public static LoadedPaneSettings Load(string? lstPath = null)
    {
        var path = lstPath ?? GetDefaultListPath();

        var urlMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var paneAssignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(path))
        {
            string section = string.Empty;

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith(';'))
                {
                    continue;
                }

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1].Trim().ToLowerInvariant();
                    continue;
                }

                var parts = line.Split(',', 2);
                if (parts.Length < 2)
                {
                    continue;
                }

                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (section == "urls")
                {
                    urlMap[key] = value;
                }
                else if (section == "panes")
                {
                    paneAssignments[key.ToLowerInvariant()] = value;
                }
            }
        }

        var presets = urlMap
            .Select(kvp => new AiPreset(kvp.Key, kvp.Value))
            .ToList();
        presets.Add(AiPreset.Custom);

        var initialPanes = new List<PaneConfig>(4);
        for (int i = 0; i < 4; i++)
        {
            var paneKey = PaneKeys[i];
            var slotLabel = PaneSlots.All[i];

            if (paneAssignments.TryGetValue(paneKey, out var selectedName) &&
                !string.IsNullOrWhiteSpace(selectedName))
            {
                var matchedUrl = urlMap.TryGetValue(selectedName, out var u) ? u : string.Empty;
                initialPanes.Add(new PaneConfig(slotLabel, selectedName, matchedUrl));
            }
            else
            {
                var fallback = presets.FirstOrDefault(p => !string.IsNullOrEmpty(p.Url))
                               ?? AiPreset.Custom;
                initialPanes.Add(new PaneConfig(slotLabel, fallback.Name, fallback.Url));
            }
        }

        return new LoadedPaneSettings(presets, initialPanes);
    }
}

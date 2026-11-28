using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace QuadBrowserLite;

// .lst の必要な行だけを書き換える。コメントや並び順は残す
public static class PaneListWriter
{
    // [urls] に名前と URL を登録する(同じ名前があれば置き換える)
    public static void SaveUrl(string name, string url, string? lstPath = null) =>
        Update("urls", new[] { (name, url) }, lstPath);

    // [panes] に起動時の4画面を保存する
    public static void SaveStartupPanes(IReadOnlyList<PaneConfig> configs, string? lstPath = null)
    {
        var entries = configs.Select(c =>
            ($"pane{Array.IndexOf(PaneSlots.All, c.SlotLabel)}", c.SelectedAiName));

        Update("panes", entries.ToArray(), lstPath);
    }

    private static void Update(string section, (string Key, string Value)[] entries, string? lstPath)
    {
        var path = lstPath ?? PaneConfigLoader.GetDefaultListPath();
        var text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var newLine = text.Contains("\r\n") || text.Length == 0 ? "\r\n" : "\n";

        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        foreach (var (key, value) in entries)
        {
            SetValue(lines, section, key, value);
        }

        // 書き込み途中で失敗しても元のファイルが壊れないように、別ファイルに書いてから置き換える
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, string.Join(newLine, lines) + newLine, new UTF8Encoding(false));
        File.Move(tempPath, path, overwrite: true);
    }

    private static void SetValue(List<string> lines, string section, string key, string value)
    {
        var newLineText = $"{key},{value}";
        string current = string.Empty;
        int insertAt = -1;
        bool replaced = false;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1].Trim().ToLowerInvariant();
                if (current == section)
                {
                    insertAt = i + 1;
                }

                continue;
            }

            if (current != section || line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            insertAt = i + 1;

            var parts = line.Split(',', 2);
            if (parts.Length == 2 && string.Equals(parts[0].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                // 同じキーが複数あると後の行が有効になるので、すべて置き換える
                lines[i] = newLineText;
                replaced = true;
            }
        }

        if (replaced)
        {
            return;
        }

        if (insertAt >= 0)
        {
            lines.Insert(insertAt, newLineText);
            return;
        }

        if (lines.Count > 0)
        {
            lines.Add(string.Empty);
        }

        lines.Add($"[{section}]");
        lines.Add(newLineText);
    }
}

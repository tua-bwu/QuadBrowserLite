using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace QuadBrowserLite;

public record AiPreset(string Name, string Url);

public record PaneConfig(string SlotLabel, string SelectedAiName, string Url);

public partial class SettingsWindow : Window
{
    private readonly IReadOnlyList<AiPreset> _presets;
    private readonly List<(ComboBox Combo, TextBox UrlBox, string SlotLabel)> _editors = new();

    public List<PaneConfig> ResultConfigs { get; private set; } = new();

    public SettingsWindow(IEnumerable<PaneConfig> currentConfigs, IEnumerable<AiPreset> presets)
    {
        InitializeComponent();

        _presets = presets.ToList();
        if (_presets.Count == 0 || !string.IsNullOrEmpty(_presets.Last().Url))
        {
            var list = _presets.ToList();
            list.Add(new AiPreset("Custom", string.Empty));
            _presets = list;
        }

        var cardsBySlot = new Dictionary<string, Border>
        {
            ["左上"] = CardTopLeft,
            ["右上"] = CardTopRight,
            ["左下"] = CardBottomLeft,
            ["右下"] = CardBottomRight
        };

        var orderedSlots = new[] { "左上", "右上", "左下", "右下" };
        var configMap = currentConfigs.ToDictionary(c => c.SlotLabel, c => c);

        foreach (var slot in orderedSlots)
        {
            if (!cardsBySlot.TryGetValue(slot, out var card)) continue;

            var fallbackPreset = _presets.FirstOrDefault(p => !string.IsNullOrEmpty(p.Url)) ?? _presets.Last();
            var cfg = configMap.TryGetValue(slot, out var existing)
                ? existing
                : new PaneConfig(slot, fallbackPreset.Name, fallbackPreset.Url);

            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var label = new TextBlock
            {
                Text = cfg.SlotLabel,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var combo = new ComboBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 6),
                DisplayMemberPath = nameof(AiPreset.Name),
                ItemsSource = _presets
            };

            var urlBox = new TextBox
            {
                Height = 28,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                Text = cfg.Url
            };

            var matched = _presets.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(p.Url) && string.Equals(p.Url, cfg.Url, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(p.Name, cfg.SelectedAiName, StringComparison.OrdinalIgnoreCase));
            combo.SelectedItem = matched ?? _presets.Last();

            bool updatingFromCombo = false;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is AiPreset preset && !string.IsNullOrEmpty(preset.Url))
                {
                    updatingFromCombo = true;
                    urlBox.Text = preset.Url;
                    updatingFromCombo = false;
                }
            };

            urlBox.TextChanged += (_, _) =>
            {
                if (updatingFromCombo) return;
                var exact = _presets.FirstOrDefault(p =>
                    !string.IsNullOrEmpty(p.Url) &&
                    string.Equals(p.Url, urlBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
                combo.SelectedItem = exact ?? _presets.Last();
            };

            panel.Children.Add(label);
            panel.Children.Add(combo);
            panel.Children.Add(urlBox);
            card.Child = panel;

            _editors.Add((combo, urlBox, cfg.SlotLabel));
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        ResultConfigs = _editors.Select(ed =>
        {
            var aiName = (ed.Combo.SelectedItem as AiPreset)?.Name ?? "Custom";
            var rawUrl = ed.UrlBox.Text.Trim();
            if (!string.IsNullOrEmpty(rawUrl) &&
                !rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !rawUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = "https://" + rawUrl;
            }
            return new PaneConfig(ed.SlotLabel, aiName, rawUrl);
        }).ToList();

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

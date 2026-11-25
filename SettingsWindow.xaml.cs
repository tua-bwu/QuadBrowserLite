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
    public static readonly List<AiPreset> Presets = new()
    {
        new AiPreset("ChatGPT", "https://chatgpt.com"),
        new AiPreset("Duck.ai", "https://duck.ai"),
        new AiPreset("Perplexity", "https://www.perplexity.ai"),
        new AiPreset("Copilot", "https://copilot.microsoft.com"),
        new AiPreset("Gemini", "https://gemini.google.com"),
        new AiPreset("Claude", "https://claude.ai"),
        new AiPreset("You.com", "https://you.com"),
        new AiPreset("Phind", "https://www.phind.com"),
        new AiPreset("任意URL（カスタム）", "")
    };

    private readonly List<(ComboBox Combo, TextBox UrlBox, string SlotLabel)> _editors = new();

    public List<PaneConfig> ResultConfigs { get; private set; } = new();

    public SettingsWindow(IEnumerable<PaneConfig> currentConfigs)
    {
        InitializeComponent();

        foreach (var cfg in currentConfigs)
        {
            var border = new Border
            {
                BorderBrush = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = cfg.SlotLabel,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(label, 0);

            var combo = new ComboBox
            {
                Height = 28,
                VerticalAlignment = VerticalAlignment.Center,
                DisplayMemberPath = nameof(AiPreset.Name),
                ItemsSource = Presets
            };
            Grid.SetColumn(combo, 1);

            var urlBox = new TextBox
            {
                Height = 28,
                VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                Text = cfg.Url
            };
            Grid.SetColumn(urlBox, 3);

            var matched = Presets.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(p.Url) && string.Equals(p.Url, cfg.Url, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(p.Name, cfg.SelectedAiName, StringComparison.OrdinalIgnoreCase));
            combo.SelectedItem = matched ?? Presets.Last();

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
                var exact = Presets.FirstOrDefault(p =>
                    !string.IsNullOrEmpty(p.Url) &&
                    string.Equals(p.Url, urlBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
                combo.SelectedItem = exact ?? Presets.Last();
            };

            grid.Children.Add(label);
            grid.Children.Add(combo);
            grid.Children.Add(urlBox);
            border.Child = grid;
            PaneRowsPanel.Children.Add(border);

            _editors.Add((combo, urlBox, cfg.SlotLabel));
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        ResultConfigs = _editors.Select(ed =>
        {
            var aiName = (ed.Combo.SelectedItem as AiPreset)?.Name ?? "任意URL（カスタム）";
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

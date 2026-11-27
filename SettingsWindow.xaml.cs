using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QuadBrowserLite;

public partial class SettingsWindow : Window
{
    // 登録すると4つの選択肢に反映されるように ObservableCollection にする
    private readonly ObservableCollection<AiPreset> _presets;
    private readonly List<(ComboBox Combo, TextBox UrlBox, string SlotLabel)> _editors = new();

    public List<PaneConfig> ResultConfigs { get; private set; } = new();

    // 登録内容を含む選択肢の一覧
    public IReadOnlyList<AiPreset> Presets => _presets;

    public SettingsWindow(IEnumerable<PaneConfig> currentConfigs, IEnumerable<AiPreset> presets)
    {
        InitializeComponent();

        // presets の末尾には Custom が入っている (PaneConfigLoader が追加する)
        _presets = new ObservableCollection<AiPreset>(presets);

        var cardsBySlot = new Dictionary<string, Border>
        {
            [PaneSlots.TopLeft] = CardTopLeft,
            [PaneSlots.TopRight] = CardTopRight,
            [PaneSlots.BottomLeft] = CardBottomLeft,
            [PaneSlots.BottomRight] = CardBottomRight
        };

        var configMap = currentConfigs.ToDictionary(c => c.SlotLabel, c => c);

        foreach (var slot in PaneSlots.All)
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

            // まず選択名で検索する
            var matched = _presets.FirstOrDefault(p =>
                string.Equals(
                    p.Name,
                    cfg.SelectedAiName,
                    StringComparison.OrdinalIgnoreCase));

            // 名前で見つからない場合だけURLで検索する
            matched ??= _presets.FirstOrDefault(p =>
                !string.IsNullOrEmpty(p.Url) &&
                string.Equals(
                    p.Url,
                    cfg.Url,
                    StringComparison.OrdinalIgnoreCase));

            combo.SelectedItem = matched ?? _presets.Last();

            // 登録済みの項目は、登録されている URL を表示する
            if (matched != null && !string.IsNullOrEmpty(matched.Url))
            {
                urlBox.Text = matched.Url;
            }

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
        ResultConfigs = ReadEditors();
        DialogResult = true;
    }

    private List<PaneConfig> ReadEditors() =>
        _editors.Select(ed => new PaneConfig(
            ed.SlotLabel,
            (ed.Combo.SelectedItem as AiPreset)?.Name ?? AiPreset.CustomName,
            PaneUrl.Normalize(ed.UrlBox.Text))).ToList();

    private void OnRegisterBoxKeyDown(object sender, KeyEventArgs e)
    {
        // Enter は「変更を反映」ではなく「登録」にする
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OnRegisterClick(sender, e);
        }
    }

    private void OnRegisterClick(object sender, RoutedEventArgs e)
    {
        StatusText.Text = string.Empty;

        var name = NewNameBox.Text.Trim();
        var url = PaneUrl.Normalize(NewUrlBox.Text);

        if (name.Length == 0 || url.Length == 0)
        {
            ShowWarning("名前と URL を入力してください。");
            return;
        }

        // 「,」は区切り、先頭の # ; [ はコメントや見出しとして扱われる
        if (name.Contains(',') || name.StartsWith('#') || name.StartsWith(';') || name.StartsWith('[') ||
            string.Equals(name, AiPreset.CustomName, StringComparison.OrdinalIgnoreCase))
        {
            ShowWarning($"この名前は使えません。\n「,」を含む名前、# ; [ で始まる名前、{AiPreset.CustomName} は登録できません。");
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            ShowWarning("URL の形式が正しくありません。");
            return;
        }

        var existing = _presets.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing != null &&
            MessageBox.Show(
                this,
                $"「{existing.Name}」は登録済みです。上書きしますか？\n{existing.Url}\n→ {url}",
                "QuadBrowserLite",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            PaneListWriter.SaveUrl(name, url);
        }
        catch (Exception ex)
        {
            ShowWarning($".lst に保存できませんでした:\n{ex.Message}");
            return;
        }

        UpdatePresets(existing, new AiPreset(name, url));

        NewNameBox.Clear();
        NewUrlBox.Clear();
        StatusText.Text = $"「{name}」を登録しました。";
    }

    private void UpdatePresets(AiPreset? oldPreset, AiPreset newPreset)
    {
        // 一覧を差し替えると選択が外れるので、先に選び直す画面を控える
        var targets = _editors
            .Where(ed =>
                ed.Combo.SelectedItem is AiPreset selected &&
                (selected == oldPreset ||
                 (selected.Name == AiPreset.CustomName &&
                  string.Equals(PaneUrl.Normalize(ed.UrlBox.Text), newPreset.Url, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        if (oldPreset != null)
        {
            _presets[_presets.IndexOf(oldPreset)] = newPreset;
        }
        else
        {
            // Custom は常に末尾に置く
            _presets.Insert(_presets.Count - 1, newPreset);
        }

        foreach (var target in targets)
        {
            target.Combo.SelectedItem = newPreset;
        }
    }

    private void OnSaveStartupClick(object sender, RoutedEventArgs e)
    {
        StatusText.Text = string.Empty;

        var configs = ReadEditors();
        var unregistered = configs
            .Where(c => c.SelectedAiName == AiPreset.CustomName)
            .Select(c => c.SlotLabel)
            .ToList();

        if (unregistered.Count > 0)
        {
            ShowWarning($"{string.Join("・", unregistered)} は Custom です。\n先に URL を登録してください。");
            return;
        }

        try
        {
            PaneListWriter.SaveStartupPanes(configs);
        }
        catch (Exception ex)
        {
            ShowWarning($".lst に保存できませんでした:\n{ex.Message}");
            return;
        }

        StatusText.Text = "この配置を、次回の起動時の設定として保存しました。";
    }

    private void ShowWarning(string message) =>
        MessageBox.Show(this, message, "QuadBrowserLite", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

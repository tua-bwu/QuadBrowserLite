using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuadBrowserLite;

public partial class SettingsWindow : Window
{
    // 登録すると4つの選択肢に反映されるように ObservableCollection にする
    private readonly ObservableCollection<AiPreset> _presets;
    private readonly List<(ComboBox Combo, string SlotLabel)> _editors = new();

    public List<PaneConfig> ResultConfigs { get; private set; } = new();

    // 登録内容を含む選択肢の一覧
    public IReadOnlyList<AiPreset> Presets => _presets;

    public SettingsWindow(IEnumerable<PaneConfig> currentConfigs, IEnumerable<AiPreset> presets)
    {
        InitializeComponent();
        Closing += OnClosing;

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

            configMap.TryGetValue(slot, out var cfg);

            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var label = new TextBlock
            {
                Text = slot,
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

            // 選んだ項目の URL を表示する(入力は下の登録欄で行う)
            var urlBox = new TextBox
            {
                Height = 28,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                IsReadOnly = true,
                Foreground = Brushes.Gray
            };

            combo.SelectionChanged += (_, _) =>
                urlBox.Text = (combo.SelectedItem as AiPreset)?.Url ?? string.Empty;

            // 一覧にない名前なら、未選択のままにする
            combo.SelectedItem = _presets.FirstOrDefault(p =>
                string.Equals(p.Name, cfg?.SelectedAiName, StringComparison.OrdinalIgnoreCase));

            panel.Children.Add(label);
            panel.Children.Add(combo);
            panel.Children.Add(urlBox);
            card.Child = panel;

            _editors.Add((combo, slot));
        }
    }

    private bool _allowClose;

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardPendingInput()) return;

        ResultConfigs = ReadEditors();
        _allowClose = true;
        DialogResult = true;
    }

    // 項目が選ばれている画面の設定だけを返す
    private List<PaneConfig> ReadEditors()
    {
        var configs = new List<PaneConfig>();
        foreach (var (combo, slot) in _editors)
        {
            if (combo.SelectedItem is AiPreset preset)
            {
                configs.Add(new PaneConfig(slot, preset.Name, PaneUrl.Normalize(preset.Url)));
            }
        }

        return configs;
    }

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
        if (name.Contains(',') || name.StartsWith('#') || name.StartsWith(';') || name.StartsWith('['))
        {
            ShowWarning("この名前は使えません。\n「,」を含む名前と、# ; [ で始まる名前は登録できません。");
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
            .Where(ed => oldPreset != null && Equals(ed.Combo.SelectedItem, oldPreset))
            .ToList();

        if (oldPreset != null)
        {
            _presets[_presets.IndexOf(oldPreset)] = newPreset;
        }
        else
        {
            _presets.Add(newPreset);
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
        if (configs.Count == 0)
        {
            StatusText.Text = "項目が選ばれていないため、保存していません。";
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

    private bool ConfirmDiscardPendingInput()
    {
        if (string.IsNullOrWhiteSpace(NewNameBox.Text) &&
            string.IsNullOrWhiteSpace(NewUrlBox.Text))
        {
            return true;
        }

        return MessageBox.Show(
            this,
            "新しいサイトの入力内容が残っています。破棄して閉じますか？",
            "QuadBrowserLite",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;

        if (!ConfirmDiscardPendingInput())
        {
            e.Cancel = true;
            return;
        }

        _allowClose = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardPendingInput()) return;

        _allowClose = true;
        DialogResult = false;
    }
}

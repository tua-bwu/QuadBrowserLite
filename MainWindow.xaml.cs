using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace QuadBrowserLite;

public partial class MainWindow : Window
{
    private const double MinPaneSize = 120.0;

    private readonly List<string> _tempDirs = new();
    private readonly Dictionary<string, WebView2> _viewsBySlot = new();
    private List<AiPreset> _presets = new();
    private List<PaneConfig> _currentConfigs = new();
    private bool _isClosed;

    public MainWindow()
    {
        InitializeComponent();
        PrivateDataCleaner.DeleteLeftovers();

        // 仕切り線でも十字の中心と同じ最小サイズより小さくできないようにする
        LeftCol.MinWidth = RightCol.MinWidth = MinPaneSize;
        TopRow.MinHeight = BottomRow.MinHeight = MinPaneSize;

        _viewsBySlot[PaneSlots.TopLeft] = WvTopLeft;
        _viewsBySlot[PaneSlots.TopRight] = WvTopRight;
        _viewsBySlot[PaneSlots.BottomLeft] = WvBottomLeft;
        _viewsBySlot[PaneSlots.BottomRight] = WvBottomRight;

        var loaded = PaneConfigLoader.Load();
        _presets = loaded.Presets;
        _currentConfigs = loaded.InitialPanes;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnCrossPointDragDelta(object sender, DragDeltaEventArgs e)
    {
        double totalWidth = LeftCol.ActualWidth + RightCol.ActualWidth;
        if (totalWidth > MinPaneSize * 2)
        {
            double newLeft = Math.Clamp(LeftCol.ActualWidth + e.HorizontalChange, MinPaneSize, totalWidth - MinPaneSize);
            double newRight = totalWidth - newLeft;
            LeftCol.Width = new GridLength(newLeft, GridUnitType.Star);
            RightCol.Width = new GridLength(newRight, GridUnitType.Star);
        }

        double totalHeight = TopRow.ActualHeight + BottomRow.ActualHeight;
        if (totalHeight > MinPaneSize * 2)
        {
            double newTop = Math.Clamp(TopRow.ActualHeight + e.VerticalChange, MinPaneSize, totalHeight - MinPaneSize);
            double newBottom = totalHeight - newTop;
            TopRow.Height = new GridLength(newTop, GridUnitType.Star);
            BottomRow.Height = new GridLength(newBottom, GridUnitType.Star);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        foreach (var cfg in _currentConfigs)
        {
            if (_isClosed)
            {
                return;
            }

            if (_viewsBySlot.TryGetValue(cfg.SlotLabel, out var view))
            {
                try
                {
                    await InitPrivateWebViewAsync(view, cfg.Url);
                }
                catch (Exception ex)
                {
                    errors.Add($"{cfg.SlotLabel}: {ex.Message}");
                }
            }
        }

        if (_isClosed)
        {
            return;
        }

        // 失敗した画面があっても、メニューは使えるようにする
        _isInitializing = false;
        UpdateSystemMenuState();

        if (errors.Count > 0)
        {
            MessageBox.Show(
                this,
                "画面を準備できませんでした:\n" + string.Join("\n", errors),
                "QuadBrowserLite",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task InitPrivateWebViewAsync(WebView2 webView, string initialUrl)
    {
        string tempDir = PrivateDataCleaner.NewDirectoryPath();
        _tempDirs.Add(tempDir);

        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: tempDir,
            options: new CoreWebView2EnvironmentOptions());

        await webView.EnsureCoreWebView2Async(env, CreatePrivateOptions(env));

        webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        NavigateSafely(webView, initialUrl);
    }

    // シークレットモード(InPrivate)で WebView2 を作る
    private static CoreWebView2ControllerOptions CreatePrivateOptions(CoreWebView2Environment env)
    {
        var options = env.CreateCoreWebView2ControllerOptions();
        options.IsInPrivateModeEnabled = true;
        return options;
    }

    private static void NavigateSafely(WebView2 view, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            view.Source = new Uri("about:blank");
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            view.Source = uri;
        }
    }

    // 指定した画面を、設定画面で指定している URL(Custom を含む)で開き直す
    private void ReloadPane(string slot)
    {
        if (!_viewsBySlot.TryGetValue(slot, out var view) || view.CoreWebView2 == null)
        {
            return;
        }

        var config = _currentConfigs.Find(c => c.SlotLabel == slot);
        if (config == null)
        {
            return;
        }

        try
        {
            // Source への代入は同じ URL だと再読み込みされないため Navigate を使う
            if (string.IsNullOrWhiteSpace(config.Url))
            {
                view.CoreWebView2.Navigate("about:blank");
            }
            else if (Uri.TryCreate(config.Url, UriKind.Absolute, out var uri))
            {
                view.CoreWebView2.Navigate(uri.AbsoluteUri);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"{slot}を更新できませんでした:\n{ex.Message}",
                "QuadBrowserLite",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenSettingsDialog()
    {
        try
        {
            var dialog = new SettingsWindow(_currentConfigs, _presets)
            {
                Owner = this
            };

            bool applied = dialog.ShowDialog() == true;

            // 登録した内容はキャンセルしても .lst に残るので、一覧は常に取り込む
            _presets = dialog.Presets.ToList();

            if (applied)
            {
                var newConfigs = dialog.ResultConfigs;

                foreach (var newCfg in newConfigs)
                {
                    var oldCfg = _currentConfigs.Find(
                        cfg => cfg.SlotLabel == newCfg.SlotLabel);

                    if (oldCfg != null)
                    {
                        // 選択名と URL がどちらも同じなら再読み込みしない
                        bool isSameSelection =
                            string.Equals(oldCfg.SelectedAiName, newCfg.SelectedAiName, StringComparison.Ordinal) &&
                            string.Equals(PaneUrl.Normalize(oldCfg.Url), newCfg.Url, StringComparison.Ordinal);

                        if (isSameSelection)
                        {
                            continue;
                        }
                    }

                    if (_viewsBySlot.TryGetValue(newCfg.SlotLabel, out var view)
                        && view.CoreWebView2 != null)
                    {
                        NavigateSafely(view, newCfg.Url);
                    }
                }

                _currentConfigs = newConfigs;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"設定画面を開けませんでした:\n{ex.Message}",
                "QuadBrowserLite",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;

        // Dispose 後は取得できないので、先にブラウザプロセスを控える
        var browserProcesses = new List<Process>();
        foreach (var view in _viewsBySlot.Values)
        {
            try
            {
                if (view.CoreWebView2 != null)
                {
                    browserProcesses.Add(Process.GetProcessById((int)view.CoreWebView2.BrowserProcessId));
                }
            }
            catch
            {
                // すでに終了している場合は待たない
            }

            view.Dispose();
        }

        PrivateDataCleaner.DeleteAfterExit(browserProcesses, _tempDirs);
    }
}

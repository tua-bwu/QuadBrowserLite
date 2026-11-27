using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

    public MainWindow()
    {
        InitializeComponent();
        PrivateDataCleaner.DeleteLeftovers();

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
        foreach (var cfg in _currentConfigs)
        {
            if (_viewsBySlot.TryGetValue(cfg.SlotLabel, out var view))
            {
                await InitPrivateWebViewAsync(view, cfg.Url);
            }
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

    private void OpenSettingsDialog()
    {
        try
        {
            var dialog = new SettingsWindow(_currentConfigs, _presets)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                var newConfigs = dialog.ResultConfigs;

                foreach (var newCfg in newConfigs)
                {
                    var oldCfg = _currentConfigs.Find(
                        cfg => cfg.SlotLabel == newCfg.SlotLabel);

                    if (oldCfg != null)
                    {
                        bool isSameSelection;

                        if (string.Equals(oldCfg.SelectedAiName, AiPreset.CustomName, StringComparison.Ordinal) &&
                            string.Equals(newCfg.SelectedAiName, AiPreset.CustomName, StringComparison.Ordinal))
                        {
                            // Custom同士はURLが完全一致する場合のみ再読み込みしない
                            isSameSelection = string.Equals(
                                oldCfg.Url,
                                newCfg.Url,
                                StringComparison.Ordinal);
                        }
                        else
                        {
                            // 通常のプリセットは選択名で比較
                            isSameSelection = string.Equals(
                                oldCfg.SelectedAiName,
                                newCfg.SelectedAiName,
                                StringComparison.Ordinal);
                        }

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

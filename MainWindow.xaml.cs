using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace QuadBrowserLite;

public partial class MainWindow : Window
{
    private readonly List<string> _tempDirs = new();
    private readonly Dictionary<string, WebView2> _viewsBySlot = new();
    private List<PaneConfig> _currentConfigs = new();

    public MainWindow()
    {
        InitializeComponent();

        _viewsBySlot["左上"] = WvTopLeft;
        _viewsBySlot["右上"] = WvTopRight;
        _viewsBySlot["左下"] = WvBottomLeft;
        _viewsBySlot["右下"] = WvBottomRight;

        _currentConfigs = new List<PaneConfig>
        {
            new("左上", "ChatGPT", "https://chatgpt.com"),
            new("右上", "Duck.ai", "https://duck.ai"),
            new("左下", "Perplexity", "https://www.perplexity.ai"),
            new("右下", "Copilot", "https://copilot.microsoft.com")
        };

        Loaded += OnLoaded;
        Closed += OnClosed;
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
        string tempDir = Path.Combine(Path.GetTempPath(), "QuadBrowserLite_" + Guid.NewGuid().ToString("N"));
        _tempDirs.Add(tempDir);

        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: tempDir,
            options: new CoreWebView2EnvironmentOptions());

        await webView.EnsureCoreWebView2Async(env);

        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        NavigateSafely(webView, initialUrl);
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

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_currentConfigs)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _currentConfigs = dialog.ResultConfigs;

            foreach (var cfg in _currentConfigs)
            {
                if (_viewsBySlot.TryGetValue(cfg.SlotLabel, out var view) && view.CoreWebView2 != null)
                {
                    NavigateSafely(view, cfg.Url);
                }
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        foreach (var view in _viewsBySlot.Values)
        {
            view.Dispose();
        }

        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // ロック残存時はスキップ
            }
        }
    }
}

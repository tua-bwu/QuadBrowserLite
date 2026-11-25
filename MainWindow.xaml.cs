using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace QuadBrowserLite;

public partial class MainWindow : Window
{
    private readonly List<string> _tempDirs = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var targets = new[]
        {
            (WvTopLeft, "https://chatgpt.com"),
            (WvTopRight, "https://claude.ai"),
            (WvBottomLeft, "https://gemini.google.com"),
            (WvBottomRight, "https://www.perplexity.ai")
        };

        foreach (var (view, url) in targets)
        {
            await InitPrivateWebViewAsync(view, url);
        }
    }

    private async Task InitPrivateWebViewAsync(WebView2 webView, string initialUrl)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "QuadBrowserLite_" + Guid.NewGuid().ToString("N"));
        _tempDirs.Add(tempDir);

        var options = new CoreWebView2EnvironmentOptions();

        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: tempDir,
            options: options);

        await webView.EnsureCoreWebView2Async(env);

        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        webView.Source = new Uri(initialUrl);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        foreach (var view in new[] { WvTopLeft, WvTopRight, WvBottomLeft, WvBottomRight })
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
                // ファイルロックが残る場合はスキップ
            }
        }
    }
}

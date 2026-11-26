using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace QuadBrowserLite;

public partial class MainWindow : Window
{
    private const int WmNcRButtonUp = 0x00A5;
    private const int HtCaption = 2;
    private const double MinPaneSize = 120.0;

    private readonly List<string> _tempDirs = new();
    private readonly Dictionary<string, WebView2> _viewsBySlot = new();
    private List<PaneConfig> _currentConfigs = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

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

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmNcRButtonUp && wParam.ToInt32() == HtCaption)
        {
            if (FindResource("TitleBarContextMenu") is ContextMenu menu && GetCursorPos(out var pt))
            {
                menu.PlacementTarget = this;
                menu.Placement = PlacementMode.AbsolutePoint;
                menu.HorizontalOffset = pt.X;
                menu.VerticalOffset = pt.Y;
                menu.IsOpen = true;
                handled = true;
            }
        }

        return IntPtr.Zero;
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

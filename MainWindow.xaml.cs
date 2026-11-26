using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace QuadBrowserLite;

public partial class MainWindow : Window
{
    private const int WmSysCommand = 0x0112;
    private const int MfString = 0x00000000;
    private const int MfSeparator = 0x00000800;
    private const int SysMenuSettingsId = 0x1001;
    private const double MinPaneSize = 120.0;

    private readonly List<string> _tempDirs = new();
    private readonly Dictionary<string, WebView2> _viewsBySlot = new();
    private List<AiPreset> _presets = new();
    private List<PaneConfig> _currentConfigs = new();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    public MainWindow()
    {
        InitializeComponent();

        _viewsBySlot["左上"] = WvTopLeft;
        _viewsBySlot["右上"] = WvTopRight;
        _viewsBySlot["左下"] = WvBottomLeft;
        _viewsBySlot["右下"] = WvBottomRight;

        var loaded = PaneConfigLoader.Load();
        _presets = loaded.Presets;
        _currentConfigs = loaded.InitialPanes;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var sysMenu = GetSystemMenu(handle, false);
        if (sysMenu != IntPtr.Zero)
        {
            // 「閉じる」の手前（位置 6 付近）に区切り線と「設定...」を挿入
            InsertMenu(sysMenu, 6, MfSeparator | 0x0400 /* MF_BYPOSITION */, UIntPtr.Zero, null);
            InsertMenu(sysMenu, 7, MfString | 0x0400 /* MF_BYPOSITION */, (UIntPtr)SysMenuSettingsId, "設定...");
        }

        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSysCommand && (wParam.ToInt64() & 0xFFFF) == SysMenuSettingsId)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(() => OpenSettingsDialog()));
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

    private void OpenSettingsDialog(object? sender = null, RoutedEventArgs? e = null)
    {
        try
        {
            var dialog = new SettingsWindow(_currentConfigs, _presets)
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

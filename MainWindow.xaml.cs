using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
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
    private const int ScClose = 0xF060;
    private const int MfByCommand = 0x00000000;
    private const int MfString = 0x00000000;
    private const int MfSeparator = 0x00000800;
    private const int SysMenuSettingsId = 0x1001;
    private const double MinPaneSize = 120.0;
    private const uint MbYesNo = 0x00000004;
    private const uint MbIconQuestion = 0x00000020;
    private const uint MbDefButton2 = 0x00000100;
    private const uint MbTopMost = 0x00040000;
    private const int IdYes = 6;

    private readonly List<string> _tempDirs = new();
    private readonly Dictionary<string, WebView2> _viewsBySlot = new();
    private readonly List<Window> _childWindows = new();
    private List<AiPreset> _presets = new();
    private List<PaneConfig> _currentConfigs = new();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    // WPF の MessageBox では「ダイアログだけ最前面」を指定できないため Win32 を直接呼ぶ
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int NativeMessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    public MainWindow()
    {
        InitializeComponent();
        PrivateDataCleaner.DeleteLeftovers();

        _viewsBySlot["左上"] = WvTopLeft;
        _viewsBySlot["右上"] = WvTopRight;
        _viewsBySlot["左下"] = WvBottomLeft;
        _viewsBySlot["右下"] = WvBottomRight;

        var loaded = PaneConfigLoader.Load();
        _presets = loaded.Presets;
        _currentConfigs = loaded.InitialPanes;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var sysMenu = GetSystemMenu(handle, false);
        if (sysMenu != IntPtr.Zero)
        {
            InsertMenu(sysMenu, ScClose, MfByCommand | MfString, (UIntPtr)SysMenuSettingsId, "設定...");
            InsertMenu(sysMenu, ScClose, MfByCommand | MfSeparator, UIntPtr.Zero, null);
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

    private async void OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (sender is not CoreWebView2 sourceCore)
        {
            e.Handled = true;
            return;
        }

        var deferral = e.GetDeferral();
        var childView = new WebView2();
        var childWindow = new Window
        {
            Title = "QuadBrowserLite - 別ウィンドウ",
            Width = 1000,
            Height = 700,
            Content = childView,
            //Owner = this
        };

        childWindow.Closed += (_, _) =>
        {
            _childWindows.Remove(childWindow);
            childView.Dispose();
        };

        _childWindows.Add(childWindow);

        try
        {
            // 先に別ウィンドウを表示してからWebView2を初期化する
            childWindow.Show();

            // 元のWebView2と同じ環境を使用する
            await childView.EnsureCoreWebView2Async(
                sourceCore.Environment,
                CreatePrivateOptions(sourceCore.Environment));

            childView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            childView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
            childView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            e.NewWindow = childView.CoreWebView2;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            childWindow.Close();
            e.Handled = true;

            MessageBox.Show(
                this,
                $"別ウィンドウを開けませんでした:\n{ex.Message}",
                "QuadBrowserLite",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_childWindows.Count == 0)
        {
            return;
        }

        // 子ウィンドウを通常のアクティブ化で前に出す（最小化されていれば元に戻す）
        foreach (var childWindow in _childWindows)
        {
            if (childWindow.WindowState == WindowState.Minimized)
            {
                SystemCommands.RestoreWindow(childWindow);
            }

            childWindow.Activate();
        }

        // 確認ダイアログだけを最前面に表示する
        int result = NativeMessageBox(
            new WindowInteropHelper(_childWindows[^1]).Handle,
            $"別ウィンドウが {_childWindows.Count} 個開いています。\n"
            + "メインウィンドウを閉じると、別ウィンドウもすべて閉じます。\n"
            + "よろしいですか？",
            "QuadBrowserLite",
            MbYesNo | MbIconQuestion | MbDefButton2 | MbTopMost);

        if (result != IdYes)
        {
            e.Cancel = true;
            return;
        }

        for (int i = _childWindows.Count - 1; i >= 0; i--)
        {
            _childWindows[i].Close();
        }
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
                var newConfigs = dialog.ResultConfigs;

                foreach (var newCfg in newConfigs)
                {
                    var oldCfg = _currentConfigs.Find(
                        cfg => cfg.SlotLabel == newCfg.SlotLabel);

                    if (oldCfg != null)
                    {
                        bool isSameSelection;

                        if (string.Equals(oldCfg.SelectedAiName, "Custom", StringComparison.Ordinal) &&
                            string.Equals(newCfg.SelectedAiName, "Custom", StringComparison.Ordinal))
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

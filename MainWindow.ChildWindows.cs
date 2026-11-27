using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace QuadBrowserLite;

public partial class MainWindow
{
    // 別ウィンドウの作成と、本体を閉じるときの確認
    private const uint MbYesNo = 0x00000004;
    private const uint MbIconQuestion = 0x00000020;
    private const uint MbDefButton2 = 0x00000100;
    private const uint MbTopMost = 0x00040000;
    private const int IdYes = 6;

    private readonly List<Window> _childWindows = new();

    // WPF の MessageBox では「ダイアログだけ最前面」を指定できないため Win32 を直接呼ぶ
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int NativeMessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

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
}

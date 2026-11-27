using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace QuadBrowserLite;

public partial class MainWindow
{
    // タイトルバー右クリックで開くシステムメニュー
    private const int WmSysCommand = 0x0112;
    private const int ScClose = 0xF060;
    private const int MfByCommand = 0x00000000;
    private const int MfString = 0x00000000;
    private const int MfSeparator = 0x00000800;
    private const int SysMenuSettingsId = 0x1001;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

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
            Dispatcher.BeginInvoke(new Action(OpenSettingsDialog));
        }

        return IntPtr.Zero;
    }
}

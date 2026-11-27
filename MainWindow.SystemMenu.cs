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
    // 独自項目の ID は下位4ビットを Windows が使うため、0x10 刻みにする
    private const int SysMenuSettingsId = 0x1000;
    private const int SysMenuReloadBaseId = 0x1010;
    private const int SysMenuReloadStep = 0x10;

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
            // 「閉じる」の上に、更新4項目 / 区切り線 / 設定 / 区切り線 の順で追加する
            for (int i = 0; i < PaneSlots.All.Length; i++)
            {
                var id = (UIntPtr)(SysMenuReloadBaseId + i * SysMenuReloadStep);
                InsertMenu(sysMenu, ScClose, MfByCommand | MfString, id, $"{PaneSlots.All[i]}を更新");
            }

            InsertMenu(sysMenu, ScClose, MfByCommand | MfSeparator, UIntPtr.Zero, null);
            InsertMenu(sysMenu, ScClose, MfByCommand | MfString, (UIntPtr)SysMenuSettingsId, "設定...");
            InsertMenu(sysMenu, ScClose, MfByCommand | MfSeparator, UIntPtr.Zero, null);
        }

        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmSysCommand)
        {
            return IntPtr.Zero;
        }

        int command = (int)(wParam.ToInt64() & 0xFFF0);
        int reloadIndex = (command - SysMenuReloadBaseId) / SysMenuReloadStep;
        bool isReload = command >= SysMenuReloadBaseId && reloadIndex < PaneSlots.All.Length;
        if (!isReload && command != SysMenuSettingsId)
        {
            return IntPtr.Zero;
        }

        handled = true;

        // 閉じる確認の表示中は何もしない
        if (_isConfirmingClose)
        {
            return IntPtr.Zero;
        }

        if (isReload)
        {
            var slot = PaneSlots.All[reloadIndex];
            Dispatcher.BeginInvoke(new Action(() => ReloadPaneFromList(slot)));
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(OpenSettingsDialog));
        }

        return IntPtr.Zero;
    }
}

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
    private const int MfByPosition = 0x00000400;
    private const int MfPopup = 0x00000010;
    private const int MfMenuBarBreak = 0x00000020;
    private const int MfGrayed = 0x00000001;
    private const uint AppendPosition = 0xFFFFFFFF;
    // 独自項目の ID は下位4ビットを Windows が使うため、0x10 刻みにする
    private const int SysMenuSettingsId = 0x1000;
    private const int SysMenuReloadBaseId = 0x1010;
    private const int SysMenuReloadStep = 0x10;

    private IntPtr _systemMenu;
    private IntPtr _reloadMenu;
    private bool _isInitializing = true; // 4画面の準備が終わるまで true

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ModifyMenu(IntPtr hMenu, uint uPosition, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);

    [DllImport("user32.dll")]
    private static extern int EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _systemMenu = GetSystemMenu(handle, false);
        if (_systemMenu != IntPtr.Zero)
        {
            // 「閉じる」の上に、更新(サブメニュー) / 区切り線 / 設定 / 区切り線 の順で追加する
            _reloadMenu = CreateReloadMenu();
            InsertMenu(_systemMenu, ScClose, MfByCommand | MfString | MfPopup, (UIntPtr)(ulong)_reloadMenu.ToInt64(), "更新");
            InsertMenu(_systemMenu, ScClose, MfByCommand | MfSeparator, UIntPtr.Zero, null);
            InsertMenu(_systemMenu, ScClose, MfByCommand | MfString, (UIntPtr)SysMenuSettingsId, "設定...");
            InsertMenu(_systemMenu, ScClose, MfByCommand | MfSeparator, UIntPtr.Zero, null);
            UpdateSystemMenuState();
        }

        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);
    }

    // 画面の配置と同じ 2×2 のメニューを作る
    private static IntPtr CreateReloadMenu()
    {
        var menu = CreatePopupMenu();

        // メニューは縦に並ぶので、左の列(左上・左下)、右の列(右上・右下)の順に追加する
        foreach (int index in new[] { 0, 2, 1, 3 })
        {
            int flags = MfByPosition | MfString;
            if (index == 1)
            {
                flags |= MfMenuBarBreak; // ここから右の列
            }

            var id = (UIntPtr)(SysMenuReloadBaseId + index * SysMenuReloadStep);
            InsertMenu(menu, AppendPosition, (uint)flags, id, PaneSlots.All[index]);
        }

        return menu;
    }

    // 準備中は更新と設定をグレー表示にする
    private void UpdateSystemMenuState()
    {
        if (_systemMenu == IntPtr.Zero)
        {
            return;
        }

        uint grayed = _isInitializing ? (uint)MfGrayed : 0;

        ModifyMenu(
            _systemMenu,
            SysMenuSettingsId,
            MfByCommand | MfString | grayed,
            (UIntPtr)SysMenuSettingsId,
            _isInitializing ? "設定...（起動中）" : "設定...");

        // サブメニューの親項目には ID がないので、位置を探して指定する
        for (int i = 0; i < GetMenuItemCount(_systemMenu); i++)
        {
            if (GetSubMenu(_systemMenu, i) == _reloadMenu)
            {
                EnableMenuItem(_systemMenu, (uint)i, MfByPosition | grayed);
                break;
            }
        }
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

        // 起動中と閉じる確認の表示中は何もしない
        if (_isInitializing || _isConfirmingClose)
        {
            return IntPtr.Zero;
        }

        if (isReload)
        {
            var slot = PaneSlots.All[reloadIndex];
            Dispatcher.BeginInvoke(new Action(() => ReloadPane(slot)));
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(OpenSettingsDialog));
        }

        return IntPtr.Zero;
    }
}

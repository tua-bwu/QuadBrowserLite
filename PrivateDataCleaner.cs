using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace QuadBrowserLite;

// WebView2 の一時データ(ユーザーデータフォルダ)の作成先と削除をまとめる
internal static class PrivateDataCleaner
{
    private const string Prefix = "QuadBrowserLite_";
    private const int ExitTimeoutMs = 5000;
    private const int DeleteRetryCount = 10;
    private const int DeleteRetryDelayMs = 200;

    public static string NewDirectoryPath() =>
        Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));

    // 前回までに消し残したフォルダを削除する。呼び出し時点で存在するものだけが対象
    public static void DeleteLeftovers()
    {
        string[] leftovers;
        try
        {
            leftovers = Directory.GetDirectories(Path.GetTempPath(), Prefix + "*");
        }
        catch
        {
            return;
        }

        Task.Run(() =>
        {
            foreach (var dir in leftovers)
            {
                try
                {
                    // 別の起動中インスタンスが使用中のフォルダは名前変更に失敗するので、そのまま残る
                    var doomed = dir.EndsWith("_del", StringComparison.Ordinal) ? dir : dir + "_del";
                    if (doomed != dir)
                    {
                        Directory.Move(dir, doomed);
                    }

                    Directory.Delete(doomed, recursive: true);
                }
                catch
                {
                    // 次回の起動時に再試行する
                }
            }
        });
    }

    // ブラウザプロセスの終了を待ってから削除する
    public static void DeleteAfterExit(
        IReadOnlyCollection<Process> browserProcesses,
        IReadOnlyCollection<string> directories)
    {
        var elapsed = Stopwatch.StartNew();
        foreach (var process in browserProcesses)
        {
            try
            {
                process.WaitForExit((int)Math.Max(0, ExitTimeoutMs - elapsed.ElapsedMilliseconds));
            }
            catch
            {
                // 待てない場合は削除の再試行に任せる
            }
            finally
            {
                process.Dispose();
            }
        }

        foreach (var dir in directories)
        {
            for (int attempt = 0; attempt < DeleteRetryCount; attempt++)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, recursive: true);
                    }

                    break;
                }
                catch
                {
                    Thread.Sleep(DeleteRetryDelayMs);
                }
            }
        }
    }
}

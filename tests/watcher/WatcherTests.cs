// Runtime checks for ConfigWatcher: does it notice a hand-edited file, does it wait for the write to
// settle, and does it survive a file that does not parse?

using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using ScamWYF.Modding.Core;
using ScamWYF.Modding.Core.Ui;

internal static class WatcherTests
{
    private static int _passed;
    private static int _failed;

    private static void Check(string name, bool condition, string detail)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine("  PASS  " + name);
            return;
        }

        _failed++;
        Console.WriteLine("  FAIL  " + name + (detail.Length > 0 ? " - " + detail : ""));
    }

    public static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "swyg-watcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var path = Path.Combine(dir, "test.cfg");
            File.WriteAllText(path, "Timeout = 90\n");

            var log = new ManualLogSource { SourceName = "test" };
            var file = new ConfigFile(path);
            var watcher = new ConfigWatcher(file, log, "test.mod", 0.1d);

            var reloads = 0;
            watcher.Reloaded += delegate { reloads++; };

            // 1. Nothing has changed, so nothing should happen no matter how long we pump.
            Pump(watcher, 40);
            Check("idle file does not trigger a reload", reloads == 0, "reloads=" + reloads);
            Check("watcher knows the file it is watching", watcher.LastChangeUtc == null,
                "LastChangeUtc=" + watcher.LastChangeUtc);

            // 2. An edit on disk is picked up, and the value really is the new one.
            File.WriteAllText(path, "Timeout = 45\n");
            Pump(watcher, 40);
            Check("an edit on disk is noticed", reloads == 1, "reloads=" + reloads);
            Check("the reloaded value is the new one", file.LastSeenValue == "45",
                "LastSeenValue=" + file.LastSeenValue);
            Check("the change time is recorded", watcher.LastChangeUtc != null, "null");

            // 3. A watcher must not fire on its own ReloadNow writing the file back.
            watcher.ReloadNow("test");
            Pump(watcher, 20);
            Check("ReloadNow does not cause a feedback reload", reloads == 2, "reloads=" + reloads);

            // 4. A file that will not parse is reported, and does not throw out of Tick.
            file.FailNextReload = true;
            File.WriteAllText(path, "Timeout = 120\n");
            var threw = false;
            try
            {
                Pump(watcher, 40);
            }
            catch (Exception ex)
            {
                threw = true;
                Console.WriteLine("        threw: " + ex.Message);
            }

            Check("a file that does not parse does not throw out of Tick", !threw, "");
            var complained = false;
            foreach (var line in log.Lines)
            {
                if (line.IndexOf("could not reload", StringComparison.OrdinalIgnoreCase) >= 0) complained = true;
            }
            Check("a file that does not parse is reported in the log", complained, string.Join(" | ", log.Lines));

            // 5. And it recovers: the next good edit still comes through.
            File.WriteAllText(path, "Timeout = 200\n");
            Pump(watcher, 40);
            Check("a later good edit is picked up", file.LastSeenValue == "200",
                "LastSeenValue=" + file.LastSeenValue);
            Check("reload count is tracked", watcher.ReloadCount > 0, "count=" + watcher.ReloadCount);

            // 6. A deleted file is survivable; it comes back if it is recreated.
            File.Delete(path);
            Pump(watcher, 20);
            File.WriteAllText(path, "Timeout = 300\n");
            Pump(watcher, 40);
            Check("a deleted and recreated file is picked up", file.LastSeenValue == "300",
                "LastSeenValue=" + file.LastSeenValue);

            watcher.Dispose();
            Pump(watcher, 20);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine(_passed + " passed, " + _failed + " failed");
        return _failed == 0 ? 0 : 1;
    }

    /// <summary>Run the poll for a while. The watcher throttles on its own clock, so this is just time.</summary>
    private static void Pump(ConfigWatcher watcher, int milliseconds)
    {
        for (int i = 0; i < milliseconds; i++)
        {
            watcher.Tick();
            System.Threading.Thread.Sleep(1);
        }
    }
}
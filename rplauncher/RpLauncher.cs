using System;
using System.Diagnostics;
using System.IO;

// Stands in for the exe of a Steam game that has Remote Play Together. Steam starts this program as that game; it starts the real target
// (read from rp-target.txt next to this exe: line 1 = path of the exe, line 2 = arguments) and stays alive as long as the target runs,
// so Steam keeps treating the donor game as running. Writes rp-launcher.log next to itself.
internal static class RpLauncher
{
    private static int Main(string[] args)
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string log = Path.Combine(dir, "rp-launcher.log");
        try
        {
            string cfg = Path.Combine(dir, "rp-target.txt");
            if (!File.Exists(cfg)) { File.AppendAllText(log, DateTime.Now.ToString("s") + " rp-target.txt fehlt\r\n"); return 2; }
            string[] lines = File.ReadAllLines(cfg);
            string exe = lines.Length > 0 ? lines[0].Trim() : "";
            string a = lines.Length > 1 ? lines[1].Trim() : "";
            if (!File.Exists(exe)) { File.AppendAllText(log, DateTime.Now.ToString("s") + " Ziel nicht gefunden: " + exe + "\r\n"); return 3; }
            var psi = new ProcessStartInfo(exe, a) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false };
            File.AppendAllText(log, DateTime.Now.ToString("s") + " start: " + exe + " " + a + "\r\n");
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                File.AppendAllText(log, DateTime.Now.ToString("s") + " Ziel beendet, Code " + p.ExitCode + "\r\n");
                return p.ExitCode;
            }
        }
        catch (Exception e) { try { File.AppendAllText(log, DateTime.Now.ToString("s") + " Fehler: " + e.Message + "\r\n"); } catch { } return 1; }
    }
}

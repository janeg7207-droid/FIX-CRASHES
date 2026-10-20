using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Gorilla.CrashDetails
{
    public sealed class CakLoaderLog
    {
        private const int MaxCharacters = 2 * 1024 * 1024;
        private string content = "";
        private string[] lines = new string[0];
        public string Status = "CakeHook has no readable load report; a missing report does not prove that a CAK failed to load.";
        public DateTime WrittenUtc;

        public static CakLoaderLog Read(string gameDir)
        {
            var result = new CakLoaderLog();
            try
            {
                string path = Path.Combine(gameDir, "Plugins", "cakehook_load_report.txt");
                if (!File.Exists(path)) return result;
                result.WrittenUtc = File.GetLastWriteTimeUtc(path);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    var buffer = new char[MaxCharacters + 1];
                    int count = 0, read;
                    while (count < buffer.Length && (read = reader.Read(buffer, count, buffer.Length - count)) > 0) count += read;
                    if (count > MaxCharacters)
                    {
                        result.Status = "CakeHook's load report exceeds the 2 Mi-character scan limit; no per-file loader status is inferred.";
                        return result;
                    }
                    result.content = new string(buffer, 0, count);
                }
                int session = result.content.LastIndexOf("load report (new session)", StringComparison.OrdinalIgnoreCase);
                if (session >= 0) result.content = result.content.Substring(session);
                result.lines = result.content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                result.Status = "CakeHook log: " + result.WrittenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
                    (session >= 0 ? "; only the last marked session was considered." : "; no session marker was found, so quoted lines may be historical.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is System.Security.SecurityException)
            { result.Status = "CakeHook's per-file log could not be read: " + ex.Message + ". No loader status is inferred."; }
            return result;
        }

        public string ForFile(string path)
        {
            string name = Path.GetFileName(path);
            var quoted = lines
                .Where(line => Mentions(line, name)).Take(4).Select(line => line.Length > 600 ? line.Substring(0, 600) + " [line shortened]" : line).ToList();
            if (quoted.Count == 0) return null;
            bool stale = WrittenUtc < File.GetLastWriteTimeUtc(path);
            return "CakeHook's own messages mentioning this filename (quoted evidence, not an inferred rejection):\n" +
                string.Join("\n", quoted.Select(line => "> " + line)) + "\n" + Status +
                (stale ? "\nThis log predates the current CAK file's modification time. Reproduce the load and refresh before attributing these messages to this copy." : "") +
                "\nA filename mention can be a successful load or another event. Read the message itself; absence of a mention does not prove a loading failure.";
        }

        private static bool Mentions(string line, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int start = 0;
            while ((start = line.IndexOf(name, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = start + name.Length;
                if ((start == 0 || !FileCharacter(line[start - 1])) && (end == line.Length || !FileCharacter(line[end]))) return true;
                start = end;
            }
            return false;
        }
        private static bool FileCharacter(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.';
    }
}

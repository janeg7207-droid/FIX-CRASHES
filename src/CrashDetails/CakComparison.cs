using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Gorilla.CrashDetails
{
    public sealed class CakFileCopy
    {
        public CakInspection Archive;
        public CakAsset Asset;
        public string Sha256;
        public string UnverifiedReason;
        public string Name => Path.GetFileName(Archive.FilePath);
    }

    public sealed class CakSharedFile
    {
        public ulong AssetId;
        public List<CakFileCopy> Copies = new List<CakFileCopy>();
        public bool SamePath => Copies.Select(c => c.Asset.Path.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
        public bool DifferentStoredBytes => SamePath && Copies.Where(c => c.Sha256 != null).Select(c => c.Sha256).Distinct().Count() > 1;
        public bool AllCompared => Copies.All(c => c.Sha256 != null);
        public string Status => !SamePath ? "Shared asset ID, different path names — identity unconfirmed" :
            DifferentStoredBytes ? "Different stored versions of the same path" :
            !AllCompared ? "Shared path — byte comparison incomplete" : "Identical stored bytes";

        public string Text()
        {
            var text = new StringBuilder();
            text.AppendLine("Internal file: " + Copies[0].Asset.Path);
            text.AppendLine("Asset ID: " + AssetId.ToString("X16"));
            text.AppendLine("Result: " + Status);
            foreach (CakFileCopy copy in Copies)
            {
                text.AppendLine("- " + copy.Name + " -> " + copy.Asset.Path + " (" + copy.Asset.StoredLength + " stored bytes)");
                text.AppendLine("  " + (copy.Sha256 == null ? "Not compared: " + copy.UnverifiedReason : "Stored SHA-256: " + copy.Sha256));
            }
            foreach (var identical in Copies.Where(c => c.Sha256 != null).GroupBy(c => c.Sha256).Where(g => g.Count() > 1))
                text.AppendLine("Matching stored-byte copies: " + string.Join(", ", identical.Select(c => c.Name)));
            if (Copies.Select(c => c.Asset.StorageLayout).Distinct().Count() > 1)
                text.AppendLine("Storage metadata differs between these entries; matching stored bytes do not establish equivalent decoded files.");
            if (Copies.Any(c => c.Asset.PayloadEncrypted))
                text.AppendLine("At least one payload is encrypted. Only stored ciphertext bytes are compared; decoded content is unverified.");
            text.AppendLine("Shared IDs can be an intentional replacement, not a proven conflict or crash cause. Different compressed bytes can still decode to the same content. The loader's chosen winner is not determined here.");
            text.Append("Next: if the same scene fails, use the manager's existing mod-off/restore controls to compare the named CAKs separately, then restore each one to reproduce the result. Keep backups; do not delete a file on an overlap alone.");
            return text.ToString();
        }
    }

    public sealed class CakArchiveCopies
    {
        public string Sha256;
        public List<string> Names = new List<string>();
        public List<string> Paths = new List<string>();
    }

    public sealed class CakComparisonResult
    {
        public readonly List<CakSharedFile> SharedFiles = new List<CakSharedFile>();
        public readonly List<CakArchiveCopies> IdenticalArchives = new List<CakArchiveCopies>();
        public bool Limited;
        public long BytesRead;
        public string FullText()
        {
            var text = new StringBuilder("SAME FILES / STORED VERSIONS\nSaved locally; review personal paths before sharing.\n\n");
            foreach (var group in IdenticalArchives)
                text.AppendLine("Identical whole CAK archives (SHA-256): " + string.Join(", ", group.Names) + "\n" + group.Sha256 + "\nDuplicates alone do not establish a crash cause.\n");
            foreach (CakSharedFile group in SharedFiles)
                text.AppendLine(group.Text() + "\n\n");
            if (IdenticalArchives.Count == 0 && SharedFiles.Count == 0)
                text.AppendLine("No identical whole archives or shared file groups were established among the inspected files. Unsupported or limited archives remain unverified.");
            text.AppendLine("Comparison reads used: " + BytesRead + " bytes.");
            if (Limited) text.AppendLine("Inspection limits or unverified copies were encountered. This is a partial report; absent results do not establish that remaining files differ or are healthy.");
            text.Append("These results compare whole-archive hashes and stored payload bytes, without decompression, decryption or game execution. They do not prove decoded asset validity, compatibility or the exact crash cause.");
            return text.ToString();
        }
    }

    public static class CakComparison
    {
        public const long DefaultByteBudget = 256L * 1024 * 1024;
        public const int MaxIndexedEntries = 200000;
        public const int MaxSharedGroups = 1000;

        public static CakComparisonResult Compare(IEnumerable<CakInspection> inspected, long byteBudget = DefaultByteBudget, int timeBudgetMilliseconds = 15000,
            long maxArchiveBytes = 64L * 1024 * 1024, long maxPayloadBytes = 32L * 1024 * 1024, CancellationToken cancellationToken = default)
        {
            var result = new CakComparisonResult();
            cancellationToken.ThrowIfCancellationRequested();
            var budget = new HashBudget(result, byteBudget, timeBudgetMilliseconds, cancellationToken);
            var files = inspected.Where(f => f != null).ToList();
            var archiveHashes = new Dictionary<string, List<CakInspection>>();
            foreach (var sized in files.Where(f => f.Bytes > 0).GroupBy(f => f.Bytes).Where(g => g.Count() > 1))
                foreach (CakInspection file in sized)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string reason;
                    string hash = budget.Read(file, 0, (ulong)file.Bytes, maxArchiveBytes, out reason);
                    if (hash == null) { result.Limited = true; continue; }
                    List<CakInspection> copies;
                    if (!archiveHashes.TryGetValue(hash, out copies)) archiveHashes.Add(hash, copies = new List<CakInspection>());
                    copies.Add(file);
                }
            foreach (var pair in archiveHashes.Where(p => p.Value.Count > 1))
                result.IdenticalArchives.Add(new CakArchiveCopies { Sha256 = pair.Key,
                    Names = pair.Value.Select(f => Path.GetFileName(f.FilePath)).ToList(), Paths = pair.Value.Select(f => f.FilePath).ToList() });

            var indexed = new Dictionary<ulong, List<CakFileCopy>>();
            int entries = 0;
            foreach (CakInspection file in files.Where(f => f.TablesRead && !f.HasErrors))
                foreach (CakAsset asset in file.Assets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entries >= MaxIndexedEntries) { result.Limited = true; break; }
                    entries++;
                    List<CakFileCopy> copies;
                    if (!indexed.TryGetValue(asset.Hash, out copies)) indexed.Add(asset.Hash, copies = new List<CakFileCopy>());
                    copies.Add(new CakFileCopy { Archive = file, Asset = asset });
                }
            var shared = indexed.Where(p => p.Value.Select(c => c.Archive.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .OrderBy(p => p.Key).Take(MaxSharedGroups + 1).ToList();
            if (shared.Count > MaxSharedGroups) { result.Limited = true; shared.RemoveAt(MaxSharedGroups); }
            foreach (var pair in shared)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var group = new CakSharedFile { AssetId = pair.Key, Copies = pair.Value };
                foreach (CakFileCopy copy in group.Copies)
                {
                    if (copy.Archive.External) { copy.UnverifiedReason = "external-reference archive; payload is not stored in this CAK"; result.Limited = true; continue; }
                    if (!group.SamePath) { copy.UnverifiedReason = "the shared ID has different internal path names"; result.Limited = true; continue; }
                    string reason;
                    copy.Sha256 = budget.Read(copy.Archive, copy.Asset.StoredOffset, copy.Asset.StoredLength, maxPayloadBytes, out reason);
                    copy.UnverifiedReason = reason;
                    if (copy.Sha256 == null) result.Limited = true;
                }
                result.SharedFiles.Add(group);
            }
            return result;
        }

        private sealed class HashBudget
        {
            private readonly CakComparisonResult result;
            private readonly long bytes;
            private readonly int milliseconds;
            private readonly CancellationToken cancellationToken;
            private readonly Stopwatch clock = Stopwatch.StartNew();
            private readonly Dictionary<string, string> cache = new Dictionary<string, string>();

            public HashBudget(CakComparisonResult result, long bytes, int milliseconds, CancellationToken cancellationToken)
            { this.result = result; this.bytes = Math.Max(0, bytes); this.milliseconds = Math.Max(0, milliseconds); this.cancellationToken = cancellationToken; }

            public string Read(CakInspection file, ulong offset, ulong length, long perFileLimit, out string reason)
            {
                reason = null;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Unchanged(file)) { reason = "archive changed since its tables were read; scan again after copying/baking finishes"; return null; }
                    if (offset > (ulong)file.Bytes || length > (ulong)file.Bytes - offset) { reason = "range outside the archive"; return null; }
                    string key = file.FilePath + "\0" + offset + ":" + length;
                    string cached;
                    if (cache.TryGetValue(key, out cached)) return cached;
                    if (length > (ulong)Math.Max(0, perFileLimit) || length > (ulong)Math.Max(0, bytes - result.BytesRead) || clock.ElapsedMilliseconds >= milliseconds)
                    { reason = "byte/time comparison limit reached; no equality is claimed"; return null; }
                    using (var stream = new FileStream(file.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var sha = SHA256.Create())
                    {
                        stream.Position = (long)offset;
                        ulong remaining = length;
                        byte[] buffer = new byte[65536];
                        while (remaining > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (clock.ElapsedMilliseconds >= milliseconds) { reason = "time comparison limit reached; no equality is claimed"; return null; }
                            int read = stream.Read(buffer, 0, (int)Math.Min((ulong)buffer.Length, remaining));
                            if (read == 0) { reason = "payload ended during comparison"; return null; }
                            sha.TransformBlock(buffer, 0, read, buffer, 0);
                            remaining -= (uint)read; result.BytesRead += read;
                        }
                        sha.TransformFinalBlock(new byte[0], 0, 0);
                        if (!Unchanged(file) || stream.Length != file.Bytes) { reason = "archive changed during comparison"; return null; }
                        string digest = BitConverter.ToString(sha.Hash).Replace("-", "");
                        cache[key] = digest;
                        return digest;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is CryptographicException || ex is System.Security.SecurityException)
                { reason = "could not read a stable copy: " + ex.Message; return null; }
            }
            private static bool Unchanged(CakInspection file)
            {
                var info = new FileInfo(file.FilePath);
                return info.Exists && info.Length == file.Bytes && info.LastWriteTimeUtc.Ticks == file.WriteUtcTicks;
            }
        }
    }
}

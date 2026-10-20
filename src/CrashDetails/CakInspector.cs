using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Gorilla.CrashDetails
{
    public sealed class CakIssue
    {
        public string Code;
        public string Message;
        public bool Error;
    }

    public sealed class CakAsset
    {
        public ulong Hash;
        public string Path;
        public uint StoredBytes;
        public uint ExpandedBytes;
    }

    public sealed class CakInspection
    {
        public string FilePath;
        public long Bytes;
        public string Version;
        public bool Encrypted;
        public bool External;
        public bool TablesRead;
        public int FileCount;
        public readonly List<CakIssue> Issues = new List<CakIssue>();
        public readonly List<CakAsset> Assets = new List<CakAsset>();
        public bool HasErrors => Issues.Any(i => i.Error);

        public string Text()
        {
            var text = new StringBuilder();
            text.AppendLine("File: " + FilePath);
            text.AppendLine("Size: " + Bytes + " bytes; CAK version: " + (Version ?? "not identified"));
            text.AppendLine("Inspection: " + (HasErrors ? "a structural/read problem was found" : TablesRead ? "supported archive tables were read" : "limited; archive contents could not be fully inspected"));
            if (TablesRead) text.AppendLine("Archive entries: " + FileCount + "; asset hashes read: " + Assets.Count);
            foreach (CakIssue issue in Issues)
                text.AppendLine((issue.Error ? "PROBLEM" : "NOTE") + " [" + issue.Code + "]: " + issue.Message);
            if (Assets.Count > 0)
            {
                text.AppendLine("Sample internal paths (up to 8):");
                foreach (CakAsset asset in Assets.Take(8)) text.AppendLine("- " + asset.Path);
            }
            text.AppendLine("Scope: table structure and payload bounds only. Compressed/encrypted asset contents are not decoded, and game-version compatibility, texture/model correctness and the exact crash cause are not established by this scan.");
            text.Append("Next: use the reported file/entry as a lead. Obtain a fresh copy or rebake for your exact game version when a structural problem is reported. If only an overlap or limited inspection is reported, compare runs with that mod disabled and restored through Gorilla; do not delete it on this evidence alone.");
            return text.ToString();
        }
    }

    // FDIR field layouts follow Nenkai's Bakery research (MIT; see notices).
    // This independent reader never extracts, decompresses, edits or executes
    // the archive. Unknown/encrypted layouts are explicitly left unvalidated.
    public static class CakInspector
    {
        public const int MaxEntries = 100000;
        public const int MaxMetadataBytes = 32 * 1024 * 1024;
        private sealed class Section { public uint Size; public uint Offset; }
        private sealed class Dir { public uint Name; }
        private sealed class Entry
        {
            public uint Name, Parent, Size, Expanded, Flags;
            public ulong Offset;
            public uint LastChunk;
            public bool HasChunks;
        }

        public static CakInspection Inspect(string path)
        {
            var result = new CakInspection { FilePath = path };
            try
            {
                var before = new FileInfo(path);
                long writeTicks = before.LastWriteTimeUtc.Ticks;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    result.Bytes = stream.Length;
                    ReadArchive(reader, result);
                    if (stream.Length != result.Bytes || new FileInfo(path).LastWriteTimeUtc.Ticks != writeTicks)
                    {
                        result.TablesRead = false;
                        result.Assets.Clear();
                        Note(result, "ChangedDuringRead", "The file changed during inspection. Close the game and baking/copying tools, then scan again.");
                    }
                }
            }
            catch (InvalidDataException ex) { Problem(result, "InvalidStructure", ex.Message); }
            catch (EndOfStreamException) { Problem(result, "Truncated", "The header or an archive table ends before its declared data. The download, copy or bake may be incomplete."); }
            catch (DecoderFallbackException) { Problem(result, "InvalidStructure", "An archive path contains invalid UTF-8 and could not be read using the supported CAK string layout."); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
            { Problem(result, "Unreadable", "The file could not be read: " + ex.Message + ". Close tools using it, check access, and retry; a read error alone does not prove file corruption."); }
            return result;
        }

        private static void ReadArchive(BinaryReader reader, CakInspection result)
        {
            if (result.Bytes == 0) { Problem(result, "Empty", "The CAK is empty. The download, copy or bake did not produce an archive."); return; }
            if (result.Bytes < 8) throw new EndOfStreamException();
            byte[] magic = reader.ReadBytes(4);
            if (Encoding.ASCII.GetString(magic) != "FDIR")
            {
                string kind = magic[0] == 'P' && magic[1] == 'K' ? "a ZIP archive" : magic[0] == 'R' && magic[1] == 'a' && magic[2] == 'r' ? "a RAR archive" : magic[0] == '7' && magic[1] == 'z' ? "a 7z archive" : "an unrecognized file";
                Problem(result, "NotFdir", "The file is " + kind + ", not a supported FDIR CAK container. Changing its extension does not convert it. Extract a download archive or obtain/rebake the actual CAK.");
                return;
            }
            byte major = reader.ReadByte(), minor = reader.ReadByte();
            ushort flags = reader.ReadUInt16();
            result.Version = major + "." + minor;
            bool supported = (major == 6 && (minor == 7 || minor == 8)) ||
                (major == 8 && (minor == 1 || minor == 2 || minor == 3 || minor == 7)) ||
                (major == 9 && (minor == 1 || minor == 2 || minor == 3));
            if (!supported)
            {
                Note(result, "UnsupportedVersion", "FDIR version " + result.Version + " is not supported by this inspector. Its contents and integrity have NOT been validated. An unsupported/new version is not evidence of a damaged mod or game incompatibility.");
                return;
            }
            int headerSize = major >= 9 ? 0x5C : 0x58;
            if (result.Bytes < headerSize) throw new EndOfStreamException();
            bool modern = major > 8 || (major == 8 && minor >= 7);
            result.Encrypted = modern ? (flags & 0x8000) != 0 : (flags & 0x4000) != 0;
            int kindValue = modern ? (flags >> 8) & 0x7F : flags & 0x3FFF;
            result.External = kindValue == 5;
            if (result.Encrypted)
            {
                Note(result, "EncryptedMetadata", "The archive header/tables are flagged as encrypted. This inspector does not decrypt them, so the entries and integrity have NOT been validated. Some CAK encryption depends on the original filename; keep the creator's original name and use a compatible tool. Encryption is not corruption.");
                return;
            }
            if (kindValue != 1 && kindValue != 4 && kindValue != 5)
            {
                Note(result, "UnsupportedKind", "The archive registry type " + kindValue + " has not been validated by this reader. No payload validity is claimed.");
                return;
            }
            uint fileCount = reader.ReadUInt32(), dirCount = reader.ReadUInt32();
            uint declaredChunks = major >= 9 ? reader.ReadUInt32() : 0;
            if (fileCount > MaxEntries || dirCount > MaxEntries)
            {
                Note(result, "InspectionLimit", "The archive exceeds this inspector's " + MaxEntries + "-entry safety limit. Its contents were not scanned; this is not evidence of corruption.");
                return;
            }
            var sections = new Section[5];
            ulong metadataSize = 0;
            for (int i = 0; i < sections.Length; i++)
            {
                uint size = reader.ReadUInt32(); reader.ReadUInt32(); // checksum is not verified without the codec/crypto path
                sections[i] = new Section { Size = size, Offset = reader.ReadUInt32() };
                metadataSize += size;
            }
            reader.ReadUInt32(); reader.ReadUInt32();
            uint tocEnd = reader.ReadUInt32();
            if (tocEnd < headerSize || tocEnd > result.Bytes) throw Invalid("The declared archive table ends outside the file. The CAK may be truncated or incorrectly baked.");
            foreach (Section section in sections)
                if (section.Size > 0 && (section.Offset < headerSize || (ulong)section.Offset + section.Size > tocEnd))
                    throw Invalid("A declared archive section lies outside the table/file bounds. The CAK table is inconsistent.");
            var nonempty = sections.Where(s => s.Size > 0).OrderBy(s => s.Offset).ToList();
            for (int i = 1; i < nonempty.Count; i++)
                if ((ulong)nonempty[i - 1].Offset + nonempty[i - 1].Size > nonempty[i].Offset)
                    throw Invalid("Archive table sections overlap; the stored table offsets are inconsistent.");
            if (metadataSize > MaxMetadataBytes)
            {
                Note(result, "InspectionLimit", "Metadata exceeds the 32 MiB scan limit. The file was not fully inspected; this is not evidence of corruption.");
                return;
            }
            if ((ulong)dirCount * 12 > sections[0].Size || (ulong)fileCount * 12 > sections[1].Size)
                throw Invalid("An archive lookup table is too short for its declared entry count.");
            var fileHashes = ReadLookup(reader, sections[1], fileCount, true);
            ReadLookup(reader, sections[0], dirCount, false);
            byte[] strings = ReadSection(reader, sections[4]);
            List<Dir> dirs = ReadDirs(reader, sections[3], dirCount, fileCount);
            List<Entry> entries = ReadEntries(reader, sections[2], major, minor, fileCount, declaredChunks);
            // Validate directory strings even when no file refers to a folder.
            foreach (Dir dir in dirs) ReadName(strings, dir.Name, major);
            foreach (var pair in fileHashes)
            {
                Entry entry = entries[(int)pair.Value];
                string name = ReadName(strings, entry.Name, major);
                string fullPath = name;
                if (major >= 8)
                {
                    if (entry.Parent >= dirs.Count) throw Invalid("A file refers to a directory index outside the archive directory table.");
                    string parent = ReadName(strings, dirs[(int)entry.Parent].Name, major);
                    fullPath = string.IsNullOrEmpty(parent) ? name : parent.TrimEnd('/', '\\') + "/" + name;
                }
                fullPath = fullPath.Replace('\\', '/');
                if (fullPath.StartsWith("/") || fullPath.IndexOf(':') >= 0 || fullPath.Split('/').Any(p => p == ".."))
                    throw Invalid("An archive entry has an absolute or parent-traversal path: " + fullPath + ". It was not extracted.");
                ulong stored = entry.HasChunks ? entry.LastChunk : (entry.Flags & 0x80000000U) != 0 ? entry.Expanded : entry.Size;
                if (!result.External && stored > 0 &&
                    (entry.Offset < tocEnd || entry.Offset > (ulong)result.Bytes || stored > (ulong)result.Bytes - entry.Offset))
                    throw Invalid("Payload for '" + fullPath + "' extends outside the CAK or overlaps its metadata (offset " + entry.Offset + ", bytes " + stored + "). The file may be truncated or incorrectly baked.");
                result.Assets.Add(new CakAsset { Hash = pair.Key, Path = fullPath, StoredBytes = entry.Size, ExpandedBytes = entry.Expanded });
            }
            result.FileCount = (int)fileCount;
            result.TablesRead = true;
            if (fileCount == 0) Note(result, "NoEntries", "The archive contains no file entries, so it cannot supply mod assets.");
            if (result.External) Note(result, "ExternalAssets", "This is an external-reference CAK: asset payloads live outside the archive and must also be present in the game's folders. Payload offset bounds were deliberately not checked against this CAK.");
            Note(result, "CompatibilityUnverified", "FDIR format " + result.Version + " was read, but that alone does not establish WWE 2K26 compatibility. Check the mod author's game-version and loader requirements.");
        }

        private static Dictionary<ulong, uint> ReadLookup(BinaryReader file, Section section, uint count, bool retain)
        {
            var result = new Dictionary<ulong, uint>();
            using (var reader = SectionReader(file, section))
            {
                for (uint i = 0; i < count; i++)
                {
                    ulong hash = reader.ReadUInt64(); uint flags = reader.ReadUInt32();
                    if ((flags & 0x80000000U) != 0) continue; // empty-file marker has no usable payload
                    uint index = flags & 0x7FFFFFFFU;
                    if (index >= count) throw Invalid("An archive lookup index is outside its entry table.");
                    if (retain)
                    {
                        if (result.ContainsKey(hash)) throw Invalid("Two file lookup entries have the same asset hash inside one CAK.");
                        result.Add(hash, index);
                    }
                }
            }
            return result;
        }

        private static List<Dir> ReadDirs(BinaryReader file, Section section, uint count, uint fileCount)
        {
            var result = new List<Dir>();
            using (var reader = SectionReader(file, section))
                for (uint i = 0; i < count; i++)
                {
                    reader.ReadUInt64();
                    var dir = new Dir { Name = reader.ReadUInt32() };
                    ushort subDirs = reader.ReadUInt16(); reader.ReadUInt16();
                    ushort files = reader.ReadUInt16(); reader.ReadUInt16();
                    for (int n = 0; n < subDirs; n++) if (reader.ReadUInt32() >= count) throw Invalid("A subdirectory reference is outside the directory table.");
                    for (int n = 0; n < files; n++) if (reader.ReadUInt32() >= fileCount) throw Invalid("A directory's file reference is outside the file table.");
                    result.Add(dir);
                }
            return result;
        }

        private static List<Entry> ReadEntries(BinaryReader file, Section section, byte major, byte minor, uint count, uint declaredChunks)
        {
            var result = new List<Entry>();
            ulong chunks = 0;
            using (var reader = SectionReader(file, section))
                for (uint i = 0; i < count; i++)
                {
                    var entry = new Entry { Name = reader.ReadUInt32(), Parent = reader.ReadUInt32() };
                    if (major >= 9)
                    {
                        entry.Size = reader.ReadUInt32(); reader.ReadUInt32();
                        entry.Offset = reader.ReadUInt64(); entry.Expanded = reader.ReadUInt32();
                        ushort chunkCount = reader.ReadUInt16(); entry.Flags = reader.ReadUInt32();
                        uint previous = 0;
                        for (int c = 0; c < chunkCount; c++)
                        {
                            uint end = reader.ReadUInt32();
                            if (end < previous) throw Invalid("Compressed chunk end offsets move backwards inside a file entry.");
                            previous = end;
                        }
                        entry.HasChunks = chunkCount > 0; entry.LastChunk = previous; chunks += chunkCount;
                    }
                    else if (major == 8 && minor == 7)
                    {
                        entry.Size = reader.ReadUInt32(); entry.Offset = reader.ReadUInt64();
                        reader.ReadUInt32(); entry.Expanded = reader.ReadUInt32(); reader.ReadByte();
                    }
                    else
                    {
                        reader.ReadUInt32(); entry.Size = reader.ReadUInt32();
                        entry.Offset = reader.ReadUInt64(); reader.ReadUInt32();
                        entry.Expanded = major >= 8 ? reader.ReadUInt32() : entry.Size;
                    }
                    result.Add(entry);
                }
            if (major >= 9 && chunks != declaredChunks) throw Invalid("The header's compressed-chunk count disagrees with the file-entry table.");
            return result;
        }

        private static string ReadName(byte[] strings, uint offset, byte major)
        {
            if (offset >= strings.Length) throw Invalid("An entry's name offset lies outside the archive string table.");
            int start = (int)offset, size;
            if (major >= 8)
            {
                size = strings[start++];
                if ((long)start + size >= strings.Length || strings[start + size] != 0)
                    throw Invalid("A length-prefixed archive name is truncated or lacks its terminator.");
            }
            else
            {
                int end = Array.IndexOf(strings, (byte)0, start);
                if (end < 0 || end - start > 4096) throw Invalid("An archive name has no bounded string terminator.");
                size = end - start;
            }
            return new UTF8Encoding(false, true).GetString(strings, start, size);
        }

        private static byte[] ReadSection(BinaryReader file, Section section)
        {
            file.BaseStream.Position = section.Offset;
            byte[] bytes = file.ReadBytes((int)section.Size);
            if (bytes.Length != section.Size) throw new EndOfStreamException();
            return bytes;
        }
        private static BinaryReader SectionReader(BinaryReader file, Section section) => new BinaryReader(new MemoryStream(ReadSection(file, section), false));
        private static InvalidDataException Invalid(string text) => new InvalidDataException(text);
        private static void Problem(CakInspection result, string code, string text)
        { result.TablesRead = false; result.Assets.Clear(); result.Issues.Add(new CakIssue { Code = code, Message = text, Error = true }); }
        private static void Note(CakInspection result, string code, string text) => result.Issues.Add(new CakIssue { Code = code, Message = text });
    }
}

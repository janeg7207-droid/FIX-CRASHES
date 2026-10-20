using System.Text;

// Small, synthetic archives written from the published FDIR layout. These
// contain no game assets and do not depend on the production reader to build.
internal sealed class CakFixture
{
    public byte[] Bytes;
    public int FileInfo, FileLookup, DirInfo, Strings, TocEnd, RecordSize;

    public static CakFixture Create(byte major = 9, byte minor = 3, string[] names = null, bool chunks = false)
    {
        names ??= new[] { "test.bin" };
        int header = major >= 9 ? 92 : 88;
        using var strings = new MemoryStream();
        using (var sw = new BinaryWriter(strings, Encoding.UTF8, true))
        {
            if (major >= 9) { sw.Write((byte)0); sw.Write((byte)0); } // empty base directory
        }
        uint dirName = (uint)strings.Position;
        WriteName(strings, major, "Characters/1");
        var offsets = new List<uint>();
        foreach (string name in names) { offsets.Add((uint)strings.Position); WriteName(strings, major, major < 8 ? "Characters/1/" + name : name); }
        int entrySize = major >= 9 ? 34 + (chunks ? 4 : 0) : major == 8 && minor == 7 ? 29 : major >= 8 ? 32 : 28;
        int[] sizes = { 12, 12 * names.Length, entrySize * names.Length, 20 + 4 * names.Length, (int)strings.Length };
        int[] sections = new int[5];
        int cursor = header;
        for (int i = 0; i < 5; i++) { sections[i] = cursor; cursor = (cursor + sizes[i] + 3) & ~3; }
        var result = new CakFixture { FileLookup = sections[1], FileInfo = sections[2], DirInfo = sections[3], Strings = sections[4], TocEnd = cursor, RecordSize = entrySize };
        using var memory = new MemoryStream();
        using (var w = new BinaryWriter(memory, Encoding.UTF8, true))
        {
            w.Write(Encoding.ASCII.GetBytes("FDIR")); w.Write(major); w.Write(minor);
            w.Write((ushort)(major > 8 || (major == 8 && minor >= 7) ? 0x100 : 1));
            w.Write((uint)names.Length); w.Write(1U);
            if (major >= 9) w.Write(chunks ? (uint)names.Length : 0U);
            for (int i = 0; i < 5; i++) { w.Write((uint)sizes[i]); w.Write(1U); w.Write((uint)sections[i]); }
            w.Write(0U); w.Write(0U); w.Write((uint)cursor);
            memory.SetLength(cursor + names.Length * 64);
            memory.Position = sections[0]; w.Write(0UL); w.Write(0U);
            memory.Position = sections[1];
            for (int i = 0; i < names.Length; i++) { w.Write(Hash("Characters/1/" + names[i])); w.Write((uint)i); }
            memory.Position = sections[2];
            for (int i = 0; i < names.Length; i++)
            {
                w.Write(offsets[i]); w.Write(0U);
                if (major >= 9)
                {
                    w.Write(64U); w.Write(0U); w.Write((ulong)(cursor + i * 64)); w.Write(64U);
                    w.Write((ushort)(chunks ? 1 : 0)); w.Write(0U);
                    if (chunks) w.Write(64U);
                }
                else if (major == 8 && minor == 7)
                { w.Write(64U); w.Write((ulong)(cursor + i * 64)); w.Write(0U); w.Write(64U); w.Write((byte)0); }
                else
                { w.Write(0U); w.Write(64U); w.Write((ulong)(cursor + i * 64)); w.Write(0U); if (major >= 8) w.Write(64U); }
            }
            memory.Position = sections[3]; w.Write(0UL); w.Write(dirName);
            w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)names.Length); w.Write((ushort)0);
            for (int i = 0; i < names.Length; i++) w.Write((uint)i);
            memory.Position = sections[4]; w.Write(strings.ToArray());
            memory.Position = cursor;
            for (int i = 0; i < names.Length * 64; i++) w.Write((byte)0x41);
        }
        result.Bytes = memory.ToArray();
        return result;
    }

    public void U32(int position, uint value) => BitConverter.GetBytes(value).CopyTo(Bytes, position);
    public void U64(int position, ulong value) => BitConverter.GetBytes(value).CopyTo(Bytes, position);
    private static void WriteName(Stream target, byte major, string name)
    { byte[] data = Encoding.UTF8.GetBytes(name); if (major >= 8) target.WriteByte((byte)data.Length); target.Write(data); target.WriteByte(0); }
    private static ulong Hash(string value)
    { ulong hash = 14695981039346656037UL; foreach (byte b in Encoding.UTF8.GetBytes(value.ToLowerInvariant())) hash = unchecked((hash ^ b) * 1099511628211UL); return hash; }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CsAim.Core
{
    /// <summary>
    /// Read-only Valve VPK (v1/v2) reader. CS2 no longer ships loose script files: items_game.txt
    /// and the weapon data live inside game/csgo/pak01_dir.vpk, so we read them straight from the
    /// archive instead of asking the user to unpack anything.
    ///
    /// Layout: a header, then a directory tree grouped extension -> path -> file name. Each entry
    /// points either into the _dir.vpk itself (archive index 0x7FFF, data after the tree) or into
    /// pak01_NNN.vpk. Only the bytes of requested files are read, never the multi-GB archives.
    /// </summary>
    public sealed class VpkArchive
    {
        const uint Signature = 0x55AA1234;
        const ushort DirArchiveIndex = 0x7FFF;

        public struct Entry
        {
            public ushort archiveIndex;
            public uint offset;
            public uint length;
            public byte[] preload;
        }

        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        readonly string _dirPath;
        long _dataStart;

        public IEnumerable<string> Files => _entries.Keys;
        public int Count => _entries.Count;

        VpkArchive(string dirPath) { _dirPath = dirPath; }

        public static VpkArchive Open(string dirPath)
        {
            var vpk = new VpkArchive(dirPath);
            using (var fs = File.OpenRead(dirPath))
            using (var br = new BinaryReader(fs))
                vpk.ReadDirectory(br);
            return vpk;
        }

        void ReadDirectory(BinaryReader br)
        {
            if (br.ReadUInt32() != Signature) throw new InvalidDataException("Not a VPK directory file (bad signature).");
            uint version = br.ReadUInt32();
            uint treeSize = br.ReadUInt32();
            int headerSize;
            if (version == 1) headerSize = 12;
            else if (version == 2)
            {
                br.ReadBytes(16);   // file data, archive MD5, other MD5, signature section sizes
                headerSize = 28;
            }
            else throw new InvalidDataException($"Unsupported VPK version {version}.");

            _dataStart = headerSize + treeSize;
            var tree = new BinaryReader(new MemoryStream(br.ReadBytes((int)treeSize)));

            while (true)
            {
                string ext = ReadCString(tree);
                if (ext.Length == 0) break;
                while (true)
                {
                    string dir = ReadCString(tree);
                    if (dir.Length == 0) break;
                    while (true)
                    {
                        string name = ReadCString(tree);
                        if (name.Length == 0) break;

                        tree.ReadUInt32();                         // CRC32
                        ushort preloadBytes = tree.ReadUInt16();
                        var e = new Entry
                        {
                            archiveIndex = tree.ReadUInt16(),
                            offset = tree.ReadUInt32(),
                            length = tree.ReadUInt32(),
                        };
                        tree.ReadUInt16();                         // terminator 0xFFFF
                        e.preload = preloadBytes > 0 ? tree.ReadBytes(preloadBytes) : Array.Empty<byte>();

                        // A single space means "none" for both the directory and the extension.
                        string path = (dir == " " ? "" : dir + "/") + name + (ext == " " ? "" : "." + ext);
                        _entries[path] = e;
                    }
                }
            }
        }

        public bool Contains(string path) => _entries.ContainsKey(Normalise(path));

        public byte[] ReadBytes(string path)
        {
            if (!_entries.TryGetValue(Normalise(path), out var e)) return null;

            var result = new byte[e.preload.Length + e.length];
            Buffer.BlockCopy(e.preload, 0, result, 0, e.preload.Length);
            if (e.length == 0) return result;

            string file;
            long offset;
            if (e.archiveIndex == DirArchiveIndex)
            {
                file = _dirPath;
                offset = _dataStart + e.offset;
            }
            else
            {
                // pak01_dir.vpk -> pak01_007.vpk
                file = _dirPath.Substring(0, _dirPath.Length - "dir.vpk".Length) + e.archiveIndex.ToString("D3") + ".vpk";
                offset = e.offset;
            }

            using (var fs = File.OpenRead(file))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                int read = 0;
                while (read < e.length)
                {
                    int n = fs.Read(result, e.preload.Length + read, (int)e.length - read);
                    if (n <= 0) throw new EndOfStreamException($"Truncated VPK data for {path} in {Path.GetFileName(file)}.");
                    read += n;
                }
            }
            return result;
        }

        public string ReadText(string path)
        {
            var bytes = ReadBytes(path);
            return bytes == null ? null : Encoding.UTF8.GetString(bytes);
        }

        static string Normalise(string path) => path.Replace('\\', '/').TrimStart('/');

        static string ReadCString(BinaryReader br)
        {
            var bytes = new List<byte>(32);
            byte b;
            while ((b = br.ReadByte()) != 0) bytes.Add(b);
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace MiNest.Services;

/// <summary>
/// 官方 Sophon chunk 清单（protobuf）解析。字段号与 Starward 的 Sophon.proto 一致。
/// 这里手写最小解析器，避免为一个只读结构引入 protobuf 运行时。
/// </summary>
public sealed class SophonChunkFile
{
    public string File = "";
    public List<SophonChunk> Chunks = new();
    public bool IsFolder;
    public long Size;
    public string Md5 = "";
}

public sealed class SophonChunk
{
    public string Id = "";
    public string UncompressedMd5 = "";
    public long Offset;
    public long CompressedSize;
    public long UncompressedSize;
    public string CompressedMd5 = "";
}

public static class SophonManifest
{
    public static List<SophonChunkFile> Parse(byte[] data)
    {
        var root = new ProtoReader(data);
        var files = new List<SophonChunkFile>();
        while (root.HasMore)
        {
            var tag = root.ReadTag(out var wire);
            var field = (int)(tag >> 3);
            if (field == 1 && wire == 2)
                files.Add(ParseFile(root.ReadBytes()));
            else
                root.Skip(wire);
        }
        return files;
    }

    private static SophonChunkFile ParseFile(byte[] data)
    {
        var r = new ProtoReader(data);
        var file = new SophonChunkFile();
        while (r.HasMore)
        {
            var tag = r.ReadTag(out var wire);
            var field = (int)(tag >> 3);
            switch (field)
            {
                case 1 when wire == 2:
                    file.File = r.ReadString();
                    break;
                case 2 when wire == 2:
                    file.Chunks.Add(ParseChunk(r.ReadBytes()));
                    break;
                case 3 when wire == 0:
                    file.IsFolder = r.ReadVarint() != 0;
                    break;
                case 4 when wire == 0:
                    file.Size = (long)r.ReadVarint();
                    break;
                case 5 when wire == 2:
                    file.Md5 = r.ReadString();
                    break;
                default:
                    r.Skip(wire);
                    break;
            }
        }
        return file;
    }

    private static SophonChunk ParseChunk(byte[] data)
    {
        var r = new ProtoReader(data);
        var chunk = new SophonChunk();
        while (r.HasMore)
        {
            var tag = r.ReadTag(out var wire);
            var field = (int)(tag >> 3);
            switch (field)
            {
                case 1 when wire == 2:
                    chunk.Id = r.ReadString();
                    break;
                case 2 when wire == 2:
                    chunk.UncompressedMd5 = r.ReadString();
                    break;
                case 3 when wire == 0:
                    chunk.Offset = (long)r.ReadVarint();
                    break;
                case 4 when wire == 0:
                    chunk.CompressedSize = (long)r.ReadVarint();
                    break;
                case 5 when wire == 0:
                    chunk.UncompressedSize = (long)r.ReadVarint();
                    break;
                case 7 when wire == 2:
                    chunk.CompressedMd5 = r.ReadString();
                    break;
                default:
                    r.Skip(wire);
                    break;
            }
        }
        return chunk;
    }

    private sealed class ProtoReader
    {
        private readonly byte[] _data;
        private int _pos;

        public ProtoReader(byte[] data) => _data = data;

        public bool HasMore => _pos < _data.Length;

        public ulong ReadVarint()
        {
            ulong value = 0;
            var shift = 0;
            while (true)
            {
                if (_pos >= _data.Length)
                    throw new InvalidOperationException("清单 varint 越界。");
                var b = _data[_pos++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
                shift += 7;
                if (shift > 63) throw new InvalidOperationException("清单 varint 过长。");
            }
        }

        public ulong ReadTag(out int wire)
        {
            var tag = ReadVarint();
            wire = (int)(tag & 7);
            return tag;
        }

        public byte[] ReadBytes()
        {
            var len = checked((int)ReadVarint());
            if (len < 0 || _pos + len > _data.Length)
                throw new InvalidOperationException("清单长度越界。");
            var bytes = new byte[len];
            Buffer.BlockCopy(_data, _pos, bytes, 0, len);
            _pos += len;
            return bytes;
        }

        public string ReadString() => Encoding.UTF8.GetString(ReadBytes());

        public void Skip(int wire)
        {
            switch (wire)
            {
                case 0:
                    ReadVarint();
                    break;
                case 1:
                    _pos += 8;
                    break;
                case 2:
                    _pos += checked((int)ReadVarint());
                    break;
                case 5:
                    _pos += 4;
                    break;
                default:
                    throw new InvalidOperationException("清单包含未知 wire type。");
            }
            if (_pos > _data.Length) throw new InvalidOperationException("清单字段越界。");
        }
    }
}

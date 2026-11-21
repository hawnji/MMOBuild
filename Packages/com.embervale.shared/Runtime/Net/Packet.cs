using System;
using System.Text;

namespace Embervale.Shared.Net
{
    /// <summary>Little-endian binary writer with a reusable growable buffer.</summary>
    public sealed class PacketWriter
    {
        private byte[] _buf;
        public int Length { get; private set; }

        public PacketWriter(int capacity = 256) { _buf = new byte[capacity]; }

        public byte[] Buffer => _buf;
        public void Reset() => Length = 0;

        public byte[] ToArray()
        {
            var r = new byte[Length];
            System.Buffer.BlockCopy(_buf, 0, r, 0, Length);
            return r;
        }

        private void Ensure(int n)
        {
            if (Length + n <= _buf.Length) return;
            int size = _buf.Length * 2;
            while (size < Length + n) size *= 2;
            Array.Resize(ref _buf, size);
        }

        public PacketWriter U8(byte v) { Ensure(1); _buf[Length++] = v; return this; }
        public PacketWriter Bool(bool v) => U8(v ? (byte)1 : (byte)0);
        public PacketWriter I16(short v) { Ensure(2); _buf[Length++] = (byte)v; _buf[Length++] = (byte)(v >> 8); return this; }
        public PacketWriter U16(ushort v) => I16((short)v);
        public PacketWriter I32(int v)
        {
            Ensure(4);
            _buf[Length++] = (byte)v; _buf[Length++] = (byte)(v >> 8); _buf[Length++] = (byte)(v >> 16); _buf[Length++] = (byte)(v >> 24);
            return this;
        }
        public PacketWriter I64(long v) { I32((int)v); I32((int)(v >> 32)); return this; }
        public PacketWriter F32(float v) => I32(BitConverter.ToInt32(BitConverter.GetBytes(v), 0));

        /// <summary>Position encoded as fixed point (1/100 m) to halve bandwidth vs floats.</summary>
        public PacketWriter Pos(float v) => I32((int)Math.Round(v * 100f));

        public PacketWriter Str(string s)
        {
            if (s == null) { U16(0xFFFF); return this; }
            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length >= 0xFFFF) throw new ArgumentException("string too long");
            U16((ushort)bytes.Length);
            Ensure(bytes.Length);
            System.Buffer.BlockCopy(bytes, 0, _buf, Length, bytes.Length);
            Length += bytes.Length;
            return this;
        }
    }

    public sealed class PacketException : Exception
    {
        public PacketException(string msg) : base(msg) { }
    }

    /// <summary>Bounds-checked reader. Malformed client packets throw PacketException, never crash the server.</summary>
    public sealed class PacketReader
    {
        private readonly byte[] _buf;
        private readonly int _end;
        public int Position { get; private set; }

        public PacketReader(byte[] buf, int offset, int length)
        {
            _buf = buf;
            Position = offset;
            _end = offset + length;
        }

        public PacketReader(byte[] buf) : this(buf, 0, buf.Length) { }

        public int Remaining => _end - Position;

        private void Need(int n)
        {
            if (Position + n > _end) throw new PacketException("Packet truncated");
        }

        public byte U8() { Need(1); return _buf[Position++]; }
        public bool Bool() => U8() != 0;
        public short I16() { Need(2); short v = (short)(_buf[Position] | (_buf[Position + 1] << 8)); Position += 2; return v; }
        public ushort U16() => (ushort)I16();
        public int I32()
        {
            Need(4);
            int v = _buf[Position] | (_buf[Position + 1] << 8) | (_buf[Position + 2] << 16) | (_buf[Position + 3] << 24);
            Position += 4;
            return v;
        }
        public long I64() { long lo = (uint)I32(); long hi = I32(); return lo | (hi << 32); }
        public float F32() => BitConverter.ToSingle(BitConverter.GetBytes(I32()), 0);
        public float Pos() => I32() / 100f;

        public string Str(int maxBytes = 4096)
        {
            ushort len = U16();
            if (len == 0xFFFF) return null;
            if (len > maxBytes) throw new PacketException("String too long");
            Need(len);
            var s = Encoding.UTF8.GetString(_buf, Position, len);
            Position += len;
            return s;
        }
    }
}

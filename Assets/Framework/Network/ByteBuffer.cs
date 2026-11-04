using System;
using System.Text;

namespace GameFramework.Network
{
    /// <summary>
    /// 可扩容字节缓冲，**大端序**（与 C++/Go 服务端默认网络字节序一致）。
    /// 读写指针分离：写满自动翻倍扩容；读时超出直接抛异常（协议错误要立刻暴露，不要静默）。
    /// ★ 复用：高频包用 static ThreadLocal 或池，别每包 new（GC 抖动主因）。
    /// </summary>
    public class ByteBuffer
    {
        private byte[] _buffer;
        private int _writePos;
        private int _readPos;

        public ByteBuffer(int capacity = 256)
        {
            _buffer = new byte[capacity < 16 ? 16 : capacity];
        }

        public int Length { get { return _writePos; } }
        public int ReadableBytes { get { return _writePos - _readPos; } }
        public int ReadPosition { get { return _readPos; } }

        public void Reset()
        {
            _writePos = 0;
            _readPos = 0;
        }

        /// <summary>把外部字节数组作为只读源（用于解包）</summary>
        public ByteBuffer Wrap(byte[] data, int offset, int count)
        {
            _buffer = data;
            _readPos = offset;
            _writePos = offset + count;
            return this;
        }

        private void EnsureCapacity(int need)
        {
            if (_writePos + need <= _buffer.Length) return;
            int newSize = _buffer.Length;
            while (newSize < _writePos + need) newSize *= 2;
            var nb = new byte[newSize];
            Buffer.BlockCopy(_buffer, 0, nb, 0, _writePos);
            _buffer = nb;
        }

        public void CheckReadable(int count)
        {
            if (_readPos + count > _writePos)
                throw new ProtocolException(string.Format(
                    "ByteBuffer 越界读取: 需要 {0} 字节, 剩余 {1}", count, _writePos - _readPos));
        }

        // ---------------- 写 ----------------

        public ByteBuffer WriteByte(byte v)
        {
            EnsureCapacity(1);
            _buffer[_writePos++] = v;
            return this;
        }

        public ByteBuffer WriteBool(bool v) { return WriteByte(v ? (byte)1 : (byte)0); }

        public ByteBuffer WriteShort(short v)
        {
            EnsureCapacity(2);
            _buffer[_writePos++] = (byte)(v >> 8);
            _buffer[_writePos++] = (byte)v;
            return this;
        }

        public ByteBuffer WriteUShort(ushort v) { return WriteShort((short)v); }

        public ByteBuffer WriteInt(int v)
        {
            EnsureCapacity(4);
            _buffer[_writePos++] = (byte)(v >> 24);
            _buffer[_writePos++] = (byte)(v >> 16);
            _buffer[_writePos++] = (byte)(v >> 8);
            _buffer[_writePos++] = (byte)v;
            return this;
        }

        public ByteBuffer WriteUInt(uint v) { return WriteInt((int)v); }

        public ByteBuffer WriteLong(long v)
        {
            EnsureCapacity(8);
            _buffer[_writePos++] = (byte)(v >> 56);
            _buffer[_writePos++] = (byte)(v >> 48);
            _buffer[_writePos++] = (byte)(v >> 40);
            _buffer[_writePos++] = (byte)(v >> 32);
            _buffer[_writePos++] = (byte)(v >> 24);
            _buffer[_writePos++] = (byte)(v >> 16);
            _buffer[_writePos++] = (byte)(v >> 8);
            _buffer[_writePos++] = (byte)v;
            return this;
        }

        public ByteBuffer WriteFloat(float v)
        {
            // 用 BitConverter 会受宿主机字节序影响，统一走 int 中转
            return WriteInt(BitConverter.ToInt32(BitConverter.GetBytes(v), 0));
        }

        public ByteBuffer WriteString(string v)
        {
            if (v == null) { return WriteShort(0); }
            var bytes = Encoding.UTF8.GetBytes(v);
            if (bytes.Length > ushort.MaxValue) throw new ProtocolException("字符串超过 65535 字节");
            WriteShort((short)bytes.Length);
            EnsureCapacity(bytes.Length);
            Buffer.BlockCopy(bytes, 0, _buffer, _writePos, bytes.Length);
            _writePos += bytes.Length;
            return this;
        }

        public ByteBuffer WriteBytes(byte[] data)
        {
            return WriteBytes(data, 0, data == null ? 0 : data.Length);
        }

        public ByteBuffer WriteBytes(byte[] data, int offset, int count)
        {
            if (data == null || count <= 0) return this;
            EnsureCapacity(count);
            Buffer.BlockCopy(data, offset, _buffer, _writePos, count);
            _writePos += count;
            return this;
        }

        // ---------------- 读 ----------------

        public byte ReadByte()
        {
            CheckReadable(1);
            return _buffer[_readPos++];
        }

        public bool ReadBool() { return ReadByte() != 0; }

        public short ReadShort()
        {
            CheckReadable(2);
            short v = (short)((_buffer[_readPos] << 8) | _buffer[_readPos + 1]);
            _readPos += 2;
            return v;
        }

        public ushort ReadUShort() { return (ushort)ReadShort(); }

        public int ReadInt()
        {
            CheckReadable(4);
            int v = (_buffer[_readPos] << 24)
                  | (_buffer[_readPos + 1] << 16)
                  | (_buffer[_readPos + 2] << 8)
                  | _buffer[_readPos + 3];
            _readPos += 4;
            return v;
        }

        public uint ReadUInt() { return (uint)ReadInt(); }

        public long ReadLong()
        {
            CheckReadable(8);
            long v = ((long)_buffer[_readPos] << 56)
                   | ((long)_buffer[_readPos + 1] << 48)
                   | ((long)_buffer[_readPos + 2] << 40)
                   | ((long)_buffer[_readPos + 3] << 32)
                   | ((long)_buffer[_readPos + 4] << 24)
                   | ((long)_buffer[_readPos + 5] << 16)
                   | ((long)_buffer[_readPos + 6] << 8)
                   | _buffer[_readPos + 7];
            _readPos += 8;
            return v;
        }

        public float ReadFloat()
        {
            int raw = ReadInt();
            return BitConverter.ToSingle(BitConverter.GetBytes(raw), 0);
        }

        public string ReadString()
        {
            int len = ReadUShort();
            if (len == 0) return string.Empty;
            CheckReadable(len);
            string s = Encoding.UTF8.GetString(_buffer, _readPos, len);
            _readPos += len;
            return s;
        }

        public byte[] ReadBytes(int count)
        {
            CheckReadable(count);
            var result = new byte[count];
            Buffer.BlockCopy(_buffer, _readPos, result, 0, count);
            _readPos += count;
            return result;
        }

        public byte[] ReadRemaining()
        {
            return ReadBytes(ReadableBytes);
        }

        /// <summary>取出已写入的全部数据（拷贝）</summary>
        public byte[] ToArray()
        {
            var result = new byte[_writePos];
            Buffer.BlockCopy(_buffer, 0, result, 0, _writePos);
            return result;
        }

        public byte[] GetRawBuffer() { return _buffer; }
    }

    /// <summary>协议层异常：解码越界 / 帧头非法 / 版本不匹配</summary>
    public class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
    }
}

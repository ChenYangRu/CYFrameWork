using System;

namespace GameFramework.Network
{
    /// <summary>
    /// 网络帧。固定 12 字节头 + 变长包体，**大端序**：
    ///   [0..3] bodyLen  (uint32) 包体字节数
    ///   [4..7] cmd      (uint32) 协议号
    ///   [8..11] seq     (uint32) 序号（请求-响应配对 / 去重）
    /// 选择"长度前置"而不是分隔符：TCP 是流，只有长度前缀能在半包/粘包时无歧义切分。
    /// </summary>
    public class Packet
    {
        public const int HeaderSize = 12;
        /// <summary>单包上限 2MB：超过即视为协议错乱（也防止恶意包耗尽内存）</summary>
        public const int MaxBodySize = 2 * 1024 * 1024;

        public uint Cmd;
        public uint Seq;
        public byte[] Body;

        public int BodyLength { get { return Body == null ? 0 : Body.Length; } }

        public Packet() { }

        public Packet(uint cmd, uint seq, byte[] body)
        {
            Cmd = cmd;
            Seq = seq;
            Body = body;
        }

        /// <summary>当前包体起始读位置（每次 ReadBody 重新包装，避免共享状态）</summary>
        public ByteBuffer ReadBody()
        {
            var bb = new ByteBuffer(64);
            bb.Wrap(Body ?? new byte[0], 0, BodyLength);
            return bb;
        }

        /// <summary>组装完整帧字节（含头）</summary>
        public byte[] Encode()
        {
            int bodyLen = BodyLength;
            var buffer = new ByteBuffer(HeaderSize + bodyLen);
            buffer.WriteUInt((uint)bodyLen);
            buffer.WriteUInt(Cmd);
            buffer.WriteUInt(Seq);
            if (bodyLen > 0) buffer.WriteBytes(Body);
            return buffer.ToArray();
        }

        public override string ToString()
        {
            return string.Format("Packet(cmd={0}, seq={1}, body={2}B)", Cmd, Seq, BodyLength);
        }
    }

    /// <summary>
    /// 粘包/半包拆帧器。把流式字节喂进来，吐出完整 Packet。
    /// ★ 必须与"读线程"同线程使用，不做加锁。
    /// </summary>
    public class PacketSplitter
    {
        private byte[] _cache = new byte[8192];
        private int _cached;

        public void Reset() { _cached = 0; }

        /// <summary>追加原始字节到拆帧缓存</summary>
        public void Append(byte[] data, int offset, int count)
        {
            if (count <= 0) return;
            EnsureCapacity(_cached + count);
            Buffer.BlockCopy(data, offset, _cache, _cached, count);
            _cached += count;
        }

        /// <summary>
        /// 尝试从缓存里取出一帧。**必须循环调用直到返回 false**，
        /// 因为一次 TCP Read 可能带回多个完整帧（粘包）。
        /// </summary>
        public bool TryParse(out Packet packet)
        {
            packet = null;
            if (_cached < Packet.HeaderSize) return false;

            uint bodyLen = ((uint)_cache[0] << 24) | ((uint)_cache[1] << 16) | ((uint)_cache[2] << 8) | _cache[3];
            if (bodyLen > Packet.MaxBodySize)
            {
                _cached = 0;
                throw new ProtocolException("包体长度非法: " + bodyLen);
            }

            int total = Packet.HeaderSize + (int)bodyLen;
            if (_cached < total) return false;

            uint cmd = ((uint)_cache[4] << 24) | ((uint)_cache[5] << 16) | ((uint)_cache[6] << 8) | _cache[7];
            uint seq = ((uint)_cache[8] << 24) | ((uint)_cache[9] << 16) | ((uint)_cache[10] << 8) | _cache[11];

            byte[] body = null;
            if (bodyLen > 0)
            {
                body = new byte[bodyLen];
                Buffer.BlockCopy(_cache, Packet.HeaderSize, body, 0, (int)bodyLen);
            }

            // 前移剩余数据
            int remain = _cached - total;
            if (remain > 0) Buffer.BlockCopy(_cache, total, _cache, 0, remain);
            _cached = remain;

            packet = new Packet(cmd, seq, body);
            return true;
        }

        private void EnsureCapacity(int need)
        {
            if (need <= _cache.Length) return;
            int size = _cache.Length;
            while (size < need) size *= 2;
            var nb = new byte[size];
            Buffer.BlockCopy(_cache, 0, nb, 0, _cached);
            _cache = nb;
        }
    }
}

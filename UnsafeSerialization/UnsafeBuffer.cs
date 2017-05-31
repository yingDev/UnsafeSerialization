using System;
using System.Runtime.InteropServices;
using System.Text;

namespace YingDev.UnsafeSerialization
{
    //todo: 每一个方法 fixed，还是依赖于外部提供已经 pinned 的指针？？
    public sealed unsafe class UnsafeBuffer
    {
        public int _wPos;
        public int _rPos;
        //public byte[] _buf;

        public byte* p;
		public byte* end;

        public void ReadBytesTo(byte* dest, int size)
		{
			//fixed (byte* p = &_buf[_rPos])
			{
                Buffer.MemoryCopy(p + _rPos, dest, size, size);
                _rPos += size;
            }
        }

        public void WriteBytes(byte* src, int size)
        {
            Buffer.MemoryCopy(src, p + _wPos, size, size);
            _wPos += size;
        }

		public void Read2BytesTo(byte* dest)
		{
			*(ushort*)dest = *(ushort*)(p + _rPos);
			_rPos += 2;
		}

		public void Write2Bytes(byte* src)
		{
			*(ushort*) (p+_wPos) = *(ushort*)src;
			_wPos += 2;
		}

        public void Read4BytesTo(byte* dest)
		{
			//fixed (byte* p = &_buf[_rPos])
			*(uint*) dest = *(uint*) (p+_rPos);
            _rPos += 4;
        }

        public void Write4Bytes(byte* src)
        {
            *(uint*)(p + _wPos) = *(uint*)src;
            _wPos += 4;
        }

        public void Read8BytesTo(byte* dest)
		{
			//Debug.Log("Read8BytesTo { rPos = " + _rPos + ", dest="+ string.Format("{0:x}",(Int64)dest));
#if PLATFORM_ANDROID
            ReadBytesTo(dest, 8);
#else
			//fixed (byte* p = &_buf[_rPos])
			*(UInt64*) dest = *(UInt64*) (p+_rPos);
            _rPos += 8;
    #endif

            // Debug.Log("Read8BytesTo }");
        }

        public void Write8Bytes(byte* src)
        {
            *(UInt64*)(p + _wPos) = *(UInt64*)src;
            _wPos += 8;
        }

        public void ReadByteTo(byte* dest)
        {
            //fixed (byte* p = &_buf[_rPos++])
                *dest = *(p+_rPos++);
        }

        public byte ReadByte()
        {
            return *(p+_rPos++);
            //return _buf[_rPos++];
        }

		public UInt32 ReadU32()
		{
			var val = *(UInt32*)(p + _rPos);
			_rPos += 4;
			return val;
		}

		public Int32 ReadI32()
		{
			var val = *(Int32*)(p + _rPos);
			_rPos += 4;
			return val;
		}

		public UInt16 ReadU16()
		{
			var val = *(UInt16*)(p + _rPos);
			_rPos += 2;
			return val;
		}

		public Int16 ReadI16()
		{
			var val = *(Int16*)(p + _rPos);
			_rPos += 2;
			return val;
		}

		public void WriteByte(byte value)
        {
            *(p + _wPos++) = value;
        }

        StringBuilder _cstrSb = new StringBuilder(128);
        public unsafe string ReadCString()
		{

            //Console.WriteLine("ReadCString: pos = " + _rPos);
            //var sb = new StringBuilder(lengthHint);
            _cstrSb.Length = 0;
            char ch;
            while ((ch = (char) *(p + _rPos++)) != 0)
                _cstrSb.Append(ch);
			//Console.WriteLine("ReadCString: end pos = " + _rPos);
			return _cstrSb.Length == 0 ? string.Empty : _cstrSb.ToString();
        }

        public unsafe void WriteCString(string str)
        {
            for (var i = 0; i < str.Length; i++)
            {
                p[_wPos++] = (byte)str[i];
            }
            p[_wPos++] = 0;
        }
    }
}

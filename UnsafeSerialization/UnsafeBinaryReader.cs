using System;
using System.Runtime.InteropServices;
using System.Text;

namespace YingDev.UnsafeSerialization
{
    public sealed unsafe class UnsafeBinaryReader
    {
        public int _wPos;
        public int _rPos;
        //public byte[] _buf;

        public byte* p;

        public void ReadBytesTo(byte* dest, int size)
		{
			//fixed (byte* p = &_buf[_rPos])
			{
                var src = p+_rPos;
                /*if(size < 16)
                {
                    while(size-->0)
                    {
                        *dest++ = *src++;
                    }
                }
                else*/
                Buffer.MemoryCopy(src, dest, size, size);
                _rPos += size;


               /* while (size >= 8)
                {
                    *dest = *src;
                    dest[1] = src[1];
                    dest[2] = src[2];
                    dest[3] = src[3];
                    dest[4] = src[4];
                    dest[5] = src[5];
                    dest[6] = src[6];
                    dest[7] = src[7];
                    dest += 8;
                    src += 8;
                    size -= 8;
                }
                while (size >= 2)
                {
                    *dest = *src;
                    dest[1] = src[1];
                    dest += 2;
                    src += 2;
                    size -= 2;
                }
                if (size <= 0)
                    return;
                *dest = *src; */
            }
        }

        public void Read4BytesTo(byte* dest)
		{
			//fixed (byte* p = &_buf[_rPos])
			*(int*) dest = *(int*) (p+_rPos);
            _rPos += 4;
        }

        public void Read8BytesTo(byte* dest)
		{
			//Debug.Log("Read8BytesTo { rPos = " + _rPos + ", dest="+ string.Format("{0:x}",(Int64)dest));
#if PLATFORM_ANDROID
            ReadBytesTo(dest, 8);
#else
			//fixed (byte* p = &_buf[_rPos])
			*(Int64*) dest = *(Int64*) (p+_rPos);
            _rPos += 8;
    #endif

            // Debug.Log("Read8BytesTo }");
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

        StringBuilder _cstrSb = new StringBuilder(128);
        public unsafe string ReadCString(int lengthHint = 16)
		{

            //Console.WriteLine("ReadCString: pos = " + _rPos);
            //var sb = new StringBuilder(lengthHint);
            _cstrSb.Length = 0;
            char ch;
            while ((ch = (char) *(p + _rPos++)) != 0)
                _cstrSb.Append(ch);
			//Console.WriteLine("ReadCString: end pos = " + _rPos);
			return _cstrSb.ToString();
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

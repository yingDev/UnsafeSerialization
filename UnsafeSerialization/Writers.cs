using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using YingDev.UnsafeSerialization.Utils;

namespace YingDev.UnsafeSerialization
{
	public delegate void StructWriter(UnsafeBuffer buf, ObjectPtrHolder ptr);
	public delegate void ObjectWriter(UnsafeBuffer buf, object obj);

	public static class Writers
	{
		public unsafe static void _LayoutWriter(UnsafeBuffer buf, ObjectPtrHolder ptr, LayoutInfo layout)
		{
			var fields = layout.Fields;
			for (var i = 0; i < fields.Length; i++)
			{
				var f = fields[i];

				if (f.StructWriter != null)
				{
					ptr.offset += f.Offset;
					f.StructWriter(buf, ptr);
					ptr.offset -= f.Offset;
				}
				else
				{
					var value = LayoutInfo.GetObjectAtOffset(ptr.obj, ptr.offset + f.Offset);
					f.ObjectWriter(buf, value);
				}
			}
		}

		public static StructWriter LayoutWriter<T>()
		{
			return LayoutWriter(typeof(T));
		}

		public static StructWriter LayoutWriter(Type type)
		{
			LayoutInfo layout = null;
			return (r, p) =>
			{
				if (layout == null)
					layout = LayoutInfoRegistry.Get(type);
				_LayoutWriter(r, p, layout);//, null);
			};
		}

		public static ObjectWriter ObjectLayoutWriter<T>()
		{
			return (r, o) => MessageWriter(r, o);
		}

		public static ObjectWriter ObjectLayoutWriter(Type type)
		{
			return (r, o) => MessageWriter(r, o);
		}


		public static void MessageWriter(UnsafeBuffer r, object msg)
		{
			var layout = LayoutInfoRegistry.Get(msg.GetType());
			_LayoutWriter(r, new ObjectPtrHolder { obj = msg }, layout);//, msg);
		}

		public static StructWriter I32Writer = _I32Writer;
		public static void _I32Writer(UnsafeBuffer r, ObjectPtrHolder ptr)
		{
			//LOGGER.WriteLine("I32Reader");
			unsafe
			{
				if (ptr.obj == null)
					r.Write4Bytes(ptr.offset); //*((int*) ptr.offset) = r.ReadInt32();
				else
					r.Write4Bytes((byte*)ObjectPtrHolder.Pin(ptr.obj) + (int)ptr.offset);
				//r.Read4BytesTo((byte*)ptr.target + (int)ptr.offset /* + ObjectPtrHolder.OBJHEADER*/);  //*((int*) (ptr.target + (int)ptr.offset + ObjectPtrHolder.OBJHEADER)) = r.ReadInt32();
			}
		}

		public static StructWriter U16Writer = _U16Writer;
		public static void _U16Writer(UnsafeBuffer r, ObjectPtrHolder ptr)
		{
			//LOGGER.WriteLine("I32Reader");
			unsafe
			{
				if (ptr.obj == null)
					r.Write2Bytes(ptr.offset); //*((int*) ptr.offset) = r.ReadInt32();
				else
					//fixed (byte* p = ptr.fixer)
					r.Write2Bytes((byte*)ObjectPtrHolder.Pin(ptr.obj) + (int)ptr.offset);
				//r.Read4BytesTo((byte*)ptr.target + (int)ptr.offset /* + ObjectPtrHolder.OBJHEADER*/);  //*((int*) (ptr.target + (int)ptr.offset + ObjectPtrHolder.OBJHEADER)) = r.ReadInt32();
			}
		}

		public static StructWriter ByteWriter = _ByteWriter;
		public static void _ByteWriter(UnsafeBuffer r, ObjectPtrHolder ptr)
		{
			//LOGGER.WriteLine("ByteReader");
			unsafe
			{
				if (ptr.obj == null)
					r.WriteByte(*ptr.offset); //*((byte*) ptr.Ptr) = r.ReadByte();
				else
					//fixed (byte* p = ptr.fixer)
					r.WriteByte(*((byte*)ObjectPtrHolder.Pin(ptr.obj) + (int)ptr.offset) /*+ ObjectPtrHolder.OBJHEADER*/); //*((byte*) (ptr.target + (int)ptr.offset + ObjectPtrHolder.OBJHEADER)) = r.ReadByte();
			}
		}

		public static StructWriter F64Writer = _F64Writer;
		public static void _F64Writer(UnsafeBuffer r, ObjectPtrHolder ptr)
		{
			//LOGGER.WriteLine("F64Reader");
			unsafe
			{
				if (ptr.obj == null)
					r.Write8Bytes(ptr.offset); //*((double*) ptr.Ptr) = r.ReadDouble();
				else
					//fixed (byte* p = ptr.fixer)
					r.Write8Bytes((byte*)ObjectPtrHolder.Pin(ptr.obj) + (int)ptr.offset /*+ ObjectPtrHolder.OBJHEADER*/); //*((double*) (ptr.target + (int)ptr.offset + ObjectPtrHolder.OBJHEADER)) = r.ReadDouble();
			}
		}

		public static ObjectWriter CStringWriter = _CStringWriter;
		public static void _CStringWriter(UnsafeBuffer buf, object s)
		{
			buf.WriteCString((string)s);
		}

		public static ObjectWriter ValueArrayWriter<T>(ObjectWriter headerWriter, StructWriter itemWriter) where T : struct
		{
			//LOGGER.WriteLine("ValueArrayReader");
			var sizeofT = (int)Marshal.SizeOf<T>();

			return (w, o) =>
			{
				headerWriter(w, o);
				var array = (T[])o;

				unsafe
				{
					//var fixer = new ObjectPtrHolder { obj = array };
					//fixed (byte* p = fixer.fixer)
					ObjectPtrHolder.Pin(array);
					for (var i = 0; i < array.Length; i++)
					{
						//var ptr = new StructAddrHelper<T> { temp = array[i] };
						var ptr = Marshal.UnsafeAddrOfPinnedArrayElement(array, i);
						itemWriter(w, new ObjectPtrHolder { offset = (byte*)ptr });
					}

				}
			};
		}
	}
}

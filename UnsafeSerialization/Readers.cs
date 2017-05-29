﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using YingDev.UnsafeSerialization.Utils;

namespace YingDev.UnsafeSerialization
{
	public delegate void StructReader(UnsafeBuffer r, ObjectPtrHolder ptr);
	public delegate object ObjectReader(UnsafeBuffer r, object owner);

	public static class Readers
	{
        public unsafe static void _LayoutReader(UnsafeBuffer r, ObjectPtrHolder ptr, LayoutInfo layout)//, object owner)
		{
			var fields = layout.Fields;
			//LOGGER.WriteLine("_layoutReader: fields=" + string.Join(",", fields.Select(f => f.Name)));
			for (var i = 0; i < fields.Length; i++)
			{
				var f = fields[i];

				//LOGGER.WriteLine("_LayoutReader: " + f.Name + " @" + f.Offset);
				if (f.StructReader != null)
				{
                    ptr.offset += f.Offset;
                    f.StructReader(r, ptr);
                    ptr.offset -= f.Offset;
                }
                else
				{
                    var result = f.ObjectReader(r, ptr.obj);
                    LayoutInfo.SetObjectAtOffset(ptr.obj, (IntPtr)ptr.offset + f.Offset, result);    
                }
            }
		}

		public static StructReader LayoutReader<T>()
		{
			return LayoutReader(typeof(T));
		}

		public static StructReader LayoutReader(Type type)
		{
			LayoutInfo layout = null;
			return (r, p) =>
			{
				if (layout == null)
					layout = LayoutInfo.Get(type);
                _LayoutReader(r, p, layout);//, null);
			};
		}

		public static ObjectReader ObjectLayoutReader<T>()
		{
			var type = typeof(T);
			return (r, o) => (T)MessageReader(r, type);
		}

		public static ObjectReader ObjectLayoutReader(Type type)
		{
			return (r, o) => MessageReader(r, type);
		}

		public static object MessageReader(UnsafeBuffer r, Type type)
		{
			var layout = LayoutInfo.Get(type);
			var msg = Activator.CreateInstance(type);
            _LayoutReader(r, new ObjectPtrHolder { obj = msg }, layout);//, msg);
			return msg;
		}



		public static void I32Reader(UnsafeBuffer r, ObjectPtrHolder ptr)
		{
			//LOGGER.WriteLine("I32Reader");
			unsafe
			{
				if (ptr.obj == null)
					r.Read4BytesTo(ptr.offset); //*((int*) ptr.offset) = r.ReadInt32();
				else
					fixed (byte* p = ptr.fixer)
						r.Read4BytesTo((byte*)ptr.target + (int)ptr.offset);
				//r.Read4BytesTo((byte*)ptr.target + (int)ptr.offset /* + ObjectPtrHolder.OBJHEADER*/);  //*((int*) (ptr.target + (int)ptr.offset + ObjectPtrHolder.OBJHEADER)) = r.ReadInt32();
			}
		}

		public static void U16Reader(UnsafeBuffer r, ObjectPtrHolder ptr)
		{
			unsafe
			{
				if (ptr.obj == null)
					r.Read2BytesTo(ptr.offset); //*((int*) ptr.offset) = r.ReadInt32();
				else
					fixed (byte* p = ptr.fixer)
						r.Read2BytesTo((byte*)ptr.target + (int)ptr.offset);
			}
		}

		public static void ByteReader(UnsafeBuffer r, ObjectPtrHolder ptr)
		{
			//LOGGER.WriteLine("ByteReader");
			unsafe
			{
				if (ptr.obj == null)
					r.ReadByteTo(ptr.offset); //*((byte*) ptr.Ptr) = r.ReadByte();
				else
					fixed (byte* p = ptr.fixer)
						r.ReadByteTo((byte*)ptr.target + (int)ptr.offset /*+ ObjectPtrHolder.OBJHEADER*/); //*((byte*) (ptr.target + (int)ptr.offset + ObjectPtrHolder.OBJHEADER)) = r.ReadByte();
			}
		}

		public static void F64Reader(UnsafeBuffer r, ObjectPtrHolder ptr)
		{
			//LOGGER.WriteLine("F64Reader");
			unsafe
			{
				if (ptr.obj == null)
					r.Read8BytesTo(ptr.offset); //*((double*) ptr.Ptr) = r.ReadDouble();
				else
					fixed (byte* p = ptr.fixer)
						r.Read8BytesTo((byte*)ptr.target + (int)ptr.offset /*+ ObjectPtrHolder.OBJHEADER*/); //*((double*) (ptr.target + (int)ptr.offset + ObjectPtrHolder.OBJHEADER)) = r.ReadDouble();
			}
		}

		public static ObjectReader CondReader<T>(Predicate<T> pred, ObjectReader targetReader)
		{
			//LOGGER.WriteLine("CondReader");
			return (r, o) =>
			{
				if (pred((T)o))
				{

					return targetReader(r, o);
				}
				return null;
			};
		}

		public static StructReader CondReader<T>(Predicate<T> pred, StructReader targetReader)
		{
			//LOGGER.WriteLine("CondReader<T>");
			return (r, ptr) =>
			{

				//Console.WriteLine("Interesting");
				var owner = ptr.obj;
				if (owner != null && pred((T)owner))
				{
					//Console.WriteLine("shitxxxxxx");
					targetReader(r, ptr);
					//Console.WriteLine("hahahahahahhahahah");
				}
			};
		}

		public static ObjectReader ValueArrayReader<T>(ObjectReader arrayFactory, StructReader itemReader, int writeStartIndex = 0) where T : struct
		{
			//LOGGER.WriteLine("ValueArrayReader");
			var sizeofT = (int)Marshal.SizeOf<T>();

			return (r, o) =>
			{
				unsafe
				{
					var array = (T[])arrayFactory(r, o);
					var h = new StructAddrHelper<T>();
					var ptr = new ObjectPtrHolder { obj = null, offset = (&h.dum - sizeofT) };
					for (var i = writeStartIndex; i < array.Length; i++)
					{
						itemReader(r, ptr);
						array[i] = h.temp;
					}

					return array;
				}
			};
		}

		public static ObjectReader BlittableValueArrayReader<T>(ObjectReader arrayFactory) where T : struct
		{
			//LOGGER.WriteLine("ValueArrayReader");
			var sizeofT = Marshal.SizeOf(typeof(T));
			return (r, o) =>
			{
				unsafe
				{
					//Console.Write("*");
					var array = (T[])arrayFactory(r, o);
					var ptr = new ObjectPtrHolder { obj = array };
					fixed (byte* p = ptr.fixer)
					{
						r.ReadBytesTo(p, array.Length * sizeofT);
					}
					//Console.Write("#");
					/*for (var i = 0; i < array.Length; i++)
                    {
                        itemReader(r, ptr);
                        array[i] = h.temp;
                    }*/

					return array;
				}
			};
		}

		public static ObjectReader ObjectArrayReader<T>(ObjectReader arrayFactory, ObjectReader itemReader,
			int writeStartIndex = 0) where T : class
		{
			return (r, o) =>
			{
				var array = (T[])arrayFactory(r, o);
				for (var i = writeStartIndex; i < array.Length; i++)
					array[i] = (T)itemReader(r, o);
				return array;
			};
		}

        public static object CStringReader(UnsafeBuffer r, object ptr)
        {
            return r.ReadCString();
        }
	}

}


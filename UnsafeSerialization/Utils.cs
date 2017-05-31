using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using YingDev.UnsafeSerialization;
using System.Linq;

namespace YingDev.UnsafeSerialization.Utils
{
	public interface ILOGGER
	{
		void WriteLine(string msg);
	}

	public static class LOGGER
	{
		public static ILOGGER impl;

		public static void WriteLine(string msg)
		{
			impl?.WriteLine(msg);
		}
	}

	[StructLayout(LayoutKind.Explicit)]
	public unsafe struct ObjectFixer
	{
		[FieldOffset(0)] public object value;
		[FieldOffset(0)] public byte[] fixer;
	}

	[StructLayout(LayoutKind.Explicit)]
	public unsafe struct ObjectPtrHolder
	{
		[FieldOffset(0)] public object obj;
		[FieldOffset(0)] public byte[] fixer;

		[FieldOffset(8)] public byte* offset;

		public void* target
		{
			get
			{
				fixed (void* p = &offset)
				{
					return *(void**)((byte*)p - 8);
				}
			}
		}

		public void* Ptr => obj == null ? offset : ((byte*)target + (int)offset);

		/*public unsafe void Assign(IntPtr val)
        {
            fixed (byte* p = fixer)
                *(void**)Ptr = (void*)val;
        }

        public unsafe void Assign(int val)
        {
            fixed (byte* p = fixer)
                *(int*)Ptr = val;
        }

        public unsafe void Assign(byte val)
        {
            fixed (byte* p = fixer)
                *(byte*)Ptr = val;
        }

        public unsafe void Assign(double val)
        {
            fixed (byte* p = fixer)
                *(double*)Ptr = val;
        }*/
	}

	/*[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct StructAddrHelper<T> //where T : struct
	{
		public T temp;
		public byte dum;
	}*/

	public static class ToStringEx
	{
		public static string ToStringUsingLayoutInfo(Type t, object obj)
		{
			var layout = LayoutInfo.Get(t);
			var sb = new StringBuilder(128);
			sb.Append(t.Name);
			sb.Append(":\n{\n");
			foreach (var f in layout.Fields)
			{
				var value = f.Field.GetValue(obj);
				var str = "";
				if (value == null)
					str = "null";
				else if (value is string)
				{
					str = $"\"{value}\"";
				}
				else if (value.GetType().IsArray)
				{
					var arr = value as Array;
					var items = string.Join(", ", ((Array)value).OfType<object>().Take(32));
					str = $"[ {items} { (arr.Length > 32 ? ", ..." : string.Empty)} ]";
				}
				else if (typeof(IDictionary).IsAssignableFrom(value.GetType()))
				{
					var sb2 = new StringBuilder(128);
					sb2.Append("{ ");
					var dict = (IDictionary)value;
					foreach (var k in dict.Keys)
					{
						sb2.Append($"{k}: {dict[k]}, ");
					}
					if (sb2.Length > 1)
						sb2.Remove(sb2.Length - 2, 1);
					sb2.Append(" }");
					str = sb2.ToString();
				}
				else
					str = value.ToString();
				sb.Append($"  {f.Name}:\t{str}\n");
			}
			sb.Append("}\n");
			return sb.ToString();
		}

		public static string ToStringUsingLayoutInfo<T>(this T obj)
		{
			return ToStringUsingLayoutInfo(typeof(T), obj);
		}
	}

}

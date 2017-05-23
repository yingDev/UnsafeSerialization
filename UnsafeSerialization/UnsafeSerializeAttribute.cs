using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YingDev.UnsafeSerialization
{
	[AttributeUsage(AttributeTargets.Class| AttributeTargets.Struct)]
	public class UnsafeSerializeAttribute : Attribute
	{
		public const string METHOD_GET_FIELD_OFFSETS_NAME = "__UnsafeSerialization_GetFieldOffsets";
	}
}

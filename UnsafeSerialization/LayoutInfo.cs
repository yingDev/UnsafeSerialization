using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using static YingDev.UnsafeSerialization.Readers;

namespace YingDev.UnsafeSerialization
{
	public class LayoutField
	{
		public readonly string Name;
		public readonly int Offset;
		public readonly Type Type;
		public readonly StructReader StructReader;
		public readonly ObjectReader ObjectReader;
		public readonly Attribute[] Attributes;
		public readonly FieldInfo Field;

        public readonly Action<object, object> SetObjectField;

		public unsafe LayoutField(string name, int offset, Type type,
			StructReader structReader, ObjectReader objectReader,
			Attribute[] attributes, FieldInfo field, Action<object, object> objectFieldSetter)
		{
			Name = name;
			Offset = offset;
			Type = type;
			StructReader = structReader;
			ObjectReader = objectReader;
			Attributes = attributes;
			Field = field;
            SetObjectField = objectFieldSetter;
		}
	}


	[AttributeUsage(AttributeTargets.Field)]
	public class ReadAttribute : Attribute
	{
		public readonly StructReader StructReader;
		public readonly ObjectReader ObjectReader;

		public ReadAttribute(string expression)
		{
			//todo
			throw new NotSupportedException();
		}

		public ReadAttribute(Type readerType)
		{
		}
	}

	//todo: Awake
	public interface IAwake
	{
		void Awake();
	}


	public class LayoutInfo
	{
		static Dictionary<Type, LayoutInfo> _infoCache = new Dictionary<Type, LayoutInfo>(64);

		public readonly LayoutField[] Fields;
        public readonly Action<object, IntPtr, object> SetObjectAtOffset;

		public static LayoutInfo Get(Type type)
		{
			LayoutInfo info;
			if (_infoCache.TryGetValue(type, out info))
				return info;

			throw new Exception("LayoutInfo Is Not Added for type: " + type.AssemblyQualifiedName);
		}

		LayoutInfo(LayoutField[] fields, Action<object, IntPtr, object> setObjectAtOffset)
		{
			Fields = fields;
            SetObjectAtOffset = setObjectAtOffset;
		}

		static IEnumerable<FieldInfo> _getAllFields(Type type)
		{
			var privFields = Enumerable.Empty<FieldInfo>();
			var baseType = type;
			while (baseType != null && !baseType.IsValueType)
			{
				privFields = privFields.Concat(baseType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic));
				baseType = baseType.BaseType;
			}

			var publicFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);

			return privFields.Concat(publicFields);
		}

		static Dictionary<Type, bool> _StructLayoutValidationCache = new Dictionary<Type, bool>(32);
		static bool _validateLayoutAttr(Type type, bool isField)
		{
			bool cachedOk;
			if (_StructLayoutValidationCache.TryGetValue(type, out cachedOk))
			{
				return cachedOk;
			}

			var ok = true;
			var layoutAttr = type.StructLayoutAttribute;
			if (!type.IsValueType)
			{
				if (!isField && (layoutAttr == null || layoutAttr.Value == LayoutKind.Auto))
					ok = false;
			}
			else
			{
				if (layoutAttr != null && layoutAttr.Value == LayoutKind.Auto)
					ok = false;
			}

			//验证字段
			if (ok && (!isField || type.IsValueType))
			{
				foreach (var f in _getAllFields(type))
				{
					if (f.FieldType.IsValueType)
					{
						ok = _validateLayoutAttr(f.FieldType, true);
						if (!ok)
						{
							throw new Exception("Field " + f.Name + " has invalid layout. ( " + f.FieldType.FullName + " )");
						}
					}
				}
			}

			_StructLayoutValidationCache[type] = ok;
			return ok;
		}

		public static LayoutInfo Add<T>(IDictionary<Type, Delegate> defaultFieldReaders)
		{
			var type = typeof(T);
			LayoutInfo info;
			if (_infoCache.TryGetValue(type, out info))
				return info;

			/*if(!_validateLayoutAttr(type, false))
                throw new ArgumentException("Type layout is not Sequential/Explicit: " + type.FullName);*/

			var privFields = Enumerable.Empty<FieldInfo>();
			var baseType = type;
			while (baseType != null && !baseType.IsValueType)
			{
				privFields = privFields.Concat(baseType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic));
				baseType = baseType.BaseType;
			}

			var publicFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);

			//_validateMarshalAs(privFields.Concat(publicFields));
			var fieldOffsets = GetFieldOffsets(type);
			if (type.IsClass && fieldOffsets == null)
			{
				/*var attr = type.GetCustomAttribute<UnsafeSerializeAttribute>();
				if (attr != null)
					throw new Exception($"Seems you forget to run UnsafeSerializatonPostProcessor on type {type.FullName}");
				else
					throw new Exception($"Type {type.FullName} not marked with attribute [{typeof(UnsafeSerializeAttribute).FullName}]. Please add it and run UnsafeSerializationPostProcessor.");*/
			}

			var recognizedFields = publicFields.OrderBy(f => f.MetadataToken)
				.Select(f =>
				{
					var attrs = f.GetCustomAttributes(true);

					//2 - fetch reader
					var readerAttr = (ReadAttribute)attrs.FirstOrDefault(a => a is ReadAttribute);
					Delegate fieldReader;
					if (readerAttr == null)
					{
						//todo: refactor
						FieldInfo staticReaderField = null;
						var searchType = type;
						while (staticReaderField == null && searchType != null && searchType.BaseType != null)
						{
							staticReaderField = searchType.GetField(f.Name + "Reader",
								BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public |
								BindingFlags.FlattenHierarchy);
							searchType = searchType.BaseType;
						}
						if (staticReaderField != null)
						{
							fieldReader = (Delegate)staticReaderField.GetValue(null);
						}
						else if (defaultFieldReaders.TryGetValue(f.FieldType, out fieldReader))
						{
						}
						else
						{
							var layout = Get(f.FieldType);
							if (layout != null)
							{
								if (f.FieldType.IsValueType)
									fieldReader = LayoutReader(f.FieldType);
								else
									fieldReader = ObjectLayoutReader(f.FieldType);
							}
						}
					}
					else
						fieldReader = (Delegate)readerAttr.ObjectReader ?? readerAttr.StructReader;

					if (fieldReader == null)
						throw new Exception(
							"No reader available for " + type.FullName + "::" + f.Name + " (" + f.FieldType.FullName +
							")");

					var structReader = fieldReader as StructReader;
					var objReader = fieldReader as ObjectReader;

					var attributes = attrs.OfType<Attribute>().ToArray();

					var fieldOffset = ((int?)fieldOffsets?[f.DeclaringType.Name + "::" + f.Name]) ?? Marshal.OffsetOf(typeof(T), f.Name).ToInt32();
                    //var offsetAttr = (FieldOffsetAttribute) attributes.FirstOrDefault(a=>a is FieldOffsetAttribute); //Marshal.OffsetOf(typeof(T), f.Name).ToInt32();
                    //var offset = offsetAttr?.Value ?? Marshal.OffsetOf(typeof(T), f.Name).ToInt32();

                    Action<object, object> setter = null;
                    if(! f.FieldType.IsValueType)
                    {
                        var methodName = $"__UnsafeSerialization_SetObject_{f.Name}";
                        var method0 = f.DeclaringType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
                        setter = (Action < object , object>) method0.CreateDelegate(typeof(Action<object, object>));
                    }


					var fld = new LayoutField(f.Name,
						fieldOffset,
						f.FieldType,
						structReader, objReader, attributes, f, setter);

                    return fld;
				})
				.ToArray();

            Action<object, IntPtr, object> setObjectAtOffset = null;
            var method = type.GetMethod("__UnsafeSerialization_SetObjectAtOffset", BindingFlags.Static | BindingFlags.Public);
            if(method != null)
            {
                setObjectAtOffset = (Action<object, IntPtr, object>) method.CreateDelegate(typeof(Action<object, IntPtr, object>));
            }

			return _infoCache[type] = new LayoutInfo(recognizedFields, setObjectAtOffset);
		}

		static Dictionary<string, ulong> GetFieldOffsets(Type type)
		{
			var dummy = Activator.CreateInstance(type);
			var method = type.GetMethod(UnsafeSerializeAttribute.METHOD_GET_FIELD_OFFSETS_NAME);

			if (method != null)
			{
				var result = (Dictionary<string, ulong>)method.Invoke(null, new[] { dummy });
				Console.WriteLine($"{type.FullName} Offsets:");
				Console.WriteLine(string.Join(",", result.Select(kv => $"{kv.Key}=>{kv.Value}")));
				return result;
			}

			return null;
		}

		static void _validateMarshalAs(IEnumerable<FieldInfo> fields)
		{
			foreach (var f in fields)
			{
				//1 - validate Marshal type
				if (!f.FieldType.IsValueType && !f.FieldType.IsArray && f.FieldType != typeof(string))
				{
					var marshalAttr = f.GetCustomAttribute<MarshalAsAttribute>();
					if (marshalAttr == null || marshalAttr.Value != UnmanagedType.Interface)
						throw new Exception(
							"Non- Array/string/struct Field must have Attribute: [MarshalAs(UnmanagedType.Interface)]; (" +
							f.Name + ")");
				}
			}
		}
	}


}


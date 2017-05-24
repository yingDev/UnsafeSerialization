using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using static YingDev.UnsafeSerialization.Readers;
using static YingDev.UnsafeSerialization.Writers;

namespace YingDev.UnsafeSerialization
{
	public class LayoutField
	{
		public readonly string Name;
		public readonly int Offset;
		public readonly Type Type;
		public readonly StructReader StructReader;
		public readonly ObjectReader ObjectReader;
        public readonly StructWriter StructWriter;
        public readonly ObjectWriter ObjectWriter;
		public readonly Attribute[] Attributes;
		public readonly FieldInfo Field;

        //public readonly Action<object, object> SetObjectField;

		public unsafe LayoutField(string name, int offset, Type type,
			StructReader structReader, ObjectReader objectReader, StructWriter structWriter, ObjectWriter objectWriter,
			Attribute[] attributes, FieldInfo field)//, Action<object, object> objectFieldSetter)
		{
			Name = name;
			Offset = offset;
			Type = type;
			StructReader = structReader;
			ObjectReader = objectReader;
            StructWriter = structWriter;
            ObjectWriter = objectWriter;
			Attributes = attributes;
			Field = field;
            //SetObjectField = objectFieldSetter;
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


    //todo: 应该 postProcess 这个，GetObjectxxX，SetObjectXXX 直接用IL替换。
	public class LayoutInfo
	{
        public delegate object GetObjectAttOffsetFunc(object obj, IntPtr offset);

		static Dictionary<RuntimeTypeHandle, LayoutInfo> _infoCache = new Dictionary<RuntimeTypeHandle, LayoutInfo>(64);

		public readonly LayoutField[] Fields;
        public readonly Action<object, IntPtr, object> SetObjectAtOffset;
        public readonly GetObjectAttOffsetFunc GetObjectAtOffset;


        public static LayoutInfo Get(Type type)
		{
			LayoutInfo info;
			if (_infoCache.TryGetValue(type.TypeHandle, out info))
				return info;

			throw new Exception("LayoutInfo Is Not Added for type: " + type.AssemblyQualifiedName);
		}

		LayoutInfo(LayoutField[] fields, Action<object, IntPtr, object> setObjectAtOffset, GetObjectAttOffsetFunc getObjectAtOffset)
		{
			Fields = fields;
            SetObjectAtOffset = setObjectAtOffset;
            GetObjectAtOffset = getObjectAtOffset;

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

        //todo: Api improve
		public static LayoutInfo Add<T>(IDictionary<Type, Delegate> defaultFieldReaders, IDictionary<Type, Delegate> defaultFieldWriter)
		{
			var type = typeof(T);
			LayoutInfo info;
			if (_infoCache.TryGetValue(type.TypeHandle, out info))
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
					var attributes = f.GetCustomAttributes(true).OfType<Attribute>().ToArray();

                    var reader = GetFieldReader(type, f, defaultFieldReaders);
                    var writer = GetFieldWriter(type, f, defaultFieldWriter);

					var fieldOffset = ((int?)fieldOffsets?[f.DeclaringType.Name + "::" + f.Name]) ?? Marshal.OffsetOf(typeof(T), f.Name).ToInt32();

                    /*Action<object, object> setter = null;
                    if(! f.FieldType.IsValueType)
                    {
                        var methodName = $"__UnsafeSerialization_SetObject_{f.Name}";
                        var method0 = f.DeclaringType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
                        setter = (Action < object , object>) method0.CreateDelegate(typeof(Action<object, object>));
                    }*/


					var fld = new LayoutField(f.Name,
						fieldOffset,
						f.FieldType,
                        reader as StructReader,
                        reader as ObjectReader,
                        writer as StructWriter,
                        writer as ObjectWriter,
                        attributes, f);

                    return fld;
				})
				.ToArray();

            Action<object, IntPtr, object> setObjectAtOffset = null;
            var method = type.GetMethod("__UnsafeSerialization_SetObjectAtOffset", BindingFlags.Static | BindingFlags.Public);
            if(method != null)
            {
                setObjectAtOffset = (Action<object, IntPtr, object>) method.CreateDelegate(typeof(Action<object, IntPtr, object>));
            }

            GetObjectAttOffsetFunc getObjectAtOffset = null;
            var method1 = type.GetMethod("__UnsafeSerialization_GetObjectAtOffset", BindingFlags.Static | BindingFlags.Public);
            if(method1 != null)
            {
                getObjectAtOffset = (GetObjectAttOffsetFunc)method1.CreateDelegate(typeof(GetObjectAttOffsetFunc));
            }


            return _infoCache[type.TypeHandle] = new LayoutInfo(recognizedFields, setObjectAtOffset, getObjectAtOffset);
		}

        //todo: ReaderAttribute -> UnsafeSerialized
        static Delegate GetFieldReader(Type type, FieldInfo f, IDictionary<Type, Delegate> defaultFieldReaders)
        {
            var attrs = f.GetCustomAttributes(true);
            var readerAttr = (ReadAttribute)attrs.FirstOrDefault(a => a is ReadAttribute);
            Delegate fieldReader;
            if (readerAttr == null)
            {
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

            return fieldReader;
        }

        static Delegate GetFieldWriter(Type type, FieldInfo f, IDictionary<Type, Delegate> defaultFieldWriters)
        {
            var attrs = f.GetCustomAttributes(true);
            var readerAttr = (ReadAttribute)attrs.FirstOrDefault(a => a is ReadAttribute);
            Delegate fieldWriter;
            if (readerAttr == null)
            {
                FieldInfo staticWriterField = null;
                var searchType = type;
                while (staticWriterField == null && searchType != null && searchType.BaseType != null)
                {
                    staticWriterField = searchType.GetField(f.Name + "Writer",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public |
                        BindingFlags.FlattenHierarchy);
                    searchType = searchType.BaseType;
                }
                if (staticWriterField != null)
                {
                    fieldWriter = (Delegate)staticWriterField.GetValue(null);
                }
                else if (defaultFieldWriters.TryGetValue(f.FieldType, out fieldWriter))
                {
                }
                else
                {
                    var layout = Get(f.FieldType);
                    if (layout != null)
                    {
                        if (f.FieldType.IsValueType)
                            fieldWriter = LayoutWriter(f.FieldType);
                        else
                            fieldWriter = ObjectLayoutWriter(f.FieldType);
                    }
                }
            }
            else
                fieldWriter = (Delegate)readerAttr.ObjectReader ?? readerAttr.StructReader;

            if (fieldWriter == null)
                throw new Exception(
                    "No writer available for " + type.FullName + "::" + f.Name + " (" + f.FieldType.FullName +
                    ")");

            return fieldWriter;
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


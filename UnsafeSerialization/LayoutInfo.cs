using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using static YingDev.UnsafeSerialization.Readers;
using static YingDev.UnsafeSerialization.Writers;

namespace YingDev.UnsafeSerialization
{
    public interface IDefaultReaderWriterProvider
    {
        Delegate GetReaderFor(Type type);
        Delegate GetWriterFor(Type type);
    }

    public class DictDefaultReaderWriterProvider : IDefaultReaderWriterProvider
    {
        public readonly IDictionary<Type, Delegate> Readers;
        public readonly IDictionary<Type, Delegate> Writers;

        public DictDefaultReaderWriterProvider(IDictionary<Type, Delegate> readers, IDictionary<Type, Delegate> writers)
        {
            Readers = readers;
            Writers = writers;
        }

        public Delegate GetReaderFor(Type type)
        {
            Delegate r = null;
            Readers.TryGetValue(type, out r);
            return r;
        }

        public Delegate GetWriterFor(Type type)
        {
            Delegate w = null;
            Writers.TryGetValue(type, out w);
            return w;
        }
    }

    public static class LayoutInfoRegistry
    {
        static Dictionary<RuntimeTypeHandle, LayoutInfo> _infos = new Dictionary<RuntimeTypeHandle, LayoutInfo>(64);

        public static IDefaultReaderWriterProvider DefaultReaderWriterProvider { get; set; }

        public static void Clear()
        {
            _infos.Clear();
        }
        

        public static void Add(IEnumerable<Type> types)
        {
            foreach (var t in types)
                Add(t);
        }

        public static LayoutInfo Get(Type type)
        {
            LayoutInfo info;
            if (_infos.TryGetValue(type.TypeHandle, out info))
                return info;

            info = Add(type);
            if(info == null)
                throw new Exception("LayoutInfo Is Not Added for type: " + type.FullName);

            return info;
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
        public static LayoutInfo Add(Type type)
        {
            LayoutInfo info;
            if (_infos.TryGetValue(type.TypeHandle, out info))
                return info;

			if (type.IsEnum)
				throw new ArgumentException("Enum Type not surpported");

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
                var attr = type.GetCustomAttribute<UnsafeSerializeAttribute>();
				if (attr != null)
					throw new Exception($"Seems you forget to run UnsafeSerializatonPostProcessor on type {type.FullName}");
				else
					throw new Exception($"Type {type.FullName} not marked with attribute [{typeof(UnsafeSerializeAttribute).FullName}]. Please add it and run UnsafeSerializationPostProcessor.");
            }

            var recognizedFields = publicFields.OrderBy(f => f.MetadataToken)
                .Select(f =>
                {
                    var attributes = f.GetCustomAttributes(true).OfType<Attribute>().ToArray();

                    var reader = GetFieldReader(type, f);
                    var writer = GetFieldWriter(type, f);

                    var fieldOffset = ((int?)fieldOffsets?[f.DeclaringType.Name + "::" + f.Name]) ?? Marshal.OffsetOf(type, f.Name).ToInt32();

                    /*Action<object, object> setter = null;
                    if(! f.FieldType.IsValueType)
                    {
                        var methodName = $"__UnsafeSerialization_SetObject_{f.Name}";
                        var method0 = f.DeclaringType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
                        setter = (Action < object , object>) method0.CreateDelegate(typeof(Action<object, object>));
                    }*/


                    var fld = new LayoutField(f.Name,
                        fieldOffset,
                        f.FieldType.IsEnum ? Enum.GetUnderlyingType(f.FieldType) : f.FieldType,
                        reader as StructReader,
                        reader as ObjectReader,
                        writer as StructWriter,
                        writer as ObjectWriter,
                        attributes, f);

                    return fld;
                })
                .ToArray();

            /*Action<object, IntPtr, object> setObjectAtOffset = null;
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
            }*/
            var newObjMethod = type.GetMethod("__UnsafeSerialization_NewObj", BindingFlags.Static | BindingFlags.Public);
            var newObj = newObjMethod == null ? null : (Func<object>)newObjMethod.CreateDelegate(typeof(Func<object>));

            var thisReaderField = type.GetField("thisReader", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var selfReader = (ObjectReader)thisReaderField?.GetValue(null);

            info = new LayoutInfo(recognizedFields, selfReader, newObj);
            return _infos[type.TypeHandle] = info;
        }

        //todo: ReaderAttribute -> UnsafeSerialized
        static Delegate GetFieldReader(Type type, FieldInfo f)
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
				else if ((fieldReader = DefaultReaderWriterProvider.GetReaderFor(f.FieldType)) != null)// defaultFieldReaders.TryGetValue(f.FieldType, out fieldReader))
				{
				}
				else if (f.FieldType.IsEnum && (fieldReader = DefaultReaderWriterProvider.GetReaderFor(Enum.GetUnderlyingType(f.FieldType))) != null)
				{
				}
				else
                {
                    if (f.FieldType.GetCustomAttribute<UnsafeSerializeAttribute>() != null)
                    {
                        var layout = Add(f.FieldType);
                        if (layout != null)
                        {
                            if (f.FieldType.IsValueType)
                                fieldReader = LayoutReader(f.FieldType);
                            else
                                fieldReader = ObjectLayoutReader(f.FieldType);
                        }
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

        static Delegate GetFieldWriter(Type type, FieldInfo f)
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
                else if ( (fieldWriter = DefaultReaderWriterProvider.GetWriterFor(f.FieldType)) != null)// defaultFieldWriters.TryGetValue(f.FieldType, out fieldWriter))
                {
                }
				else if (f.FieldType.IsEnum && (fieldWriter = DefaultReaderWriterProvider.GetWriterFor(Enum.GetUnderlyingType(f.FieldType))) != null)
				{
				}
				else
                {
                    if(f.FieldType.GetCustomAttribute<UnsafeSerializeAttribute>() != null)
                    {
                        var layout = Add(f.FieldType);
                        if (layout != null)
                        {
                            if (f.FieldType.IsValueType)
                                fieldWriter = LayoutWriter(f.FieldType);
                            else
                                fieldWriter = ObjectLayoutWriter(f.FieldType);
                        }
                        else
                            throw new Exception("Unable to Add LayoutInfo for field " + type.FullName + "::" + f.Name + " of type " + f.FieldType);
                    }

                }
            }
            else
                fieldWriter = (Delegate)readerAttr.ObjectReader ?? readerAttr.StructReader;

            /*if (fieldWriter == null)
                throw new Exception(
                    "No writer available for " + type.FullName + "::" + f.Name + " (" + f.FieldType.FullName +
                    ")");*/

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
    
    
	public sealed class LayoutInfo
	{
		public readonly LayoutField[] Fields;
		public readonly ObjectReader SelfReader;// { get; private set; }
		public readonly Func<object> NewObj;// { get; private set; }

		internal LayoutInfo(LayoutField[] fields, ObjectReader selfReader, Func<object> newObj)
		{
			Fields = fields;
			SelfReader = selfReader;
			NewObj = newObj;
        }

		public static void SetObjectAtOffset(object obj, IntPtr offset, object value)
		{
			throw new NotImplementedException(nameof(SetObjectAtOffset) + " is Supposed to be implemented in IL by the postprocessor");
		}

		public static object GetObjectAtOffset(object target, IntPtr offset)
		{
			throw new NotImplementedException(nameof(GetObjectAtOffset) + " is Supposed to be implemented in IL by the postprocessor");
		}
        
	}


}


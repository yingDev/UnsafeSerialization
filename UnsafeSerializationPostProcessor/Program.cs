using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using OC = Mono.Cecil.Cil.OpCodes;
using static System.Console;

namespace YingDev.UnsafeSerializationPostProcessor
{
	static class Program
	{
		const string GENERATED_METHOD_NAME = "__UnsafeSerialization_GetFieldOffsets";
		static readonly string MARKER_ATTR = "YingDev.UnsafeSerialization.UnsafeSerializeAttribute";
		const string HELP = "Usage: UnsafeSerializationPostProcessor file [true|false]";

		static MethodReference ObjectPtrHolderPinRef;

		struct Point { int x; int y; }
		static object New()
		{
			return default(Point);
		}

		static void Main(string[] args)
		{
			var file = ValidateArgs(args);
			var writeSymbols = true;
			if (args.Length > 1)
			{
				if (!bool.TryParse(args[1], out writeSymbols))
				{
					WriteLine(HELP);
					Environment.Exit(0);
				}
			}

			var readerParam = new ReaderParameters { ReadSymbols = writeSymbols, ReadWrite = true, InMemory = true };
			var writerParam = new WriterParameters { WriteSymbols = writeSymbols };

			var dll = AssemblyDefinition.ReadAssembly(file, readerParam);

			var assemblyModified = false;


			foreach (var mod in dll.Modules)
			{
				var types = mod.GetAllTypes();
				Init(mod);

				foreach (var t in types)
					assemblyModified |= ReplacePinImpl(t);

				var markedTypes = types.Where(t => null != t.CustomAttributes.Where(a => a.AttributeType.FullName == MARKER_ATTR).SingleOrDefault());
				foreach (var type in markedTypes)
				{
					WriteLine($"Processing: {type.FullName}");

					Emit_NewObj_IL(type);
					Emit_GetFieldOffsets_IL(type);

					assemblyModified = true;
				}

				// process UnsafeSerialization library itself
				// the dll may or may not contain class LayoutInfo
				var layoutInfoClass = types.Where(t => t.FullName == "YingDev.UnsafeSerialization.LayoutInfo").SingleOrDefault();
				if (layoutInfoClass != null)
				{
					WriteLine("Found LayoutInfo, replacing some methods...");

					Emit_SetObjectAtOffset_IL(layoutInfoClass);
					Emit_GetObjectAtOffset_IL(layoutInfoClass);

					assemblyModified = true;
				}
			}

			if (assemblyModified)
			{
				dll.Write(file, writerParam);

				WriteLine($"File has been processed.");
			}
			else
				WriteLine($"No class/struct with attribute [{MARKER_ATTR}] found!");
		}

		static string ValidateArgs(string[] args)
		{
			if (args.Length < 1)
			{
				WriteLine();
				Environment.Exit(-1);
			}

			var file = args[0];
			if (!File.Exists(file))
			{
				WriteLine($"File Not Exists: {file}");
				Environment.Exit(-2);
			}
			return file;
		}

		static void Init(ModuleDefinition mod)
		{
			var ObjectPtrHolderPinName = "System.Byte* YingDev.UnsafeSerialization.Utils.ObjectPtrHolder::Pin(System.Object)";
			var methodRefs = mod.GetMemberReferences();
			ObjectPtrHolderPinRef = (MethodReference)methodRefs.SingleOrDefault(m => m.FullName == ObjectPtrHolderPinName);
			if (ObjectPtrHolderPinRef == null)
			{
				foreach (var t in mod.Types)
				{
					ObjectPtrHolderPinRef = t.Methods.SingleOrDefault(m => m.FullName == ObjectPtrHolderPinName);
					if (ObjectPtrHolderPinRef != null)
						break;
				}
			}
		}

		static bool ReplacePinImpl(TypeDefinition type)
		{
			//return false;
			if (ObjectPtrHolderPinRef == null)
				return false;
			var modified = false;
			foreach (var m in type.Methods)
			{
				if (!m.HasBody || !m.IsIL)
					continue;

				var il = m.Body.GetILProcessor();
				var calls = m.Body.Instructions.Where(i => i.OpCode == OC.Call && i.Operand == ObjectPtrHolderPinRef).ToArray();

				foreach (var c in calls)
				{
					WriteLine("Replacing ObjectPtrHolder::Pin in method " + m.FullName);

					var pinVar = new VariableDefinition(new PinnedType(m.Module.TypeSystem.Object));
					m.Body.Variables.Add(pinVar);

					var pinIl = il.Create(OC.Stloc, pinVar.Index);
					var load = il.Create(OC.Ldloc, pinVar.Index);
					var conv = il.Create(OC.Conv_I);

					il.Replace(c, pinIl);
					if (pinIl.Next.OpCode == OC.Pop)
					{
						il.Remove(pinIl.Next);
					}
					else
					{
						il.InsertAfter(pinIl, load);
						il.InsertAfter(load, conv);
					}

					modified = true;
				}
			}
			return modified;
		}

		static void Emit_GetFieldOffsets_IL(TypeDefinition type)
		{
			var mod = type.Module;
			var targetMethod = type.Methods.FirstOrDefault(m => m.Name == GENERATED_METHOD_NAME);
			if (targetMethod != null)
				type.Methods.Remove(targetMethod);

			var string_t = mod.ImportReference(typeof(string));
			var ulong_t = mod.ImportReference(typeof(ulong));
			var dict_t = mod.ImportReference(mod.ImportReference(typeof(Dictionary<,>)).Resolve().MakeGenericInstanceType(string_t, ulong_t));

			var method = new MethodDefinition(GENERATED_METHOD_NAME, MethodAttributes.Public | MethodAttributes.Static, dict_t);
			//method.Parameters.Add(new ParameterDefinition("inst", ParameterAttributes.None, mod.TypeSystem.Object));
			type.Methods.Add(method);

			var il = method.Body.GetILProcessor();
			var body = method.Body;

			var int_t = mod.ImportReference(typeof(int));

			var dict_tdef = mod.ImportReference(typeof(Dictionary<,>)).Resolve();
			var dictInst_t = (GenericInstanceType)mod.ImportReference(dict_tdef.MakeGenericInstanceType(string_t, ulong_t));

			var dictCtor = mod.ImportReference(dict_tdef.GetConstructors().Where(m => m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == int_t.Name).Single());
			var dictSetter = mod.ImportReference(dict_tdef.Methods.Where(m => m.Name == "set_Item").Single());

			dictCtor = mod.ImportReference(MakeGenericMethod(dictCtor, dictInst_t, int_t));
			dictSetter = mod.ImportReference(MakeGenericMethod(dictSetter, dictInst_t, dict_tdef.GenericParameters[0], dict_tdef.GenericParameters[1]));

			var fields = GetInstFields(type).ToArray();

			body.InitLocals = true;
			body.Variables.Add(new VariableDefinition(dictInst_t));
			body.Variables.Add(new VariableDefinition(ulong_t));

			//the Instance @ loc2
			body.Variables.Add(new VariableDefinition(type));

			//method.Body.Variables.Add(new VariableDefinition(new PinnedType(mod.ImportReference(typeof(object)))));

			il.Append(GetLdcI4((sbyte)fields.Length));
			il.Emit(OC.Newobj, dictCtor);
			il.Emit(OC.Stloc_0);

			if (!type.IsValueType)
			{
				var ctor = type.GetConstructors().First(c => !c.HasParameters);
				il.Emit(OC.Newobj, ctor);
				il.Emit(OC.Stloc_2);
			}

			// store base addr of the Instance
			il.Emit(OC.Ldloca, 1);
			if (type.IsValueType)
				il.Emit(OC.Ldloca, 2);
			else
				il.Emit(OC.Ldloc_2);
			il.Emit(OC.Stind_Ref);

			//il.EmitWriteLine(mod, "C");

			for (var i = 0; i < fields.Length; i++)
			{
				var f = fields[i];

				il.Emit(OC.Ldloc_0);
				il.Emit(OC.Ldstr, f.DeclaringType.Name + "::" + f.Name);

				if (type.IsValueType)
					il.Emit(OC.Ldloca, 2);
				else
					il.Emit(OC.Ldloc_2);
				//il.Emit(OC.Castclass, f.DeclaringType);
				il.Emit(OC.Ldflda, f);
				il.Emit(OC.Conv_U8);

				il.Emit(OC.Ldloc_1);
				il.Emit(OC.Sub);

				il.Emit(OC.Callvirt, dictSetter);
			}

			il.Emit(OC.Ldloc_0);
			il.Emit(OC.Ret);
		}

		static void Emit_Add_SetObject_Method_IL(TypeDefinition type)
		{
			var fields = GetInstFields(type).ToArray();
			foreach (var f in fields)
			{
				if (f.FieldType.IsValueType)
					continue;

				var methodName = $"__UnsafeSerialization_SetObject_{f.Name}";
				if (f.DeclaringType.GetMethods().Where(m => m.Name == methodName).SingleOrDefault() != null)
					continue;

				var setMethod = new MethodDefinition(methodName, MethodAttributes.Static | MethodAttributes.Public, type.Module.ImportReference(typeof(void)));
				setMethod.Parameters.Add(new ParameterDefinition(type.Module.ImportReference(typeof(object)))); //target
				setMethod.Parameters.Add(new ParameterDefinition(type.Module.ImportReference(typeof(object)))); //value

				var il = setMethod.Body.GetILProcessor();
				il.Emit(OC.Ldarg_0);
				il.Emit(OC.Ldarg_1);
				il.Emit(OC.Stfld, f);
				il.Emit(OC.Nop);
				il.Emit(OC.Ret);

				f.DeclaringType.Methods.Add(setMethod);
			}


		}

		static void Emit_SetObjectAtOffset_IL(TypeDefinition type)
		{
			var mod = type.Module;
			var methodName = $"SetObjectAtOffset";

			var method = type.Methods.Where(m => m.Name == methodName).SingleOrDefault();
			if (method != null)
			{
				WriteLine("Method " + methodName + " stub found. Replacing Impl...");
				method.Body.Instructions.Clear();
			}
			else
				throw new Exception(methodName + ": Stub method not defined!");

			// var method = new MethodDefinition(methodName, MethodAttributes.Static | MethodAttributes.Public, mod.ImportReference(typeof(void)));
			// method.Parameters.Add(new ParameterDefinition(mod.ImportReference(typeof(object)))); //target
			// method.Parameters.Add(new ParameterDefinition(mod.ImportReference(typeof(IntPtr)))); //offset
			// method.Parameters.Add(new ParameterDefinition(mod.ImportReference(typeof(object)))); //value

			//setMethod.Body.Variables.Add(new VariableDefinition(mod.ImportReference(typeof(object))));
			method.Body.Variables.Add(new VariableDefinition(new PinnedType(mod.ImportReference(typeof(object)))));


			var il = method.Body.GetILProcessor();
			//pin
			il.Emit(OC.Ldarg_0);
			il.Emit(OC.Stloc_0);
			//il.EmitWriteLine(mod, "A");

			//addr
			il.Emit(OC.Ldarg_0);
			il.Emit(OC.Conv_I);


			//label: target != null
			il.Emit(OC.Ldarg_1);
			il.Emit(OC.Conv_I);
			il.Emit(OC.Add);
			il.Emit(OC.Conv_I);
			//il.EmitWriteLine(mod, "B");

			//value
			il.Emit(OC.Ldarg_2);
			//il.EmitWriteLine(mod, "B_0");

			//store ref.
			il.Emit(OC.Stind_Ref);
			//il.EmitWriteLine(mod, "c");

			il.Emit(OC.Nop);
			il.Emit(OC.Ret);

			//type.Methods.Add(method);
		}

		static void Emit_GetObjectAtOffset_IL(TypeDefinition type)
		{
			var mod = type.Module;
			var methodName = $"GetObjectAtOffset";
			var method = type.Methods.Where(m => m.Name == methodName).SingleOrDefault();
			if (method != null)
			{
				WriteLine("Method " + methodName + " stub found. Replacing Impl...");
				method.Body.Instructions.Clear();
			}
			else
				throw new Exception(methodName + ": Stub method not defined!");

			//var method = new MethodDefinition(methodName, MethodAttributes.Static | MethodAttributes.Public, mod.ImportReference(typeof(object)));
			//method.Parameters.Add(new ParameterDefinition(mod.ImportReference(typeof(object)))); //target
			//method.Parameters.Add(new ParameterDefinition(mod.ImportReference(typeof(IntPtr)))); //offset

			method.Body.Variables.Add(new VariableDefinition(mod.ImportReference(typeof(object))));
			method.Body.Variables.Add(new VariableDefinition(new PinnedType(mod.ImportReference(typeof(object)))));

			var il = method.Body.GetILProcessor();

			//pin
			il.Emit(OC.Ldarg_0);
			il.Emit(OC.Stloc_1);

			//ret value
			il.Emit(OC.Ldloca, 0);

			il.Emit(OC.Ldarg_0);//+
			il.Emit(OC.Conv_I);
			il.Emit(OC.Ldarg_1);
			il.Emit(OC.Add);

			il.Emit(OC.Ldind_I); //get the ref

			il.Emit(OC.Stind_Ref);
			il.Emit(OC.Ldloc_0);
			il.Emit(OC.Ret);

			//type.Methods.Add(method);


		}

		static void Emit_NewObj_IL(TypeDefinition type)
		{
			var mod = type.Module;
			var methodName = $"__UnsafeSerialization_NewObj";
			if (type.Methods.Where(m => m.Name == methodName).Count() > 0)
				return;

			var method = new MethodDefinition(methodName, MethodAttributes.Static | MethodAttributes.Public, mod.TypeSystem.Object);
			var il = method.Body.GetILProcessor();

			if (!type.IsValueType)
			{
				var ctor = AddDefaultCtor(type);

				//il.Body.Variables.Add(new VariableDefinition(type));

				il.Emit(OC.Newobj, ctor);
				il.Emit(OC.Ret);
			}
			else
			{
				method.Body.Variables.Add(new VariableDefinition(type));
				method.Body.Variables.Add(new VariableDefinition(mod.TypeSystem.Object));

				//IL_0000: nop
				//IL_0001: ldloca.s 0
				//IL_0003: initobj YingDev.UnsafeSerializationPostProcessor.Program / Point
				//IL_0009: ldloc.0
				//IL_000a: box YingDev.UnsafeSerializationPostProcessor.Program / Point
				//IL_000f: stloc.1
				//IL_0010: br.s IL_0012

				//IL_0012: ldloc.1
				//IL_0013: ret

				il.Emit(OC.Ldloca, 0);
				il.Emit(OC.Initobj, type);
				il.Emit(OC.Ldloc_0);
				il.Emit(OC.Box, type);
				il.Emit(OC.Stloc_1);

				il.Emit(OC.Ldloc_1);
				il.Emit(OC.Ret);
			}

			type.Methods.Add(method);
		}

		static MethodDefinition AddDefaultCtor(TypeDefinition type)
		{
			var mod = type.Module;
			var ctor = type.GetConstructors().Where(c => c.Parameters.Count == 0).FirstOrDefault();
			if (ctor == null)
			{
				var baseType = type.BaseType.Resolve();
				if (baseType == null)
					throw new Exception("Unable to add default ctor for type " + type.FullName);
				var baseCtor = mod.ImportReference(AddDefaultCtor(baseType));

				ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, type.Module.TypeSystem.Void);
				ctor.Body.Instructions.Add(Instruction.Create(OC.Ldarg_0));
				ctor.Body.Instructions.Add(Instruction.Create(OC.Call, baseCtor));
				ctor.Body.Instructions.Add(Instruction.Create(OC.Ret));
				type.Methods.Add(ctor);
			}
			return ctor;
		}

		static void EmitWriteLine(this ILProcessor il, ModuleDefinition module, string msg)
		{
			var console_tdef = module.ImportReference(typeof(Console)).Resolve();
			var string_tName = module.ImportReference(typeof(string)).FullName;
			var writeLine = module.ImportReference(console_tdef.Methods.Where(m => m.Name == nameof(Console.WriteLine) && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == string_tName).Single());

			il.Emit(OC.Ldstr, msg);
			il.Emit(OC.Call, writeLine);
		}

		static IEnumerable<FieldDefinition> GetInstFields(TypeDefinition type)
		{
			var temp = type;
			while (true)
			{
				foreach (var f in temp.Fields.Where(f => !f.IsStatic))
					yield return f;

				if (!temp.IsValueType && temp.BaseType != null)
					temp = temp.BaseType.Resolve();
				else
					break;
			}
		}

		static MethodReference MakeGenericMethod(MethodReference method, GenericInstanceType declType, params TypeReference[] args)
		{
			var newMethod = new MethodReference(method.Name, method.ReturnType)
			{
				DeclaringType = declType,
				HasThis = method.HasThis,
				ExplicitThis = method.ExplicitThis,
				CallingConvention = method.CallingConvention,
			};

			foreach (var param in args)
			{
				var def = new ParameterDefinition(param);
				newMethod.Parameters.Add(def);
			}

			return newMethod;
		}

		static Instruction GetLdcI4(sbyte value)
		{
			if (value <= 8)
			{
				var field = typeof(OpCodes).GetField("Ldc_I4_" + value);
				return Instruction.Create((OpCode)field.GetValue(null));
			}

			return Instruction.Create(OpCodes.Ldc_I4_S, value);
		}
	}
}

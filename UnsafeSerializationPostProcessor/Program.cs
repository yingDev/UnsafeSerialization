﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YingDev.UnsafeSerialization;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using OC = Mono.Cecil.Cil.OpCodes;
using static System.Console;

namespace YingDev.UnsafeSerializationPostProcessor
{
	static class Program
	{
		const string GENERATED_METHOD_NAME = UnsafeSerializeAttribute.METHOD_GET_FIELD_OFFSETS_NAME;
		static readonly string MARKER_ATTR = typeof(UnsafeSerializeAttribute).FullName;

		static void Main(string[] args)
		{
			var file = ValidateArgs(args);
			var dll = AssemblyDefinition.ReadAssembly(file);

			var assemblyModified = false;

			foreach (var mod in dll.Modules)
			{
				var markedTypes = mod.GetAllTypes()
								  .Where(t => null != t.CustomAttributes.Where(a => a.AttributeType.FullName == MARKER_ATTR).SingleOrDefault());
				foreach (var type in markedTypes)
				{
					WriteLine($"Processing: {type.FullName}");
					AddGeneratedMethod(type);
					assemblyModified = true;
				}
			}

			if (assemblyModified)
			{
				dll.Write(file);
				WriteLine($"File has been processed.");
			}
			else
				WriteLine($"No class/struct with attribute [{MARKER_ATTR}] found!");
		}

		static string ValidateArgs(string[] args)
		{
			if (args.Length < 1)
			{
				WriteLine("Usage: UnsafeSerializationPostProcessor file");
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

		static void AddGeneratedMethod(TypeDefinition type)
		{
			var mod = type.Module;
			var targetMethod = type.Methods.FirstOrDefault(m => m.Name == GENERATED_METHOD_NAME);
			if (targetMethod != null)
				type.Methods.Remove(targetMethod);

			var string_t = mod.Import(typeof(string));
			var ulong_t = mod.Import(typeof(ulong));
			var dict_t = mod.Import(mod.Import(typeof(Dictionary<,>)).Resolve().MakeGenericInstanceType(string_t, ulong_t));

			var method = new MethodDefinition(GENERATED_METHOD_NAME, MethodAttributes.Public | MethodAttributes.Static, dict_t);
			method.Parameters.Add(new ParameterDefinition("inst", ParameterAttributes.None, mod.Import(typeof(object))));

			Emit_GetFieldOffsets_IL(type, method);
			type.Methods.Add(method);

            Emit_Add_SetObject_Method_IL(type);
            Emit_SetObjectAtOffset_IL(type);

        }

		static void Emit_GetFieldOffsets_IL(TypeDefinition type, MethodDefinition method)
		{
			var mod = type.Module;
			var il = method.Body.GetILProcessor();
			var body = method.Body;

			var ulong_t = mod.Import(typeof(ulong));
			var int_t = mod.Import(typeof(int));
			var string_t = mod.Import(typeof(string));

			var dict_tdef = mod.Import(typeof(Dictionary<,>)).Resolve();
			var dictInst_t = (GenericInstanceType)mod.Import(dict_tdef.MakeGenericInstanceType(string_t, ulong_t));

			var dictCtor = mod.Import(dict_tdef.GetConstructors().Where(m => m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == int_t.Name).Single());
			var dictSetter = mod.Import(dict_tdef.Methods.Where(m => m.Name == "set_Item").Single());

			dictCtor = mod.Import(MakeGenericMethod(dictCtor, dictInst_t, int_t));
			dictSetter = mod.Import(MakeGenericMethod(dictSetter, dictInst_t, dict_tdef.GenericParameters[0], dict_tdef.GenericParameters[1]));

			var fields = GetInstFields(type).ToArray();

			body.InitLocals = true;
			body.Variables.Add(new VariableDefinition(dictInst_t));
			body.Variables.Add(new VariableDefinition(ulong_t));
			body.Variables.Add(new VariableDefinition(new PinnedType(new PointerType(mod.Import(typeof(byte))))));

			//il.EmitWriteLine(mod, "A");

			il.Append(GetLdcI4((sbyte)fields.Length));
			il.Emit(OC.Newobj, dictCtor);
			il.Emit(OC.Stloc_0);

			//il.EmitWriteLine(mod, "B");

			//pin
			il.Emit(OC.Ldarg_0);
			il.Emit(OC.Stloc_2);

			il.Emit(OC.Ldloc_2);
			il.Emit(OC.Conv_U8);
			il.Emit(OC.Stloc_1);

			//il.EmitWriteLine(mod, "C");

			for (var i = 0; i < fields.Length; i++)
			{
				var f = fields[i];

				il.Emit(OC.Ldloc_0);
				il.Emit(OC.Ldstr, f.DeclaringType.Name + "::" + f.Name);

				il.Emit(OC.Ldarg_0);
				il.Emit(OC.Castclass, f.DeclaringType);
				il.Emit(OC.Ldflda, f);
				il.Emit(OC.Conv_U8);

				il.Emit(OC.Ldloc_1);
				il.Emit(OC.Sub);

				il.Emit(OC.Callvirt, dictSetter);
			}
			//il.EmitWriteLine(mod, "D");

			il.Emit(OC.Ldloc_0);
			il.Emit(OC.Ret);
		}

        static void Emit_Add_SetObject_Method_IL(TypeDefinition type)
        {
            var fields = GetInstFields(type).ToArray();
            foreach(var f in fields)
            {
                if (f.FieldType.IsValueType)
                    continue;

                var methodName = $"__UnsafeSerialization_SetObject_{f.Name}";
                if (f.DeclaringType.GetMethods().Where(m => m.Name == methodName).SingleOrDefault() != null)
                    continue;

                var setMethod = new MethodDefinition(methodName, MethodAttributes.Static | MethodAttributes.Public, type.Module.Import(typeof(void)));
                setMethod.Parameters.Add(new ParameterDefinition(type.Module.Import(typeof(object)))); //target
                setMethod.Parameters.Add(new ParameterDefinition(type.Module.Import(typeof(object)))); //value

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
            var methodName = $"__UnsafeSerialization_SetObjectAtOffset";

            var setMethod = new MethodDefinition(methodName, MethodAttributes.Static | MethodAttributes.Public, type.Module.Import(typeof(void)));
            setMethod.Parameters.Add(new ParameterDefinition(type.Module.Import(typeof(object)))); //target
            setMethod.Parameters.Add(new ParameterDefinition(type.Module.Import(typeof(IntPtr)))); //offset
            setMethod.Parameters.Add(new ParameterDefinition(type.Module.Import(typeof(object)))); //value

            setMethod.Body.Variables.Add(new VariableDefinition(type.Module.Import(typeof(object))));
            setMethod.Body.Variables.Add(new VariableDefinition(new PinnedType(new PointerType(mod.Import(typeof(byte))))));


            var il = setMethod.Body.GetILProcessor();
            var label0 = il.Create(OpCodes.Nop);
            //pin
            il.Emit(OC.Ldarg_0);
            il.Emit(OC.Stloc_1);
            //il.EmitWriteLine(mod, "A");
            
            //addr
            il.Emit(OC.Ldarg_0);
            // il.Emit(OC.Conv_I);

            //il.Emit(OC.Brfalse, label0.Offset);

            //label: target != null
            il.Emit(OC.Ldarg_1);
            il.Emit(OC.Add);
            il.Emit(OC.Conv_I);
            //il.EmitWriteLine(mod, "B");

            il.Append(label0);

            //value
            il.Emit(OC.Ldarg_2);
            //il.EmitWriteLine(mod, "B_0");

            //store ref.
            il.Emit(OC.Stind_Ref);
            //il.EmitWriteLine(mod, "c");

            il.Emit(OC.Nop);
            il.Emit(OC.Ret);

            type.Methods.Add(setMethod);


        }

        static void EmitWriteLine(this ILProcessor il, ModuleDefinition module, string msg)
		{
			var console_tdef = module.Import(typeof(Console)).Resolve();
			var string_tName = module.Import(typeof(string)).FullName;
			var writeLine = module.Import(console_tdef.Methods.Where(m => m.Name == nameof(Console.WriteLine) && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == string_tName).Single());

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

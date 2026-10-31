using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace TUFReplayRenderer.Replay;

// A getter patched after Unity has JIT-compiled its callers can remain inlined
// with the original wall clock. Rewrite the callers to a clock-adjusted copy of
// small clock accessors/converters instead. No overlay names or handlers are required.
internal static class OverlayClockAccessor
{
    private sealed class Instruction { internal int Offset; internal OpCode Code; internal object Operand; }
    private static readonly Dictionary<short, OpCode> codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);

    internal static MethodInfo Create(MethodInfo method, IReadOnlyDictionary<MemberInfo, MethodInfo> clocks)
    {
        try { return CreateAvailable(method, clocks); }
        catch (FileNotFoundException) { return null; }
        catch (TypeLoadException) { return null; }
    }

    private static MethodInfo CreateAvailable(MethodInfo method, IReadOnlyDictionary<MemberInfo, MethodInfo> clocks)
    {
        bool getter = method.IsSpecialName && method.Name.StartsWith("get_", StringComparison.Ordinal);
        bool inline = (method.GetMethodImplementationFlags() & MethodImplAttributes.AggressiveInlining) != 0;
        if ((!getter && !inline) || method.IsVirtual || method.DeclaringType.IsValueType
            || method.ContainsGenericParameters || method.GetParameters().Any(p => !ClockValue(p.ParameterType))
            || !ClockValue(method.ReturnType)) return null;
        MethodBody body = method.GetMethodBody();
        if (body == null || body.ExceptionHandlingClauses.Count != 0 || body.GetILAsByteArray().Length > 128
            || body.LocalVariables.Any(l => !ClockValue(l.LocalType))) return null;
        var instructions = Read(method, body.GetILAsByteArray());
        if (instructions == null || !instructions.Any(i => i.Operand is MemberInfo m && clocks.ContainsKey(m))) return null;
        // Converters can retain their own timestamp offset, but cannot call input
        // handlers, schedulers or arbitrary functions. Getter copies stay read-only.
        foreach (var instruction in instructions)
        {
            OpCode op = instruction.Code;
            if (instruction.Operand is FieldInfo field)
            {
                if (field.DeclaringType != method.DeclaringType
                    || (field.FieldType != typeof(Stopwatch) && !ClockValue(field.FieldType) && field.FieldType != typeof(bool))) return null;
                if (op != OpCodes.Ldsfld && op != OpCodes.Ldfld && !(inline && !getter && (op == OpCodes.Stfld || op == OpCodes.Stsfld))) return null;
            }
            else if (instruction.Operand is MethodInfo called)
            {
                if (!clocks.ContainsKey(called) && !(called.DeclaringType == typeof(TimeSpan) && called.IsSpecialName && called.Name.StartsWith("get_", StringComparison.Ordinal))) return null;
            }
            else if (op.FlowControl == FlowControl.Call || op == OpCodes.Stsfld || op == OpCodes.Stfld
                || op == OpCodes.Ldfld || op == OpCodes.Ldflda || op == OpCodes.Ldsflda
                || op == OpCodes.Throw || op == OpCodes.Rethrow || op == OpCodes.Newarr
                || op == OpCodes.Stobj || op == OpCodes.Cpobj || op == OpCodes.Cpblk || op == OpCodes.Initblk
                || op.Name.StartsWith("stind", StringComparison.Ordinal) || op.Name.StartsWith("stelem", StringComparison.Ordinal)) return null;
        }
        Type[] arguments = method.GetParameters().Select(p => p.ParameterType).ToArray();
        if (!method.IsStatic) arguments = new[] { method.DeclaringType }.Concat(arguments).ToArray();
        var copy = new DynamicMethod("ReplayClock_" + method.MetadataToken, method.ReturnType, arguments, method.Module, true) { InitLocals = body.InitLocals };
        ILGenerator il = copy.GetILGenerator();
        foreach (var local in body.LocalVariables) il.DeclareLocal(local.LocalType, local.IsPinned);
        var labels = instructions.ToDictionary(i => i.Offset, _ => il.DefineLabel());
        foreach (var instruction in instructions)
        {
            il.MarkLabel(labels[instruction.Offset]);
            OpCode op = instruction.Code; object operand = instruction.Operand;
            if (operand is MemberInfo member && clocks.TryGetValue(member, out var replacement)) { op = OpCodes.Call; operand = replacement; }
            if (op.OperandType == OperandType.ShortInlineBrTarget || op.OperandType == OperandType.InlineBrTarget) il.Emit(op, labels[(int)operand]);
            else if (op.OperandType == OperandType.InlineSwitch) il.Emit(op, ((int[])operand).Select(offset => labels[offset]).ToArray());
            else if (operand == null) il.Emit(op);
            else if (operand is MethodInfo target) il.Emit(op, target);
            else if (operand is FieldInfo targetField) il.Emit(op, targetField);
            else if (operand is byte variable) il.Emit(op, variable);
            else if (operand is short variableLong) il.Emit(op, variableLong);
            else if (operand is sbyte small) il.Emit(op, small);
            else if (operand is int number) il.Emit(op, number);
            else if (operand is long wide) il.Emit(op, wide);
            else if (operand is float single) il.Emit(op, single);
            else if (operand is double precision) il.Emit(op, precision);
            else throw new InvalidOperationException("Unsupported clock accessor operand.");
        }
        return copy;
    }

    private static bool ClockValue(Type type) => type == typeof(long) || type == typeof(int) || type == typeof(float)
        || type == typeof(double) || type == typeof(TimeSpan) || type == typeof(DateTime);

    private static List<Instruction> Read(MethodInfo method, byte[] bytes)
    {
        var result = new List<Instruction>();
        int offset = 0;
        while (offset < bytes.Length)
        {
            int start = offset; short value = bytes[offset++];
            if (value == 0xfe) value = unchecked((short)(0xfe00 | bytes[offset++]));
            OpCode op = codes[value]; object operand;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: operand = null; break;
                case OperandType.ShortInlineVar: operand = bytes[offset++]; break;
                case OperandType.InlineVar: operand = (short)BitConverter.ToUInt16(bytes, offset); offset += 2; break;
                case OperandType.ShortInlineI: operand = (sbyte)bytes[offset++]; break;
                case OperandType.InlineI: operand = BitConverter.ToInt32(bytes, offset); offset += 4; break;
                case OperandType.InlineI8: operand = BitConverter.ToInt64(bytes, offset); offset += 8; break;
                case OperandType.ShortInlineR: operand = BitConverter.ToSingle(bytes, offset); offset += 4; break;
                case OperandType.InlineR: operand = BitConverter.ToDouble(bytes, offset); offset += 8; break;
                case OperandType.ShortInlineBrTarget: int distance = (sbyte)bytes[offset++]; operand = offset + distance; break;
                case OperandType.InlineBrTarget: int far = BitConverter.ToInt32(bytes, offset); offset += 4; operand = offset + far; break;
                case OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(bytes, offset); offset += 4;
                    int end = offset + count * 4; var targets = new int[count];
                    for (int index = 0; index < count; index++, offset += 4) targets[index] = end + BitConverter.ToInt32(bytes, offset);
                    operand = targets; break;
                case OperandType.InlineMethod:
                case OperandType.InlineField:
                    operand = method.Module.ResolveMember(BitConverter.ToInt32(bytes, offset)); offset += 4; break;
                default: return null;
            }
            result.Add(new Instruction { Offset = start, Code = op, Operand = operand });
        }
        return result;
    }
}

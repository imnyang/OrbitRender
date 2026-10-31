using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

namespace TUFReplayRenderer.Replay;

internal static class ManagedInstructionReader
{
    private static readonly Dictionary<short, OpCode> Codes = BuildCodes();
    private static Dictionary<short, OpCode> BuildCodes()
    {
        var codes = new Dictionary<short, OpCode>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.FieldType == typeof(OpCode)) { var op = (OpCode)field.GetValue(null); codes[op.Value] = op; }
        return codes;
    }
    internal static IEnumerable<MemberInfo> Members(MethodBase method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null) yield break;
        int offset = 0;
        while (offset < il.Length)
        {
            short value = il[offset++];
            if (value == 0xfe) value = unchecked((short)(0xfe00 | il[offset++]));
            OpCode op = Codes[value];
            int size = op.OperandType switch {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + BitConverter.ToInt32(il, offset) * 4),
                _ => 4
            };
            if (size > il.Length - offset) throw new BadImageFormatException("Incomplete IL operand.");
            if (op.OperandType == OperandType.InlineMethod || op.OperandType == OperandType.InlineField)
            {
                MemberInfo member = null;
                try { member = method.Module.ResolveMember(BitConverter.ToInt32(il, offset),
                    method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null); }
                catch (ArgumentException) { } catch (BadImageFormatException) { }
                // A guarded optional integration can refer to an uninstalled DLL.
                // It is not a standard input/clock member we can replace.
                catch (FileNotFoundException) { } catch (TypeLoadException) { }
                if (member != null) yield return member;
            }
            offset += size;
        }
    }
}

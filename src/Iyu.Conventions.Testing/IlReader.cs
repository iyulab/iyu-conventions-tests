using System.Reflection;
using System.Reflection.Emit;

namespace Iyu.Conventions.Testing;

/// <summary>One decoded IL instruction: its offset, opcode and raw operand (token or immediate).</summary>
internal readonly record struct IlInstruction(int Offset, OpCode OpCode, long Operand);

/// <summary>
/// A full IL decoder over a method body, driven by the runtime's own <see cref="OpCodes"/> table so every
/// operand is skipped at its real width. Byte-pattern scanning is enough to find call targets, but reading
/// the order of instructions (which literal feeds which constructor) needs real instruction boundaries.
/// </summary>
internal static class IlReader
{
    private static readonly OpCode[] OneByte = new OpCode[0x100];
    private static readonly OpCode[] TwoByte = new OpCode[0x100];

    static IlReader()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode code)
            {
                continue;
            }

            var value = (ushort)code.Value;
            if (code.Size == 1)
            {
                OneByte[value] = code;
            }
            else
            {
                TwoByte[value & 0xFF] = code;
            }
        }
    }

    public static IReadOnlyList<IlInstruction> Read(MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception)
        {
            return [];
        }

        if (il is null)
        {
            return [];
        }

        var result = new List<IlInstruction>();
        var i = 0;
        while (i < il.Length)
        {
            var offset = i;
            OpCode code;
            if (il[i] == 0xFE)
            {
                if (i + 1 >= il.Length)
                {
                    break;
                }

                code = TwoByte[il[i + 1]];
                i += 2;
            }
            else
            {
                code = OneByte[il[i]];
                i += 1;
            }

            long operand = 0;
            switch (code.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    operand = il[i];
                    i += 1;
                    break;
                case OperandType.InlineVar:
                    operand = BitConverter.ToUInt16(il, i);
                    i += 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    operand = BitConverter.ToInt64(il, i);
                    i += 8;
                    break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(il, i);
                    i += 4 + (count * 4);
                    break;
                default:
                    // InlineBrTarget, InlineField, InlineI, InlineMethod, InlineSig, InlineString,
                    // InlineTok, InlineType, ShortInlineR: four bytes.
                    operand = BitConverter.ToInt32(il, i);
                    i += 4;
                    break;
            }

            result.Add(new IlInstruction(offset, code, operand));
        }

        return result;
    }
}

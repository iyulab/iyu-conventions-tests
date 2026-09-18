using System.Reflection;

namespace Iyu.Conventions.Testing;

/// <summary>Reflection and raw-IL helpers shared by the scanners. No dependency beyond the runtime.</summary>
internal static class IlInspection
{
    private const byte Call = 0x28;
    private const byte CallVirt = 0x6F;
    private const byte NewObj = 0x73;

    /// <summary>
    /// The types of <paramref name="assembly"/> that load. A dependency that cannot be resolved makes
    /// <see cref="Assembly.GetTypes"/> throw for the whole assembly; failing the scan there would push a
    /// caller to drop the assembly from the list, which silently narrows what is checked.
    /// </summary>
    public static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    /// <summary>
    /// Every method a body calls: <c>call</c> / <c>callvirt</c> followed by a MethodDef or MemberRef token.
    /// A byte that merely looks like the opcode inside another operand yields a token that resolves to
    /// something else, or to nothing; callers match exact methods only.
    /// </summary>
    public static IEnumerable<MethodBase> Calls(MethodBase method, Module module)
        => Targets(method, module, Call, CallVirt);

    /// <summary>Whether <paramref name="type"/> is <paramref name="container"/> or nested inside it.</summary>
    public static bool IsWithin(Type type, Type container)
    {
        for (var t = type; t is not null; t = t.DeclaringType)
        {
            if (t == container)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A method that exists to produce another instance of the type it lives on: the compiler's record copy
    /// method, or a method returning that type whose body creates one (a constructor call, the record copy
    /// method, or <see cref="object.MemberwiseClone"/>). Reading a property in order to carry it into a new
    /// instance is not consuming it.
    /// </summary>
    /// <remarks>
    /// Judged by the body, not the name or the signature alone. A <c>WithRetries</c> that applies the option
    /// is a real read; a fluent <c>Validate()</c> returning <c>this</c> has the same signature as a copy but
    /// creates nothing, so its reads count.
    /// </remarks>
    public static bool IsCopy(MethodBase method)
    {
        if (method.Name == "<Clone>$")
        {
            return true;
        }

        if (method is not MethodInfo { ReturnType: { } returned } || returned != method.DeclaringType)
        {
            return false;
        }

        var declaring = method.DeclaringType!;
        foreach (var created in Targets(method, method.Module, NewObj))
        {
            if (created is ConstructorInfo && created.DeclaringType == declaring)
            {
                return true;
            }
        }

        foreach (var called in Calls(method, method.Module))
        {
            if ((called.Name == "<Clone>$" && called.DeclaringType == declaring)
                || (called.Name == nameof(MemberwiseClone) && called.DeclaringType == typeof(object)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<MethodBase> Targets(MethodBase method, Module module, params byte[] opcodes)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception)
        {
            yield break;
        }

        if (il is null)
        {
            yield break;
        }

        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (Array.IndexOf(opcodes, il[i]) < 0)
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, i + 1);
            if ((token >> 24) is not (0x06 or 0x0A))
            {
                continue;
            }

            MethodBase? target;
            try
            {
                target = module.ResolveMethod(token);
            }
            catch (Exception)
            {
                continue;
            }

            if (target is not null)
            {
                yield return target;
            }
        }
    }
}

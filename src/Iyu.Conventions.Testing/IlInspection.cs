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
        => Targets(method, module, Call, CallVirt).Select(site => site.Target);

    /// <summary>
    /// The calls of <see cref="Calls"/>, each paired with whether its result goes straight into the same-named
    /// property's setter on the same type (<c>callvirt get_X</c> immediately followed by <c>callvirt set_X</c>), which
    /// is how a copy of an options object is written: <c>new Options { X = source.X }</c>. Such a getter call carries
    /// the value into another instance; it does not consume it.
    /// </summary>
    public static IEnumerable<(MethodBase Target, bool CopiedIntoSameProperty)> CallsWithCopies(MethodBase method, Module module)
    {
        var sites = Targets(method, module, Call, CallVirt).ToList();
        var byOffset = sites.ToDictionary(site => site.Offset, site => site.Target);
        foreach (var (offset, target) in sites)
        {
            var copied = target.Name.StartsWith("get_", StringComparison.Ordinal)
                && byOffset.TryGetValue(offset + 5, out var next)
                && next.DeclaringType == target.DeclaringType
                && next.Name == "set_" + target.Name[4..];
            yield return (target, copied);
        }
    }

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
    /// <summary>
    /// Validation <em>of</em> options, as opposed to a feature whose job is validating something else: a <c>Validate…</c>
    /// method declared by the options type itself (<paramref name="optionsType"/>, the type the method sits in), or any
    /// method of a type implementing <c>IValidateOptions&lt;T&gt;</c>. A guardrail's <c>ValidateInputAsync</c> reading its own
    /// options is the feature and still counts.
    /// </summary>
    public static bool IsOptionsValidation(MethodBase method, Type? optionsType)
    {
        var declaring = method.DeclaringType;
        if (declaring is null)
        {
            return false;
        }

        if (declaring.GetInterfaces().Any(i => i.IsGenericType
                && i.GetGenericTypeDefinition().FullName == "Microsoft.Extensions.Options.IValidateOptions`1"))
        {
            return true;
        }

        if (optionsType is null || !IsWithin(declaring, optionsType))
        {
            return false;
        }

        var name = method.Name;
        var dot = name.LastIndexOf('.');   // explicit interface implementation: "System.….IValidatableObject.Validate"
        return (dot >= 0 ? name[(dot + 1)..] : name).StartsWith("Validate", StringComparison.Ordinal);
    }

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
        foreach (var (_, created) in Targets(method, method.Module, NewObj))
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

    private static IEnumerable<(int Offset, MethodBase Target)> Targets(MethodBase method, Module module, params byte[] opcodes)
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
                yield return (i, target);
            }
        }
    }
}

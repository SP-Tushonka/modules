using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace SPTushonka.Reflection.Il2Cpp;

/// <summary>
/// Calls a game method's own implementation on an object whose class overrides it.
/// </summary>
/// <remarks>
/// A mod class injected into il2cpp overrides game methods as ordinary C# overrides. Calling the interop wrapper from
/// outside that class, for example from a patch, goes through virtual dispatch and lands back in the override. C#
/// only allows <c>base.Method()</c> inside the derived class itself. The delegate built here calls the wrapper
/// non-virtually, which runs the game's il2cpp implementation for that exact type. Build it once and cache it.
/// </remarks>
public static class Il2CppBaseCall
{
    /// <summary>
    /// Builds a delegate that calls <paramref name="method"/> without virtual dispatch. The delegate takes the
    /// instance first, followed by the method's parameters.
    /// </summary>
    public static TDelegate CreateBaseCall<TDelegate>(this MethodInfo method) where TDelegate : Delegate
    {
        var types = new[] { method.DeclaringType }.Concat(method.GetParameters().Select(p => p.ParameterType)).ToArray();
        DynamicMethod caller = new(method.Name + "_NonVirtual", method.ReturnType, types, method.DeclaringType, true);
        var il = caller.GetILGenerator();
        for (var i = 0; i < types.Length; i++)
        {
            il.Emit(OpCodes.Ldarg, i);
        }

        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        return (TDelegate)caller.CreateDelegate(typeof(TDelegate));
    }
}

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;

namespace SPTushonka.Reflection.Il2Cpp;

/// <summary>
/// Finds interop wrapper methods by their native il2cpp name.
/// </summary>
/// <remarks>
/// Compiler generated methods such as lambdas and local functions get unstable wrapper names from the interop
/// generator, like <c>_Awake_b__22_2</c>, which change whenever the game is regenerated. The native name,
/// <c>&lt;Awake&gt;b__22_2</c>, is what the game itself was compiled with, so patch targets are matched on that.
/// </remarks>
public static class Il2CppMethods
{
    /// <summary>
    /// Returns the method declared on <paramref name="type"/> whose native il2cpp name is <paramref name="nativeName"/>.
    /// Throws <see cref="MissingMethodException"/> when there is none.
    /// </summary>
    public static MethodInfo ByNativeName(Type type, string nativeName)
    {
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (method.IsAbstract || method.IsGenericMethodDefinition)
            {
                continue;
            }

            var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
            if (field == null)
            {
                continue;
            }

            var pointer = (IntPtr)field.GetValue(null);
            if (pointer != IntPtr.Zero && Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(pointer)) == nativeName)
            {
                return method;
            }
        }

        throw new MissingMethodException(type.FullName, nativeName);
    }
}

using System;
using System.Runtime.InteropServices;

namespace SPTushonka.PrePatch.Loader;

// Calls the same exports Il2CppInterop uses to find classes, so a broken restore shows up in the
// log before Il2CppInterop hands out placeholder methods that crash inside il2cpp_runtime_invoke.
internal static unsafe class Il2CppProbe
{
    [DllImport("GameAssembly")]
    private static extern IntPtr il2cpp_domain_get();

    [DllImport("GameAssembly")]
    private static extern IntPtr* il2cpp_domain_get_assemblies(IntPtr domain, ref uint size);

    [DllImport("GameAssembly")]
    private static extern IntPtr il2cpp_assembly_get_image(IntPtr assembly);

    [DllImport("GameAssembly")]
    private static extern IntPtr il2cpp_image_get_name(IntPtr image);

    [DllImport("GameAssembly")]
    private static extern IntPtr il2cpp_class_get_methods(IntPtr klass, ref IntPtr iter);

    [DllImport("GameAssembly")]
    private static extern uint il2cpp_method_get_token(IntPtr method);

    [DllImport("GameAssembly")]
    private static extern IntPtr il2cpp_class_from_name(IntPtr image, [MarshalAs(UnmanagedType.LPStr)] string ns, [MarshalAs(UnmanagedType.LPStr)] string name);

    public static int AssemblyCount()
    {
        var domain = il2cpp_domain_get();
        if (domain == IntPtr.Zero)
        {
            return -1;
        }

        uint count = 0;
        il2cpp_domain_get_assemblies(domain, ref count);
        return (int)count;
    }

    // Same walk Il2CppInterop does in GetIl2CppMethodByToken
    public static IntPtr FindMethodByToken(IntPtr klass, int token, out int methods)
    {
        methods = 0;
        var iter = IntPtr.Zero;
        IntPtr method;
        while ((method = il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
        {
            methods++;
            if (il2cpp_method_get_token(method) == (uint)token)
            {
                return method;
            }
        }

        return IntPtr.Zero;
    }

    public static IntPtr FindClass(string imageName, string ns, string name)
    {
        var domain = il2cpp_domain_get();
        if (domain == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        uint count = 0;
        var assemblies = il2cpp_domain_get_assemblies(domain, ref count);
        for (uint i = 0; i < count; i++)
        {
            var image = il2cpp_assembly_get_image(assemblies[i]);
            if (image == IntPtr.Zero)
            {
                continue;
            }

            if (Marshal.PtrToStringAnsi(il2cpp_image_get_name(image)) == imageName)
            {
                return il2cpp_class_from_name(image, ns, name);
            }
        }

        return IntPtr.Zero;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using EFT.UI;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Reflection.Commands;

/// <summary>
///     Register methods tagged with <see cref="ConsoleCommandAttribute"/> with the game console. The game's
///     RegisterCommandGroup scans with il2cpp reflection, which never sees managed methods.
/// </summary>
public static class ConsoleCommandRegistry
{
    /// <summary>
    ///     Command groups waiting for the console
    /// </summary>
    private static readonly List<Type> _pending = [];

    /// <summary>
    ///     Command groups already registered, so a group added twice registers once
    /// </summary>
    private static readonly HashSet<Type> _registered = [];

    private static ConsoleInitPatch _patch;

    /// <summary>
    ///     Set once the game has created its console. ConsoleScreen's static constructor assigns Processor, so a
    ///     non-null Processor cannot show this.
    /// </summary>
    private static bool _consoleReady;

    /// <summary>
    ///     Hook console creation. SPTushonka.Core calls this at load, before any command group can miss it
    /// </summary>
    public static void Initialise()
    {
        if (_patch != null)
        {
            return;
        }

        _patch = new ConsoleInitPatch();
        _patch.Enable();
    }

    /// <summary>
    ///     Register every tagged public static method of <typeparamref name="T"/>, now or once the console exists
    /// </summary>
    /// <typeparam name="T">Class holding the commands</typeparam>
    public static void RegisterCommandGroup<T>()
    {
        RegisterCommandGroup(typeof(T));
    }

    /// <summary>
    ///     Register every tagged public static method of <paramref name="type"/>, now or once the console exists
    /// </summary>
    /// <param name="type">Class holding the commands</param>
    public static void RegisterCommandGroup(Type type)
    {
        _pending.Add(type);
        if (_consoleReady)
        {
            Flush();
        }
    }

    /// <summary>
    ///     Register the pending command groups
    /// </summary>
    private static void Flush()
    {
        foreach (var type in _pending)
        {
            if (!_registered.Add(type))
            {
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var command = method.GetCustomAttribute<ConsoleCommandAttribute>();
                if (command == null)
                {
                    continue;
                }

                Register(command.Name, command.Description, method);
                foreach (var alias in command.Aliases)
                {
                    Register(alias, command.Description, method);
                }
            }
        }

        _pending.Clear();
    }

    /// <summary>
    ///     Register one method under one name. A method with parameters goes in as an anonymous command so the console
    ///     parses its arguments.
    /// </summary>
    /// <param name="name">Name typed in the console</param>
    /// <param name="description">Text shown in the console's help</param>
    /// <param name="method">Command method</param>
    private static void Register(string name, string description, MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            ConsoleScreen.Processor.RegisterCommand(name, new Action(() => Invoke(method, [])), description);
            return;
        }

        Il2CppReferenceArray<Il2CppSystem.Type> types = new(parameters.Select(p => Il2CppType.From(p.ParameterType)).ToArray());
        Il2CppSystem.Action<Il2CppReferenceArray<Il2CppSystem.Object>> call =
            new Action<Il2CppReferenceArray<Il2CppSystem.Object>>(args => Invoke(method, Convert(parameters, args)));
        ConsoleScreen.Processor.RegisterAnonymousCommand(name, types, call, description);
    }

    /// <summary>
    ///     Run a command and report its exception in the console
    /// </summary>
    /// <param name="method">Command method</param>
    /// <param name="args">Arguments for the method</param>
    private static void Invoke(MethodInfo method, object[] args)
    {
        try
        {
            method.Invoke(null, args);
        }
        catch (TargetInvocationException ex)
        {
            ConsoleScreen.LogError($"{method.Name}: {ex.InnerException?.Message}");
        }
    }

    /// <summary>
    ///     Convert the console's boxed il2cpp arguments to the method's parameter types
    /// </summary>
    /// <param name="parameters">Method parameters</param>
    /// <param name="args">Arguments the console parsed</param>
    /// <returns>Managed arguments, with defaults for any left out</returns>
    private static object[] Convert(ParameterInfo[] parameters, Il2CppReferenceArray<Il2CppSystem.Object> args)
    {
        var values = new object[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var arg = args != null && i < args.Length ? args[i] : null;
            values[i] = arg == null ? DefaultFor(parameters[i]) : Unbox(arg, parameters[i].ParameterType);
        }

        return values;
    }

    /// <summary>
    ///     Get the value for an argument left out
    /// </summary>
    /// <param name="parameter">Parameter left out</param>
    /// <returns>Default from <see cref="ConsoleArgumentAttribute"/>, the C# default value or the type's default</returns>
    private static object DefaultFor(ParameterInfo parameter)
    {
        var argument = parameter.GetCustomAttribute<ConsoleArgumentAttribute>();
        if (argument != null)
        {
            return argument.DefaultValue;
        }

        if (parameter.HasDefaultValue)
        {
            return parameter.DefaultValue;
        }

        return parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
    }

    /// <summary>
    ///     Read a string, bool, enum or other value type out of an il2cpp object
    /// </summary>
    /// <param name="arg">Boxed il2cpp argument</param>
    /// <param name="type">Parameter type to read it as</param>
    /// <returns>Managed value</returns>
    private static object Unbox(Il2CppSystem.Object arg, Type type)
    {
        if (type == typeof(string))
        {
            return IL2CPP.Il2CppStringToManaged(arg.Pointer);
        }

        var data = IL2CPP.il2cpp_object_unbox(arg.Pointer);
        if (type == typeof(bool))
        {
            return Marshal.ReadByte(data) != 0;
        }

        if (type.IsEnum)
        {
            return Enum.ToObject(type, Marshal.PtrToStructure(data, Enum.GetUnderlyingType(type)));
        }

        return Marshal.PtrToStructure(data, type);
    }

    /// <summary>
    ///     Register the pending command groups once the game creates its console
    /// </summary>
    private sealed class ConsoleInitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ConsoleScreen), nameof(ConsoleScreen.InitConsole));
        }

        [PatchPostfix]
        public static void Postfix()
        {
            _consoleReady = true;
            Flush();
        }
    }
}

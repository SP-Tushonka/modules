using System;

namespace SPTushonka.Reflection.Commands;

/// <summary>
///     Mark a public static method as a console command for <see cref="ConsoleCommandRegistry"/>. The game's own
///     attribute derives from Il2CppSystem.Attribute, so C# cannot put it on a method.
/// </summary>
/// <remarks>
///     The constructor takes the same arguments as the game's ConsoleCommandAttribute, so a command written against it
///     only needs its using changed. The execution contexts are accepted but not used.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ConsoleCommandAttribute(
    string name,
    string executionContext = "",
    string awaitExecutionContext = null,
    string description = "",
    string[] aliases = null
) : Attribute
{
    /// <summary>
    ///     Name typed in the console
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    ///     Text shown in the console's help
    /// </summary>
    public string Description { get; } = description;

    /// <summary>
    ///     Extra names the command answers to
    /// </summary>
    public string[] Aliases { get; } = aliases ?? [];
}

/// <summary>
///     Default value and help text for a console command parameter
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ConsoleArgumentAttribute(object defaultValue, string description = "") : Attribute
{
    /// <summary>
    ///     Value used when the argument is left out
    /// </summary>
    public object DefaultValue { get; } = defaultValue;

    /// <summary>
    ///     Text shown in the console's help
    /// </summary>
    public string Description { get; } = description;
}

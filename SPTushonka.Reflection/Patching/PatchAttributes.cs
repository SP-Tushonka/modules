using System;

namespace SPTushonka.Reflection.Patching;

[AttributeUsage(AttributeTargets.Method)]
public class PatchPrefixAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public class PatchPostfixAttribute : Attribute;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace SPTushonka.Reflection.Patching;

public abstract class ModulePatch
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>
    ///     Method this patch targets
    /// </summary>
    public MethodBase TargetMethod { get; private set; }

    /// <summary>
    ///     Is this patch active?
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    ///     Is this patch managed by the PatchManager?
    /// </summary>
    public bool IsManaged { get; private set; }

    /// <summary>
    ///     The harmony Id assigned to this patch, usually the name of the patch class.
    /// </summary>
    public string HarmonyId
    {
        get { return _harmony?.Id ?? "Harmony Id is null for this patch"; }
    }

    protected static ManualLogSource Logger { get; private set; }

    private static readonly List<string> Missing = new();
    private static int _requested;
    private static int _reportedRequests;
    private static int _reportedMissing;

    private Harmony _harmony;

    private readonly List<HarmonyMethod> _prefixList;
    private readonly List<HarmonyMethod> _postfixList;
    private readonly List<HarmonyMethod> _finalizerList;

    protected ModulePatch()
        : this(null) { }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="name">Name</param>
    protected ModulePatch(string name = null)
    {
        Logger ??= BepInEx.Logging.Logger.CreateLogSource("SPTushonka");
        _harmony = new Harmony(name ?? GetType().Name);
        _prefixList = GetPatchMethods(typeof(PatchPrefixAttribute));
        _postfixList = GetPatchMethods(typeof(PatchPostfixAttribute));
        _finalizerList = GetPatchMethods(typeof(PatchFinalizerAttribute));

        if (_prefixList.Count == 0 && _postfixList.Count == 0 && _finalizerList.Count == 0)
        {
            throw new PatchException($"{GetType().Name}: At least one of the patch methods must be specified");
        }
    }

    /// <summary>
    /// Get original method
    /// </summary>
    /// <returns>Method</returns>
    protected abstract MethodBase GetTargetMethod();

    /// <summary>
    /// Get HarmonyMethod from string
    /// </summary>
    /// <param name="attributeType">Attribute type</param>
    /// <returns>Method</returns>
    private List<HarmonyMethod> GetPatchMethods(Type attributeType)
    {
        var methods = new List<HarmonyMethod>();

        foreach (var method in GetType().GetMethods(Flags))
        {
            if (method.GetCustomAttribute(attributeType) != null)
            {
                methods.Add(new HarmonyMethod(method));
            }
        }

        return methods;
    }

    /// <summary>
    /// Apply patch to target
    /// </summary>
    public void Enable()
    {
        var name = GetType().Name;
        _requested++;
        TargetMethod = GetTargetMethod();

        if (TargetMethod == null)
        {
            Missing.Add(name);
            throw new PatchException($"{name}: TargetMethod is null");
        }

        try
        {
            foreach (var prefix in _prefixList)
            {
                _harmony.Patch(TargetMethod, prefix: prefix);
            }

            foreach (var postfix in _postfixList)
            {
                _harmony.Patch(TargetMethod, postfix: postfix);
            }

            foreach (var finalizer in _finalizerList)
            {
                _harmony.Patch(TargetMethod, finalizer: finalizer);
            }

            Logger.LogInfo($"Enabled patch {name}");
            IsActive = true;
        }
        catch (Exception ex)
        {
            Missing.Add(name);
            Logger.LogError($"{name}: {ex}");
            throw new PatchException($"{name}:", ex);
        }
    }

    /// <summary>
    ///     Internal use only, called from the patch manager.
    /// </summary>
    /// <param name="harmony">Harmony instance of the patch manager</param>
    internal void Enable(Harmony harmony)
    {
        if (!ReferenceEquals(_harmony, harmony))
        {
            // Override the initial harmony instance with the PatchManagers instance
            _harmony = harmony;
        }

        IsManaged = true;
        Enable();
    }

    /// <summary>
    /// Remove applied patch from target
    /// </summary>
    public void Disable()
    {
        var name = GetType().Name;
        TargetMethod = GetTargetMethod();

        if (TargetMethod == null)
        {
            throw new PatchException($"{name}: TargetMethod is null");
        }

        try
        {
            _harmony.Unpatch(TargetMethod, HarmonyPatchType.All, _harmony.Id);
            Logger.LogInfo($"Disabled patch {name}");

            IsActive = false;
        }
        catch (Exception ex)
        {
            Logger.LogError($"{name}: {ex}");
            throw new PatchException($"{name}:", ex);
        }
    }

    /// <summary>
    ///     Internal use only, called from the patch manager.
    /// </summary>
    /// <param name="harmony">Harmony instance of the patch manager</param>
    internal void Disable(Harmony harmony)
    {
        //  Attempting to disable a patch that is not managed by the patch manager
        if (harmony is null || !ReferenceEquals(_harmony, harmony))
        {
            throw new PatchException(
                $"Patch: {GetType().Name} is attempting to be disabled internally while not managed by the patch manager."
            );
        }

        Disable();

        // This patch is no longer considered managed.
        IsManaged = false;
    }

    // A rename in a new client shows up as a handful of errors scattered through a very large
    // log; this is the one line that says how many patches did not take.
    public static void Summarise(string scope)
    {
        var requested = _requested - _reportedRequests;
        var missing = Missing.Skip(_reportedMissing).ToArray();
        _reportedRequests = _requested;
        _reportedMissing = Missing.Count;

        if (missing.Length == 0)
        {
            Logger.LogMessage($"{scope}: {requested} patch(es) applied");
            return;
        }

        Logger.LogWarning($"{scope}: {requested - missing.Length}/{requested} patch(es) applied - "
                          + $"not applied: {string.Join(", ", missing)}");
    }
}

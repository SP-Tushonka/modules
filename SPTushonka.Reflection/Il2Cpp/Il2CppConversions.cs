using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;

namespace SPTushonka.Reflection.Il2Cpp;

/// <summary>
/// Converts common values and collection containers between System and Il2CppSystem.
/// </summary>
/// <remarks>
/// Collection conversions create new containers and retain the original elements. They do not clone game objects.
/// Byte-segment conversions copy only the selected bytes. Calls that access IL2CPP require an attached thread.
/// </remarks>
public static class Il2CppConversions
{
    public static Il2CppSystem.Collections.Generic.List<T> ToIl2CppList<T>(this IEnumerable<T> source)
    {
        Il2CppSystem.Collections.Generic.List<T> list = new();
        foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }

    public static List<T> ToManagedList<T>(this Il2CppSystem.Collections.Generic.IEnumerable<T> source)
    {
        List<T> list = [];
        foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }

    public static Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue> ToIl2CppDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> source)
    {
        Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue> dictionary = new();
        foreach (var pair in source)
        {
            dictionary[pair.Key] = pair.Value;
        }

        return dictionary;
    }

    public static Dictionary<TKey, TValue> ToManagedDictionary<TKey, TValue>(this Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue> source)
    {
        Dictionary<TKey, TValue> dictionary = [];
        foreach (var pair in source)
        {
            dictionary[pair.Key] = pair.Value;
        }

        return dictionary;
    }

    public static DateTime ToManaged(this Il2CppSystem.DateTime value)
    {
        return new DateTime(value.Ticks, (DateTimeKind)value.Kind);
    }

    public static Il2CppSystem.DateTime ToIl2Cpp(this DateTime value)
    {
        return new Il2CppSystem.DateTime(value.Ticks, (Il2CppSystem.DateTimeKind)value.Kind);
    }

    public static TimeSpan ToManaged(this Il2CppSystem.TimeSpan value)
    {
        return new TimeSpan(value.Ticks);
    }

    public static Il2CppSystem.TimeSpan ToIl2Cpp(this TimeSpan value)
    {
        return new Il2CppSystem.TimeSpan(value.Ticks);
    }

    public static EFT.MongoID ToMongoId(this Il2CppSystem.Nullable<EFT.MongoID> value)
    {
        return value != null && value.HasValue ? value.Value : null;
    }

    public static Il2CppSystem.Nullable<EFT.MongoID> ToIl2CppNullable(this EFT.MongoID value)
    {
        return value is null ? new Il2CppSystem.Nullable<EFT.MongoID>() : new Il2CppSystem.Nullable<EFT.MongoID>(value);
    }

    public static Il2CppSystem.Nullable<Il2CppSystem.DateTime> ToIl2Cpp(this DateTime? value)
    {
        return value.HasValue ? new Il2CppSystem.Nullable<Il2CppSystem.DateTime>(value.Value.ToIl2Cpp()) : new Il2CppSystem.Nullable<Il2CppSystem.DateTime>();
    }

    public static Il2CppSystem.Nullable<Il2CppSystem.TimeSpan> ToIl2Cpp(this TimeSpan? value)
    {
        return value.HasValue ? new Il2CppSystem.Nullable<Il2CppSystem.TimeSpan>(value.Value.ToIl2Cpp()) : new Il2CppSystem.Nullable<Il2CppSystem.TimeSpan>();
    }

    public static TimeSpan? ToManaged(this Il2CppSystem.Nullable<Il2CppSystem.TimeSpan> value)
    {
        return value != null && value.HasValue ? value.Value.ToManaged() : null;
    }

    public static DateTime? ToManaged(this Il2CppSystem.Nullable<Il2CppSystem.DateTime> value)
    {
        return value != null && value.HasValue ? value.Value.ToManaged() : null;
    }

    /// <summary>
    /// Boxes an enum with its IL2CPP type metadata so game code can inspect its type and name.
    /// </summary>
    public static Il2CppSystem.Object BoxIl2Cpp<T>(this T value) where T : struct, Enum
    {
        return Il2CppSystem.Enum.ToObject(Il2CppType.From(typeof(T)), Convert.ToInt64(value));
    }

    /// <summary>
    /// Creates a one-element IL2CPP list containing the existing object wrapper.
    /// </summary>
    public static Il2CppSystem.Collections.Generic.IEnumerable<T> AsIl2CppEnumerable<T>(this T item) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        var list = new Il2CppSystem.Collections.Generic.List<T>(1);
        list.Add(item);
        return list;
    }

    /// <summary>
    /// Copies the selected bytes into a new managed array with a segment offset of zero.
    /// </summary>
    public static ArraySegment<byte> ToManaged(this Il2CppSystem.ArraySegment<byte> segment)
    {
        var bytes = new byte[segment.Count];
        var source = segment.Array;
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = source[segment.Offset + i];
        }
        return new ArraySegment<byte>(bytes);
    }

    /// <summary>
    /// Copies the selected bytes into a new IL2CPP array with a segment offset of zero.
    /// </summary>
    public static Il2CppSystem.ArraySegment<byte> ToIl2Cpp(this ArraySegment<byte> segment)
    {
        var bytes = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(segment.Count);
        for (var i = 0; i < segment.Count; i++)
        {
            bytes[i] = segment.Array[segment.Offset + i];
        }
        return new Il2CppSystem.ArraySegment<byte>(bytes, 0, segment.Count);
    }

    public static Il2CppSystem.Type ToIl2Cpp(this Type type)
    {
        return Il2CppType.From(type);
    }
}

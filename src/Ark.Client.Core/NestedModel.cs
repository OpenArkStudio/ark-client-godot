using System.Collections.ObjectModel;

namespace Ark.Client.Core;

// Local type names only. Their numbers are not a wire contract.
public enum NestedScalarType
{
    Boolean, Int32, UInt32, Int64, UInt64, Float, Double, Bytes, Guid
}

/// <summary>An explicitly typed, owning scalar. Byte arrays are copied on input and output.</summary>
public sealed class NestedScalar : IEquatable<NestedScalar>
{
    private readonly object _value;
    public NestedScalarType Type
    {
        get;
    }
    private NestedScalar(NestedScalarType type, object value)
    {
        Type = type;
        _value = value;
    }
    public static NestedScalar FromBool(bool value) => new(NestedScalarType.Boolean, value);
    public static NestedScalar FromInt32(int value) => new(NestedScalarType.Int32, value);
    public static NestedScalar FromUInt32(uint value) => new(NestedScalarType.UInt32, value);
    public static NestedScalar FromInt64(long value) => new(NestedScalarType.Int64, value);
    public static NestedScalar FromUInt64(ulong value) => new(NestedScalarType.UInt64, value);
    public static NestedScalar FromFloat(float value) => new(NestedScalarType.Float, value);
    public static NestedScalar FromDouble(double value) => new(NestedScalarType.Double, value);
    public static NestedScalar FromBytes(ReadOnlySpan<byte> value) => new(NestedScalarType.Bytes, value.ToArray());
    public static NestedScalar FromGuid(long value) => new(NestedScalarType.Guid, value);
    private T Read<T>(NestedScalarType expected) => Type == expected ? (T)_value : throw new InvalidOperationException("Wrong nested scalar type.");
    public bool AsBool() => Read<bool>(NestedScalarType.Boolean);
    public int AsInt32() => Read<int>(NestedScalarType.Int32);
    public uint AsUInt32() => Read<uint>(NestedScalarType.UInt32);
    public long AsInt64() => Read<long>(NestedScalarType.Int64);
    public ulong AsUInt64() => Read<ulong>(NestedScalarType.UInt64);
    public float AsFloat() => Read<float>(NestedScalarType.Float);
    public double AsDouble() => Read<double>(NestedScalarType.Double);
    public byte[] AsBytes() => Read<byte[]>(NestedScalarType.Bytes).ToArray();
    public int ByteLength => Read<byte[]>(NestedScalarType.Bytes).Length;
    public long AsGuid() => Read<long>(NestedScalarType.Guid);
    public bool Equals(NestedScalar? other) => other is not null && Type == other.Type &&
        (Type == NestedScalarType.Bytes ? ((byte[])_value).AsSpan().SequenceEqual((byte[])other._value) : _value.Equals(other._value));
    public override bool Equals(object? obj) => obj is NestedScalar other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Type);
        if (Type == NestedScalarType.Bytes)
        {
            foreach (byte item in (byte[])_value)
            {
                hash.Add(item);
            }
        }
        else
        {
            hash.Add(_value);
        }
        return hash.ToHashCode();
    }
}

internal static class NestedMaps
{
    public static IReadOnlyDictionary<TKey, TValue> Own<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? input) where TKey : notnull =>
        new ReadOnlyDictionary<TKey, TValue>(input is null ? new SortedDictionary<TKey, TValue>() : new SortedDictionary<TKey, TValue>(input.ToDictionary(pair => pair.Key, pair => pair.Value)));
    public static IReadOnlyDictionary<TKey, IReadOnlyDictionary<TInner, TValue>> OwnNested<TKey, TInner, TValue>(IReadOnlyDictionary<TKey, IReadOnlyDictionary<TInner, TValue>>? input)
        where TKey : notnull where TInner : notnull => Own(input?.ToDictionary(pair => pair.Key, pair => Own(pair.Value)));
    public static bool Same<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> left, IReadOnlyDictionary<TKey, TValue> right, Func<TValue, TValue, bool> same) where TKey : notnull =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out TValue? value) && same(pair.Value, value));
}

public sealed record NestedTableLayout
{
    public string ClassName
    {
        get;
    }
    public IReadOnlyDictionary<uint, NestedScalarType> Columns
    {
        get;
    }
    public NestedTableLayout(string className, IReadOnlyDictionary<uint, NestedScalarType> columns)
    {
        ClassName = className;
        Columns = NestedMaps.Own(columns);
    }
}

/// <summary>Trusted authorization, supplied independently of received messages. Child keys are signed GUIDs.</summary>
public sealed record NestedEntityLayout
{
    public string ClassName
    {
        get;
    }
    public IReadOnlyDictionary<uint, NestedScalarType> Fields
    {
        get;
    }
    public IReadOnlyDictionary<uint, NestedTableLayout> Tables
    {
        get;
    }
    public IReadOnlyDictionary<uint, IReadOnlyDictionary<long, NestedEntityLayout>> Containers
    {
        get;
    }
    public NestedEntityLayout(string className, IReadOnlyDictionary<uint, NestedScalarType>? fields = null,
        IReadOnlyDictionary<uint, NestedTableLayout>? tables = null, IReadOnlyDictionary<uint, IReadOnlyDictionary<long, NestedEntityLayout>>? containers = null)
    {
        ClassName = className;
        Fields = NestedMaps.Own(fields);
        Tables = NestedMaps.Own(tables);
        Containers = NestedMaps.OwnNested(containers);
    }
}

public sealed record NestedStreamBinding(string SchemaId, long RootId, ulong RootIncarnation, ulong Generation, NestedEntityLayout Root);
public sealed record NestedSnapshotLimits(int MaxDepth = 8, int MaxEntities = 1024, int MaxRows = 65536, int MaxFields = 65536, long MaxStringBytes = 1024 * 1024);
public sealed record NestedStreamLimits(NestedSnapshotLimits? SnapshotLimits = null, int MaxPatchOperations = 65536, int MaxPatchValues = 65536,
    long MaxPatchStringBytes = 1024 * 1024, int MaxLifetimeEntries = 132096)
{
    private static readonly NestedSnapshotLimits DefaultSnapshotLimits = new();
    public NestedSnapshotLimits Snapshot => SnapshotLimits ?? DefaultSnapshotLimits;
}
public enum NestedApplyResult
{
    Applied, Duplicate, Rejected
}

public sealed record NestedRowSnapshot
{
    public ulong Incarnation
    {
        get;
    }
    public IReadOnlyDictionary<uint, NestedScalar> Fields
    {
        get;
    }
    public NestedRowSnapshot(ulong incarnation, IReadOnlyDictionary<uint, NestedScalar>? fields = null)
    {
        Incarnation = incarnation;
        Fields = NestedMaps.Own(fields);
    }
}

public sealed record NestedTableSnapshot
{
    public string ClassName
    {
        get;
    }
    public IReadOnlyDictionary<uint, NestedRowSnapshot> Rows
    {
        get;
    }
    public NestedTableSnapshot(string className, IReadOnlyDictionary<uint, NestedRowSnapshot>? rows = null)
    {
        ClassName = className;
        Rows = NestedMaps.Own(rows);
    }
}

public sealed record NestedEntitySnapshot
{
    public long EntityId
    {
        get;
    }
    public ulong Incarnation
    {
        get;
    }
    public string ClassName
    {
        get;
    }
    public IReadOnlyDictionary<uint, NestedScalar> Fields
    {
        get;
    }
    public IReadOnlyDictionary<uint, NestedTableSnapshot> Tables
    {
        get;
    }
    public IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, NestedEntitySnapshot>> Containers
    {
        get;
    }
    public NestedEntitySnapshot(long entityId, ulong incarnation, string className, IReadOnlyDictionary<uint, NestedScalar>? fields = null,
        IReadOnlyDictionary<uint, NestedTableSnapshot>? tables = null, IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, NestedEntitySnapshot>>? containers = null)
    {
        EntityId = entityId;
        Incarnation = incarnation;
        ClassName = className;
        Fields = NestedMaps.Own(fields);
        Tables = NestedMaps.Own(tables);
        Containers = NestedMaps.OwnNested(containers);
    }
}

public sealed record NestedSnapshot(string SchemaId, NestedEntitySnapshot Root)
{
    public bool ContentEquals(NestedSnapshot? other) => other is not null && SchemaId == other.SchemaId && SameEntity(Root, other.Root);
    private static bool SameEntity(NestedEntitySnapshot left, NestedEntitySnapshot right) =>
        left.EntityId == right.EntityId && left.Incarnation == right.Incarnation && left.ClassName == right.ClassName &&
        NestedMaps.Same(left.Fields, right.Fields, (a, b) => a.Equals(b)) &&
        NestedMaps.Same(left.Tables, right.Tables, (a, b) => a.ClassName == b.ClassName &&
            NestedMaps.Same(a.Rows, b.Rows, (x, y) => x.Incarnation == y.Incarnation && NestedMaps.Same(x.Fields, y.Fields, (v, w) => v.Equals(w)))) &&
        NestedMaps.Same(left.Containers, right.Containers, (a, b) => NestedMaps.Same(a, b, SameEntity));
}
public sealed record NestedBaseline(ulong Generation, ulong Revision, NestedSnapshot Snapshot);

public sealed record NestedRowDelta
{
    public ulong RequiredIncarnation
    {
        get;
    }
    public ulong Incarnation
    {
        get;
    }
    public IReadOnlyDictionary<uint, NestedScalar> Fields
    {
        get;
    }
    public NestedRowDelta(ulong requiredIncarnation, ulong incarnation, IReadOnlyDictionary<uint, NestedScalar>? fields = null)
    {
        RequiredIncarnation = requiredIncarnation;
        Incarnation = incarnation;
        Fields = NestedMaps.Own(fields);
    }
}

public sealed record NestedContainerDelta
{
    public bool Replace
    {
        get;
    }
    public IReadOnlyDictionary<uint, NestedEntitySnapshot> Slots
    {
        get;
    }
    public IReadOnlyDictionary<uint, NestedEntityDelta> Changed
    {
        get;
    }
    public NestedContainerDelta(bool replace, IReadOnlyDictionary<uint, NestedEntitySnapshot>? slots = null, IReadOnlyDictionary<uint, NestedEntityDelta>? changed = null)
    {
        Replace = replace;
        Slots = NestedMaps.Own(slots);
        Changed = NestedMaps.Own(changed);
    }
}

public sealed record NestedEntityDelta
{
    public long EntityId
    {
        get;
    }
    public ulong Incarnation
    {
        get;
    }
    public IReadOnlyDictionary<uint, NestedScalar> Fields
    {
        get;
    }
    public IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, NestedRowDelta>> Tables
    {
        get;
    }
    public IReadOnlyDictionary<uint, NestedContainerDelta> Containers
    {
        get;
    }
    public bool IsEmpty => Fields.Count == 0 && Tables.Count == 0 && Containers.Count == 0;
    public NestedEntityDelta(long entityId, ulong incarnation, IReadOnlyDictionary<uint, NestedScalar>? fields = null,
        IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, NestedRowDelta>>? tables = null, IReadOnlyDictionary<uint, NestedContainerDelta>? containers = null)
    {
        EntityId = entityId;
        Incarnation = incarnation;
        Fields = NestedMaps.Own(fields);
        Tables = NestedMaps.OwnNested(tables);
        Containers = NestedMaps.Own(containers);
    }
}
public sealed record NestedDelta(string SchemaId, ulong Generation, ulong RequiredRevision, ulong Revision, NestedEntityDelta Root, bool Removed = false);

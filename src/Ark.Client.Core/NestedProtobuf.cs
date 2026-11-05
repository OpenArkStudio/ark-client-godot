using Google.Protobuf;
using Wire = Ark.Client.Protocol.EntitySync;

namespace Ark.Client.Core;

/// <summary>Copies generated messages into immutable owning values and rejects duplicate indexed records.</summary>
public static class NestedProtobuf
{
    public static NestedBaseline Decode(Wire.nested_baseline input, NestedStreamLimits? limits = null)
    {
        NestedValidation.Require(input.Snapshot?.Root is not null);
        return new(input.Generation, input.Revision,
            new NestedSnapshot(input.Snapshot!.SchemaId, DecodeEntity(input.Snapshot.Root, 0, (limits ?? new()).Snapshot.MaxDepth)));
    }
    public static NestedDelta Decode(Wire.nested_delta input, NestedStreamLimits? limits = null)
    {
        NestedValidation.Require(input.Root is not null);
        return new(input.SchemaId, input.Generation, input.RequiredRevision, input.Revision,
            DecodeEntityDelta(input.Root!, 0, (limits ?? new()).Snapshot.MaxDepth), input.Removed);
    }
    private static Dictionary<uint, TOutput> Indexed<TInput, TOutput>(IEnumerable<TInput> input, Func<TInput, uint> index, Func<TInput, TOutput> read)
    {
        var result = new Dictionary<uint, TOutput>();
        foreach (TInput value in input)
        {
            NestedValidation.Require(result.TryAdd(index(value), read(value)));
        }
        return result;
    }
    private static NestedScalar DecodeScalar(Wire.scalar_field input) => input.ValueCase switch
    {
        Wire.scalar_field.ValueOneofCase.BoolValue => NestedScalar.FromBool(input.BoolValue),
        Wire.scalar_field.ValueOneofCase.Int32Value => NestedScalar.FromInt32(input.Int32Value),
        Wire.scalar_field.ValueOneofCase.Uint32Value => NestedScalar.FromUInt32(input.Uint32Value),
        Wire.scalar_field.ValueOneofCase.Int64Value => NestedScalar.FromInt64(input.Int64Value),
        Wire.scalar_field.ValueOneofCase.Uint64Value => NestedScalar.FromUInt64(input.Uint64Value),
        Wire.scalar_field.ValueOneofCase.FloatValue => NestedScalar.FromFloat(input.FloatValue),
        Wire.scalar_field.ValueOneofCase.DoubleValue => NestedScalar.FromDouble(input.DoubleValue),
        Wire.scalar_field.ValueOneofCase.StringValue => NestedScalar.FromBytes(input.StringValue.Span),
        Wire.scalar_field.ValueOneofCase.GuidValue => NestedScalar.FromGuid(input.GuidValue),
        _ => throw new InvalidDataException("Missing nested scalar alternative.")
    };
    private static Dictionary<uint, NestedScalar> DecodeFields(IEnumerable<Wire.scalar_field> input) => Indexed(input, field => field.Index, DecodeScalar);
    private static NestedEntitySnapshot DecodeEntity(Wire.entity_snapshot input, int depth, int maxDepth)
    {
        NestedValidation.Require(depth <= maxDepth);
        var tables = Indexed(input.Tables, table => table.Index, table => new NestedTableSnapshot(table.ClassName,
            Indexed(table.Rows, row => row.Number, row => new NestedRowSnapshot(row.Incarnation, DecodeFields(row.Fields)))));
        var containers = Indexed(input.Containers, container => container.Index,
            container => (IReadOnlyDictionary<uint, NestedEntitySnapshot>)Indexed(container.Slots, slot => slot.Slot, slot =>
            {
                NestedValidation.Require(slot.State is not null);
                return DecodeEntity(slot.State!, depth + 1, maxDepth);
            }));
        return new(input.EntityId, input.Incarnation, input.ClassName, DecodeFields(input.Fields), tables, containers);
    }
    private static NestedEntityDelta DecodeEntityDelta(Wire.entity_delta input, int depth, int maxDepth)
    {
        NestedValidation.Require(depth <= maxDepth);
        var tables = Indexed(input.Tables, table => table.Index,
            table => (IReadOnlyDictionary<uint, NestedRowDelta>)Indexed(table.Rows, row => row.Number,
                row => new NestedRowDelta(row.RequiredIncarnation, row.Incarnation, DecodeFields(row.Fields))));
        var containers = Indexed(input.Containers, container => container.Index, container => container.UpdateCase switch
        {
            Wire.container_delta.UpdateOneofCase.Replacement => new NestedContainerDelta(true,
                Indexed(container.Replacement.Slots, slot => slot.Slot, slot =>
                {
                    NestedValidation.Require(slot.State is not null);
                    return DecodeEntity(slot.State!, depth + 1, maxDepth);
                })),
            Wire.container_delta.UpdateOneofCase.Patch => new NestedContainerDelta(false, changed:
                Indexed(container.Patch.Changed, change => change.Slot, change =>
                {
                    NestedValidation.Require(change.Change is not null);
                    return DecodeEntityDelta(change.Change!, depth + 1, maxDepth);
                })),
            _ => throw new InvalidDataException("Missing nested container operation.")
        });
        return new(input.EntityId, input.Incarnation, DecodeFields(input.Fields), tables, containers);
    }
    public static Wire.nested_baseline Encode(NestedBaseline input) => new()
    {
        Generation = input.Generation,
        Revision = input.Revision,
        Snapshot = new Wire.nested_snapshot { SchemaId = input.Snapshot.SchemaId, Root = EncodeEntity(input.Snapshot.Root) }
    };
    public static Wire.nested_delta Encode(NestedDelta input) => new()
    {
        SchemaId = input.SchemaId,
        Generation = input.Generation,
        RequiredRevision = input.RequiredRevision,
        Revision = input.Revision,
        Root = EncodeEntityDelta(input.Root),
        Removed = input.Removed
    };
    private static Wire.scalar_field EncodeScalar(uint index, NestedScalar input)
    {
        var result = new Wire.scalar_field { Index = index };
        switch (input.Type)
        {
            case NestedScalarType.Boolean:
                result.BoolValue = input.AsBool();
                break;
            case NestedScalarType.Int32:
                result.Int32Value = input.AsInt32();
                break;
            case NestedScalarType.UInt32:
                result.Uint32Value = input.AsUInt32();
                break;
            case NestedScalarType.Int64:
                result.Int64Value = input.AsInt64();
                break;
            case NestedScalarType.UInt64:
                result.Uint64Value = input.AsUInt64();
                break;
            case NestedScalarType.Float:
                result.FloatValue = input.AsFloat();
                break;
            case NestedScalarType.Double:
                result.DoubleValue = input.AsDouble();
                break;
            case NestedScalarType.Bytes:
                result.StringValue = ByteString.CopyFrom(input.AsBytes());
                break;
            case NestedScalarType.Guid:
                result.GuidValue = input.AsGuid();
                break;
            default:
                throw new InvalidDataException("Unknown nested scalar type.");
        }
        return result;
    }
    private static IEnumerable<Wire.scalar_field> EncodeFields(IReadOnlyDictionary<uint, NestedScalar> fields) => fields.Select(pair => EncodeScalar(pair.Key, pair.Value));
    private static Wire.entity_snapshot EncodeEntity(NestedEntitySnapshot input)
    {
        var result = new Wire.entity_snapshot { EntityId = input.EntityId, Incarnation = input.Incarnation, ClassName = input.ClassName };
        result.Fields.Add(EncodeFields(input.Fields));
        foreach (var (index, table) in input.Tables)
        {
            var output = new Wire.table_snapshot { Index = index, ClassName = table.ClassName };
            foreach (var (number, row) in table.Rows)
            {
                var item = new Wire.row_snapshot { Number = number, Incarnation = row.Incarnation };
                item.Fields.Add(EncodeFields(row.Fields));
                output.Rows.Add(item);
            }
            result.Tables.Add(output);
        }
        foreach (var (index, children) in input.Containers)
        {
            var container = new Wire.container_snapshot { Index = index };
            foreach (var (slot, child) in children)
            {
                container.Slots.Add(new Wire.entity_slot { Slot = slot, State = EncodeEntity(child) });
            }
            result.Containers.Add(container);
        }
        return result;
    }
    private static Wire.entity_delta EncodeEntityDelta(NestedEntityDelta input)
    {
        var result = new Wire.entity_delta { EntityId = input.EntityId, Incarnation = input.Incarnation };
        result.Fields.Add(EncodeFields(input.Fields));
        foreach (var (index, rows) in input.Tables)
        {
            var table = new Wire.table_delta { Index = index };
            foreach (var (number, row) in rows)
            {
                var item = new Wire.row_delta { Number = number, RequiredIncarnation = row.RequiredIncarnation, Incarnation = row.Incarnation };
                item.Fields.Add(EncodeFields(row.Fields));
                table.Rows.Add(item);
            }
            result.Tables.Add(table);
        }
        foreach (var (index, change) in input.Containers)
        {
            var container = new Wire.container_delta { Index = index };
            if (change.Replace)
            {
                NestedValidation.Require(change.Changed.Count == 0);
                container.Replacement = new();
                foreach (var (slot, child) in change.Slots)
                {
                    container.Replacement.Slots.Add(new Wire.entity_slot { Slot = slot, State = EncodeEntity(child) });
                }
            }
            else
            {
                NestedValidation.Require(change.Slots.Count == 0);
                container.Patch = new();
                foreach (var (slot, child) in change.Changed)
                {
                    container.Patch.Changed.Add(new Wire.entity_change { Slot = slot, Change = EncodeEntityDelta(child) });
                }
            }
            result.Containers.Add(container);
        }
        return result;
    }
}

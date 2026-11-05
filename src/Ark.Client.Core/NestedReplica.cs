using System.Text;

namespace Ark.Client.Core;

/// <summary>One owner-thread root stream. Invalid input permanently rejects this receiver; reconnect binds a new one.</summary>
public sealed class NestedReplica
{
    private readonly NestedStreamBinding _binding;
    private readonly NestedStreamLimits _limits;
    private NestedHistory _history = new();
    private bool _installed, _failed;
    public bool Valid => _installed && !_failed;
    public NestedSnapshot? State
    {
        get; private set;
    }
    public ulong Revision
    {
        get; private set;
    }
    public NestedStreamBinding Binding => _binding;

    public NestedReplica(NestedStreamBinding binding, NestedStreamLimits? limits = null)
    {
        _binding = binding;
        _limits = limits ?? new();
        NestedValidation.Binding(binding, _limits);
    }

    private NestedReplica(NestedReplica previous)
    {
        _binding = previous._binding;
        _limits = previous._limits;
        _history = previous._history.Copy();
        _installed = previous._installed;
        _failed = previous._failed;
        State = previous.State;
        Revision = previous.Revision;
    }

    /// <summary>An isolated candidate for a larger atomic room commit. Immutable state can be shared safely.</summary>
    public NestedReplica Fork() => new(this);

    public NestedApplyResult Apply(NestedBaseline input)
    {
        if (_failed)
        {
            return NestedApplyResult.Rejected;
        }
        try
        {
            NestedValidation.Require(input.Generation == _binding.Generation && input.Revision != 0);
            NestedValidation.Snapshot(input.Snapshot, _binding, _limits);
            if (_installed)
            {
                if (input.Revision < Revision || (input.Revision == Revision && State is not null && State.ContentEquals(input.Snapshot)))
                {
                    return NestedApplyResult.Duplicate;
                }
                return Reject();
            }
            var history = new NestedHistory();
            history.Remember(input.Snapshot.Root, _limits.MaxLifetimeEntries);
            State = input.Snapshot;
            _history = history;
            Revision = input.Revision;
            _installed = true;
            return NestedApplyResult.Applied;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException) { return Reject(); }
    }

    public NestedApplyResult Apply(NestedDelta input)
    {
        if (_failed)
        {
            return NestedApplyResult.Rejected;
        }
        try
        {
            NestedValidation.Delta(input, _binding, _limits);
            NestedValidation.Require(_installed);
            if (input.Revision <= Revision)
            {
                return NestedApplyResult.Duplicate;
            }
            NestedValidation.Require(State is not null && input.RequiredRevision == Revision);
            if (input.Removed)
            {
                State = null;
                Revision = input.Revision;
                return NestedApplyResult.Applied;
            }
            var candidate = new NestedSnapshot(State!.SchemaId, ApplyPatch(State.Root, input.Root));
            NestedValidation.Snapshot(candidate, _binding, _limits);
            NestedHistory history = _history.Copy();
            history.Remember(candidate.Root, _limits.MaxLifetimeEntries);
            State = candidate;
            _history = history;
            Revision = input.Revision;
            return NestedApplyResult.Applied;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or KeyNotFoundException or OverflowException) { return Reject(); }
    }

    private NestedApplyResult Reject()
    {
        State = null;
        _failed = true;
        return NestedApplyResult.Rejected;
    }

    private static NestedEntitySnapshot ApplyPatch(NestedEntitySnapshot before, NestedEntityDelta patch)
    {
        NestedValidation.Require(before.EntityId == patch.EntityId && before.Incarnation == patch.Incarnation);
        var fields = before.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (index, value) in patch.Fields)
        {
            _ = fields[index];
            fields[index] = value;
        }
        var tables = before.Tables.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (index, changes) in patch.Tables)
        {
            NestedTableSnapshot previous = tables[index];
            var rows = previous.Rows.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var (number, change) in changes)
            {
                bool exists = rows.TryGetValue(number, out NestedRowSnapshot? old);
                NestedValidation.Require(change.RequiredIncarnation != 0 ? exists && old!.Incarnation == change.RequiredIncarnation : !exists);
                if (change.Incarnation == 0)
                {
                    rows.Remove(number);
                }
                else if (change.Incarnation != change.RequiredIncarnation)
                {
                    rows[number] = new NestedRowSnapshot(change.Incarnation, change.Fields);
                }
                else
                {
                    var columns = old!.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
                    foreach (var (column, value) in change.Fields)
                    {
                        _ = columns[column];
                        columns[column] = value;
                    }
                    rows[number] = new NestedRowSnapshot(old.Incarnation, columns);
                }
            }
            tables[index] = new NestedTableSnapshot(previous.ClassName, rows);
        }
        var containers = before.Containers.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (index, change) in patch.Containers)
        {
            IReadOnlyDictionary<uint, NestedEntitySnapshot> previous = containers[index];
            if (change.Replace)
            {
                containers[index] = change.Slots;
            }
            else
            {
                var children = previous.ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var (slot, child) in change.Changed)
                {
                    children[slot] = ApplyPatch(children[slot], child);
                }
                containers[index] = children;
            }
        }
        return new NestedEntitySnapshot(before.EntityId, before.Incarnation, before.ClassName, fields, tables, containers);
    }
}

internal sealed class NestedHistory
{
    private sealed record EntityLife(ulong Incarnation, string ClassName);
    private sealed record TableLife(ulong Watermark, Dictionary<uint, ulong> Rows);
    private readonly Dictionary<long, EntityLife> _entities = new();
    private readonly Dictionary<(long Entity, ulong Incarnation, uint Table), TableLife> _tables = new();
    private int _entries;
    public NestedHistory Copy()
    {
        var result = new NestedHistory { _entries = _entries };
        foreach (var pair in _entities)
        {
            result._entities.Add(pair.Key, pair.Value);
        }
        foreach (var pair in _tables)
        {
            result._tables.Add(pair.Key, new TableLife(pair.Value.Watermark, new(pair.Value.Rows)));
        }
        return result;
    }
    private void AddEntry(int limit)
    {
        NestedValidation.Require(_entries < limit);
        _entries++;
    }
    public void Remember(NestedEntitySnapshot input, int limit)
    {
        if (!_entities.TryGetValue(input.EntityId, out EntityLife? known))
        {
            AddEntry(limit);
            _entities.Add(input.EntityId, new EntityLife(input.Incarnation, input.ClassName));
        }
        else
        {
            NestedValidation.Require(input.Incarnation >= known.Incarnation);
            if (input.Incarnation == known.Incarnation)
            {
                NestedValidation.Require(input.ClassName == known.ClassName);
            }
            else
            {
                foreach (var key in _tables.Keys.Where(key => key.Entity == input.EntityId).ToArray())
                {
                    _entries -= 1 + _tables[key].Rows.Count;
                    _tables.Remove(key);
                }
                _entities[input.EntityId] = new EntityLife(input.Incarnation, input.ClassName);
            }
        }
        foreach (var (index, table) in input.Tables)
        {
            var key = (input.EntityId, input.Incarnation, index);
            if (!_tables.TryGetValue(key, out TableLife? old))
            {
                AddEntry(limit);
                old = new TableLife(0, new());
                _tables.Add(key, old);
            }
            int withoutRows = _entries - old.Rows.Count;
            NestedValidation.Require(withoutRows <= limit && table.Rows.Count <= limit - withoutRows);
            ulong watermark = old.Watermark;
            var rows = new Dictionary<uint, ulong>();
            foreach (var (number, row) in table.Rows)
            {
                NestedValidation.Require((old.Rows.TryGetValue(number, out ulong incarnation) && incarnation == row.Incarnation) || row.Incarnation > old.Watermark);
                watermark = Math.Max(watermark, row.Incarnation);
                rows.Add(number, row.Incarnation);
            }
            _tables[key] = new TableLife(watermark, rows);
            _entries = withoutRows + rows.Count;
        }
        foreach (var children in input.Containers.Values)
        {
            foreach (var child in children.Values)
            {
                Remember(child, limit);
            }
        }
    }
}

internal static class NestedValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidDataException("Invalid nested binding, state or operation.");
        }
    }
    internal static void Consume(long count, ref long remaining)
    {
        Require(count >= 0 && count <= remaining);
        remaining -= count;
    }
    internal static int StringBytes(string input) => StrictUtf8.GetByteCount(input);
    private sealed class StateBudget
    {
        public readonly NestedSnapshotLimits Limits;
        public long Entities, Rows, Fields, Strings;
        public readonly HashSet<long> Seen = new();
        public StateBudget(NestedSnapshotLimits limits)
        {
            Limits = limits;
            Entities = limits.MaxEntities;
            Rows = limits.MaxRows;
            Fields = limits.MaxFields;
            Strings = limits.MaxStringBytes;
        }
    }
    private sealed class PatchBudget
    {
        public long Operations, Values, Strings;
        public PatchBudget(NestedStreamLimits limits)
        {
            Operations = limits.MaxPatchOperations;
            Values = limits.MaxPatchValues;
            Strings = limits.MaxPatchStringBytes;
        }
    }
    private sealed record SchemaMember(int Kind, NestedScalarType Type, string Reference);

    public static void Binding(NestedStreamBinding input, NestedStreamLimits limits)
    {
        Require(limits.Snapshot.MaxDepth >= 0 && limits.Snapshot.MaxEntities >= 0 && limits.Snapshot.MaxRows >= 0 && limits.Snapshot.MaxFields >= 0 && limits.Snapshot.MaxStringBytes >= 0 &&
            limits.MaxPatchOperations >= 0 && limits.MaxPatchValues >= 0 && limits.MaxPatchStringBytes >= 0 && limits.MaxLifetimeEntries >= 0);
        Require(!string.IsNullOrEmpty(input.SchemaId) && input.RootId != 0 && input.RootIncarnation != 0 && input.Generation != 0);
        var budget = new StateBudget(limits.Snapshot);
        Consume(StringBytes(input.SchemaId), ref budget.Strings);
        Layout(input.Root, 0, budget, new());
    }
    private static void Member(Dictionary<(string Owner, uint Index), SchemaMember> registry, string owner, uint index, SchemaMember next)
    {
        Require(index != 0);
        var key = (owner, index);
        if (!registry.TryGetValue(key, out SchemaMember? old))
        {
            registry.Add(key, next);
            return;
        }
        Require(old.Kind == next.Kind && (old.Kind != 0 || old.Type == next.Type));
        Require(old.Reference.Length == 0 || next.Reference.Length == 0 || old.Reference == next.Reference);
        if (old.Reference.Length == 0)
        {
            registry[key] = next;
        }
    }
    private static void FieldLayout(IReadOnlyDictionary<uint, NestedScalarType> input, string owner, StateBudget budget, Dictionary<(string, uint), SchemaMember> registry)
    {
        Consume(input.Count, ref budget.Fields);
        foreach (var (index, type) in input)
        {
            Require(type is >= NestedScalarType.Boolean and <= NestedScalarType.Guid);
            Member(registry, owner, index, new(0, type, ""));
        }
    }
    private static void Layout(NestedEntityLayout input, int depth, StateBudget budget, Dictionary<(string, uint), SchemaMember> registry)
    {
        Require(depth <= budget.Limits.MaxDepth && !string.IsNullOrEmpty(input.ClassName));
        Consume(1, ref budget.Entities);
        Consume(StringBytes(input.ClassName), ref budget.Strings);
        FieldLayout(input.Fields, input.ClassName, budget, registry);
        Consume(input.Tables.Count, ref budget.Fields);
        foreach (var (index, table) in input.Tables)
        {
            Require(!string.IsNullOrEmpty(table.ClassName));
            Member(registry, input.ClassName, index, new(1, default, table.ClassName));
            Consume(StringBytes(table.ClassName), ref budget.Strings);
            FieldLayout(table.Columns, table.ClassName, budget, registry);
        }
        Consume(input.Containers.Count, ref budget.Fields);
        foreach (var (index, children) in input.Containers)
        {
            Member(registry, input.ClassName, index, new(2, default, ""));
            foreach (var (id, child) in children)
            {
                Require(id != 0);
                Member(registry, input.ClassName, index, new(2, default, child.ClassName));
                Layout(child, depth + 1, budget, registry);
            }
        }
    }
    private static void Value(NestedScalar input, NestedScalarType expected, ref long bytes)
    {
        Require(input.Type == expected);
        if (expected == NestedScalarType.Float)
        {
            Require(float.IsFinite(input.AsFloat()));
        }
        else if (expected == NestedScalarType.Double)
        {
            Require(double.IsFinite(input.AsDouble()));
        }
        else if (expected == NestedScalarType.Bytes)
        {
            Consume(input.ByteLength, ref bytes);
        }
    }
    private static void Fields(IReadOnlyDictionary<uint, NestedScalar> input, IReadOnlyDictionary<uint, NestedScalarType> layout, bool complete, ref long count, ref long bytes)
    {
        Require(!complete || input.Count == layout.Count);
        Consume(input.Count, ref count);
        foreach (var (index, value) in input)
        {
            Require(layout.TryGetValue(index, out NestedScalarType expected));
            Value(value, expected, ref bytes);
        }
    }
    public static void Snapshot(NestedSnapshot input, NestedStreamBinding trusted, NestedStreamLimits limits)
    {
        Require(input.SchemaId == trusted.SchemaId && input.Root.EntityId == trusted.RootId && input.Root.Incarnation == trusted.RootIncarnation);
        var budget = new StateBudget(limits.Snapshot);
        Consume(StringBytes(input.SchemaId), ref budget.Strings);
        Entity(input.Root, trusted.Root, 0, budget);
    }
    private static void Entity(NestedEntitySnapshot input, NestedEntityLayout trusted, int depth, StateBudget budget)
    {
        Require(depth <= budget.Limits.MaxDepth);
        Consume(1, ref budget.Entities);
        Require(input.Incarnation != 0 && input.EntityId != 0 && input.ClassName == trusted.ClassName && budget.Seen.Add(input.EntityId));
        Consume(StringBytes(input.ClassName), ref budget.Strings);
        Fields(input.Fields, trusted.Fields, true, ref budget.Fields, ref budget.Strings);
        Require(input.Tables.Count == trusted.Tables.Count && input.Containers.Count == trusted.Containers.Count);
        Consume(input.Tables.Count, ref budget.Fields);
        foreach (var (index, table) in input.Tables)
        {
            Require(trusted.Tables.TryGetValue(index, out NestedTableLayout? expected) && table.ClassName == expected.ClassName);
            Consume(StringBytes(table.ClassName), ref budget.Strings);
            Consume(table.Rows.Count, ref budget.Rows);
            var incarnations = new HashSet<ulong>();
            foreach (var (number, row) in table.Rows)
            {
                Require(number != 0 && row.Incarnation != 0 && incarnations.Add(row.Incarnation));
                Fields(row.Fields, expected!.Columns, true, ref budget.Fields, ref budget.Strings);
            }
        }
        Consume(input.Containers.Count, ref budget.Fields);
        foreach (var (index, children) in input.Containers)
        {
            Require(trusted.Containers.TryGetValue(index, out IReadOnlyDictionary<long, NestedEntityLayout>? allowed));
            foreach (var (slot, child) in children)
            {
                Require(slot != 0 && allowed!.ContainsKey(child.EntityId));
                Entity(child, allowed![child.EntityId], depth + 1, budget);
            }
        }
    }
    private static void CountValues(IReadOnlyDictionary<uint, NestedScalar> values, PatchBudget budget)
    {
        Consume(values.Count, ref budget.Values);
        foreach (NestedScalar item in values.Values)
        {
            Value(item, item.Type, ref budget.Strings);
        }
    }
    private static void CountEntity(NestedEntitySnapshot input, PatchBudget budget)
    {
        Consume(1, ref budget.Operations);
        Consume(StringBytes(input.ClassName), ref budget.Strings);
        CountValues(input.Fields, budget);
        Consume(input.Tables.Count, ref budget.Operations);
        foreach (NestedTableSnapshot table in input.Tables.Values)
        {
            Consume(StringBytes(table.ClassName), ref budget.Strings);
            Consume(table.Rows.Count, ref budget.Operations);
            foreach (NestedRowSnapshot row in table.Rows.Values)
            {
                CountValues(row.Fields, budget);
            }
        }
        Consume(input.Containers.Count, ref budget.Operations);
        foreach (var children in input.Containers.Values)
        {
            foreach (var child in children.Values)
            {
                CountEntity(child, budget);
            }
        }
    }
    private static void ValidatePatch(NestedEntityDelta input, NestedEntityLayout trusted, int depth, PatchBudget budget, StateBudget replacements)
    {
        Require(depth <= replacements.Limits.MaxDepth);
        Consume(1, ref budget.Operations);
        Require(input.EntityId != 0 && input.Incarnation != 0 && replacements.Seen.Add(input.EntityId));
        Fields(input.Fields, trusted.Fields, false, ref budget.Values, ref budget.Strings);
        Consume(input.Tables.Count, ref budget.Operations);
        foreach (var (index, rows) in input.Tables)
        {
            Require(trusted.Tables.TryGetValue(index, out NestedTableLayout? expected) && rows.Count != 0);
            Consume(rows.Count, ref budget.Operations);
            foreach (var (number, row) in rows)
            {
                Require(number != 0 && (row.RequiredIncarnation != 0 || row.Incarnation != 0));
                if (row.Incarnation == 0)
                {
                    Require(row.Fields.Count == 0);
                }
                else
                {
                    bool complete = row.Incarnation != row.RequiredIncarnation;
                    Require(complete ? row.Incarnation > row.RequiredIncarnation : row.Fields.Count != 0);
                    Fields(row.Fields, expected!.Columns, complete, ref budget.Values, ref budget.Strings);
                }
            }
        }
        Consume(input.Containers.Count, ref budget.Operations);
        foreach (var (index, update) in input.Containers)
        {
            Require(trusted.Containers.TryGetValue(index, out IReadOnlyDictionary<long, NestedEntityLayout>? allowed));
            if (update.Replace)
            {
                Require(update.Changed.Count == 0);
                foreach (var (slot, child) in update.Slots)
                {
                    Require(slot != 0 && allowed!.ContainsKey(child.EntityId));
                    Entity(child, allowed![child.EntityId], depth + 1, replacements);
                    CountEntity(child, budget);
                }
            }
            else
            {
                Require(update.Slots.Count == 0 && update.Changed.Count != 0);
                foreach (var (slot, child) in update.Changed)
                {
                    Require(slot != 0 && allowed!.ContainsKey(child.EntityId) && !child.IsEmpty);
                    ValidatePatch(child, allowed![child.EntityId], depth + 1, budget, replacements);
                }
            }
        }
    }
    public static void Delta(NestedDelta input, NestedStreamBinding trusted, NestedStreamLimits limits)
    {
        Require(input.SchemaId == trusted.SchemaId && input.Generation == trusted.Generation && input.RequiredRevision != 0 &&
            input.RequiredRevision != ulong.MaxValue && input.Revision == input.RequiredRevision + 1 && input.Root.EntityId == trusted.RootId &&
            input.Root.Incarnation == trusted.RootIncarnation && (input.Removed ? input.Root.IsEmpty : !input.Root.IsEmpty));
        var budget = new PatchBudget(limits);
        Consume(StringBytes(input.SchemaId), ref budget.Strings);
        ValidatePatch(input.Root, trusted.Root, 0, budget, new StateBudget(limits.Snapshot));
    }
}

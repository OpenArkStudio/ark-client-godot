using System.Text;
using Ark.Client.Core;
using Google.Protobuf;
using Wire = Ark.Client.Protocol.EntitySync;

internal static class NestedReplicaTests
{
    private static Dictionary<uint, NestedScalarType> Columns() => new() { [1] = NestedScalarType.UInt32, [2] = NestedScalarType.Bytes };
    private static NestedEntityLayout ItemLayout() => new("Item", new Dictionary<uint, NestedScalarType> { [1] = NestedScalarType.UInt32 },
        new Dictionary<uint, NestedTableLayout> { [200] = new("Line", Columns()) });
    private static NestedStreamBinding Binding() => new("nested", 10, 1, 1,
        new NestedEntityLayout("Root", new Dictionary<uint, NestedScalarType> { [1] = NestedScalarType.Int32, [2] = NestedScalarType.Bytes },
            new Dictionary<uint, NestedTableLayout> { [200] = new("Line", Columns()) },
            new Dictionary<uint, IReadOnlyDictionary<long, NestedEntityLayout>>
            {
                [201] = new Dictionary<long, NestedEntityLayout> { [20] = ItemLayout(), [21] = ItemLayout() },
                [202] = new Dictionary<long, NestedEntityLayout> { [20] = ItemLayout(), [21] = ItemLayout() }
            }));
    private static Wire.scalar_field UInt(uint index, uint value) => new() { Index = index, Uint32Value = value };
    private static Wire.scalar_field Bytes(uint index, string value) => new() { Index = index, StringValue = ByteString.CopyFromUtf8(value) };
    private static Wire.row_snapshot Row(ulong incarnation, uint value = 5) => new() { Number = 1, Incarnation = incarnation, Fields = { UInt(1, value), Bytes(2, "owned-row") } };
    private static Wire.entity_snapshot Child(long id = 20, ulong incarnation = 2) => new()
    {
        EntityId = id,
        Incarnation = incarnation,
        ClassName = "Item",
        Fields = { UInt(1, 7) },
        Tables = { new Wire.table_snapshot { Index = 200, ClassName = "Line", Rows = { Row(1, 3) } } }
    };
    private static Wire.nested_baseline Initial() => new()
    {
        Generation = 1,
        Revision = 1,
        Snapshot = new Wire.nested_snapshot
        {
            SchemaId = "nested",
            Root = new Wire.entity_snapshot
            {
                EntityId = 10,
                Incarnation = 1,
                ClassName = "Root",
                Fields = { new Wire.scalar_field { Index = 1, Int32Value = -7 }, Bytes(2, "owned-root") },
                Tables = { new Wire.table_snapshot { Index = 200, ClassName = "Line", Rows = { Row(1) } } },
                Containers = { new Wire.container_snapshot { Index = 201, Slots = { new Wire.entity_slot { Slot = 1, State = Child() } } }, new Wire.container_snapshot { Index = 202 } }
            }
        }
    };
    private static Wire.nested_delta Delta(ulong required = 1) => new()
    {
        SchemaId = "nested",
        Generation = 1,
        RequiredRevision = required,
        Revision = required + 1,
        Root = new Wire.entity_delta { EntityId = 10, Incarnation = 1 }
    };
    private static NestedReplica Joined(NestedStreamLimits? limits = null)
    {
        var receiver = new NestedReplica(Binding(), limits);
        Program.Check(receiver.Apply(NestedProtobuf.Decode(Initial())) == NestedApplyResult.Applied, "Initial nested state rejected.");
        return receiver;
    }
    private static Wire.nested_delta Changed()
    {
        var change = Delta();
        change.Root.Fields.Add(Bytes(2, new string('x', 120)));
        change.Root.Tables.Add(new Wire.table_delta
        {
            Index = 200,
            Rows = {
            new Wire.row_delta { Number = 1, RequiredIncarnation = 1, Incarnation = 1, Fields = { UInt(1, 9) } },
            new Wire.row_delta { Number = 2, Incarnation = 2, Fields = { UInt(1, 12), Bytes(2, new string('r', 80)) } } }
        });
        change.Root.Containers.Add(new Wire.container_delta
        {
            Index = 201,
            Patch = new Wire.container_patch
            {
                Changed = {
            new Wire.entity_change { Slot = 1, Change = new Wire.entity_delta { EntityId = 20, Incarnation = 2, Fields = { UInt(1, 8) } } } }
            }
        });
        return change;
    }
    private static NestedApplyResult Apply(NestedReplica receiver, Wire.nested_delta input) => receiver.Apply(NestedProtobuf.Decode(input));
    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException) { return; }
        throw new Exception("Expected nested malformed input rejection.");
    }
    public static void Verify()
    {
        OwnedValuesAndAllScalarTypes();
        CompleteAuthorizationAndMalformedState();
        RowLifetimes();
        MembershipMovesAndIncarnations();
        AtomicCandidatesAndTerminalRejection();
        DuplicateBaselineAndTombstone();
        RecursiveChildren();
        Limits();
        WireShapeAndDuplicates();
    }
    private static void OwnedValuesAndAllScalarTypes()
    {
        byte[] source = { 0, 255, 128 };
        NestedScalar owned = NestedScalar.FromBytes(source);
        source[0] = 9;
        byte[] exposed = owned.AsBytes();
        exposed[1] = 0;
        Program.Check(owned.AsBytes().SequenceEqual(new byte[] { 0, 255, 128 }), "Nested bytes alias caller storage.");
        var values = new Dictionary<uint, NestedScalar>
        {
            [1] = NestedScalar.FromBool(false),
            [2] = NestedScalar.FromInt32(int.MinValue),
            [3] = NestedScalar.FromUInt32(uint.MaxValue),
            [4] = NestedScalar.FromInt64(long.MinValue),
            [5] = NestedScalar.FromUInt64(ulong.MaxValue),
            [6] = NestedScalar.FromFloat(1.25f),
            [7] = NestedScalar.FromDouble(Math.BitIncrement(2.5)),
            [8] = owned,
            [9] = NestedScalar.FromGuid(long.MinValue)
        };
        var layout = new NestedEntityLayout("All", values.ToDictionary(pair => pair.Key, pair => pair.Value.Type));
        var root = new NestedEntitySnapshot(-10, 1, "All", values);
        var input = new NestedBaseline(1, 1, new NestedSnapshot("all", root));
        values.Clear();
        var receiver = new NestedReplica(new NestedStreamBinding("all", -10, 1, 1, layout));
        Wire.nested_baseline encoded = NestedProtobuf.Encode(input);
        NestedBaseline decoded = NestedProtobuf.Decode(ProtobufMessages.Parse(encoded.ToByteArray(), Wire.nested_baseline.Parser, Wire.nested_baseline.Descriptor));
        Program.Check(receiver.Apply(decoded) == NestedApplyResult.Applied && receiver.State!.ContentEquals(input.Snapshot) && receiver.State.Root.Fields.Count == 9,
            "Nested exact scalar alternatives, signed GUIDs or ownership changed.");
        NestedSnapshot retained = receiver.State!;
        var next = new NestedDelta("all", 1, 1, 2, new NestedEntityDelta(-10, 1,
            new Dictionary<uint, NestedScalar> { [1] = NestedScalar.FromBool(true), [8] = NestedScalar.FromBytes(Encoding.UTF8.GetBytes("next")) }));
        Program.Check(receiver.Apply(NestedProtobuf.Decode(NestedProtobuf.Encode(next))) == NestedApplyResult.Applied &&
            !retained.Root.Fields[1].AsBool() && receiver.State!.Root.Fields[1].AsBool(), "Nested retained state was modified by a patch.");
        encoded.Snapshot.Root.Fields.Clear();
        Program.Check(retained.Root.Fields[8].AsBytes().SequenceEqual(new byte[] { 0, 255, 128 }), "Generated messages escaped into committed nested state.");
        try
        {
            ((IDictionary<uint, NestedScalar>)retained.Root.Fields).Clear();
            throw new Exception("Nested state exposed mutable maps.");
        }
        catch (NotSupportedException) { }
    }
    private static void CompleteAuthorizationAndMalformedState()
    {
        foreach (Action<Wire.nested_baseline> corrupt in new Action<Wire.nested_baseline>[]
        {
            b => b.Generation = 2, b => b.Revision = 0, b => b.Snapshot.SchemaId = "other", b => b.Snapshot.Root.EntityId = 999,
            b => b.Snapshot.Root.Incarnation = 2, b => b.Snapshot.Root.ClassName = "Other", b => b.Snapshot.Root.Fields.RemoveAt(0),
            b => b.Snapshot.Root.Fields[0].Uint32Value = 7, b => b.Snapshot.Root.Fields.Add(UInt(99, 1)),
            b => b.Snapshot.Root.Tables[0].ClassName = "Other", b => b.Snapshot.Root.Tables[0].Rows[0].Fields[0].Int32Value = 5,
            b => b.Snapshot.Root.Tables.Clear(), b => b.Snapshot.Root.Containers.Clear(),
            b => b.Snapshot.Root.Containers[0].Slots[0].Slot = 0, b => b.Snapshot.Root.Containers[0].Slots[0].State.EntityId = 99,
            b => { var row = b.Snapshot.Root.Tables[0].Rows[0].Clone(); row.Number = 2; b.Snapshot.Root.Tables[0].Rows.Add(row); },
            b => b.Snapshot.Root.Containers[1].Slots.Add(new Wire.entity_slot { Slot = 2, State = Child() })
        })
        {
            var receiver = new NestedReplica(Binding());
            var wrong = Initial();
            corrupt(wrong);
            Program.Check(receiver.Apply(NestedProtobuf.Decode(wrong)) == NestedApplyResult.Rejected && !receiver.Valid && receiver.State is null &&
                receiver.Apply(NestedProtobuf.Decode(Initial())) == NestedApplyResult.Rejected, "Invalid nested baseline was accepted or repaired a terminal receiver.");
        }
        var initial = Initial();
        initial.Snapshot.Root.Tables[0].Rows.Clear();
        var empty = new NestedReplica(Binding());
        Program.Check(empty.Apply(NestedProtobuf.Decode(initial)) == NestedApplyResult.Applied, "Trusted empty table rejected.");
        var wrongFirstRow = Delta();
        wrongFirstRow.Root.Tables.Add(new Wire.table_delta
        {
            Index = 200,
            Rows = {
            new Wire.row_delta { Number = 1, Incarnation = 1, Fields = { new Wire.scalar_field { Index = 1, Int32Value = 3 }, Bytes(2, "bad") } } }
        });
        Program.Check(Apply(empty, wrongFirstRow) == NestedApplyResult.Rejected, "Empty-table column types were inferred from remote values.");
        var trusted = Binding();
        var conflicting = new NestedEntityLayout("Root", trusted.Root.Fields, trusted.Root.Tables,
            new Dictionary<uint, IReadOnlyDictionary<long, NestedEntityLayout>>
            {
                [201] = new Dictionary<long, NestedEntityLayout>
                {
                    [20] = new("Item", new Dictionary<uint, NestedScalarType> { [1] = NestedScalarType.UInt32 },
                    new Dictionary<uint, NestedTableLayout> { [200] = new("Line", new Dictionary<uint, NestedScalarType> { [1] = NestedScalarType.Int32, [2] = NestedScalarType.Bytes }) })
                }
            });
        Reject(() => new NestedReplica(trusted with { Root = conflicting }));
        Reject(() => new NestedReplica(trusted with { RootId = 0 }));
        Reject(() => new NestedReplica(trusted with { RootIncarnation = 0 }));
        Reject(() => new NestedReplica(trusted with { Generation = 0 }));
        Reject(() => new NestedReplica(trusted with { SchemaId = "" }));
    }
    private static Wire.nested_delta RowChange(ulong revision, ulong requiredIncarnation, ulong incarnation, bool complete = true)
    {
        var result = Delta(revision);
        var row = new Wire.row_delta { Number = 1, RequiredIncarnation = requiredIncarnation, Incarnation = incarnation };
        if (incarnation != 0)
        {
            row.Fields.Add(UInt(1, 5));
            if (complete)
            {
                row.Fields.Add(Bytes(2, "owned-row"));
            }
        }
        result.Root.Tables.Add(new Wire.table_delta { Index = 200, Rows = { row } });
        return result;
    }
    private static void RowLifetimes()
    {
        var receiver = Joined();
        Program.Check(Apply(receiver, RowChange(1, 1, 2)) == NestedApplyResult.Applied, "Same-value row replacement lost incarnation.");
        Program.Check(Apply(receiver, RowChange(2, 2, 0)) == NestedApplyResult.Applied && receiver.State!.Root.Tables[200].Rows.Count == 0, "Row removal failed.");
        Program.Check(Apply(receiver, RowChange(3, 0, 3)) == NestedApplyResult.Applied && receiver.State!.Root.Tables[200].Rows[1].Incarnation == 3, "New row lifetime rejected.");
        Program.Check(Apply(receiver, RowChange(4, 3, 0)) == NestedApplyResult.Applied && Apply(receiver, RowChange(5, 0, 2)) == NestedApplyResult.Rejected,
            "A deleted row bypassed the retained table watermark.");
        Program.Check(Apply(Joined(), RowChange(1, 1, 2, false)) == NestedApplyResult.Rejected, "Incomplete replacement row accepted.");
        Program.Check(Apply(Joined(), RowChange(1, 99, 99, false)) == NestedApplyResult.Rejected, "Wrong required row incarnation accepted.");
    }
    private static Wire.nested_delta Membership(ulong revision, bool first, Wire.entity_snapshot? child)
    {
        var result = Delta(revision);
        var update = new Wire.container_delta { Index = first ? 201u : 202u, Replacement = new Wire.container_replacement() };
        if (child is not null)
        {
            update.Replacement.Slots.Add(new Wire.entity_slot { Slot = 7, State = child });
        }
        result.Root.Containers.Add(update);
        return result;
    }
    private static void MembershipMovesAndIncarnations()
    {
        var receiver = Joined();
        var move = Membership(1, true, null);
        move.Root.Containers.Add(Membership(1, false, Child()).Root.Containers[0]);
        Program.Check(Apply(receiver, move) == NestedApplyResult.Applied && receiver.State!.Root.Containers[201].Count == 0 && receiver.State.Root.Containers[202][7].EntityId == 20,
            "Relationship-only move did not commit atomically.");
        Program.Check(Apply(receiver, Membership(2, false, null)) == NestedApplyResult.Applied && Apply(receiver, Membership(3, true, Child())) == NestedApplyResult.Applied,
            "Same-incarnation authorized detached child could not reappear.");
        Program.Check(Apply(receiver, Membership(4, true, Child(20, 3))) == NestedApplyResult.Applied && Apply(receiver, Membership(5, true, null)) == NestedApplyResult.Applied &&
            Apply(receiver, Membership(6, true, Child())) == NestedApplyResult.Rejected, "Child incarnation regressed after removal.");
    }
    private static void AtomicCandidatesAndTerminalRejection()
    {
        foreach (Action<Wire.nested_delta> corrupt in new Action<Wire.nested_delta>[]
        {
            d => { d.RequiredRevision++; d.Revision++; }, d => d.Generation++, d => d.SchemaId = "other", d => d.Root.EntityId++,
            d => d.Root.Incarnation++, d => d.Root.Fields[0].BoolValue = true, d => d.Root.Tables[0].Rows[1].Fields[0].Int32Value = 12,
            d => d.Root.Containers[0].Patch.Changed[0].Change.Incarnation = 99, d => d.Removed = true
        })
        {
            var receiver = Joined();
            NestedSnapshot retained = receiver.State!;
            var candidate = receiver.Fork();
            var bad = Changed();
            corrupt(bad);
            Program.Check(Apply(candidate, bad) == NestedApplyResult.Rejected && candidate.State is null && !candidate.Valid && receiver.Valid && ReferenceEquals(receiver.State, retained),
                "Nested candidate rejection modified the installed receiver.");
            Program.Check(Apply(candidate, Changed()) == NestedApplyResult.Rejected && retained.ContentEquals(NestedProtobuf.Decode(Initial()).Snapshot), "Rejected nested receiver revived or retained state mutated.");
            Program.Check(Apply(receiver, Changed()) == NestedApplyResult.Applied && receiver.State!.Root.Tables[200].Rows[2].Fields[1].AsUInt32() == 12, "Failed candidate consumed installed history/revision.");
        }
    }
    private static void DuplicateBaselineAndTombstone()
    {
        var receiver = Joined();
        NestedBaseline initial = NestedProtobuf.Decode(Initial());
        Program.Check(receiver.Apply(initial) == NestedApplyResult.Duplicate && Apply(receiver, Changed()) == NestedApplyResult.Applied &&
            receiver.Apply(initial) == NestedApplyResult.Duplicate && Apply(receiver, Changed()) == NestedApplyResult.Duplicate, "Nested duplicate or stale input regressed state.");
        var malformedDuplicate = Changed();
        malformedDuplicate.Root.Fields[0].BoolValue = true;
        Program.Check(Apply(receiver, malformedDuplicate) == NestedApplyResult.Rejected, "Malformed duplicate bypassed shape/type validation.");
        receiver = Joined();
        var removed = Delta();
        removed.Removed = true;
        Program.Check(Apply(receiver, removed) == NestedApplyResult.Applied && receiver.Valid && receiver.State is null &&
            receiver.Apply(initial) == NestedApplyResult.Duplicate && Apply(receiver, removed) == NestedApplyResult.Duplicate, "Nested tombstone duplicate rules changed.");
        var resurrect = Delta(2);
        resurrect.Root.Fields.Add(Bytes(2, "revive"));
        Program.Check(Apply(receiver, resurrect) == NestedApplyResult.Rejected, "Nested tombstone resurrected.");
        receiver = Joined();
        var replacement = Initial();
        replacement.Revision = 2;
        Program.Check(receiver.Apply(NestedProtobuf.Decode(replacement)) == NestedApplyResult.Rejected, "Fresh baseline replaced an installed nested stream.");
        receiver = Joined();
        replacement = Initial();
        replacement.Snapshot.Root.Fields[0].Int32Value = 42;
        Program.Check(receiver.Apply(NestedProtobuf.Decode(replacement)) == NestedApplyResult.Rejected, "Conflicting same-revision baseline accepted.");
        var beforeBaseline = new NestedReplica(Binding());
        Program.Check(Apply(beforeBaseline, Changed()) == NestedApplyResult.Rejected, "Delta before baseline accepted.");
    }
    private static void RecursiveChildren()
    {
        NestedStreamBinding trusted = Binding();
        NestedEntityLayout item = ItemLayout();
        var recursive = new NestedEntityLayout(item.ClassName, item.Fields, item.Tables,
            new Dictionary<uint, IReadOnlyDictionary<long, NestedEntityLayout>> { [201] = new Dictionary<long, NestedEntityLayout> { [30] = ItemLayout() } });
        var rootLayout = new NestedEntityLayout(trusted.Root.ClassName, trusted.Root.Fields, trusted.Root.Tables,
            new Dictionary<uint, IReadOnlyDictionary<long, NestedEntityLayout>> { [201] = new Dictionary<long, NestedEntityLayout> { [20] = recursive }, [202] = new Dictionary<long, NestedEntityLayout>() });
        trusted = trusted with
        {
            Root = rootLayout
        };
        var baseline = Initial();
        baseline.Snapshot.Root.Containers[0].Slots[0].State.Containers.Add(new Wire.container_snapshot { Index = 201, Slots = { new Wire.entity_slot { Slot = 7, State = Child(30, 3) } } });
        var receiver = new NestedReplica(trusted);
        Program.Check(receiver.Apply(NestedProtobuf.Decode(baseline)) == NestedApplyResult.Applied, "Recursive nested baseline rejected.");
        var change = Delta();
        change.Root.Containers.Add(new Wire.container_delta
        {
            Index = 201,
            Patch = new Wire.container_patch
            {
                Changed = {
            new Wire.entity_change { Slot = 1, Change = new Wire.entity_delta { EntityId = 20, Incarnation = 2, Containers = {
                new Wire.container_delta { Index = 201, Patch = new Wire.container_patch { Changed = {
                    new Wire.entity_change { Slot = 7, Change = new Wire.entity_delta { EntityId = 30, Incarnation = 3, Fields = { UInt(1, 99) } } } } } } } } } }
            }
        });
        Program.Check(Apply(receiver, change) == NestedApplyResult.Applied && receiver.State!.Root.Containers[201][1].Containers[201][7].Fields[1].AsUInt32() == 99,
            "Recursive child patch failed.");
    }
    private static void Limits()
    {
        foreach (NestedStreamLimits limits in new[]
        {
            new NestedStreamLimits(new NestedSnapshotLimits(MaxStringBytes: 100)), new NestedStreamLimits(MaxPatchValues: 1),
            new NestedStreamLimits(MaxPatchOperations: 1), new NestedStreamLimits(MaxPatchStringBytes: 20),
            new NestedStreamLimits(new NestedSnapshotLimits(MaxRows: 2)), new NestedStreamLimits(MaxLifetimeEntries: 6)
        })
        {
            var receiver = Joined(limits);
            NestedSnapshot retained = receiver.State!;
            Program.Check(Apply(receiver, Changed()) == NestedApplyResult.Rejected && receiver.State is null && retained.ContentEquals(NestedProtobuf.Decode(Initial()).Snapshot),
                "Nested snapshot/patch/history limit missed reconstructed state or mutated a retained publication.");
        }
        Reject(() => new NestedReplica(Binding(), new NestedStreamLimits(new NestedSnapshotLimits(MaxDepth: 0))));
        Reject(() => new NestedReplica(Binding(), new NestedStreamLimits(new NestedSnapshotLimits(MaxEntities: 1))));
        Reject(() => new NestedReplica(Binding(), new NestedStreamLimits(new NestedSnapshotLimits(MaxFields: 1))));
        var baseline = Initial();
        baseline.Snapshot.Root.Fields[0].DoubleValue = double.PositiveInfinity;
        var binding = Binding();
        var fields = binding.Root.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        fields[1] = NestedScalarType.Double;
        var real = new NestedEntityLayout(binding.Root.ClassName, fields, binding.Root.Tables, binding.Root.Containers);
        var finite = new NestedReplica(binding with
        {
            Root = real
        });
        Program.Check(finite.Apply(NestedProtobuf.Decode(baseline)) == NestedApplyResult.Rejected, "Nonfinite nested scalar accepted.");
    }
    private static void WireShapeAndDuplicates()
    {
        foreach (Action<Wire.nested_baseline> corrupt in new Action<Wire.nested_baseline>[]
        {
            b => b.Snapshot.Root.Fields.Add(b.Snapshot.Root.Fields[0].Clone()),
            b => b.Snapshot.Root.Tables.Add(b.Snapshot.Root.Tables[0].Clone()),
            b => b.Snapshot.Root.Tables[0].Rows.Add(b.Snapshot.Root.Tables[0].Rows[0].Clone()),
            b => b.Snapshot.Root.Containers.Add(b.Snapshot.Root.Containers[0].Clone()),
            b => b.Snapshot.Root.Containers[0].Slots.Add(b.Snapshot.Root.Containers[0].Slots[0].Clone()),
            b => b.Snapshot.Root.Containers[0].Slots[0].State = null,
            b => b.Snapshot.Root.Fields[0].ClearValue()
        })
        {
            var input = Initial();
            corrupt(input);
            Reject(() => NestedProtobuf.Decode(input));
        }
        var missingOperation = Delta();
        missingOperation.Root.Containers.Add(new Wire.container_delta { Index = 201 });
        Reject(() => NestedProtobuf.Decode(missingOperation));
        var duplicateRow = Changed();
        duplicateRow.Root.Tables[0].Rows.Add(duplicateRow.Root.Tables[0].Rows[0].Clone());
        Reject(() => NestedProtobuf.Decode(duplicateRow));
        var duplicateChild = Changed();
        duplicateChild.Root.Containers[0].Patch.Changed.Add(duplicateChild.Root.Containers[0].Patch.Changed[0].Clone());
        Reject(() => NestedProtobuf.Decode(duplicateChild));
        Reject(() => ProtobufMessages.Parse(new byte[] { 0x12, 0, 0x1a, 0 }, Wire.container_delta.Parser, Wire.container_delta.Descriptor));
        Reject(() => NestedProtobuf.Decode(Initial(), new NestedStreamLimits(new NestedSnapshotLimits(MaxDepth: 0))));
    }
}

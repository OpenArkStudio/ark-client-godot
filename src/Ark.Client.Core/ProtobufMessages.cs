using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Ark.Client.Core;

/// <summary>Generated message parsing plus duplicate/shape checks through the official protobuf reader.</summary>
public static class ProtobufMessages
{
    public static T Parse<T>(byte[] body, MessageParser<T> parser, MessageDescriptor descriptor) where T : IMessage<T>
    {
        if (body.Length > FrameCodec.MaximumBodyLength)
        {
            throw new InvalidDataException("Room body is too large.");
        }
        try
        {
            ValidateShape(body, descriptor, 0);
            T message = parser.ParseFrom(body);
            if (message is Ark.Client.Protocol.action_request action &&
                action.Kind is not (Ark.Client.Protocol.action_kind.ActionDamage or Ark.Client.Protocol.action_kind.ActionSkill or Ark.Client.Protocol.action_kind.ActionRest))
            {
                throw new InvalidDataException("Unknown action enum.");
            }
            return message;
        }
        catch (Exception error) when (error is InvalidProtocolBufferException or ArgumentException or System.Text.DecoderFallbackException)
        {
            throw new InvalidDataException("Invalid protobuf room message.", error);
        }
    }

    private static void ValidateShape(byte[] bytes, MessageDescriptor descriptor, int depth)
    {
        if (depth > 12)
        {
            throw new InvalidDataException("Protobuf nesting limit exceeded.");
        }
        using var input = new CodedInputStream(bytes);
        var singular = new HashSet<int>();
        var oneofs = new HashSet<OneofDescriptor>();
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            FieldDescriptor? field = descriptor.FindFieldByNumber(WireFormat.GetTagFieldNumber(tag));
            if (field is null)
            {
                input.SkipLastField();
                continue;
            }
            if (!field.IsRepeated && !singular.Add(field.FieldNumber))
            {
                throw new InvalidDataException("Duplicate protobuf field.");
            }
            if (field.ContainingOneof is { } oneof && !oneofs.Add(oneof))
            {
                throw new InvalidDataException("Duplicate scalar value alternative.");
            }
            WireFormat.WireType expected = field.FieldType switch
            {
                FieldType.Double or FieldType.Fixed64 or FieldType.SFixed64 => WireFormat.WireType.Fixed64,
                FieldType.Float or FieldType.Fixed32 or FieldType.SFixed32 => WireFormat.WireType.Fixed32,
                FieldType.String or FieldType.Bytes or FieldType.Message => WireFormat.WireType.LengthDelimited,
                _ => WireFormat.WireType.Varint
            };
            if (WireFormat.GetTagWireType(tag) != expected)
            {
                throw new InvalidDataException("Wrong protobuf field wire type.");
            }
            if (field.FieldType == FieldType.Message)
            {
                ValidateShape(input.ReadBytes().ToByteArray(), field.MessageType, depth + 1);
            }
            else if (field.FieldType == FieldType.String)
            {
                _ = new System.Text.UTF8Encoding(false, true).GetString(input.ReadBytes().Span);
            }
            else if (expected == WireFormat.WireType.Varint)
            {
                int start = checked((int)input.Position);
                ulong value = input.ReadUInt64();
                if ((field.FieldType is FieldType.UInt32 or FieldType.SInt32 && value > uint.MaxValue) ||
                    (field.FieldType == FieldType.Bool && value > 1))
                {
                    throw new InvalidDataException("Protobuf value exceeds scalar width.");
                }
                byte[] canonical = new byte[CodedOutputStream.ComputeUInt64Size(value)];
                using var output = new CodedOutputStream(canonical);
                output.WriteUInt64(value);
                output.CheckNoSpaceLeft();
                if (!bytes.AsSpan(start, checked((int)input.Position) - start).SequenceEqual(canonical))
                {
                    throw new InvalidDataException("Noncanonical or overflowing protobuf integer.");
                }
            }
            else
            {
                input.SkipLastField();
            }
        }
    }
}

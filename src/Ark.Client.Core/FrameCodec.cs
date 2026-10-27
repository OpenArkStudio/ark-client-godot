using System.Buffers.Binary;

namespace Ark.Client.Core;

public sealed record Frame(ushort MessageId, byte[] Body);

public sealed class FrameCodec
{
    public const int HeaderLength = 12;
    public const int MaximumBodyLength = 8192;
    private byte[] _buffer = new byte[HeaderLength + MaximumBodyLength];
    private int _count;

    public static byte[] Encode(ushort messageId, ReadOnlySpan<byte> body)
    {
        if (body.Length > MaximumBodyLength)
        {
            throw new InvalidDataException("Body exceeds 8192 bytes.");
        }

        var frame = new byte[HeaderLength + body.Length];
        "ARK2"u8.CopyTo(frame);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), messageId);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8), (uint)body.Length);
        body.CopyTo(frame.AsSpan(HeaderLength));
        return frame;
    }

    public IReadOnlyList<Frame> Append(ReadOnlySpan<byte> bytes)
    {
        var frames = new List<Frame>();
        while (!bytes.IsEmpty)
        {
            int copied = Math.Min(bytes.Length, _buffer.Length - _count);
            bytes[..copied].CopyTo(_buffer.AsSpan(_count));
            _count += copied;
            bytes = bytes[copied..];
            int consumed = 0;
            while (_count - consumed >= HeaderLength)
            {
                var header = _buffer.AsSpan(consumed, HeaderLength);
                if (!header[..4].SequenceEqual("ARK2"u8))
                {
                    throw new InvalidDataException("Unknown protocol marker.");
                }

                if (BinaryPrimitives.ReadUInt16BigEndian(header[6..]) != 0)
                {
                    throw new InvalidDataException("Unsupported frame flags.");
                }

                uint length = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
                if (length > MaximumBodyLength)
                {
                    throw new InvalidDataException("Body exceeds 8192 bytes.");
                }

                int size = HeaderLength + (int)length;
                if (_count - consumed < size)
                {
                    break;
                }

                frames.Add(new Frame(BinaryPrimitives.ReadUInt16BigEndian(header[4..]),
                    _buffer.AsSpan(consumed + HeaderLength, (int)length).ToArray()));
                consumed += size;
            }
            if (consumed > 0)
            {
                _buffer.AsSpan(consumed, _count - consumed).CopyTo(_buffer);
                _count -= consumed;
            }
            if (_count == _buffer.Length)
            {
                throw new InvalidDataException("Frame buffer cannot progress.");
            }
        }
        return frames;
    }

    public void Reset() => _count = 0;
}

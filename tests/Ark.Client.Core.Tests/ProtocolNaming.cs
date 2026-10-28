using System.Text.Json;
using System.Text.RegularExpressions;
using Ark.Client.Protocol;
using Google.Protobuf;
using Google.Protobuf.Reflection;

internal static class ProtocolNaming
{
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    public static void Verify()
    {
        FileDescriptor file = GuestRoomReflection.Descriptor;
        CheckName(Path.GetFileNameWithoutExtension(file.Name));
        foreach (string component in file.Package.Split('.'))
        {
            CheckName(component);
        }
        foreach (MessageDescriptor message in file.MessageTypes)
        {
            CheckMessage(message);
        }
        foreach (EnumDescriptor enumeration in file.EnumTypes)
        {
            CheckEnum(enumeration);
        }

        string[] names = { "action_unspecified", "action_damage", "action_skill", "action_rest" };
        action_kind[] kinds = { action_kind.ActionUnspecified, action_kind.ActionDamage, action_kind.ActionSkill, action_kind.ActionRest };
        for (int number = 0; number < kinds.Length; number++)
        {
            Program.Check((int)kinds[number] == number && action_request.Descriptor.Fields[2].EnumType.FindValueByNumber(number).Name == names[number],
                "Action enum spelling or numeric contract changed.");
            var request = new action_request { Sequence = 1, Kind = kinds[number] };
            byte[] expected = number == 0 ? new byte[] { 8, 1 } : new byte[] { 8, 1, 16, (byte)number };
            Program.Check(request.ToByteArray().SequenceEqual(expected), "Enum naming changed protobuf binary bytes.");
            if (number == 0)
            {
                continue;
            }
            string json = JsonFormatter.Default.Format(request);
            using JsonDocument document = JsonDocument.Parse(json);
            Program.Check(document.RootElement.GetProperty("kind").GetString() == names[number], "JSON enum text is not lower snake_case.");
            Program.Check(JsonParser.Default.Parse<action_request>(json).Equals(request), "Lowercase enum JSON did not roundtrip.");
        }
    }

    private static void CheckMessage(MessageDescriptor message)
    {
        CheckName(message.Name);
        foreach (FieldDescriptor field in message.Fields.InDeclarationOrder())
        {
            CheckName(field.Name);
        }
        foreach (MessageDescriptor nested in message.NestedTypes)
        {
            CheckMessage(nested);
        }
        foreach (EnumDescriptor enumeration in message.EnumTypes)
        {
            CheckEnum(enumeration);
        }
    }

    private static void CheckEnum(EnumDescriptor enumeration)
    {
        CheckName(enumeration.Name);
        foreach (EnumValueDescriptor value in enumeration.Values)
        {
            CheckName(value.Name);
        }
    }

    private static void CheckName(string name) => Program.Check(SnakeCase.IsMatch(name), "Protocol source identifier must be lower snake_case: " + name);
}

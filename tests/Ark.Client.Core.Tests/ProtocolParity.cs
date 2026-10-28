using System.Security.Cryptography;
using System.Text.Json;

internal static class ProtocolParity
{
    public static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "protocol", "manifest.json")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidDataException("Cannot locate protocol manifest.");
    }
    public static void Verify(string root, string? serverRoot)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "protocol", "manifest.json")));
        foreach (JsonProperty file in manifest.RootElement.GetProperty("sha256").EnumerateObject())
        {
            string path = Path.Combine(root, file.Name);
            string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            Program.Check(digest == file.Value.GetString(), "Pinned protocol digest mismatch: " + file.Name);
        }
        if (!string.IsNullOrEmpty(serverRoot))
        {
            foreach ((string client, string server) in new[] {
                ("protocol/guest_room.proto", "src/proto/proto/guest_room.proto"),
                ("tests/fixtures/wire-vectors.json", "examples/guest_room/wire-vectors.json") })
            {
                Program.Check(File.ReadAllBytes(Path.Combine(root, client)).SequenceEqual(File.ReadAllBytes(Path.Combine(serverRoot, server))), "Server/client protocol byte mismatch: " + client);
            }
        }
        Console.WriteLine("PROTOCOL PARITY PASS protoc=33.4 Google.Protobuf=3.33.4" + (string.IsNullOrEmpty(serverRoot) ? " pinned_sha256" : " pinned_sha256 server_bytes"));
    }
}

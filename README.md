# ark-client-godot

A Godot C# client for OpenArkStudio [ark](https://github.com/OpenArkStudio/ark).
The first example targets a 2D guest room with movement and reconnect.

[中文说明](README_CN.md)

This bootstrap establishes the repository. Client implementation and validation
follow in a focused change. Guest identities are temporary; this example does
not provide account authentication or durable player data.

## Development

Use the Godot .NET editor and its matching .NET SDK. Keep portable protocol and
transport code independent from Godot scene nodes. Keep executable tests in C#
and Godot, with shell scripts permitted for orchestration.

See [AGENTS.md](AGENTS.md) for contribution conventions. Licensed under
[Apache-2.0](LICENSE).

# Local macOS development export

Use the Godot **4.7.2 .NET** editor, its matching **.NET export templates**,
and .NET SDK **8.0.427** on macOS. Select the external engine and SDK as described
in [run instructions](run.md), including the SDK directory on `PATH`. Install the
matching templates through **Editor > Manage Export Templates**, or extract them
into the engine's external template directory. A self-contained Godot editor can
keep its templates and settings outside the normal user profile; see the
[official data-path documentation](https://docs.godotengine.org/en/4.7/tutorials/io/data_paths.html#self-contained-mode).
The first export also needs network access or a populated external NuGet cache
for the `osx-arm64` and `osx-x64` .NET runtime packs. Keep downloaded tools,
packages and app bundles outside version control.

```sh
# Choose a new absolute destination; an existing app is never overwritten.
bash tools/export-macos.sh /absolute/output/ArkGuestRoom.app
```

The checked-in `macOS development` preset uses the official **Universal 2**
template and exports a debug app with code signing and notarization disabled.
The engine imports resources and publishes the C# assembly and runtime for each
architecture into the bundle. Core dependencies retain locked restore: macOS
publishes select separate RID lock files; ordinary portable builds keep the
original lock. The project enables ETC2/ASTC imports, which Godot requires for
universal exports. The script also rejects engine error diagnostics because a
failed .NET publish can otherwise leave an incomplete app with engine exit 0.

This is a local development package. No application signing or notarization is
performed; the upstream template executable may retain Godot's own signature,
which does not sign this application bundle. It has no distribution acceptance;
downloaded unsigned apps may be blocked
by Gatekeeper. The script does not change system settings or remove quarantine
attributes.

Launch the embedded executable directly, including the `--` separator before
client options:

```sh
"/absolute/output/ArkGuestRoom.app/Contents/MacOS/ARK Guest Room" \
  -- --host=127.0.0.1 --port=19100 --name=Alice --connect
```

For automated acceptance, start a matching guest-room server with an empty
lobby, then run the existing two-client smoke against the exported executable:

```sh
EXPORTED_CLIENT="/absolute/output/ArkGuestRoom.app/Contents/MacOS/ARK Guest Room" \
  bash tools/smoke-two-clients.sh 127.0.0.1 19100 --render
```

`EXPORTED_CLIENT` must be an absolute executable path. In this mode the runner
omits `--path` so each process uses its bundled resources and assemblies. It runs
the same scene assertions for join, movement, own/peer HP/MP and reconnect with
fresh identities, retaining logs and viewport captures in the printed temporary
directory. `--render` requires a graphical desktop; omit it for a headless run.
Without `EXPORTED_CLIENT`, the runner continues to launch the project through
`GODOT`. Export success, bundled-app execution, local server integration and
visual inspection establish separate facts. A universal bundle does not itself
establish Intel Mac execution. This flow does not validate Windows, Linux,
cross-host behavior, release packaging or production use.

Official references: [macOS export](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html)
and [Godot .NET downloads](https://godotengine.org/download/macos/).

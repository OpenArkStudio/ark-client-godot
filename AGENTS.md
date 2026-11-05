# Contributor and agent instructions

- Use English Conventional Commits and short lowercase scopes. Name topic
  branches `type/short-kebab-case`. Changes to `main` go through a pull request.
- Use conventional C# naming for handwritten code: PascalCase for types,
  namespaces, methods and public properties; camelCase for locals and parameters; `_camelCase` for private
  fields. Keep namespaces under `Ark.Client`.
- Keep portable protocol, state and transport code independent from Godot APIs.
  Keep scene nodes responsible for engine lifecycle and presentation.
- Preserve the approved sample wire contract and validate lengths, flags, finite
  positions and sequence bounds before changing room state. Do not infer login,
  authentication, persistence, or compatibility with legacy Unity protocols.
- Room message bodies use Protobuf; Entity definitions and business profiles
  remain JSON. The canonical schemas are ark's `src/proto/proto/entity_sync.proto`
  and `src/proto/proto/guest_room.proto`. Keep both local schemas and shared golden vectors byte-identical, regenerate
  message source with the declared protoc version, and keep the runtime pinned.
  Do not hand-edit generated C#. Run protocol parity, portable tests and actual
  server/two-client checks for wire changes. Upgrade incompatible client/server
  contracts together and document the migration; preserve owning, atomic
  baselines/deltas, revision validation and bounded reconnect recovery. The
  current generic Entity protocol is the only contract; use handshake value 2,
  without version suffixes in files, messages or APIs and without a legacy
  scalar compatibility path. Rebuild incompatible peers together.
- Keep `NestedReplica` independent from the guest-room projection. Supply trusted
  layouts before remote state, preserve all nine scalar types and owning byte
  values, and validate complete selected tables/containers, row incarnations,
  child bindings and lifetime watermarks within configured limits. Stage receiver
  forks and presentation projections before committing a whole room envelope.
  Inventory/equipment UI must change only from committed server values. Tests
  must retain evidence for both players at each action phase, row reuse and
  child GUID/incarnation continuity before accepting reconnect recovery.
- Use lower snake_case for `.proto` filenames, package components, messages,
  enums, fields and enum values. Keep field tags and enum numbers stable during
  naming changes. C# properties and enum members follow the official generator's
  naming and retain its PascalCase API; do not rename them by hand. Enum source
  renames also change Protobuf JSON enum text, even when binary bytes stay equal.
- Keep English and Chinese documentation aligned with changed behavior. Document
  exact prerequisites, commands, tested revisions and material limitations.
- Follow the ark/arknet README layout: separate light/dark ARK and Godot pictures, language
  navigation, centered introduction and badges, then concise features and setup.
  Keep packet magic and internal test labels in protocol documentation rather
  than presenting them as product names or release versions.
- Use permanent C# and actual Godot tests. Do not use Python or Ruby launch/test
  entrypoints. Shell scripts may orchestrate builds and tests. Keep artifacts,
  SDKs, engine binaries, logs and credentials out of version control.
- Coordinate shared compiler resources and use serial build execution when
  required. Distinguish portable checks, engine build, engine execution and real
  server integration; one does not establish the others.
- Across the coordinated work, maintain three delivery lines when independent,
  authorized tasks have ready dependencies. Refill each completed line in the
  confirmed priority order; do not repeat passed validation or expand unconfirmed
  scope to fill a slot. This does not authorize scheduled or unattended automation.
- Preserve unrelated work. The root agent alone observes hosted CI and controls
  remote creation, pushes, PR readiness and per-repository FIFO admission for the
  current coordinated delivery.

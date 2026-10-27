# Contributor and agent instructions

- Use English Conventional Commits and short lowercase scopes. Name topic
  branches `type/short-kebab-case`. Changes to `main` go through a pull request.
- Use conventional C# naming: PascalCase for types, namespaces, methods and public
  properties; camelCase for locals and parameters; `_camelCase` for private
  fields. Keep namespaces under `Ark.Client`.
- Keep portable protocol, state and transport code independent from Godot APIs.
  Keep scene nodes responsible for engine lifecycle and presentation.
- Preserve the approved sample wire contract and validate lengths, flags, finite
  positions and sequence bounds before changing room state. Do not infer login,
  authentication, persistence, or compatibility with legacy Unity protocols.
- Keep English and Chinese documentation aligned with changed behavior. Document
  exact prerequisites, commands, tested revisions and material limitations.
- Follow the ark/arknet README layout: shared light/dark ARK branding, language
  navigation, centered introduction and badges, then concise features and setup.
  Keep packet magic and internal test labels in protocol documentation rather
  than presenting them as product names or release versions.
- Use permanent C# and actual Godot tests. Do not use Python or Ruby launch/test
  entrypoints. Shell scripts may orchestrate builds and tests. Keep artifacts,
  SDKs, engine binaries, logs and credentials out of version control.
- Coordinate shared compiler resources and use serial build execution when
  required. Distinguish portable checks, engine build, engine execution and real
  server integration; one does not establish the others.
- Preserve unrelated work. The root agent owns remote creation, pushes, pull
  requests and hosted CI observation for the current coordinated delivery.

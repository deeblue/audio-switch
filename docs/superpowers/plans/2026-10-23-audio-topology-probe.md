# Audio Topology Probe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a disposable, read-only Python probe that determines whether Windows can map each audio endpoint through device topology to the physical connectors on this ThinkPad T14p Gen 3 (21RU).

**Architecture:** Collect raw Core Audio, PnP, jack, display-path, and host-model data into versioned snapshots. Classify and render endpoints from evidence plus an explicitly calibrated host connector map; preserve unknown relationships and per-property errors instead of guessing. Physical plug/unplug operations are user-run acceptance steps, not automated actions.

**Tech Stack:** Python 3.12; `comtypes` for COM; standard-library `ctypes`, `json`, `argparse`, and `unittest`; optional `pycaw` only where it reduces Core Audio declarations without hiding endpoint IDs or errors.

**Spec:** `README.md` sections “呈現方式”, “端點來源分類與接口對應”, “本機可行性實驗”, and “實作計畫”; `CLAUDE.md`; `probes/TASK.md`.

## Global Constraints

- The probe is read-only: never call `IPolicyConfig`, change a default endpoint, write device properties, or modify the registry.
- Target Python 3.12 using `py -3.12`; create the environment at `probes/.venv`.
- Use Traditional Chinese for output and probe documentation; use English for code and identifiers.
- Process `DEVICE_STATE_ACTIVE` endpoints for classification; raw output may include inactive endpoints for context.
- A failed endpoint or property read records its error and does not stop collection of other endpoints.
- Write snapshots and JSON output under `probes/out/`; ignore that directory in Git.
- Do not modify `D:\WORK\Claude\poc\AI-Vibe-Hub`; do not commit unless asked.
- Machine identity is read from SMBIOS and checked against the 21RU connector map; warn on mismatch and never apply the map silently to another model.
- Do not treat names, VID/PID, ContainerId, or a bus label alone as proof of a physical connector relationship.

## Review Focus

- Two endpoints with identical friendly names remain distinct by endpoint ID and do not collapse in tree output.
- Missing or unsupported properties (StableId, jack descriptions, `_PLD`, sink information) become null plus a recorded error; collection continues.
- USB hub, composite-device, and Dock ContainerId patterns may differ from the hypothesis; classifier must preserve the raw chain and return unknown when evidence is insufficient.
- Display-audio sink `PortId` may not match DisplayConfig LUID/target identifiers; the probe must record both raw forms and the attempted match method.
- Snapshot comparisons can contain repeated IDs, disappeared parents, changed IDs, and identical monitor EDIDs; `diff` must report evidence without inventing a match.

---

### Task 1: Define snapshot model, CLI shell, and output directory

**Files:**
- Create: `probes/requirements.txt`
- Create: `probes/audio_topology.py`
- Create: `probes/audio_topology/__init__.py`
- Create: `probes/audio_topology/models.py`
- Create: `probes/audio_topology/cli.py`
- Create: `probes/tests/__init__.py`
- Create: `probes/tests/test_models.py`
- Create: `probes/tests/test_cli.py`
- Create or modify: `.gitignore`

**Interfaces:**
- `EndpointRecord`, `DeviceNodeRecord`, `JackRecord`, `DisplayPathRecord`, and `MachineRecord` are dataclasses in `models.py`.
- `SnapshotRecord` contains `schema_version: int`, `captured_at: str`, `machine: MachineRecord`, `endpoints: list[EndpointRecord]`, `devnodes: list[DeviceNodeRecord]`, `display_paths: list[DisplayPathRecord]`, and `errors: list[dict[str, str]]`.
- `to_jsonable(value: object) -> object` converts dataclasses, enums, bytes, LUIDs, and nested collections to stable JSON-safe values.
- `build_parser() -> argparse.ArgumentParser` exposes `tree`, `raw`, `json`, `snapshot`, `diff`, `calibrate`, and optional `tone` subcommands; execution is wired in later tasks.

- [ ] Write `test_jsonable_serializes_nested_records_and_bytes` and `test_snapshot_schema_version_is_preserved` in `probes/tests/test_models.py`.
- [ ] Run `py -3.12 -m unittest probes.tests.test_models -v`; verify the tests fail because model serialization is not implemented.
- [ ] Implement the dataclasses and stable serializer; use schema version `1` and UTF-8 JSON.
- [ ] Add parser tests for required subcommand arguments and the exact `calibrate <interface-id> <before> <after>` shape.
- [ ] Run `py -3.12 -m unittest discover -s probes/tests -v`; verify model and parser tests pass.
- [ ] Add `probes/out/` and `probes/.venv/` to `.gitignore`; verify `git status --short` does not report generated output after a smoke run.

### Task 2: Enumerate Core Audio endpoints and record endpoint-level errors

**Files:**
- Create: `probes/audio_topology/native/core_audio.py`
- Create: `probes/tests/test_core_audio.py`
- Modify: `probes/audio_topology/models.py`

**Interfaces:**
- `collect_audio_endpoints(enumerator: AudioEnumerator | None = None) -> tuple[list[EndpointRecord], list[dict[str, str]]]` returns active endpoint records and collection errors.
- Each endpoint records endpoint ID, flow, state, friendly name, form factor, ContainerId, StableId if present, and whether it is the default for each of `eConsole`, `eMultimedia`, and `eCommunications` in that flow.
- Native COM objects stay inside `core_audio.py`; downstream modules consume records only.

- [ ] Write `test_duplicate_names_keep_distinct_endpoint_ids`, `test_unsupported_property_is_null_and_recorded`, and `test_failed_endpoint_does_not_stop_collection` with a fake enumerator.
- [ ] Run `py -3.12 -m unittest probes.tests.test_core_audio -v`; verify these tests fail before collection is implemented.
- [ ] Implement Core Audio enumeration with `comtypes` (use `pycaw` only if it exposes every required identity and default-role value); isolate every endpoint/property read in error capture.
- [ ] Run the focused tests and full unit suite; verify duplicate names stay separate and one COM error does not abort later endpoints.
- [ ] Run `py -3.12 probes/audio_topology.py raw`; verify the local active endpoint count and names match the previously recorded 3 render / 2 capture endpoints, or record any changed machine state without treating it as a failure.

### Task 3: Map endpoint IDs to PnP devnodes and collect ancestor properties

**Files:**
- Create: `probes/audio_topology/native/pnp.py`
- Create: `probes/tests/test_pnp.py`
- Modify: `probes/audio_topology/models.py`

**Interfaces:**
- `resolve_endpoint_devnode(endpoint_id: str, topology_device_id: str | None) -> tuple[str | None, str]` returns a devnode instance ID and the successful method (`device_topology`, `mmdevapi_devnode`, or `unresolved`).
- `walk_ancestors(instance_id: str, api: CfgMgrApi | None = None) -> tuple[list[DeviceNodeRecord], list[dict[str, str]]]` walks parent nodes until root, cycle, missing parent, or API error.
- Store InstanceId, parent InstanceId, name, bus-reported description, class, location info, location paths, ContainerId, removal policy, capabilities/removable flag, and raw `DEVPKEY_Device_PhysicalDeviceLocation` bytes plus parsed `_PLD` fields when parseable.

- [ ] Write tests for each endpoint-to-devnode strategy, parent traversal to root, cycle protection, missing properties, and a parent lookup error.
- [ ] Run `py -3.12 -m unittest probes.tests.test_pnp -v`; verify failure before implementation.
- [ ] Implement the two resolution methods specified in `probes/TASK.md`; retain both raw results and the method selected when both resolve.
- [ ] Implement `CM_Get_DevNode_PropertyW`, `CM_Get_Parent`, and best-effort `_PLD` parsing with property-level error capture.
- [ ] Run focused and full tests; then run `raw` locally and verify the `Speakers (speaker)` ancestor chain retains `USB\VID_303A&PID_2986` when present.

### Task 4: Collect audio jack and display-sink connector data

**Files:**
- Create: `probes/audio_topology/native/audio_topology.py`
- Create: `probes/tests/test_jacks.py`
- Modify: `probes/audio_topology/models.py`

**Interfaces:**
- `collect_jacks(endpoint_id: str) -> tuple[list[JackRecord], list[dict[str, str]]]` queries `IKsJackDescription` and `IKsJackDescription2` on the connected adapter connector.
- `collect_jack_sink(endpoint_id: str) -> tuple[dict[str, object] | None, list[dict[str, str]]]` queries `IKsJackSinkInformation` and retains raw `PortId` LUID fields, ConnType, ManufacturerId, ProductId, and SinkDescription.
- Unsupported interfaces return no value plus a structured HRESULT/message; they do not invalidate the endpoint.

- [ ] Write tests that retain all jack fields, preserve raw LUID components, and treat `E_NOINTERFACE` as a local capability gap.
- [ ] Run `py -3.12 -m unittest probes.tests.test_jacks -v`; verify failure before collection is implemented.
- [ ] Implement connector traversal and the three optional interfaces; do not infer a jack or monitor name from the endpoint's friendly name.
- [ ] Run focused and full tests; run `raw` locally and check whether the two Realtek render endpoints expose distinct connector/jack records.

### Task 5: Enumerate active DisplayConfig paths and connector identity

**Files:**
- Create: `probes/audio_topology/native/display_config.py`
- Create: `probes/tests/test_display_config.py`
- Modify: `probes/audio_topology/models.py`

**Interfaces:**
- `collect_display_paths(api: DisplayConfigApi | None = None) -> tuple[list[DisplayPathRecord], list[dict[str, str]]]` calls `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` and `DisplayConfigGetDeviceInfo` for source and target.
- Each record includes raw outputTechnology, connectorInstance, source GDI name, target friendly name, monitorDevicePath, EDID manufacturer/product IDs, adapter LUID, source position, and primary-display status where available.
- `match_sink_to_display(sink: dict[str, object], paths: list[DisplayPathRecord]) -> dict[str, object]` returns `matched`, `method`, `candidate_ids`, and confidence evidence; it must not silently select among multiple EDID matches.

- [ ] Write tests for exact LUID/target match, EDID fallback, duplicate EDID ambiguity, and no match.
- [ ] Run `py -3.12 -m unittest probes.tests.test_display_config -v`; verify failure before implementation.
- [ ] Implement DisplayConfig enumeration, preserving numeric enums and raw IDs; add the explicit sink matching procedure from `probes/TASK.md`.
- [ ] Run focused and full tests; compare local active monitor count and names with Windows Display Settings and inspect sink match output without claiming physical port mapping yet.

### Task 6: Host connector map, snapshots, diff, and calibration

**Files:**
- Create: `probes/ports.json`
- Create: `probes/audio_topology/snapshots.py`
- Create: `probes/audio_topology/calibration.py`
- Create: `probes/tests/test_snapshots.py`
- Create: `probes/tests/test_calibration.py`
- Modify: `probes/audio_topology/models.py`

**Interfaces:**
- `collect_machine_identity() -> MachineRecord` reads SMBIOS manufacturer, model, and MTM.
- `load_port_map(path: Path) -> PortMap` checks schema and machine identity; a mismatch produces a warning and disables calibration matches.
- `save_snapshot(snapshot: SnapshotRecord, name: str, output_dir: Path) -> Path` writes `probes/out/<name>.json`.
- `diff_snapshots(before: SnapshotRecord, after: SnapshotRecord) -> DiffRecord` reports added/removed endpoints, devnodes, display paths, and changed existing devnodes (including LocationPaths), plus common prefixes.
- `calibrate_port_map(interface_id: str, before: SnapshotRecord, after: SnapshotRecord, port_map: PortMap) -> CalibrationResult` adds only unambiguous new USB LocationPaths prefixes or display connector identifiers and records source `calibrated`.

- [ ] Write tests for deterministic snapshot round-trip, repeated IDs, parent disappearance, changed IDs, common path prefixes, ambiguous candidates, and machine mismatch.
- [ ] Run focused tests; verify failures before implementing persistence and matching.
- [ ] Create the 21RU port map from the Lenovo guide; each physical connector has a source citation and an ID, with position `unknown` where the manual does not establish it. Begin calibration arrays empty.
- [ ] Implement snapshot/diff/calibration writes atomically; never modify the map when candidates are ambiguous.
- [ ] Run focused and full tests; verify generated snapshots go only to `probes/out/` and that calibration is refused for a non-21RU identity.

### Task 7: Classify endpoints and render the interface-rooted tree

**Files:**
- Create: `probes/audio_topology/classify.py`
- Create: `probes/audio_topology/render.py`
- Create: `probes/tests/test_classify.py`
- Create: `probes/tests/test_render.py`
- Modify: `probes/tests/test_cli.py`

**Interfaces:**
- `classify_endpoint(endpoint: EndpointRecord, nodes: list[DeviceNodeRecord], jacks: list[JackRecord], sink_match: dict[str, object] | None, paths: list[DisplayPathRecord], port_map: PortMap) -> ClassificationRecord` returns category A/B/C/D, matched interface, visible ancestors, evidence sources, and unresolved reasons.
- `build_tree(snapshot: SnapshotRecord, port_map: PortMap) -> TreeRecord` creates the README connector-root hierarchy, including `built-in`, `Bluetooth/wireless`, `virtual`, and `other/unrecognized` roots.
- `render_tree(tree: TreeRecord) -> str` prints Traditional Chinese labels and source markers `OEM`, `Windows`, `校準`, `推定`, or `未知`.

- [ ] Write scenario tests for a Dock with its own audio endpoint and a USB speaker below its hub, a display-audio endpoint matched by sink/DisplayConfig, a 3.5mm jack with and without presence detection, identical endpoint names, and an unknown device.
- [ ] Run `py -3.12 -m unittest probes.tests.test_classify probes.tests.test_render -v`; verify failure before implementation.
- [ ] Implement only the four documented evidence-based classifications. ContainerId and removability may group Dock nodes but cannot establish the group alone; ambiguous cases stay unknown and raw evidence remains visible.
- [ ] Run focused and full tests; compare rendered fixture output to the README tree's structure and verify endpoint IDs remain unique.

### Task 8: Wire CLI commands, user-operated calibration procedure, and report

**Files:**
- Modify: `probes/audio_topology/cli.py`
- Modify: `probes/audio_topology.py`
- Create: `probes/README.md`
- Create: `probes/out/.gitkeep` only if the empty directory must be retained
- Modify: root `README.md` feasibility results after actual probe evidence exists

**Interfaces:**
- `run_command(args: argparse.Namespace, collector: Collector | None = None) -> int` dispatches all subcommands and returns a process exit code.
- `tree` collects current state and renders; `raw` emits JSON-friendly detailed chains; `json [filename]` emits raw JSON; `snapshot <name>` stores a snapshot; `diff <before> <after>` prints added/removed records; `calibrate <interface-id> <before> <after>` persists only unambiguous matches.
- `tone` is optional; when present it opens a shared-mode WASAPI stream only for the explicitly supplied active render endpoint and plays about one second. It never sets a default endpoint.

- [ ] Add CLI integration tests using fixture collectors and temporary output directories for each required command and invalid input.
- [ ] Run `py -3.12 -m unittest discover -s probes/tests -v`; verify all tests pass.
- [ ] Implement command dispatch and Traditional Chinese help/errors; add `probes/README.md` with setup, command examples, read-only guarantee, data handling, and a numbered plug/unplug checklist with the exact commands and expected diffs.
- [ ] In a disposable environment, run each CLI command against fixtures; verify `tone` is omitted unless its WASAPI path is independently verified.
- [ ] Run `py -3.12 probes/audio_topology.py raw` and `tree` against this machine; verify collection survives every unsupported property and the output preserves unknown classifications.
- [ ] Present the user with the manual test checklist for USB-C/TB ports, USB-A ports, HDMI, 3.5mm, Dock, display, USB headset, USB speaker, and 7C; do not claim those physical paths verified until the user runs the exact snapshots/diffs.
- [ ] After receiving the resulting evidence, update only current findings in `README.md` “本機可行性實驗” and list remaining gaps in “尚未驗證”.

## Completion Gate

The probe is ready for physical validation when all required commands run, unit tests pass, local `raw` and `tree` complete without terminating on unsupported properties, `ports.json` is tied to the 21RU machine identity, and `probes/README.md` gives exact user-operated calibration steps. Product implementation remains a separate later plan based on the probe report and resolved product decisions.


> 執行狀態：探針已在 feature/audio-topology-probe 分支建立部分實作。各任務尚有未完成項目；目前證據、缺口與人工驗證步驟記錄在 README 與 probes/README.md。

# Deco Limit Lifter (From The Depths)

Lift the ~5k decoration cap in **From The Depths** by extending the blueprint save format. Small crafts keep vanilla compatibility; huge crafts gain headroom without corrupting saves.

> **TL;DR**
>
> * Saves and loads **vanilla** format when it fits (under the usual limits).
> * Automatically switches to an extended **sentinel** format only when required (big crafts).
> * Vanilla FtD can load **legacy** saves; **sentinel** saves require this mod.

---

## Why this exists

FtD blueprints store a header and a data block. With lots of decorations, those lengths exceed vanilla’s 16-bit limits. This mod writes a backwards-compatible “legacy” layout when it fits and a “sentinel” layout when it doesn’t, so you can build big without breaking small.

---

## Features

* Backwards-compatible load path for both **legacy** and **sentinel** saves.
* Auto-switching saver: prefers **legacy**, escalates to **sentinel** only when needed.
* Sensible default buffers for small crafts; safe **auto-grow** for large crafts.
* Runtime soft cap for decorations (default 100k).
* Capacity guards to prevent index out-of-range and save corruption.
* Minimal overhead for normal-sized builds; predictable growth for huge ones.

---

## Requirements

* From The Depths (PC)
* Harmony patching (the mod uses Harmony attributes under the hood) (not important for end-users)
* Windows-compatible .NET runtime included with the game (no separate install for end-users)

---

## Installation

1. Download the release (or build from source).
2. Copy the entire **DecoLimitless** folder into your FtD **Mods** folder.

   * Typical paths:

     * Windows (Steam):
       `...\Steam\steamapps\common\From The Depths\Mods\`
     * Linux (Proton) path varies; place alongside other working mods.
3. Start the game. Enable the mod in the Mod Manager if it isn’t already.
4. The mod logs one informational startup line. A customer-facing error appears only if a required Harmony patch fails.

---

## Usage

Just play as normal.

* Saves remain **vanilla-compatible** while a serialized module has at most 9,362 headers and 6,553,500 data bytes.
* Payloads beyond either wire-format limit switch to **sentinel** automatically and require this mod to load.

---

## Configuration (advanced)

Tuning knobs live in `DecoLimits.cs`. Keep default values unless you consistently hit the guardrails.

| Key                  |     Default | What it controls                     |
| -------------------- | ----------: | ------------------------------------ |
| `MaxDecorations`     |     100 000 | Runtime soft cap on decorations      |
| `SaveBufferBytes`    |  20 000 000 | Global save buffer floor (ByteStore) |
| `MaxSaveBufferBytes` | 268 435 456 | Hard ceiling for global buffer       |
| `MaxDataSortedBytes` |  67 108 864 | Hard ceiling for data block          |
| `MaxHeaderBytes`     |   4 194 304 | Hard ceiling for header block        |

Known FtD output buffers grow automatically from the exact serialized size. No decoration-count estimate is required.

---

## Compatibility & limits

* **Vanilla FtD** can load **legacy** saves. It **cannot** load **sentinel** saves.
* **Sentinel** multiplayer is experimental. Every peer must run the same mod version; unmodded peers cannot decode it.
* Extremely large blueprints are ultimately bounded by your configured ceilings.

---

## Troubleshooting

* **A configured buffer ceiling was exceeded**
  The mod stops before writing partial data. Raise the relevant hard ceiling only if the machine has enough memory.

* **“Harmony failed to patch (explicit interface)”**
  Usually a version mismatch. Update the mod to match your game build so method signatures resolve correctly.

* **Performance feels worse on small saves**
  Ensure you didn’t crank the buffer ceilings unnecessarily. Defaults keep small saves snappy while allowing on-demand growth.

---

## How it works (short)

### Saver formats

* **Legacy (vanilla-compatible)**

  ```
  [UInt16 headerLen]
  [UInt16 pad=0]
  [dataLen split into ≤100 chunks of UInt16]
  [header bytes]
  [data bytes]
  ```

* **Sentinel (extended)**

  ```
  [0xFFFF]
  [UInt32 headerLen]
  [UInt32 dataLen]
  [header bytes]
  [data bytes]
  ```

The loader reads **both** layouts. The saver writes **legacy** whenever possible, and **sentinel** only if any length exceeds vanilla limits.

### Buffering strategy

* Known global output buffers (`ByteStore.MegaBytes` and multiplayer `fullArray`) grow from the exact required size and never shrink during the session.
* Header/Data arrays grow on demand to the next power-of-two, preserving existing bytes and respecting hard ceilings.
* Small saves reuse vanilla-sized pools; large saves take a few predictable growth steps.

---

## Building from source

> Target framework: **.NET Framework 4.7.2**
> IDE: Visual Studio 2017+ (or `msbuild`)

1. Open `DecoLimitLifter.sln`.
2. Set `FTD_DIR` to the game installation directory; the project resolves the required game assemblies from it.
3. Build `Release`.
4. A Release build copies the produced DLL into `DecoLimitless`; copy that complete folder into `From The Depths\Mods\`.

**Notes**

* No external NuGet packages are required for the core patches.
* If you change any constants in `DecoLimits.cs`, keep the ceilings sane to avoid OOM.

---

## Performance expectations

* Small blueprints: no extra allocations on save/delete.
* Large blueprints: bounded growth; guardrails prevent overruns and corruption.
* Debug tracing is disabled by default; successful startup emits only an informational log line.

---

## Sharing blueprints

* **Legacy**: freely share with anyone (vanilla loads fine).
* **Sentinel**: share only with players using this mod.

---

## Contributing

Issues, bugs and PRs are welcome. Please include:

* Game build number and steps to reproduce.
* A minimal blueprint (or counts) that triggers the problem.
* Logs or exact exception messages.

You can also DM me on Discord: **albeeettt**.

---

## Roadmap / future ideas

* Safer multiplayer behavior (investigation).
* Config UI for buffer sizing hints and ceilings.
* Additional diagnostics toggles, sentinel / legacy pre-viewer. 

---

## Credits

* Built with **Harmony**; thanks to community members who helped with early testing and interfaces: DeltaEpsilon & wolficik

---

## License

MIT. See `DecoLimitless/LICENSE`.

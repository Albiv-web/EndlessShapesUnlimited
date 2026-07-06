# Automation Editor nested workspace goal

Source: persistent user goal captured in this Codex thread.

## Goal

Turn the ESU Automation Editor into a Blender-like nested automation workspace
for From the Depths.

The editor should let users select and place native FtD automation controllers,
discover valid craft targets in the 3D viewport, link targets to controllers,
open a HUD-native graph editor, and build automation using native
Breadboard/ACB-compatible nodes. It should support beginner-friendly templates
and advanced nested System Blocks, where a single visible block can contain an
internal graph with exposed input and output ports. Users must be able to open a
System Block, edit its internals, define ports, reuse it as a template, and
lower it to native FtD behavior wherever possible.

## Production constraints

- Do not create a parallel automation runtime for the first production version.
- Prefer native Breadboard, AI Breadboard, ACB, ACB Controller, and Missile
  Breadboard Controller data for committed behavior.
- Store ESU-only data only for layout, names, comments, templates, breadcrumbs,
  and optional grouping metadata.
- Treat dynamic Generic Getter/Setter targets as runtime-discovered
  capabilities.
- Keep code and recipe features deterministic and compile them into graph nodes;
  do not run arbitrary C# or arbitrary scripts.
- Every edit path needs Check, Apply, and Revert.
- Every HUD state must make the next safe user action obvious.

## Phases

1. Add the UX vocabulary, mode states, color grammar, and onboarding copy.
2. Polish the HUD with collapsible panels, next-step prompts, search, tooltips,
   and empty states.
3. Stabilize world controller/target discovery and link previews.
4. Build the native graph viewer.
5. Build the native graph editor.
6. Add nested System Blocks with exposed ports and breadcrumbs.
7. Add deterministic recipe/code compilation to graph nodes.
8. Add live simulation/debugging values.

## Definition of done

- A new user can create a working simple automation through guided selection and
  templates.
- An advanced user can create a reusable nested System Block with named ports.
- Existing vanilla Breadboards/ACBs can be viewed without mutation.
- Generated behavior remains native-compatible unless explicitly marked
  otherwise.
- The UI remains readable on a large craft with thousands of potential targets.

## Current implementation notes

Use this document together with
`docs/AUTOMATION_EDITOR_RESEARCH_AND_DESIGN.md`. The research document records
the discovered FtD runtime surfaces and current ESU architecture. This goal
document records the desired end state and should be referenced when deciding
whether a smaller Automation Editor change is moving toward the nested native
workspace rather than only polishing the current HUD.

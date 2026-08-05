# Architecture Decision Records (ADRs)

Flirty's decision history. An ADR records **one** fundamental decision – above all
the thing that gets left nowhere else: the **discarded alternatives** and their reason.

This makes the boundary to the `docs/` guides clear: a **guide describes how something works**
and is kept up to date with the code. An **ADR describes why it is this way and not otherwise** and
is not kept up to date. Whoever is looking for an instruction is right in the guide (signpost in the
`CLAUDE.md` in the repo root); whoever wants to know why a nearby alternative was *not* chosen,
here.

## Decisions

| No. | Title | Status | Context issue |
|---|---|---|---|
| [0001](./0001-migrations-per-provider.md) | Migrations per provider (separate assemblies) | Accepted | #19 |
| [0002](./0002-mediator-as-in-process-bus.md) | Mediator (martinothamar) as the in-process bus | Accepted | #14 |
| [0003](./0003-aspnet-free-core.md) | ASP.NET-free core, web as an opt-in package | Accepted | #13 |
| [0004](./0004-sandboxed-expression-engine.md) | Sandboxed expression engine behind an abstraction | Accepted | #22/#23 |
| [0005](./0005-immutable-published-dialog-version.md) | Published dialog versions are immutable | Accepted | #95 |
| [0006](./0006-canvas-technology-in-the-designer.md) | Canvas technology in the designer: in-house SVG instead of a diagram library | Accepted | #100 |
| [0007](./0007-layout-as-its-own-table.md) | Canvas layout as its own table, with a guard-free layout command | Accepted | #102 |
| [0008](./0008-gestures-on-the-canvas.md) | Editing gestures on the canvas: existing commands, reload, lock per gesture | Accepted | #103 |
| [0009](./0009-mcp-as-its-own-opt-in-package.md) | MCP server as its own opt-in package | Accepted | #126 |
| [0010](./0010-mcp-database-targets-by-route.md) | MCP database targets declared by the host, selected by the route | Accepted | #129 |

The ADRs lean on one another: 0002 forces all handlers to live in the core – which secures 0003
(a thin, replaceable web layer) technically. 0004 hangs on the designer's ability to
change expressions without a deployment; 0001 on a single package bringing all three providers along. 0005
is the latecomer: it makes a promise redeemable that the domain model had only *prepared* since #17 –
and is thereby the example that a guide can describe what the code does not hold. 0006 is the flip side of 0003:
because there the *core* is kept free of ASP.NET, the designer may conversely be browser-near –
a collocated JS module in the designer breaks no promise of the package. And
it is the only ADR that stands on **measured** numbers instead of a trade-off. 0007 draws a
boundary at 0005: the publish lock applies to the *graph*, and because canvas coordinates live in their own
table, the guard-free layout command is not a gap but the edge of the scope.
0005 is thereby **not** rewritten – its 16 call sites stay as they are. 0008 conversely restricts
0007: its "the commit does not reload" still holds, but only for the *layout* path, because
only its command returns the complete state – a graph change must reload, otherwise
the graph-wide computed warnings would be a claim of the client. Here too 0007 is not
rewritten but quoted. 0009 finally applies 0003 one layer further out: there the *core* is kept free of
ASP.NET so that a console consumer does not pay for HTTP, and by the same argument the *web* package is
kept free of the MCP SDK so that an HTTP consumer does not pay for MCP. It is thereby the first ADR that
overturns a sentence in its own EPIC – the EPIC had promised "no new project", and the measured
dependency was the reason not to keep that promise. 0010 redeems the one **Open** point 0009 left, and
overturns a second sentence of the same EPIC, this time not on a measurement but on a *protocol fact*: the
revision that makes 0009's stateless transport mandatory also removed the session the EPIC wanted to keep
the selected database in. So the authority moves to the host, and the client names a target in the route –
which is why 0009 is not rewritten either: nothing in it prejudged this, and its own text says so.

## Format

The template is ADR 0001; new ADRs adopt the structure unchanged so the folder stays homogeneous:

```markdown
# ADR NNNN – <Title>

- **Status:** Accepted | Superseded by NNNN
- **Context issue:** #NN – <Issue title>
- **Affected:** <Projects / paths>

## Context                  What problem was at hand? Which constraints applied?
## Decision                 What holds now – concise and verifiable.
## Discarded alternatives   What was nearby and why did it fall out?
## Consequences             Positive / Negative / Open – including the uncomfortable.

Details: [<GUIDE>.md](../<GUIDE>.md).
```

The language is **English** (as in the whole repo), line length ~100 characters.

## Maintenance

- **An ADR is not rewritten.** If a decision changes, the ADR either gets a
  short **addendum** (when only an "Open" point was resolved, see 0001) or it is superseded by a
  **new** ADR; the old one keeps its text and switches its status to
  `Superseded by NNNN`. The value of an ADR lies in showing the state at the time of the decision –
  a retroactively smoothed ADR no longer answers the question "why, actually?".
- **Numbers are assigned consecutively and never reassigned.** 0001 stays 0001, even though the
  decision fell chronologically after #13/#14: the file is pointed at by `CLAUDE.md`,
  [PERSISTENCE.md](../PERSISTENCE.md) and `.claude/skills/flirty-ef-migration/SKILL.md`.
- **Not every change needs an ADR.** An ADR is worth it when a nearby alternative was
  deliberately ruled out and the decision would be expensive to revise later. Everything else
  belongs in the responsible guide.

<!-- Title format: <type>: <short description> – #<issue>  (type = feat|fix|chore|docs|test) -->

## Overview

<!-- What does this PR change and why? 1–3 sentences. -->

Closes #<issue>

## Type of change

- [ ] Feature
- [ ] Bugfix
- [ ] Chore / infrastructure
- [ ] Docs
- [ ] Test

## Checklist

- [ ] Builds clean: `dotnet build Flirty.sln` (no warnings – `TreatWarningsAsErrors`)
- [ ] Tests green: `dotnet test` (new logic covered by tests)
- [ ] English XML docs on new/changed public API (CS1591 is an error in the packable projects)
- [ ] On a domain/schema change: migration generated for **all three** providers (SQLite/PostgreSQL/SQL Server)
- [ ] **Docs kept in sync:** affected `docs/` guide updated
- [ ] **Context/skills kept in sync:** `CLAUDE.md` and the affected `.claude/skills/` are still correct
      (see section "Keeping context & docs in sync" in `CLAUDE.md`)
- [ ] Project status updated if a feature was completed (section "Status & open work")

## Notes for reviewers

<!-- Optional: specifics, open points, testing instructions. -->

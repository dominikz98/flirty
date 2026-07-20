<!-- Titel im Format: <typ>: <kurzbeschreibung> – #<issue>  (typ = feat|fix|chore|docs|test) -->

## Überblick

<!-- Was ändert dieser PR und warum? 1–3 Sätze. -->

Schließt #<issue>

## Art der Änderung

- [ ] Feature
- [ ] Bugfix
- [ ] Chore / Infrastruktur
- [ ] Doku
- [ ] Test

## Checkliste

- [ ] Baut sauber: `dotnet build Flirty.sln` (keine Warnungen – `TreatWarningsAsErrors`)
- [ ] Tests grün: `dotnet test` (neue Logik durch Tests abgedeckt)
- [ ] Deutsche XML-Docs auf neuer/geänderter public API (CS1591 ist Fehler in den packbaren Projekten)
- [ ] Bei Domain-/Schema-Änderung: Migration für **alle drei** Provider erzeugt (SQLite/PostgreSQL/SQL Server)
- [ ] **Doku mitgepflegt:** betroffener `docs/`-Guide aktualisiert
- [ ] **Kontext/Skills mitgepflegt:** `CLAUDE.md` und die betroffenen `.claude/skills/` sind noch korrekt
      (siehe Abschnitt „Kontext & Doku mitpflegen" in `CLAUDE.md`)
- [ ] Projektstatus nachgezogen, falls ein Feature abgeschlossen wurde (Abschnitt „Stand & offene Baustellen")

## Hinweise für Reviewer

<!-- Optional: Besonderheiten, offene Punkte, Testanweisungen. -->

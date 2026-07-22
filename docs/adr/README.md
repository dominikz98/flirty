# Architecture Decision Records (ADRs)

Die Entscheidungshistorie von Flirty. Ein ADR hält **eine** Grundsatzentscheidung fest – vor allem
das, was sonst nirgends stehen bleibt: die **verworfenen Alternativen** und ihren Grund.

Damit ist die Abgrenzung zu den `docs/`-Guides klar: Ein **Guide beschreibt, wie etwas funktioniert**
und wird mit dem Code fortgeschrieben. Ein **ADR beschreibt, warum es so und nicht anders ist** und
wird nicht fortgeschrieben. Wer eine Anleitung sucht, ist im Guide richtig (Wegweiser in der
`CLAUDE.md` im Repo-Root); wer wissen will, warum eine naheliegende Alternative *nicht* gewählt
wurde, hier.

## Entscheidungen

| Nr. | Titel | Status | Kontext-Issue |
|---|---|---|---|
| [0001](./0001-migrationen-pro-provider.md) | Migrationen pro Provider (getrennte Assemblies) | Akzeptiert | #19 |
| [0002](./0002-mediator-als-in-process-bus.md) | Mediator (martinothamar) als In-Process-Bus | Akzeptiert | #14 |
| [0003](./0003-aspnet-freier-core.md) | ASP.NET-freier Core, Web als opt-in-Paket | Akzeptiert | #13 |
| [0004](./0004-gesandboxte-expression-engine.md) | Gesandboxte Expression-Engine hinter einer Abstraktion | Akzeptiert | #22/#23 |

Die vier ADRs stützen sich gegenseitig: 0002 erzwingt, dass alle Handler im Core liegen – was 0003
(dünne, austauschbare Web-Schicht) technisch absichert. 0004 hängt an der Designer-Fähigkeit,
Ausdrücke ohne Deployment zu ändern; 0001 daran, dass ein Paket alle drei Provider mitbringt.

## Format

Vorlage ist ADR 0001; neue ADRs übernehmen die Gliederung unverändert, damit der Ordner homogen bleibt:

```markdown
# ADR NNNN – <Titel>

- **Status:** Akzeptiert | Abgelöst durch NNNN
- **Kontext-Issue:** #NN – <Issue-Titel>
- **Betroffen:** <Projekte / Pfade>

## Kontext                  Welches Problem stand an? Welche Zwänge galten?
## Entscheidung             Was gilt jetzt – knapp und überprüfbar.
## Verworfene Alternativen  Was lag nahe und warum ist es ausgeschieden?
## Konsequenzen             Positiv / Negativ / Offen – auch das Unbequeme.

Details: [<GUIDE>.md](../<GUIDE>.md).
```

Sprache ist **deutsch** (wie im gesamten Repo), Zeilenlänge ~100 Zeichen.

## Pflege

- **Ein ADR wird nicht umgeschrieben.** Ändert sich eine Entscheidung, bekommt der ADR entweder einen
  kurzen **Nachtrag** (wenn nur ein „Offen"-Punkt aufgelöst wurde, siehe 0001) oder er wird durch einen
  **neuen** ADR abgelöst; der alte behält seinen Text und wechselt den Status auf
  `Abgelöst durch NNNN`. Der Wert eines ADRs liegt darin, den Stand zum Entscheidungszeitpunkt zu
  zeigen – ein rückwirkend geglätteter ADR beantwortet die Frage „warum eigentlich?" nicht mehr.
- **Nummern werden fortlaufend vergeben und nie neu verteilt.** 0001 bleibt 0001, obwohl die
  Entscheidung chronologisch nach #13/#14 fiel: Auf die Datei verweisen `CLAUDE.md`,
  [PERSISTENCE.md](../PERSISTENCE.md) und `.claude/skills/flirty-ef-migration/SKILL.md`.
- **Nicht jede Änderung braucht einen ADR.** Ein ADR lohnt, wenn eine naheliegende Alternative
  bewusst ausgeschieden ist und die Entscheidung später teuer zu revidieren wäre. Alles andere
  gehört in den zuständigen Guide.

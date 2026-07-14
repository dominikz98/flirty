# Flirty – Domänenmodell

> Referenz für die POCO-Entities und Enums im Core-Projekt `Flirty` (Namespace `Flirty.Domain`).
> Diese Typen sind die Grundlage der Persistenz; die EF-Core-Konfiguration (Keys, Indizes,
> JSON-Spalten, Beziehungen) folgt separat im `FlirtyDbContext`. Konzeptioneller Überblick:
> [ARCHITECTURE.md](./ARCHITECTURE.md) §5 (Konfiguration) und §6 (Runtime-/Session-State).

## Aggregate & Navigationen

Das Modell hat zwei Aggregate mit klaren Grenzen:

- **Konfigurations-Aggregat** – Wurzel `Dialog`. Der Dialog bündelt seine `Question`s,
  `Transition`s, `LoopDefinition`s und `TriggerDefinition`s; eine `Question` bündelt ihre
  `AnswerOption`s. Navigationen bilden diesen Baum ab (`Dialog.Questions`, `Question.Options`, …).
- **Runtime-Aggregat** – Wurzel `DialogSession`. Die Session bündelt ihre `SessionAnswer`s
  (`DialogSession.Answers`).

**Bewusst ohne Navigation** – als skalare `Guid` belassen, um mehrdeutige/über Aggregatgrenzen
laufende Beziehungen zu vermeiden (explizite Konfiguration erfolgt bei Bedarf im DbContext):

- Mehrfach-Verweise auf dieselbe Entity: `Transition.FromQuestionId`/`TargetQuestionId`,
  `LoopDefinition.EntryQuestionId`/`BreakingQuestionId`, `TriggerDefinition.QuestionId`,
  `Dialog.StartQuestionId`, `DialogSession.CurrentQuestionId`.
- **Runtime → Konfiguration**: `DialogSession.DialogId` und `SessionAnswer.QuestionId`. Sessions
  pinnen über `DialogVersion` die Dialogversion und bleiben dadurch vom editierbaren
  Konfigurationsgraphen entkoppelt (ARCHITECTURE.md §11.4) – so brechen spätere Dialogänderungen
  keine laufenden Sessions.

## Enums

| Enum | Werte (Ordinal) |
|---|---|
| `QuestionType` | SingleChoice(0), MultiChoice(1), FreeText(2), Number(3), Date(4), Boolean(5) |
| `TriggerScope` | OnDialogStarted(0), AfterAnswer(1), AfterQuestion(2), OnDialogCompleted(3) |
| `TriggerKind` | InProcess(0), Webhook(1) |
| `SessionStatus` | InProgress(0), Completed(1), Abandoned(2) |

## Konfigurations-Entities

| Entity | Properties | Navigationen |
|---|---|---|
| `Dialog` | `Id`, `Key`, `Name`, `Description?`, `Version`, `IsPublished`, `StartQuestionId?`, `CreatedAt`, `UpdatedAt` | `Questions`, `Transitions`, `Loops`, `Triggers` |
| `Question` | `Id`, `DialogId`, `Key`, `Text`, `Type`, `Order`, `IsRequired`, `ValidationRules?` (JSON) | `Dialog`, `Options` |
| `AnswerOption` | `Id`, `QuestionId`, `Key`, `Label`, `Value`, `Order` | `Question` |
| `Transition` | `Id`, `DialogId`, `FromQuestionId`, `ConditionExpression?`, `TargetQuestionId`, `Priority`, `IsDefault` | `Dialog` |
| `LoopDefinition` | `Id`, `DialogId`, `CollectionKey`, `EntryQuestionId`, `BreakingQuestionId` | `Dialog` |
| `TriggerDefinition` | `Id`, `DialogId`, `Scope`, `QuestionId?`, `Kind`, `Config` (JSON), `ConditionExpression?` | `Dialog` |

## Runtime-Entities

| Entity | Properties | Navigationen |
|---|---|---|
| `DialogSession` | `Id`, `DialogId`, `DialogVersion`, `ExternalUserKey`, `Status`, `CurrentQuestionId?`, `StartedAt`, `CompletedAt?` | `Answers` |
| `SessionAnswer` | `Id`, `SessionId`, `QuestionId`, `Value` (JSON), `AnsweredAt`, `Sequence`, `LoopInstanceId?`, `IterationIndex?` | `Session` |

`LoopInstanceId`/`IterationIndex` erlauben mehrere Antworten pro `QuestionId` (ein Eintrag je
Loop-Iteration); außerhalb einer Schleife sind beide `null`.

## Konventionen

- Ids: `Guid`. Zeitstempel: `DateTimeOffset`. Enum-Storage-Mapping erfolgt im DbContext.
- Pflicht-Strings als `required string`, optionale als `string?`.
- Navigations-Collections initialisiert (`= []`), Rückverweise als `= null!` (von EF gesetzt).
- Alle Typen `sealed`; deutsche XML-Docs auf allen public Membern (CS1591 = Build-Fehler).

# Flirty – Domänenmodell

> Referenz für die POCO-Entities und Enums im Core-Projekt `Flirty` (Namespace `Flirty.Domain`).
> Diese Typen sind die Grundlage der Persistenz; die EF-Core-Konfiguration (Keys, Indizes,
> JSON-Spalten, Beziehungen) erfolgt im `FlirtyDbContext` – siehe
> [Persistenz-Konfiguration](#persistenz-konfiguration-flirtydbcontext). Konzeptioneller Überblick:
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
| `Transition` | `Id`, `DialogId`, `FromQuestionId`, `Expression?`, `TargetQuestionId`, `Priority`, `IsDefault` | `Dialog` |
| `LoopDefinition` | `Id`, `DialogId`, `CollectionKey`, `EntryQuestionId`, `BreakingQuestionId` | `Dialog` |
| `TriggerDefinition` | `Id`, `DialogId`, `Scope`, `QuestionId?`, `Kind`, `Config` (JSON), `Expression?` | `Dialog` |

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

## Persistenz-Konfiguration (`FlirtyDbContext`)

Der `FlirtyDbContext` (Namespace `Flirty.Persistence`, Ordner `src/Flirty/Persistence/`) ist
**provider-agnostisch**: er besitzt nur den Options-Konstruktor
`FlirtyDbContext(DbContextOptions<FlirtyDbContext>)` und legt keinen Provider fest. Die
Provider-Wahl (SQLite/PostgreSQL/SQL Server) und die Migrationen je Provider sind in Issue #19
umgesetzt – Details in [PERSISTENCE.md](./PERSISTENCE.md) (vgl. [ARCHITECTURE.md](./ARCHITECTURE.md) §8).

- **DbSets nur für die Aggregat-Roots** – `Dialogs` und `DialogSessions`. Die Kind-Entities werden
  über ihre Navigationen bzw. `Set<T>()` erreicht (spiegelt die Aggregatgrenzen wider).
- **Fluent-API-Konfiguration** – je Entity eine `internal sealed`
  `IEntityTypeConfiguration<T>`-Klasse unter `Persistence/Configurations/`; angewendet über
  `ApplyConfigurationsFromAssembly`. Die POCOs bleiben frei von Data Annotations.
- **Enum-Storage als `int`** – `QuestionType`, `TriggerScope`, `TriggerKind`, `SessionStatus` werden
  explizit über `HasConversion<int>()` gemappt (Guard passend zum Ordinal-Pinning der Domain-Tests).
- **JSON-Spalten = einfache Textspalten** – `SessionAnswer.Value`, `TriggerDefinition.Config`
  (Pflicht) und `Question.ValidationRules` (optional) tragen anwendungsseitig serialisiertes JSON und
  werden als unbegrenzte Textspalten (ohne `MaxLength`) gespeichert. Provider-native `json`/`jsonb`-
  Typen sind bewusst nicht gesetzt (kleinster gemeinsamer Nenner aller Provider; bestätigt in #19).
  Das Schema von `Question.ValidationRules` (camelCase-Felder `minLength`/`maxLength`/`min`/`max`/
  `pattern`, typ-skopiert) wertet seit #30 der `IAnswerValidator` aus – siehe [VALIDATION.md](./VALIDATION.md).
- **Skalare `Guid`-Verweise ohne Fremdschlüssel** – die oben unter *Bewusst ohne Navigation*
  gelisteten Verweise bleiben einfache Spalten (keine Relationship, kein Shadow-FK).
- **Kaskadierendes Löschen** – innerhalb beider Aggregate (`Dialog` → Questions/Options/Transitions/
  Loops/Triggers; `DialogSession` → Answers) via `OnDelete(Cascade)` mit explizitem `HasForeignKey`.

### Keys & Indizes

| Entity | Schlüssel / Index | Art |
|---|---|---|
| `Dialog` | PK `Id`; `(Key, Version)` | **eindeutig** (mehrere Versionen je `Key` erlaubt) |
| `Question` | PK `Id`; `(DialogId, Key)` | **eindeutig** |
| `AnswerOption` | PK `Id`; `(QuestionId, Key)` | **eindeutig** |
| `Transition` | PK `Id`; `(DialogId, FromQuestionId, Priority)` | nicht eindeutig (Auswertungsreihenfolge) |
| `LoopDefinition` | PK `Id` | – |
| `TriggerDefinition` | PK `Id` | – |
| `DialogSession` | PK `Id`; `(DialogId, ExternalUserKey)` | nicht eindeutig (mehrere Sessions je Anwender) |
| `SessionAnswer` | PK `Id`; `(SessionId, Sequence)` | nicht eindeutig |

Indizierte fachliche Schlüssel (`Dialog.Key`, `Question.Key`, `AnswerOption.Key`,
`DialogSession.ExternalUserKey`) sind auf 256 Zeichen begrenzt, damit sie über alle Provider
indizierbar bleiben. **Kein** eindeutiger Index über `SessionAnswer(SessionId, QuestionId)`:
Loop-Iterationen erlauben mehrere Antworten pro Frage. Eindeutige Indizes werden generell nicht über
`null`-fähige Spalten gelegt (divergente Null-Semantik zwischen SQL Server und SQLite/PostgreSQL).

> **Zeitstempel UTC-normalisiert speichern.** Der PostgreSQL-Provider (Npgsql) mappt
> `DateTimeOffset` auf `timestamptz` und verlangt Offset == UTC. Zeitstempel daher als UTC ablegen.

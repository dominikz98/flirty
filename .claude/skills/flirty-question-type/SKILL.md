---
name: flirty-question-type
description: Neuen Fragetyp (QuestionType) und/oder eine Antwort-Validierungsregel in Flirty ergänzen. Verwenden bei "neuer QuestionType", "neuer Fragetyp", "Validierungsregel", "ValidationRules", "Antwort-Validierung erweitern", "IAnswerValidator".
---

# Neuen QuestionType + Validierungsregel hinzufügen

Der Fragetyp bestimmt, wie eine eingereichte Antwort **fachlich** validiert wird. Die Validierung läuft
als Mediator-`IPipelineBehavior` (`AnswerValidationPipelineBehavior`) **vor** den Submit/Edit-Handlern.
Referenz: `docs/VALIDATION.md`, `docs/DOMAIN-MODEL.md`.

## Vorbilder (vor dem Schreiben lesen)

- `src/Flirty/Domain/QuestionType.cs` – das Enum (als `int` persistiert).
- `src/Flirty/Validation/AnswerValidator.cs` – die typ- und regelbasierte Prüfung.
- `src/Flirty/Validation/ValidationRules.cs` – JSON-Schema der optionalen Regeln.
- `src/Flirty/Validation/AnswerValidationResult.cs` – strukturiertes Ergebnis (`IsValid` + `Errors`).

## Bestehende Typen & Regeln

Typen: `SingleChoice`, `MultiChoice`, `FreeText`, `Number`, `Date`, `Boolean`.
Regeln (`Question.ValidationRules`, camelCase-JSON, alle optional): `minLength`/`maxLength`/`pattern`
(FreeText), `min`/`max` (Number). Der Antwortwert ist **roher JSON-Text** und wird tolerant gelesen
(gültiges JSON typisiert, sonst als String).

## Schritte

### Neuen QuestionType

1. Wert in `src/Flirty/Domain/QuestionType.cs` **anhängen** (Enum ist `int`-persistiert → keine
   bestehenden Ordinalwerte ändern).
2. In `AnswerValidator.cs` einen `case` für den neuen Typ ergänzen: gültig-wenn-Logik definieren,
   Antwortwert über den vorhandenen toleranten JSON-Reader interpretieren.
3. Bei Wert-Verstoß **kein** Wurf, sondern `AnswerValidationResult` mit `Errors` zurückgeben. Nur echte
   **Fehlkonfiguration** der Frage (unbekannter Typ, ungültiges Regex/JSON) wirft
   `InvalidOperationException`.

### Neue Validierungsregel

1. Feld in `ValidationRules.cs` ergänzen (optional, camelCase, case-insensitiv gelesen; fehlend =
   „keine Einschränkung"). Regel **typ-skopiert** halten – auf nicht betroffene Typen ignorieren.
2. Anwendung in `AnswerValidator.cs` für die betroffenen Typen einbauen. Bei Regex: mit **Timeout**
   (ReDoS-Schutz), Teiltreffer via `Regex.IsMatch` (zum Vollmatch im Muster `^…$` verankern).
3. Regel in der Tabelle in `docs/VALIDATION.md` dokumentieren.

## Wichtig

- Kein DB-Zugriff im Validator – er bekommt die bereits geladene `Question` (inkl. Optionen +
  `ValidationRules`) und den rohen Wert. Er ist **zustandslos** und als Singleton registriert.
- Neue Auswahltypen (choice-artig) prüfen Zugehörigkeit gegen `AnswerOption.Value` der Frage.

## Tests

`tests/Flirty.Tests/Validation/AnswerValidatorTests.cs` – Validator isoliert (neuer Typ/Regel: gültig,
ungültig, toleranter Fallback, Fehlkonfiguration). `AnswerValidationPipelineBehaviorTests.cs` – end-to-end
via `IFlirtyEngine` gegen SQLite (ungültige Antwort → `AnswerValidationException` **ohne** Persistenz).

## Definition of Done

Deutsche XML-Docs · `docs/VALIDATION.md` (Typ-Tabelle bzw. Regel-Tabelle) aktualisiert · Tests grün. Ändert
sich das Domänenmodell/Schema, zusätzlich Skill `flirty-ef-migration` durchlaufen.

## Verifikation

```pwsh
dotnet build Flirty.sln
dotnet test tests/Flirty.Tests
```

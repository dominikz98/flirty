# Antwort-Validierung: `IAnswerValidator` (Pipeline-Behavior)

Wie eine eingereichte Antwort **fachlich** validiert wird – anhand des Fragetyps
(`Question.Type`) und der optionalen, je Frage konfigurierten Regeln (`Question.ValidationRules`),
umgesetzt als Mediator-`IPipelineBehavior`, das **vor** den Runtime-Handlern greift. Umgesetzt in
Issue **#30** – EPIC 3 – Dialog-Runtime. Referenz: [ARCHITECTURE.md](./ARCHITECTURE.md) §7,
Runtime-Ablauf in [RUNTIME.md](./RUNTIME.md), Mediator-Grundlagen in [MEDIATOR.md](./MEDIATOR.md).

## Überblick

Die Basis-Pipeline validiert bislang nur **deklarativ** (DataAnnotations, `[Required]`) über das
`ValidationPipelineBehavior`. Die **fachliche** Prüfung – „passt der Wert zum Fragetyp und zu den
konfigurierten Regeln?" – leistet seit #30 der `IAnswerValidator`, aufgerufen aus dem
`AnswerValidationPipelineBehavior`. Eine ungültige Antwort wird abgewiesen, **bevor** sie persistiert
(`SubmitAnswerCommand`) bzw. der Pfad neu berechnet wird (`EditAnswerCommand`).

```
ISender.Send(SubmitAnswerCommand)
  └─ LoggingPipelineBehavior            (protokolliert)
       └─ ValidationPipelineBehavior    (DataAnnotations: [Required] gegen null/leer)
            └─ AnswerValidationBehavior  (fachlich: Typ + ValidationRules)  ← #30
                 └─ SubmitAnswerCommandHandler
```

## `IAnswerValidator`

```csharp
public interface IAnswerValidator
{
    AnswerValidationResult Validate(Question question, string value);
}
```

- Reiner, **zustandsloser** Service (Default `AnswerValidator`, als Singleton registriert – analog zum
  `IExpressionEvaluator`). Kein DB-Zugriff: Er erhält die bereits geladene `Question` (inkl. Optionen
  und `ValidationRules`) und den rohen JSON-Antwortwert.
- Liefert ein strukturiertes `AnswerValidationResult` (`IsValid` + `Errors`) statt zu werfen –
  konsistent zum `ExpressionValidationResult` des Expression-Pfads.
- Wirft `InvalidOperationException` bei **Fehlkonfiguration** der Frage (unbekannter Typ, ungültiges
  `ValidationRules`-JSON, ungültiges Regex-Muster) – abgegrenzt von Wert-Fehlern.

### Werte-Format (tolerant)

Der Antwortwert ist **roher JSON-Text** (wie `SessionAnswer.Value`). Der Validator liest ihn – wie der
`DynamicExpressoExpressionEvaluator` – tolerant: gültiges JSON wird typisiert interpretiert, ist der
Text kein gültiges JSON, gilt er unverändert als Zeichenkette (z. B. `"\"dev\""` **und** `dev` werden
für eine Auswahl gleich behandelt).

## Prüfung je `QuestionType`

| Typ | Gültig, wenn … |
|---|---|
| `FreeText` | beliebiger Text; zusätzlich Regeln `minLength`/`maxLength`/`pattern` |
| `Number` | JSON-Zahl oder numerischer String; zusätzlich Regeln `min`/`max` |
| `Boolean` | JSON-`true`/`false` bzw. `"true"`/`"false"` |
| `Date` | als ISO-8601-Datum parsebar (`DateTimeOffset`/`DateOnly`, invariant) |
| `SingleChoice` | Wert entspricht genau einem `AnswerOption.Value` der Frage |
| `MultiChoice` | JSON-Array von Zeichenketten; jeder Eintrag ein bekannter `AnswerOption.Value` |

## `ValidationRules` (JSON-Schema)

`Question.ValidationRules` trägt optionales JSON (camelCase, case-insensitiv gelesen). Alle Felder
sind optional; ein fehlendes Feld bedeutet „keine Einschränkung". Die Regeln sind **typ-skopiert** –
nicht anwendbare Regeln werden ignoriert.

| Feld | Typ | Wirkt auf | Bedeutung |
|---|---|---|---|
| `minLength` | int | `FreeText` | Mindestlänge (Zeichen) |
| `maxLength` | int | `FreeText` | Maximallänge (Zeichen) |
| `min` | number | `Number` | kleinster zulässiger Wert (inklusiv) |
| `max` | number | `Number` | größter zulässiger Wert (inklusiv) |
| `pattern` | string | `FreeText` | regulärer Ausdruck (Teiltreffer via `Regex.IsMatch`; zur Vollprüfung im Muster verankern, z. B. `^…$`) – mit Timeout (ReDoS-Schutz) |

```json
{ "minLength": 2, "maxLength": 50, "pattern": "^[A-Za-z ]+$" }
```

## `AnswerValidationPipelineBehavior`

Das Behavior verbindet Validator und Mediator-Pipeline:

1. Greift nur für antworteinreichende Commands (`SubmitAnswerCommand`, `EditAnswerCommand`, erkannt am
   internen Marker `IAnswerCommand`) mit nicht-leerem `Value`.
2. Löst über den `IDialogStore` **Session → gepinnte Dialogversion → Frage** auf.
3. Ruft `IAnswerValidator.Validate(question, value)`. Bei `IsValid == false` wird eine
   `AnswerValidationException` geworfen (leitet von `ValidationException` ab und trägt `QuestionId`
   + `Errors`), bevor der Handler läuft.
4. **Defer-Regel:** Kann die Frage nicht aufgelöst werden (Session/Dialog/Frage fehlt), validiert das
   Behavior nicht und ruft nur `next` – die kanonischen Fehler (`SessionNotFoundException`,
   `InvalidOperationException`, DataAnnotations-`ValidationException`) bleiben allein Sache des
   Handlers bzw. des `ValidationPipelineBehavior`.

### Registrierung (warum geschlossen)

`AddFlirty()` registriert das Behavior **geschlossen je Command-Typ** (nicht offen-generisch) als
`Scoped`:

```csharp
services.AddSingleton<IAnswerValidator, AnswerValidator>();
services.AddScoped<IPipelineBehavior<SubmitAnswerCommand, SubmitAnswerResult>,
    AnswerValidationPipelineBehavior<SubmitAnswerCommand, SubmitAnswerResult>>();
services.AddScoped<IPipelineBehavior<EditAnswerCommand, EditAnswerResult>,
    AnswerValidationPipelineBehavior<EditAnswerCommand, EditAnswerResult>>();
```

Das Behavior benötigt den scoped `IDialogStore` (und damit einen registrierten `FlirtyDbContext`).
Eine offen-generische Registrierung würde es für **jede** Nachricht konstruieren – auch dort, wo kein
`FlirtyDbContext` vorhanden ist – und die Auflösung brechen. Geschlossen greift es nur für Submit/Edit;
`Scoped` teilt es sich denselben Kontext wie der Handler (`GetSessionAsync` liefert getrackt → keine
zweite Abfrage).

## Fehlerfälle

| Situation | Verhalten |
|---|---|
| Wert passt nicht zum Typ / unbekannte Auswahl / Regelverstoß | `AnswerValidationException` (aus der Pipeline, vor dem Handler) |
| Frage fehlkonfiguriert (ungültiges `ValidationRules`-JSON / Regex-Muster / Typ) | `InvalidOperationException` |
| Session/gepinnter Dialog/Frage nicht auflösbar | keine Validierung → kanonischer Handler-Fehler |
| `null`/leeres `Value` | `ValidationException` (DataAnnotations, vor der fachlichen Validierung) |

## Verifikation

```pwsh
dotnet test tests/Flirty.Tests
```

`tests/Flirty.Tests/Validation/AnswerValidatorTests.cs` prüft den Validator isoliert (alle Typen,
Regeln, Membership, toleranter Fallback, Fehlkonfiguration).
`tests/Flirty.Tests/Validation/AnswerValidationPipelineBehaviorTests.cs` treibt das Behavior end-to-end
durch die volle Pipeline via `IFlirtyEngine` gegen SQLite: ungültige Antwort → `AnswerValidationException`
ohne Persistenz/Invalidierung, gültige Antwort läuft durch, plus die DI-Registrierung.

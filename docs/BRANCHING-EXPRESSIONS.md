# Branching & Expressions: Condition-Engine

Wie Flirty Bedingungsausdrücke des Branchings auswertet – die Abstraktion `IConditionEvaluator`
und das Kontext-Modell `ExpressionContext`. Umgesetzt in Issue **#22** (EPIC 2). Referenz:
[ARCHITECTURE.md](./ARCHITECTURE.md) §7/§10/§11, Modell-Details in [DOMAIN-MODEL.md](./DOMAIN-MODEL.md).

## Überblick

Verzweigungen (Branching) und Schleifen (Loops) hängen an **booleschen Bedingungsausdrücken**:

- `Transition.ConditionExpression` – entscheidet, welcher Übergang von einer Frage greift.
- `TriggerDefinition.ConditionExpression` – entscheidet, ob ein Trigger auslöst.

Ausgewertet werden diese Ausdrücke über die austauschbare Engine `IConditionEvaluator`
(Namespace `Flirty.Expressions`). Der Kern legt in #22 nur die Abstraktion fest; die konkrete,
gesandboxte Engine und der Validierungs-Pfad folgen in eigenen Issues (siehe [Ausblick](#ausblick)).

## `IConditionEvaluator`

```csharp
namespace Flirty.Expressions;

public interface IConditionEvaluator
{
    bool Evaluate(string expression, ExpressionContext context);
}
```

- **Synchron:** Die Auswertung ist eine reine In-Memory-Operation (die Default-Engine DynamicExpresso
  arbeitet synchron) – daher kein `async`/`CancellationToken`.
- **Null/leerer Ausdruck:** Ein `null`er oder leerer `ConditionExpression` gilt fachlich als
  *bedingungslos zutreffend*. Diese Kurzschluss-Behandlung liegt bei der **Runtime**, nicht beim
  Evaluator; Implementierungen dürfen einen nicht-leeren Ausdruck erwarten.

## Kontext-Modell `ExpressionContext`

Der unveränderliche `ExpressionContext` bündelt die zum Auswertungszeitpunkt sichtbaren Daten einer
laufenden Session. Er bildet die fünf in ARCHITECTURE §7 genannten Bausteine ab:

| Baustein | Property | Typ | Bedeutung |
|---|---|---|---|
| `answers` | `Answers` | `IReadOnlyDictionary<string, string?>` | Antworten indiziert nach `Question.Key` |
| Loop-Collections | `Collections` | `IReadOnlyDictionary<string, IReadOnlyList<string?>>` | je Iteration gesammelte Antworten, indiziert nach `LoopDefinition.CollectionKey` |
| `iterationIndex` | `IterationIndex` | `int?` | nullbasierter Iterationsindex, `null` außerhalb einer Schleife |
| `now` | `Now` | `DateTimeOffset` | Auswertungszeitpunkt |
| `session` | `Session` | `DialogSession` | die laufende Session |

Die Werte sind **roher JSON-Text** – exakt wie in `SessionAnswer.Value` gespeichert (Format
abhängig vom Fragetyp). Die typisierte Deserialisierung (z. B. `"42"` → `int`) übernimmt die konkrete
Engine (#23); das Kontext-Modell selbst bleibt bewusst untypisiert.

Nicht angegebene Sammlungen werden als **leere, nicht-`null`e** Sammlungen initialisiert; `Session`
ist Pflicht (Guard via `ArgumentNullException`).

```csharp
var context = new ExpressionContext(
    session,
    now: DateTimeOffset.UtcNow,
    answers: new Dictionary<string, string?> { ["age"] = "42" },
    collections: new Dictionary<string, IReadOnlyList<string?>>
    {
        ["positions"] = ["{\"title\":\"Dev\"}", "{\"title\":\"Lead\"}"],
    },
    iterationIndex: null);
```

## Beispiel-Ausdrücke

Die spätere Default-Engine wertet Ausdrücke wie diese aus (vgl. ARCHITECTURE §10):

```text
age > 18                 // Verzweigung nach numerischer Antwort
positions.Count > 0      // Break-Bedingung einer Schleife über die gesammelte Collection
```

## Ausblick

Dieses Issue (#22) liefert nur Interface + Kontext-Modell. Darauf bauen auf:

- **#23 – DynamicExpresso-Implementierung (Sandbox):** die Default-Engine
  `DynamicExpressoConditionEvaluator` mit Member-Whitelist (kein roher C#-`eval`).
- **#24 – Expression-Validierung / Compile-Check:** Ausdrücke werden im Designer beim Speichern
  kompiliert/validiert (Operatoren, UND/ODER, Fehlerfälle, Injection-Abwehr).
- **#34 – DI-Integration:** Registrierung und Austausch der Engine über
  `services.AddFlirty(o => o.UseConditionEvaluator<MyEval>())` (Alternative z. B. NCalc).

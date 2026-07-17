# Loops (Schleifen): Iterationen, Collections & Break-Bedingungen

Wie die Dialog-Runtime Schleifen ausführt: **Zyklus-Erkennung**, **Iterations-Zähler**, **Sammlung je
Iteration** in einer Collection, **Break-Bedingung** und **Editieren innerhalb einer Iteration**.
Umgesetzt in Issue **#29** (EPIC 3 – Dialog-Runtime). Referenz:
[ARCHITECTURE.md](./ARCHITECTURE.md) §10, Domänenmodell in [DOMAIN-MODEL.md](./DOMAIN-MODEL.md),
Ausdrücke/Kontext in [BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md), Runtime-Commands in
[RUNTIME.md](./RUNTIME.md).

## Überblick

Loops entstehen **über das vorhandene Branching**: eine `Transition` zeigt auf eine **frühere** Frage und
bildet damit einen Zyklus. Der `LoopDefinition`-Marker liegt nur als Metadaten-Ebene darüber – es gibt
**keinen separaten Runtime-Sonderpfad** (ARCHITECTURE §11.5). Die gesamte Loop-Logik kapselt der interne
`LoopResolver`; er wird vom geteilten `TransitionResolver` (Kontextaufbau für Submit **und** Edit) und vom
`SubmitAnswerCommandHandler` (Feldzuweisung beim Persistieren) genutzt.

Der Marker bewirkt zweierlei:
1. **Runtime**: die Antworten des Schleifenbereichs werden je Iteration in `CollectionKey` gesammelt
   (statt überschrieben); `SessionAnswer.LoopInstanceId`/`IterationIndex` erlauben mehrere Antworten pro
   Frage (eine je Iteration).
2. **Designer**: der Zyklus wird als Loop-Block mit markierter Breaking Question visualisiert (späteres Epic).

## `LoopDefinition` (Marker)

| Feld | Bedeutung |
|---|---|
| `CollectionKey` | Schlüssel, unter dem die je Iteration gesammelten Antworten im Ausdruckskontext liegen (z. B. `positions`). |
| `EntryQuestionId` | Einstiegsfrage der Schleife (Ziel der Loop-Back-Transition). |
| `BreakingQuestionId` | Frage, deren Exit-Übergang den Zyklus verlässt. |

Der **Exit** ist keine eigene Eigenschaft, sondern läuft über die normale `Transition`-Mechanik: die
Breaking Question hat (mindestens) einen Loop-Back-Übergang auf die Einstiegsfrage und einen
Exit-Übergang, der den Zyklus verlässt. Welcher greift, entscheidet die Break-Bedingung.

## Ablauf

Der `LoopResolver` wird je gepinnter Dialogversion aufgebaut und leitet seinen Zustand ausschließlich aus
den vorhandenen `SessionAnswer`-Zeilen ab (kein zusätzliches Session-Feld):

1. **Body-Ermittlung** (einmalig je Schleife, aus dem Übergangs-Graphen): der Schleifenbereich ist
   `(vorwärts ab Entry erreichbar) ∩ (rückwärts zu Breaking erreichbar) ∪ {Entry, Breaking}`. Die
   Vorwärts-Suche stoppt an der Breaking Question (deren Loop-Back-/Exit-Kanten werden nicht verfolgt).
   Dadurch bleiben früh aus dem Zyklus austretende Zweige (vorwärts erreichbar, aber ohne Weg zu Breaking)
   und dem Zyklus vorgelagerte Fragen (erreichen Breaking, sind aber nicht ab Entry erreichbar) außerhalb
   des Body. Ein Ein-Fragen-Loop (`Entry == Breaking`) ergibt `{Entry}`.
2. **Iterations-/Instanz-Zuordnung** (beim Persistieren einer Antwort auf Frage `Q`, vor dem Anhängen):
   - `Q` in keinem Body → `LoopInstanceId`/`IterationIndex` bleiben `null` (unverändertes Nicht-Loop-Verhalten).
   - Erster Eintritt (keine Body-Antwort der Schleife vorhanden) → **frische** `LoopInstanceId`, `IterationIndex = 0`.
   - Sonst: aktive Instanz = Instanz der jüngsten Body-Antwort, `maxIter` = größter Iterationsindex dieser
     Instanz. Wird die **Einstiegsfrage** in der laufenden Iteration erneut beantwortet (Loop-Back), beginnt
     die nächste Iteration (`maxIter + 1`); alle übrigen Fragen bleiben in der aktuellen Iteration (`maxIter`).
   - Invariante: höchstens eine Antwort je `(Instanz, Frage, Iteration)`.
3. **Collection-Aufbau** (für den `ExpressionContext`, gebaut **nach** dem Persistieren): je
   `CollectionKey` die `Value` der Einstiegsfrage je Iteration der jüngsten Instanz, geordnet nach
   Iterationsindex. Jeder `CollectionKey` wird **immer** gebunden (leere Liste, solange die Schleife noch
   nicht betreten wurde), damit Ausdrücke wie `positions.Count > 0` auch vor der ersten Iteration
   auswertbar sind. Die **laufende** Iteration zählt automatisch mit: Der Kontext wird nach dem
   Persistieren gebaut, sodass die Einstiegsantwort der aktuellen Iteration bereits in der Collection
   liegt, wenn die Break-Bedingung an der Breaking Question ausgewertet wird.
4. **Break-Bedingung**: An der Breaking Question entscheidet das übliche Branching. Der Loop-Back-Übergang
   (auf die Einstiegsfrage) und der Exit-Übergang werden nach `Priority` geprüft; die Bedingungsausdrücke
   sehen die gesammelte Collection und den `iterationIndex`.
5. **Danach normaler Fluss**: Greift der Exit-Übergang, verlässt die Session den Zyklus auf die
   nachgelagerte Frage; deren Antworten tragen wieder keine Loop-Felder (`LoopInstanceId`/`IterationIndex`
   = `null`).

## Break-Bedingungen

Die Break-/Loop-Back-Bedingung ist ein gewöhnlicher `Transition.Expression` und sieht dieselben Variablen
wie jedes Branching – zusätzlich die Loop-Collections und den Iterationsindex:

```text
more == "yes"          // Loop-Back anhand der Antwort der Breaking Question
positions.Count < 2    // Loop-Back, bis zwei Einträge gesammelt sind (collection-getrieben)
iterationIndex < 3     // höchstens vier Iterationen (Index 0..3)
```

Die Werte der Collection sind – wie alle Ausdruckswerte – **roher JSON-Text** je Iteration
(die Einstiegsantwort), z. B. `positions = ["{\"title\":\"Dev\"}", "{\"title\":\"Lead\"}"]`. Details zur
Bindung und Typisierung: [BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md).

## Editieren innerhalb einer Iteration

`EditAnswerCommand`/`IFlirtyEngine.EditAnswerAsync` tragen einen optionalen nullbasierten
`IterationIndex`:

- **`null`** (Default) → wie außerhalb von Schleifen: die **früheste** Antwort der Frage wird editiert
  (bei einer Loop-Frage die Iteration 0). Rückwärtskompatibel.
- **gesetzt** → gezielt die Antwort der angegebenen Iteration.

Das Überschreiben lässt `Sequence`, `LoopInstanceId` und `IterationIndex` unverändert. Die Invalidierung
der nachgelagerten Antworten bleibt **Sequence-basiert** (alle Antworten mit höherer `Sequence` werden
verworfen) – das erfasst korrekt den Rest der editierten Iteration **und** alle Folge-Iterationen; der
Nutzer durchläuft die späteren Iterationen anschließend neu.

## Fehlerfälle

| Situation | Verhalten |
|---|---|
| Zwei Schleifen-Bereiche überlappen sich (verschachtelte/überlappende Loops) | `InvalidOperationException` (nicht unterstützt, siehe MVP-Grenzen) |
| Editieren einer nicht vorhandenen Iteration (`IterationIndex` ohne passende Antwort) | `InvalidOperationException` |

Die übrigen Fehlerfälle von Submit/Edit gelten unverändert (siehe [RUNTIME.md](./RUNTIME.md)).

## Bewusst außerhalb des MVP

- **Verschachtelte/überlappende Loops** – werden fail-loud abgelehnt (ein Body je Frage).
- **Loop-Wiedereintritt nach Exit** – innerhalb eines Session-Laufs wird pro Schleife genau eine Instanz
  geführt; ein erneutes Betreten nach dem Verlassen legt keine zweite Instanz an.
- **Strukturierte Iterations-Objekte** – gesammelt wird je Iteration genau die Einstiegsantwort (ein
  Eintrag pro Iteration), nicht alle Antworten des Bereichs.
- **`CollectionKey` ↔ `Question.Key`-Kollision** – Designer-Konvention: Collection- und Frage-Schlüssel
  disjunkt halten.

## Nutzung

```csharp
// Loop-Dialog: position (Entry) -> more (Breaking, yes/no) -> summary (nach der Schleife)
// Übergänge: position -> more (Default);
//            more -> position  (Expression "more == \"yes\"", Priorität 0, Loop-Back);
//            more -> summary   (Default, Priorität 1, Exit).
// LoopDefinition { CollectionKey = "positions", EntryQuestionId = position, BreakingQuestionId = more }.

var start = await engine.StartDialogAsync("loop", "user-1");
await engine.SubmitAnswerAsync(start.SessionId, positionId, "\"Dev\"");   // Iteration 0
await engine.SubmitAnswerAsync(start.SessionId, moreId, "\"yes\"");        // Loop-Back
await engine.SubmitAnswerAsync(start.SessionId, positionId, "\"Lead\"");  // Iteration 1
var afterMore = await engine.SubmitAnswerAsync(start.SessionId, moreId, "\"no\""); // Exit -> summary

// Zustand samt Iterationen lesen:
var state = await engine.ResumeDialogAsync(start.SessionId);
foreach (var answer in state.Answers)
{
    Console.WriteLine($"{answer.QuestionKey} [Iter {answer.IterationIndex?.ToString() ?? "-"}] = {answer.Value}");
}

// Gezielt die erste Iteration korrigieren (verwirft nachgelagerte Iterationen):
var edited = await engine.EditAnswerAsync(start.SessionId, positionId, "\"Developer\"", iterationIndex: 0);
Console.WriteLine($"Verworfene nachgelagerte Antworten: {edited.InvalidatedAnswers}");
```

## Verifikation

```pwsh
dotnet test tests/Flirty.Tests
```

Die Tests unter `tests/Flirty.Tests/Runtime/` decken die Loop-Runtime ab: `LoopResolverTests` prüft
Body-Ermittlung (inkl. Ein-Fragen-Loop und Überlappungs-Ablehnung), Iterations-/Instanz-Zuordnung,
Collection-Aufbau und Iterationsindex isoliert; `LoopRuntimeTests` treibt mehrere Iterationen, das
Verlassen des Zyklus, collection- und iterationsindex-getriebene Break-Bedingungen sowie das Editieren
einer Iteration gegen eine echte SQLite-Datenbank durch; `FlirtyEngineTests` spielt eine Schleife
end-to-end über `IFlirtyEngine` durch.

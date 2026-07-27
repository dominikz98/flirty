# ADR 0005 – Veröffentlichte Dialogversionen sind unveränderlich

- **Status:** Akzeptiert
- **Kontext-Issue:** #95 – Befunde aus manuellem Abnahme-Durchlauf (Versionierung, Designer-UI)
- **Betroffen:** `src/Flirty/Runtime/Admin/`, `src/Flirty/Persistence/IDialogAdminStore.cs`,
  `src/Flirty.AspNetCore/FlirtyAdminEndpointRouteBuilderExtensions.cs`,
  `src/Flirty.Designer/Components/Pages/`

## Kontext

Das Domänenmodell trägt seit #17 die Bausteine einer Versionierung: `Dialog.Version`, den
Unique-Index `(Key, Version)` und `DialogSession.DialogVersion`. Darauf beruhte eine Zusage, die in
`ARCHITECTURE.md`, `DOMAIN-MODEL.md`, `RUNTIME.md`, `DESIGNER.md` und `CLAUDE.md` stand: *Sessions
pinnen ihre Dialogversion, das Editieren publizierter Dialoge bricht laufende Sessions nicht.*

Ein Abnahme-Durchlauf gegen die laufende Anwendung (#95) hat gezeigt, dass die Zusage nicht hielt:

- `Version` wurde ausschließlich beim Anlegen auf `1` gesetzt, kein Command zählte sie hoch. Ein
  zweiter Dialog mit gleichem `Key` wurde abgelehnt (`CreateDialogCommand` prüfte nur den Schlüssel).
  **Es gab also überhaupt keinen Weg zu einer zweiten Version** – das Feld war toter Ballast.
- Die Laufzeit lädt den Graphen einer Session über `IDialogStore.GetDialogAsync(dialogId)`, also aus
  **derselben Zeile**, die das Admin-CRUD in-place verändert. Das Pinning konnte damit nie greifen.
- Praktisch: Wird die aktuell offene Frage eines veröffentlichten Dialogs gelöscht, antworten
  `GET /flirty/sessions/{id}` **und** der Submit mit `409` – die Session ist weder fortsetzbar noch
  lesbar.

Zu entscheiden war deshalb, ob die Zusage erfüllt oder zurückgenommen wird.

## Entscheidung

Die Zusage wird **erfüllt**. Dafür gelten drei Regeln:

1. **Eine veröffentlichte Version ist unveränderlich.** Jede Änderung an ihrem Konfigurationsgraphen –
   Fragen, Antwortoptionen, Übergänge, Schleifen-Marker, Trigger und die Einstiegsfrage – wird mit
   `DialogPublishedException` (→ HTTP `409`) abgelehnt. Durchgesetzt wird das von
   `DialogEditGuard.EnsureEditable*`, aufgerufen als erste Vorbedingung der 15 Graph-Commands und im
   `UpdateDialogCommand` beim Wechsel der Einstiegsfrage. **Name und Beschreibung bleiben änderbar** –
   sie sind rein beschreibend und wirken auf keinen Ablauf.
2. **Weiterentwickelt wird über eine neue Version.** `CreateDialogVersionCommand`
   (`POST {prefix}/dialogs/{id}/versions`) klont den Graphen mit neuen Guids als **Entwurf** mit der
   nächsten Versionsnummer und schreibt alle Frage-Verweise auf die Kopien um. Freigegeben wird
   getrennt; `PublishDialogCommand` zieht dabei die bisher produktive Version desselben Schlüssels
   zurück, sodass je Schlüssel höchstens eine Version veröffentlicht ist.
3. **Eine Version mit laufenden Sessions wird nicht gelöscht.** `DeleteDialogCommand` lehnt ab, solange
   Sessions mit `InProgress` existieren (die Meldung nennt die Anzahl);
   `AbandonDialogSessionsCommand` beendet sie auf Wunsch vorher (Status `Abandoned`, Antworten und
   Verlauf bleiben erhalten). Das Löschen ist der eine Fall, den das Pinning nicht abdecken kann – ohne
   Graph gibt es keinen Ablauf mehr.

Der Nachweis liegt als Test vor: `DialogVersioningTests` spielt in
`Laufende_Session_ueberlebt_eine_neu_veroeffentlichte_Version` eine Session auf Version 1 zu Ende,
während Version 2 abgeleitet, geändert und veröffentlicht wird – und weist nach, dass ein neuer
Anwender auf Version 2 landet.

## Verworfene Alternativen

**Zusage zurücknehmen und nur dokumentieren.** Die billigste Variante: Doku und Designer sagen künftig,
dass Änderungen an veröffentlichten Dialogen laufende Sessions brechen können. Ausgeschieden, weil das
Versprechen zum Kern des Produkts gehört – ein Dialog läuft über Tage, und die README führt
„Dialog-Versionierung" als Feature. Zudem blieben `Dialog.Version`, `(Key, Version)` und
`DialogSession.DialogVersion` dauerhaft funktionslos: Ballast, der eine Fähigkeit vorspiegelt.

**Nur sperren, ohne Klon-Funktion.** Graph-Änderungen an veröffentlichten Dialogen ablehnen und den Weg
immer über *Zurückziehen → ändern → veröffentlichen* führen. Verhindert das versehentliche Brechen,
löst das Problem aber nicht: Während der Bearbeitung kann niemand den Dialog starten, und nach dem
Zurückziehen liegen die Änderungen wieder auf derselben Zeile, an der die laufenden Sessions hängen –
sie brechen also weiter, nur später. Für ein Werkzeug, das produktive Dialoge pflegt, zu wenig.

**Copy-on-write beim ersten Zugriff.** Ein `UpdateQuestionCommand` auf eine veröffentlichte Version
hätte implizit eine neue Version angelegt und die Änderung dort ausgeführt. Bequem, aber die Antwort
eines `PUT .../questions/{id}` hätte plötzlich eine **andere** Id betroffen als die Route nannte, und der
Aufrufer hätte unbemerkt eine zweite Version erzeugt. Eine so überraschende API-Semantik wiegt schwerer
als der gesparte Klick; die Ableitung bleibt darum ein ausdrücklicher Schritt.

**Sessions unveränderlich machen statt Dialoge** – etwa durch das Einfrieren des Graphen in die Session
(Snapshot als JSON). Löst dasselbe Problem und bricht auch beim Löschen nicht. Ausgeschieden, weil es
das Datenmodell verdoppelt (Konfiguration liegt in Tabellen *und* in jeder Session), Antworten nicht
mehr per Fremdschlüssel auf Fragen zeigen könnten und der Designer keine Sicht auf laufende Sessions
mehr hätte. Der Aufwand steht in keinem Verhältnis zum Klon einer Dialogzeile.

**Sessions beim Löschen mitentfernen** (Cascade), statt das Löschen zu verweigern. Verhindert Waisen,
vernichtet aber die Antwortdaten – in der Regel die eigentliche Ausbeute eines Dialogs. Die Engine kennt
bewusst kein Löschen von Sessions; das bleibt so.

## Konsequenzen

**Positiv**

- Die Zusage aus vier Guides und `CLAUDE.md` gilt jetzt tatsächlich und ist durch Tests abgesichert.
- Der Designer spiegelt die Regel: gesperrte Editoren, ein Banner mit dem Ausweg und die Schaltfläche
  „Neue Version anlegen"; die Löschsperre zeigt die Anzahl laufender Sessions samt Abbruch-Aktion.
- `Dialog.Version` und `(Key, Version)` haben eine Funktion; die Versionsreihe eines Schlüssels ist in
  der Dialogliste sichtbar.

**Negativ**

- Mehr Schritte für den Anwender: Eine Korrektur an einem produktiven Dialog erfordert Ableiten,
  Ändern, Veröffentlichen. Für einen Tippfehler im Fragetext ist das mehr Aufwand als vorher.
- Jede Version ist eine vollständige Kopie des Graphen – bei vielen Versionen wächst die Datenbank.
  Ein Aufräumen alter Versionen gibt es nicht (Löschen ist manuell und durch laufende Sessions
  gesperrt).
- Die 15 Graph-Commands tragen je einen zusätzlichen Datenbankzugriff für die Prüfung.

**Offen**

- **Umbenennen einer versionierten Familie.** Der `Key` identifiziert die Familie; ihn an nur einer von
  mehreren Versionen zu ändern, würde die Reihe zerreißen und wird abgelehnt. Ein „alle Versionen
  umbenennen" gibt es noch nicht.
- **Vergleich zweier Versionen** (Diff) und das **Zurückrollen** auf eine frühere Version sind nicht
  umgesetzt – beides ließe sich ohne Modelländerung nachziehen (Klon der älteren Version + Publish).
- **Aufräumen** alter Entwürfe/Versionen ist Handarbeit über die Dialogliste.

Details: [RUNTIME.md § Versions-Pinning](../RUNTIME.md#versions-pinning),
[DESIGNER.md § Versionierung](../DESIGNER.md#versionierung-95).

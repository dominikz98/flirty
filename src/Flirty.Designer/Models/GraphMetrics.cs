namespace Flirty.Designer.Models;

/// <summary>
/// Die Maße des Graph-Canvas (#101) – eine Quelle für das Layout in C#, das Markup in
/// <c>DialogGraph.razor</c> und die Grenzwerte, die an das JS-Modul gereicht werden.
/// </summary>
/// <remarks>
/// Die Abstände sind bewusst <b>ganzzahlig und gerade</b>: Die Koordinaten entstehen ausschließlich als
/// ganzzahlige Vielfache aus Schicht und Spalte, nie aus einem Mittelwert. Andernfalls hingen die
/// letzten Nachkommastellen an der Gleitkomma-Reihenfolge, und die Zusage „gleicher Graph ⇒ gleiche
/// Koordinaten" (Akzeptanzkriterium) wäre nur noch meistens wahr.
/// </remarks>
public static class GraphMetrics
{
    /// <summary>Breite einer Knotenkarte in px.</summary>
    public const double NodeWidth = 240;

    /// <summary>
    /// Höhe einer Knotenkarte in px. Bemessen für den vollen Inhalt – Badge-Zeile, zwei Zeilen
    /// Fragetext, Metazeile <b>und</b> eine Reihe Trigger-Chips. Die Karte schneidet Überlauf ab; wäre
    /// sie knapper, verschwänden die Chips unsichtbar.
    /// </summary>
    public const double NodeHeight = 112;

    /// <summary>Waagerechter Abstand zwischen zwei Knoten derselben Schicht in px.</summary>
    public const double GapX = 60;

    /// <summary>Senkrechter Abstand zwischen zwei Schichten in px.</summary>
    public const double GapY = 80;

    /// <summary>Rand links und rechts des Graphen in px.</summary>
    public const double MarginX = 40;

    /// <summary>Rand oben und unten in px.</summary>
    public const double MarginY = 40;

    /// <summary>Waagerechter Rasterabstand: Knotenbreite plus Lücke.</summary>
    public const double PitchX = NodeWidth + GapX;

    /// <summary>Senkrechter Rasterabstand: Knotenhöhe plus Lücke.</summary>
    public const double PitchY = NodeHeight + GapY;

    /// <summary>
    /// Seitlicher Versatz je zusätzlicher Kante zwischen demselben Knotenpaar in px. Ohne ihn lägen
    /// mehrere Übergänge deckungsgleich übereinander und wären nicht unterscheidbar.
    /// </summary>
    public const double FanStep = 18;

    /// <summary>Auslenkung der Bézier-Kontrollpunkte einer Vorwärtskante in px.</summary>
    public const double EdgeBend = 70;

    /// <summary>Abstand zweier Rücksprung-Kanäle rechts des Graphen in px.</summary>
    public const double GutterStep = 34;

    /// <summary>Innenabstand eines Schleifen-Rahmens zu den Knoten seines Bereichs in px.</summary>
    public const double LoopFramePadding = 20;

    /// <summary>
    /// Zusätzlicher Innenabstand je weiterem Schleifen-Rahmen in px – damit sich zwei Rahmen nicht
    /// exakt decken.
    /// </summary>
    public const double LoopFramePaddingStep = 10;

    /// <summary>
    /// Kantenlänge des Ausgangs-Ports am Knoten in px (#103).
    /// </summary>
    /// <remarks>
    /// Der Port sitzt an der <b>Unterkante-Mitte</b> – genau dort, wo <c>GraphLayout.Route</c> eine
    /// Vorwärtskante ansetzt. Die Affordanz behauptet damit denselben Ansatzpunkt, den die erzeugte
    /// Kante später hat; ein Port an einer anderen Stelle wäre eine sichtbare Lüge über die Geometrie.
    /// </remarks>
    public const double PortSize = 26;

    /// <summary>
    /// Kleinste Breite der Zeichenfläche in px – unabhängig davon, wie wenig darauf liegt.
    /// </summary>
    /// <remarks>
    /// Seit #103 ist der Canvas eine <b>Ablagefläche</b>: Ein Fragetyp wird aus der Palette darauf
    /// gezogen. Ohne Untergrenze wäre die Fläche eines leeren Dialogs
    /// <c>MarginX * 2</c> × <c>MarginY * 2</c> = 80 × 80 px – zu klein, um darauf zu zielen, und genau
    /// im leeren Dialog beginnt man.
    /// </remarks>
    public const double MinCanvasWidth = 960;

    /// <summary>Kleinste Höhe der Zeichenfläche in px. Begründung wie bei <see cref="MinCanvasWidth"/>.</summary>
    public const double MinCanvasHeight = 540;

    /// <summary>Kleinster Zoomfaktor des Canvas.</summary>
    public const double MinZoom = 0.25;

    /// <summary>Größter Zoomfaktor des Canvas.</summary>
    public const double MaxZoom = 2.5;

    /// <summary>Zoomschritt je Rasterung des Mausrads bzw. Klick auf die Werkzeugleiste.</summary>
    public const double ZoomStep = 1.15;
}

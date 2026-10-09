using DrawingSpace.Core;

namespace DrawingSpace.Stencils;

public static class StencilCatalog
{
    public static IReadOnlyList<Stencil> All { get; } =
    [
        new("instromVerteiler", "Verteiler und Leiter", [
            new("einspeisung", "Einspeisung", ShapeKind.Rectangle, 72, 72),
            new("verteilung", "Verteilung", ShapeKind.Rectangle, 72, 72),
            new("zweig", "Zweig", ShapeKind.Rectangle, 72, 72),
            new("baustromverteiler", "Baustromverteiler", ShapeKind.Rectangle, 72, 72),
            new("zaehler", "Zähler", ShapeKind.Rectangle, 72, 72),
            new("hak", "Hausanschlusskasten", ShapeKind.Rectangle, 72, 72)
        ]),
        new("instromVerbraucher", "Verbraucher", [
            new("allgverbraucher", "Allg. Verbraucher", ShapeKind.Rectangle, 72, 72),
            new("allgtaumheizung", "Allg. Raumheizung", ShapeKind.Rectangle, 72, 72),
            new("allgverbr3steckdose", "Allg. Verbraucher an Drehstromsteckdose", ShapeKind.Rectangle, 72, 72),
            new("backofen", "Backofen", ShapeKind.Rectangle, 72, 72),
            new("bhkw", "Blockheizkraftwerk", ShapeKind.Rectangle, 72, 72),
            new("steckdose3p", "Drehstromsteckdose", ShapeKind.Rectangle, 72, 72),
            new("durchlauferhitzer", "Durchlauferhitzer", ShapeKind.Rectangle, 72, 72),
            new("heisswasserspeicher", "Heisswasserspeicher 30-80 Liter", ShapeKind.Rectangle, 72, 72),
            new("kochfeld3er", "Kochfeld mit 3 Platten", ShapeKind.Rectangle, 72, 72),
            new("kochfeld4er", "Kochfeld mit 4 Platten", ShapeKind.Rectangle, 72, 72),
            new("kompensationsanlage", "Kompensationsanlage", ShapeKind.Rectangle, 72, 72),
        ]),
        new("instromSicherheit", "Sichern und Schalten", [
            new("schmelzsicherung", "Sicherung", ShapeKind.Rectangle, 72, 72),
            new("rcd", "RCD", ShapeKind.Rectangle, 72, 72),
            new("sicherungslasttrenner", "Sicherungslasttrenner", ShapeKind.Rectangle, 72, 72),
            new("lsrcdkombi", "Kombinierter RCD-LS-Schalter", ShapeKind.Rectangle, 72, 72),
            new("selthlschutzschalter", "Selektiver Hauptleitungsschutzschalter", ShapeKind.Rectangle, 72, 72),
            new("schalter", "Schalter", ShapeKind.Rectangle, 72, 72),
            new("leistungsschalter", "Leistungsschalter", ShapeKind.Rectangle, 72, 72),
            new("leitungsschutzschalter", "Leitungsschutzschalter", ShapeKind.Rectangle, 72, 72),
            new("motorschutzschalter", "Motorschutzschalter", ShapeKind.Rectangle, 72, 72),
        ]),
        new("instromReference", "Seiten Referenzen", [
            new("off-page", "Off-page reference", ShapeKind.OffPage, 72, 72),
            new("on-page", "On-page reference", ShapeKind.Ellipse, 72, 72)
        ]),
        new("annotations", "Beschriftungen", [
            new("callout", "Callout", ShapeKind.Callout, 184, 104),
            new("note", "Note", ShapeKind.Note, 152, 128),
            new("annotation", "Annotation", ShapeKind.Annotation, 160, 88),
            new("label", "Label", ShapeKind.Text, 200, 40)
        ])
    ];
    public static IEnumerable<StencilMaster> Search(string query) => All.SelectMany(s => s.Masters).Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    public static StencilMaster Find(string id) => All.SelectMany(s => s.Masters).First(m => m.Id == id);
}

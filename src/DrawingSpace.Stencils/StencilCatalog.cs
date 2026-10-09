using DrawingSpace.Core;

namespace DrawingSpace.Stencils;

public static class StencilCatalog
{
    public static IReadOnlyList<Stencil> All { get; } =
    [
        new("instromVerteiler", "Verteiler und Leiter", [
            new("einspeisung", "Einspeisung", ShapeKind.Rectangle, 100, 100),
            new("verteilung", "Verteilung", ShapeKind.Verteilung, 100, 100),
            new("zweig", "Zweig", ShapeKind.Zweig, 100, 100),
            new("baustromverteiler", "Baustromverteiler", ShapeKind.Rectangle, 100, 100),
            new("zaehler", "Zähler", ShapeKind.Zaehler, 100, 100),
            new("hak", "Hausanschlusskasten", ShapeKind.Hak, 100, 100)
        ]),
        new("instromVerbraucher", "Verbraucher", [
            new("allgverbraucher", "Allg. Verbraucher", ShapeKind.Rectangle, 100, 100),
            new("allgtaumheizung", "Allg. Raumheizung", ShapeKind.Rectangle, 100, 100),
            new("allgverbr3steckdose", "Allg. Verbraucher an Drehstromsteckdose", ShapeKind.Rectangle, 100, 100),
            new("backofen", "Backofen", ShapeKind.Rectangle, 100, 100),
            new("bhkw", "Blockheizkraftwerk", ShapeKind.Rectangle, 100, 100),
            new("steckdose3p", "Drehstromsteckdose", ShapeKind.Rectangle, 100, 100),
            new("durchlauferhitzer", "Durchlauferhitzer", ShapeKind.Rectangle, 100, 100),
            new("heisswasserspeicher", "Heisswasserspeicher 30-80 Liter", ShapeKind.Rectangle, 100, 100),
            new("kochfeld3er", "Kochfeld mit 3 Platten", ShapeKind.Rectangle, 100, 100),
            new("kochfeld4er", "Kochfeld mit 4 Platten", ShapeKind.Rectangle, 100, 100),
            new("kompensationsanlage", "Kompensationsanlage", ShapeKind.Rectangle, 100, 100),
        ]),
        new("instromSicherheit", "Sichern und Schalten", [
            new("schmelzsicherung", "Sicherung", ShapeKind.Sicherung, 100, 100),
            new("rcd", "RCD", ShapeKind.Rcd, 100, 100),
            new("sicherungslasttrenner", "Sicherungslasttrenner", ShapeKind.Rectangle, 100, 100),
            new("lsrcdkombi", "Kombinierter RCD-LS-Schalter", ShapeKind.Rectangle, 100, 100),
            new("selthlschutzschalter", "Selektiver Hauptleitungsschutzschalter", ShapeKind.Rectangle, 100, 100),
            new("schalter", "Schalter", ShapeKind.Rectangle, 100, 100),
            new("leistungsschalter", "Leistungsschalter", ShapeKind.Rectangle, 100, 100),
            new("leitungsschutzschalter", "Leitungsschutzschalter", ShapeKind.Rectangle, 100, 100),
            new("motorschutzschalter", "Motorschutzschalter", ShapeKind.Rectangle, 100, 100),
        ]),
        new("beleuchtung", "Beleuchtung", [
            new ("gluehlampe", "Glühlampe", ShapeKind.Gluehlampe, 100, 100),
            new("halogenlampeevg", "Halogenlampe mit EVG", ShapeKind.Halogenlampe, 100, 100),
            new ("halogenlampetrafo", "Halogenlampe mit Trafo", ShapeKind.Halogenlampe, 100,100),
            new("leuchtstoff1x", "Leuchte mit 1 Leuchtstofflampe", ShapeKind.Leuchtstoff1X, 100, 100),
            new("leuchtstoff2x", "Leuchte mit 2 Leuchtstofflampen", ShapeKind.Leuchtstoff2X, 100, 100),
            new("leuchtstoff3x", "Leuchte mit 3 Leuchtstofflampen", ShapeKind.Leuchtstoff3X, 100, 100),
            new("leuchtstoff4x", "Leuchte mit 4 Leuchtstofflampen", ShapeKind.Leuchtstoff4X, 100, 100),
        ]),
        new("instromReference", "Seiten Referenzen", [
            new("off-page", "Off-page reference", ShapeKind.OffPage, 100, 100),
            new("on-page", "On-page reference", ShapeKind.Ellipse, 100, 100)
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

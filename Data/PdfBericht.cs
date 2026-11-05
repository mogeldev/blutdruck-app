using System.Globalization;
using BlutdruckApp.Models;
using SkiaSharp;

namespace BlutdruckApp.Data;

/// <summary>
/// Erstellt ein Blutdruck-Protokoll als PDF (A4): Übersicht, Verlaufsdiagramm und Tabelle aller Einträge.
/// </summary>
public static class PdfBericht
{
    const float Breite = 595, Hoehe = 842, Rand = 40;
    const float ZeilenHoehe = 16;
    const float TabelleUnten = Hoehe - Rand - 20;

    static readonly CultureInfo De = new("de-CH");

    static readonly SKColor Text = new(0x21, 0x21, 0x21);
    static readonly SKColor Grau = new(0x75, 0x75, 0x75);
    static readonly SKColor Linie = new(0xE0, 0xE0, 0xE0);
    static readonly SKColor Flaeche = new(0xF6, 0xF6, 0xF6);
    static readonly SKColor Rot = new(0xC6, 0x28, 0x28);
    static readonly SKColor Blau = new(0x1E, 0x5A, 0xA8);
    static readonly SKColor Violett = new(0x5C, 0x6B, 0xC0);

    // Spalten der Tabelle (x-Position, rechtsbündig ja/nein)
    const float SpDatum = Rand, SpZeit = Rand + 66, SpSys = Rand + 136, SpDia = Rand + 174, SpPuls = Rand + 212, SpBem = Rand + 228;

    public static void Schreiben(Stream ziel, IReadOnlyList<Eintrag> eintraege, DateTime von, DateTime bis)
    {
        var meta = new SKDocumentPdfMetadata
        {
            Title = "Blutdruck-Protokoll",
            Creator = "Blutdruck-App",
            Creation = DateTime.Now,
        };
        using var doc = SKDocument.CreatePdf(ziel, meta);
        Zeichnen(eintraege, von, bis, () => doc.BeginPage(Breite, Hoehe), doc.EndPage);
        doc.Close();
    }

    /// <summary>
    /// Zeichnet alle Seiten über die übergebenen Callbacks. Getrennt von <see cref="Schreiben"/>,
    /// damit sich das Layout auch als Bild rendern lässt.
    /// </summary>
    public static void Zeichnen(IReadOnlyList<Eintrag> eintraege, DateTime von, DateTime bis,
        Func<SKCanvas> neueSeite, Action seiteFertig)
    {
        var liste = eintraege.OrderBy(e => e.Zeitpunkt).ToList();
        var messungen = liste.Where(e => e.IstMessung).ToList();

        const float ersteZeileSeite1 = 418, ersteZeileFolgeseite = Rand + 26;
        int proSeite1 = (int)((TabelleUnten - ersteZeileSeite1) / ZeilenHoehe);
        int proFolgeseite = (int)((TabelleUnten - ersteZeileFolgeseite) / ZeilenHoehe);
        int seiten = 1 + (liste.Count <= proSeite1 ? 0 : (int)Math.Ceiling((liste.Count - proSeite1) / (double)proFolgeseite));

        // ---------- Seite 1 ----------
        var c = neueSeite();
        Kopf(c, von, bis);
        Uebersicht(c, messungen, liste.Count(e => e.IstEinnahme));
        Diagramm(c, messungen, liste.Where(e => e.IstEinnahme).ToList(), 206, 372);

        int index = 0;
        if (liste.Count == 0)
        {
            Schreib(c, "Keine Einträge im gewählten Zeitraum.", Rand, 430, 10, Grau);
        }
        else
        {
            TabellenKopf(c, ersteZeileSeite1 - 16);
            index = Zeilen(c, liste, index, proSeite1, ersteZeileSeite1);
        }
        Fuss(c, 1, seiten);
        seiteFertig();

        // ---------- Folgeseiten ----------
        for (int s = 2; index < liste.Count; s++)
        {
            c = neueSeite();
            TabellenKopf(c, ersteZeileFolgeseite - 16);
            index = Zeilen(c, liste, index, proFolgeseite, ersteZeileFolgeseite, ersteZeileDatumImmer: true);
            Fuss(c, s, seiten);
            seiteFertig();
        }
    }

    // ---------- Bausteine ----------

    static void Kopf(SKCanvas c, DateTime von, DateTime bis)
    {
        Schreib(c, "Blutdruck-Protokoll", Rand, Rand + 18, 20, Text, fett: true);
        Schreib(c, $"Zeitraum {von:dd.MM.yyyy} – {bis:dd.MM.yyyy}   ·   erstellt am {DateTime.Now:dd.MM.yyyy}",
            Rand, Rand + 36, 9.5f, Grau);
    }

    static void Uebersicht(SKCanvas c, List<Eintrag> m, int tabletten)
    {
        const float top = 92, hoehe = 58, abstand = 10;
        float boxBreite = (Breite - 2 * Rand - 3 * abstand) / 4;

        var morgens = m.Where(e => e.Zeitpunkt.Hour < 12).ToList();
        var abends = m.Where(e => e.Zeitpunkt.Hour >= 18).ToList();

        (string Titel, string Wert, string Info)[] boxen =
        [
            ("Ø gesamt", Schnitt(m), Info(m)),
            ("Ø morgens (bis 12 Uhr)", Schnitt(morgens), Info(morgens)),
            ("Ø abends (ab 18 Uhr)", Schnitt(abends), Info(abends)),
            ("Tabletten", $"{tabletten} ×", "erfasste Einnahmen"),
        ];

        for (int i = 0; i < boxen.Length; i++)
        {
            float x = Rand + i * (boxBreite + abstand);
            using var fill = new SKPaint { Color = Flaeche, IsAntialias = true };
            c.DrawRoundRect(new SKRect(x, top, x + boxBreite, top + hoehe), 6, 6, fill);
            Schreib(c, boxen[i].Titel, x + 9, top + 15, 8, Grau);
            Schreib(c, boxen[i].Wert, x + 9, top + 35, 15, Text, fett: true);
            Schreib(c, boxen[i].Info, x + 9, top + 49, 8, Grau);
        }

        if (m.Count > 0)
        {
            var max = m.MaxBy(e => e.Sys)!;
            var min = m.MinBy(e => e.Sys)!;
            Schreib(c, $"Höchster Wert: {max.Sys}/{max.Dia} ({max.Zeitpunkt:dd.MM. HH:mm})     ·     "
                     + $"Tiefster Wert: {min.Sys}/{min.Dia} ({min.Zeitpunkt:dd.MM. HH:mm})",
                Rand, top + hoehe + 17, 9, Text);
        }
        Schreib(c, "Farben: grün = normal (< 130/85) · gelb = hoch-normal · orange = erhöht (≥ 140/90) · rot = deutlich erhöht (≥ 160/100)",
            Rand, top + hoehe + 31, 7.5f, Grau);
    }

    static string Schnitt(List<Eintrag> m) =>
        m.Count == 0 ? "–" : $"{m.Average(e => e.Sys):0} / {m.Average(e => e.Dia):0}";

    static string Info(List<Eintrag> m)
    {
        if (m.Count == 0) return "keine Messungen";
        var puls = m.Where(e => e.Puls.HasValue).Select(e => e.Puls!.Value).ToList();
        var anzahl = m.Count == 1 ? "1 Messung" : $"{m.Count} Messungen";
        return puls.Count > 0 ? $"Puls {puls.Average():0} · {anzahl}" : anzahl;
    }

    static void Diagramm(SKCanvas c, List<Eintrag> m, List<Eintrag> tabletten, float top, float bottom)
    {
        Schreib(c, "Verlauf", Rand, top, 11, Text, fett: true);
        Legende(c, top);

        float links = Rand + 26, rechts = Breite - Rand, oben = top + 12, unten = bottom - 14;

        if (m.Count == 0)
        {
            Schreib(c, "Keine Messungen im Zeitraum.", links, (oben + unten) / 2, 9, Grau);
            return;
        }

        // Wertebereich (immer inkl. der Grenzwerte 90 und 140)
        int yMin = Math.Min(80, (int)Math.Floor((m.Min(e => e.Dia) - 10) / 20.0) * 20);
        int yMax = Math.Max(160, (int)Math.Ceiling((m.Max(e => e.Sys) + 10) / 20.0) * 20);
        float Y(double v) => unten - (float)((v - yMin) / (yMax - yMin)) * (unten - oben);

        var tMin = m[0].Zeitpunkt;
        var tMax = m[^1].Zeitpunkt;
        if ((tMax - tMin).TotalHours < 24) { tMin = tMin.AddHours(-12); tMax = tMax.AddHours(12); }
        double spanne = (tMax - tMin).TotalSeconds;
        float X(DateTime t) => links + (float)((t - tMin).TotalSeconds / spanne) * (rechts - links);

        using var gitter = new SKPaint { Color = Linie, StrokeWidth = 0.5f, IsAntialias = true };
        for (int v = yMin; v <= yMax; v += 20)
        {
            c.DrawLine(links, Y(v), rechts, Y(v), gitter);
            Schreib(c, v.ToString(), links - 5, Y(v) + 3, 7.5f, Grau, align: SKTextAlign.Right);
        }

        // Grenzwerte
        using var dash = SKPathEffect.CreateDash([4f, 3f], 0);
        using (var g = new SKPaint { Color = Rot.WithAlpha(150), StrokeWidth = 0.8f, PathEffect = dash, IsAntialias = true })
            c.DrawLine(links, Y(140), rechts, Y(140), g);
        using (var g = new SKPaint { Color = Blau.WithAlpha(150), StrokeWidth = 0.8f, PathEffect = dash, IsAntialias = true })
            c.DrawLine(links, Y(90), rechts, Y(90), g);

        // Tabletten als Striche am unteren Rand
        using (var t = new SKPaint { Color = Violett, StrokeWidth = 1.2f, IsAntialias = true })
            foreach (var e in tabletten.Where(e => e.Zeitpunkt >= tMin && e.Zeitpunkt <= tMax))
                c.DrawLine(X(e.Zeitpunkt), unten, X(e.Zeitpunkt), unten - 7, t);

        Kurve(c, m.Select(e => new SKPoint(X(e.Zeitpunkt), Y(e.Sys))).ToList(), Rot, m.Count <= 120);
        Kurve(c, m.Select(e => new SKPoint(X(e.Zeitpunkt), Y(e.Dia))).ToList(), Blau, m.Count <= 120);

        // x-Achse
        using var achse = new SKPaint { Color = Grau, StrokeWidth = 0.8f, IsAntialias = true };
        c.DrawLine(links, unten, rechts, unten, achse);
        string format = (tMax - tMin).TotalDays > 3 ? "dd.MM." : "dd.MM. HH:mm";
        for (int i = 0; i <= 4; i++)
        {
            var t = tMin.AddSeconds(spanne * i / 4);
            var align = i == 0 ? SKTextAlign.Left : i == 4 ? SKTextAlign.Right : SKTextAlign.Center;
            Schreib(c, t.ToString(format, De), X(t), unten + 11, 7.5f, Grau, align: align);
        }
    }

    static void Kurve(SKCanvas c, List<SKPoint> punkte, SKColor farbe, bool mitPunkten)
    {
        using var linie = new SKPaint { Color = farbe, StrokeWidth = 1.4f, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round };
        using var pfad = new SKPath();
        pfad.MoveTo(punkte[0]);
        foreach (var p in punkte.Skip(1)) pfad.LineTo(p);
        c.DrawPath(pfad, linie);

        if (!mitPunkten && punkte.Count > 1) return;
        using var punkt = new SKPaint { Color = farbe, IsAntialias = true };
        foreach (var p in punkte) c.DrawCircle(p, 2, punkt);
    }

    static void Legende(SKCanvas c, float y)
    {
        float x = Breite - Rand;
        void Zeichen(string text, SKColor farbe, bool gestrichelt = false, bool senkrecht = false)
        {
            x -= TextBreite(text, 7.5f);
            Schreib(c, text, x, y, 7.5f, Grau);
            x -= 16;
            using var p = new SKPaint { Color = farbe, StrokeWidth = senkrecht ? 1.2f : 1.4f, IsAntialias = true };
            if (gestrichelt) p.PathEffect = SKPathEffect.CreateDash([3f, 2f], 0);
            if (senkrecht) c.DrawLine(x + 6, y + 1, x + 6, y - 7, p);
            else c.DrawLine(x, y - 3, x + 12, y - 3, p);
            x -= 12;
        }
        Zeichen("Grenze 140/90", Grau, gestrichelt: true);
        Zeichen("Tablette", Violett, senkrecht: true);
        Zeichen("DIA", Blau);
        Zeichen("SYS", Rot);
    }

    static void TabellenKopf(SKCanvas c, float y)
    {
        Schreib(c, "Datum", SpDatum, y, 8.5f, Grau, fett: true);
        Schreib(c, "Zeit", SpZeit, y, 8.5f, Grau, fett: true);
        Schreib(c, "SYS", SpSys, y, 8.5f, Grau, fett: true, align: SKTextAlign.Right);
        Schreib(c, "DIA", SpDia, y, 8.5f, Grau, fett: true, align: SKTextAlign.Right);
        Schreib(c, "Puls", SpPuls, y, 8.5f, Grau, fett: true, align: SKTextAlign.Right);
        Schreib(c, "Bemerkung", SpBem, y, 8.5f, Grau, fett: true);
        using var p = new SKPaint { Color = Grau, StrokeWidth = 0.8f };
        c.DrawLine(Rand, y + 5, Breite - Rand, y + 5, p);
    }

    /// <summary>Zeichnet bis zu <paramref name="max"/> Zeilen ab <paramref name="start"/> und gibt den nächsten Index zurück.</summary>
    static int Zeilen(SKCanvas c, List<Eintrag> liste, int start, int max, float y, bool ersteZeileDatumImmer = false)
    {
        using var trenner = new SKPaint { Color = Linie, StrokeWidth = 0.6f };
        int i = start;
        for (int n = 0; n < max && i < liste.Count; n++, i++)
        {
            var e = liste[i];
            float basis = y + n * ZeilenHoehe;
            bool neuerTag = i == 0 || liste[i - 1].Zeitpunkt.Date != e.Zeitpunkt.Date;

            if (neuerTag && n > 0)
                c.DrawLine(Rand, basis - ZeilenHoehe + 4, Breite - Rand, basis - ZeilenHoehe + 4, trenner);
            if (neuerTag || (n == 0 && ersteZeileDatumImmer))
                Schreib(c, e.Zeitpunkt.ToString("ddd dd.MM.yy", De), SpDatum, basis, 9, Text);

            Schreib(c, e.Zeitpunkt.ToString("HH:mm"), SpZeit, basis, 9, Text);

            if (e.IstMessung)
            {
                var farbe = WertFarbe(e);
                bool fett = e.Sys >= 140 || e.Dia >= 90;
                Schreib(c, e.Sys.ToString(), SpSys, basis, 9, farbe, fett, SKTextAlign.Right);
                Schreib(c, e.Dia.ToString(), SpDia, basis, 9, farbe, fett, SKTextAlign.Right);
                Schreib(c, e.Puls?.ToString() ?? "", SpPuls, basis, 9, Text, align: SKTextAlign.Right);
                if (!string.IsNullOrWhiteSpace(e.Notiz))
                    Schreib(c, Kuerzen(e.Notiz!, Breite - Rand - SpBem - 12, 9), SpBem + 12, basis, 9, Grau);
            }
            else
            {
                var text = "Tablette"
                    + (string.IsNullOrWhiteSpace(e.Medikament) ? "" : ": " + e.Medikament)
                    + (string.IsNullOrWhiteSpace(e.Notiz) ? "" : " – " + e.Notiz);
                Schreib(c, Kuerzen(text, Breite - Rand - SpBem - 12, 9), SpBem + 12, basis, 9, Violett);
            }
        }
        return i;
    }

    static SKColor WertFarbe(Eintrag e) =>
        e.Sys >= 160 || e.Dia >= 100 ? new SKColor(0xC6, 0x28, 0x28)
        : e.Sys >= 140 || e.Dia >= 90 ? new SKColor(0xE6, 0x51, 0x00)
        : e.Sys >= 130 || e.Dia >= 85 ? new SKColor(0x9E, 0x77, 0x00)
        : new SKColor(0x2E, 0x7D, 0x32);

    static void Fuss(SKCanvas c, int seite, int seiten)
    {
        Schreib(c, $"Blutdruck-Protokoll   ·   Seite {seite} von {seiten}", Breite / 2, Hoehe - 24, 7.5f, Grau,
            align: SKTextAlign.Center);
    }

    // ---------- Text ----------

    static SKTypeface? _normal, _fett;

    static SKTypeface Schrift(bool fett)
    {
        if (fett) return _fett ??= Laden("font.OpenSans-Semibold.ttf") ?? SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        return _normal ??= Laden("font.OpenSans-Regular.ttf") ?? SKTypeface.Default;
    }

    static SKTypeface? Laden(string name)
    {
        using var s = typeof(PdfBericht).Assembly.GetManifestResourceStream(name);
        if (s is null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        using var data = SKData.CreateCopy(ms.ToArray());
        return SKTypeface.FromData(data);
    }

    static void Schreib(SKCanvas c, string text, float x, float y, float groesse, SKColor farbe,
        bool fett = false, SKTextAlign align = SKTextAlign.Left)
    {
        using var font = new SKFont(Schrift(fett), groesse);
        using var paint = new SKPaint { Color = farbe, IsAntialias = true };
        c.DrawText(text, x, y, align, font, paint);
    }

    static float TextBreite(string text, float groesse, bool fett = false)
    {
        using var font = new SKFont(Schrift(fett), groesse);
        return font.MeasureText(text);
    }

    static string Kuerzen(string text, float maxBreite, float groesse)
    {
        if (TextBreite(text, groesse) <= maxBreite) return text;
        while (text.Length > 1 && TextBreite(text + "…", groesse) > maxBreite)
            text = text[..^1];
        return text.TrimEnd() + "…";
    }
}

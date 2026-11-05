using System.Globalization;
using System.Text;
using BlutdruckApp.Models;

namespace BlutdruckApp.Data;

/// <summary>
/// CSV mit Semikolon als Trennzeichen (öffnet direkt in Excel mit Schweizer/deutschen Einstellungen).
/// Spalten: typ;datum;uhrzeit;sys;dia;puls;medikament;notiz
/// </summary>
public static class Csv
{
    const char Sep = ';';
    static readonly string[] Kopf = ["typ", "datum", "uhrzeit", "sys", "dia", "puls", "medikament", "notiz"];

    public static string Schreiben(IEnumerable<Eintrag> eintraege)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(Sep, Kopf));
        foreach (var e in eintraege.OrderBy(e => e.Zeitpunkt))
        {
            string[] felder =
            [
                e.IstMessung ? "messung" : "tablette",
                e.Zeitpunkt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                e.Zeitpunkt.ToString("HH:mm", CultureInfo.InvariantCulture),
                e.IstMessung ? e.Sys.ToString(CultureInfo.InvariantCulture) : "",
                e.IstMessung ? e.Dia.ToString(CultureInfo.InvariantCulture) : "",
                e.Puls?.ToString(CultureInfo.InvariantCulture) ?? "",
                e.Medikament ?? "",
                e.Notiz ?? "",
            ];
            sb.AppendLine(string.Join(Sep, felder.Select(Feld)));
        }
        return sb.ToString();
    }

    // Text, der mit = + - @ beginnt, würde Excel als Formel ausführen: mit ' davor entschärfen (beim Lesen wieder entfernt)
    const string FormelStart = "=+-@";

    static string Feld(string s)
    {
        if (s.Length > 0 && FormelStart.Contains(s[0])) s = "'" + s;
        return s.IndexOfAny([Sep, '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    static string? Entschaerft(string s) =>
        s.Length > 1 && s[0] == '\'' && FormelStart.Contains(s[1]) ? NullWennLeer(s[1..]) : NullWennLeer(s);

    public static List<Eintrag> Lesen(string text)
    {
        var zeilen = Zerlegen(text);
        if (zeilen.Count == 0) return [];

        var kopf = zeilen[0].Select(k => k.Trim().ToLowerInvariant()).ToList();
        int Spalte(string name) => kopf.IndexOf(name);
        int iTyp = Spalte("typ"), iDatum = Spalte("datum"), iZeit = Spalte("uhrzeit"),
            iSys = Spalte("sys"), iDia = Spalte("dia"), iPuls = Spalte("puls"),
            iMed = Spalte("medikament"), iNotiz = Spalte("notiz");

        if (iTyp < 0 || iDatum < 0 || iZeit < 0)
            throw new InvalidDataException("Unbekanntes CSV-Format (Spalten typ, datum, uhrzeit fehlen).");

        var liste = new List<Eintrag>();
        for (int z = 1; z < zeilen.Count; z++)
        {
            var f = zeilen[z];
            string Wert(int i) => i >= 0 && i < f.Count ? f[i].Trim() : "";
            if (f.All(string.IsNullOrWhiteSpace)) continue;

            var zeitpunkt = ParseZeitpunkt(Wert(iDatum), Wert(iZeit))
                ?? throw new InvalidDataException($"Zeile {z + 1}: ungültiges Datum/Uhrzeit.");
            var notiz = Entschaerft(Wert(iNotiz));

            var typ = Wert(iTyp).ToLowerInvariant();
            if (typ.StartsWith("mess"))
            {
                if (!int.TryParse(Wert(iSys), out var sys) || !int.TryParse(Wert(iDia), out var dia))
                    throw new InvalidDataException($"Zeile {z + 1}: SYS/DIA fehlt oder ist keine Zahl.");
                if (sys is < Eintrag.SysMin or > Eintrag.SysMax || dia is < Eintrag.DiaMin or > Eintrag.DiaMax)
                    throw new InvalidDataException($"Zeile {z + 1}: SYS/DIA ausserhalb des gültigen Bereichs.");
                int? puls = int.TryParse(Wert(iPuls), out var p) ? p : null;
                if (puls is < Eintrag.PulsMin or > Eintrag.PulsMax)
                    throw new InvalidDataException($"Zeile {z + 1}: Puls ausserhalb des gültigen Bereichs.");
                liste.Add(new Eintrag
                {
                    Typ = EintragTyp.Messung,
                    Zeitpunkt = zeitpunkt,
                    Sys = sys,
                    Dia = dia,
                    Puls = puls,
                    Notiz = notiz,
                });
            }
            else if (typ.StartsWith("tab") || typ.StartsWith("einn"))
            {
                liste.Add(new Eintrag
                {
                    Typ = EintragTyp.Einnahme,
                    Zeitpunkt = zeitpunkt,
                    Medikament = Entschaerft(Wert(iMed)),
                    Notiz = notiz,
                });
            }
            else
            {
                throw new InvalidDataException($"Zeile {z + 1}: unbekannter Typ \"{Wert(iTyp)}\".");
            }
        }
        return liste;
    }

    static string? NullWennLeer(string s) => s.Length == 0 ? null : s;

    static readonly string[] DatumFormate = ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy"];
    static readonly string[] ZeitFormate = ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"];

    static DateTime? ParseZeitpunkt(string datum, string zeit)
    {
        if (!DateTime.TryParseExact(datum, DatumFormate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return null;
        if (!DateTime.TryParseExact(zeit, ZeitFormate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return null;
        return d.Date + t.TimeOfDay;
    }

    /// <summary>Zerlegt CSV-Text in Zeilen und Felder (mit Unterstützung für "..." und "").</summary>
    static List<List<string>> Zerlegen(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var zeilen = new List<List<string>>();
        var zeile = new List<string>();
        var feld = new StringBuilder();
        bool inAnf = false;
        // Falls die Datei mit Komma statt Semikolon gespeichert wurde (nur die Kopfzeile entscheidet)
        int ende = text.IndexOf('\n');
        char trenner = (ende < 0 ? text : text[..ende]).Contains(Sep) ? Sep : ',';

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inAnf)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { feld.Append('"'); i++; }
                else if (c == '"') inAnf = false;
                else feld.Append(c);
            }
            else if (c == '"') inAnf = true;
            else if (c == trenner) { zeile.Add(feld.ToString()); feld.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n') { zeile.Add(feld.ToString()); feld.Clear(); zeilen.Add(zeile); zeile = []; }
            else feld.Append(c);
        }
        if (feld.Length > 0 || zeile.Count > 0) { zeile.Add(feld.ToString()); zeilen.Add(zeile); }
        return zeilen;
    }
}

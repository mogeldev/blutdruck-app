using System.Globalization;

namespace BlutdruckApp.Models;

public enum EintragTyp { Messung, Einnahme }

/// <summary>Ein Eintrag im Verlauf: entweder eine Blutdruckmessung oder eine Tabletteneinnahme.</summary>
public class Eintrag
{
    public long Id { get; set; }
    public EintragTyp Typ { get; set; }
    public DateTime Zeitpunkt { get; set; }

    // Messung
    public int Sys { get; set; }
    public int Dia { get; set; }
    public int? Puls { get; set; }

    // Einnahme
    public string? Medikament { get; set; }

    public string? Notiz { get; set; }

    // ---------- Anzeige ----------

    static readonly CultureInfo De = new("de-CH");

    public bool IstMessung => Typ == EintragTyp.Messung;
    public bool IstEinnahme => Typ == EintragTyp.Einnahme;

    public string Uhrzeit => Zeitpunkt.ToString("HH:mm", De);

    public string Titel => IstMessung
        ? $"{Sys} / {Dia}"
        : "Tablette eingenommen";

    public string Untertitel
    {
        get
        {
            var teile = new List<string>();
            if (IstMessung && Puls is int p) teile.Add($"Puls {p}");
            if (IstEinnahme && !string.IsNullOrWhiteSpace(Medikament)) teile.Add(Medikament!);
            if (!string.IsNullOrWhiteSpace(Notiz)) teile.Add(Notiz!);
            return string.Join("  ·  ", teile);
        }
    }

    public bool HatUntertitel => Untertitel.Length > 0;

    public string Symbol => IstMessung ? "♥" : "💊";

    /// <summary>Grobe Einstufung nach ESC/ESH-Grenzwerten (Praxismessung).</summary>
    public Color Farbe => !IstMessung ? Color.FromArgb("#5C6BC0")
        : Sys >= 160 || Dia >= 100 ? Color.FromArgb("#C62828")
        : Sys >= 140 || Dia >= 90 ? Color.FromArgb("#EF6C00")
        : Sys >= 130 || Dia >= 85 ? Color.FromArgb("#F9A825")
        : Color.FromArgb("#2E7D32");

    public string Einstufung => !IstMessung ? ""
        : Sys >= 160 || Dia >= 100 ? "deutlich erhöht"
        : Sys >= 140 || Dia >= 90 ? "erhöht"
        : Sys >= 130 || Dia >= 85 ? "hoch-normal"
        : "normal";
}

/// <summary>Alle Einträge eines Tages, für die gruppierte Liste.</summary>
public class TagGruppe : List<Eintrag>
{
    public DateTime Datum { get; }

    public TagGruppe(DateTime datum, IEnumerable<Eintrag> eintraege) : base(eintraege)
    {
        Datum = datum;
    }

    public string Ueberschrift
    {
        get
        {
            var heute = DateTime.Today;
            if (Datum == heute) return "Heute";
            if (Datum == heute.AddDays(-1)) return "Gestern";
            return Datum.ToString("dddd, d. MMMM yyyy", new CultureInfo("de-CH"));
        }
    }
}

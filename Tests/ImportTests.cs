using BlutdruckApp.Data;
using BlutdruckApp.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BlutdruckApp.Tests;

public class ImportTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public ImportTests() => Database.DbPath = Path.Combine(_dir, "test.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    static Eintrag Messung(int sys, int dia, string? notiz = null) => new()
    {
        Typ = EintragTyp.Messung,
        Zeitpunkt = new DateTime(2026, 1, 2, 8, 30, 0),
        Sys = sys,
        Dia = dia,
        Puls = 60,
        Notiz = notiz,
    };

    [Fact]
    public void CsvRoundTrip_KeepsSpecialCharactersAndFormulaNotes()
    {
        var eintraege = new[] { Messung(120, 80, "Kopf; \"wehe\"\nzweite Zeile"), Messung(130, 85, "=SUMME(A1)") };
        eintraege[1].Zeitpunkt = eintraege[1].Zeitpunkt.AddHours(1);

        var csv = Csv.Schreiben(eintraege);
        Assert.Contains("'=SUMME(A1)", csv); // für Excel entschärft

        var zurueck = Csv.Lesen(csv);
        Assert.Equal(2, zurueck.Count);
        Assert.Equal("Kopf; \"wehe\"\nzweite Zeile", zurueck[0].Notiz);
        Assert.Equal("=SUMME(A1)", zurueck[1].Notiz);
    }

    [Fact]
    public void CsvWithCommas_NoteWithSemicolonIsNotMisread()
    {
        var csv = "typ,datum,uhrzeit,sys,dia,puls,medikament,notiz\nmessung,2026-01-02,08:30,120,80,60,,\"a;b\"\n";
        var e = Assert.Single(Csv.Lesen(csv));
        Assert.Equal("a;b", e.Notiz);
    }

    [Fact]
    public void CsvOutOfRange_IsRejected()
    {
        var csv = "typ;datum;uhrzeit;sys;dia;puls;medikament;notiz\nmessung;2026-01-02;08:30;0;80;60;;\n";
        Assert.Throws<InvalidDataException>(() => Csv.Lesen(csv));
    }

    [Fact]
    public void ImportEintraege_SecondRunAddsNothing()
    {
        var csv = Csv.Schreiben([Messung(120, 80)]);
        Assert.Equal((1, 0), Database.ImportEintraege(Csv.Lesen(csv)));
        Assert.Equal((0, 1), Database.ImportEintraege(Csv.Lesen(csv)));
    }

    [Fact]
    public void ImportDb_SkipsInvalidRows_AppStillLoads()
    {
        var fremd = Path.Combine(_dir, "fremd.db");
        using (var con = new SqliteConnection($"Data Source={fremd}"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE messung (id INTEGER PRIMARY KEY, zeitpunkt TEXT, sys, dia, puls, notiz);
                INSERT INTO messung (zeitpunkt, sys, dia, puls) VALUES
                    ('2026-01-02T08:30:00', 120, 80, 60),
                    ('2026-01-02 09:00',    120, 80, 60),
                    ('2026-01-02T10:00:00', 'viel', 80, 60),
                    ('2026-01-02T11:00:00', 0, 80, 60);
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        Assert.Equal((1, 3), Database.ImportDb(fremd));
        Assert.Single(Database.AlleEintraege()); // wirft nicht
    }
}

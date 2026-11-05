using System.Globalization;
using BlutdruckApp.Models;
using Microsoft.Data.Sqlite;

namespace BlutdruckApp.Data;

/// <summary>
/// Zugriff auf die lokale SQLite-Datenbank (blutdruck.db im App-Datenordner).
/// Zeitpunkte werden als lokale Zeit im Format yyyy-MM-ddTHH:mm:ss gespeichert.
/// </summary>
public static class Database
{
    public const string ZeitFormat = "yyyy-MM-dd'T'HH:mm:ss";

    public static string DbPath => Path.Combine(FileSystem.AppDataDirectory, "blutdruck.db");

    static bool _schemaOk;

    static SqliteConnection Open()
    {
#if WINDOWS
        if (!_schemaOk) AlteDbUebernehmen();
#endif
        var con = new SqliteConnection($"Data Source={DbPath}");
        con.Open();
        if (!_schemaOk)
        {
            Exec(con, """
                CREATE TABLE IF NOT EXISTS messung (
                    id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    zeitpunkt  TEXT    NOT NULL,
                    sys        INTEGER NOT NULL,
                    dia        INTEGER NOT NULL,
                    puls       INTEGER,
                    notiz      TEXT
                );
                CREATE TABLE IF NOT EXISTS einnahme (
                    id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    zeitpunkt  TEXT    NOT NULL,
                    medikament TEXT,
                    notiz      TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_messung_zeit  ON messung(zeitpunkt);
                CREATE INDEX IF NOT EXISTS ix_einnahme_zeit ON einnahme(zeitpunkt);
                """);
            _schemaOk = true;
        }
        return con;
    }

#if WINDOWS
    /// <summary>
    /// Der Windows-Datenordner hängt an der App-ID (%LOCALAPPDATA%\Blutdruck\&lt;app-id&gt;\Data).
    /// Nach einer Änderung der ID die Datenbank aus dem alten Ordner kopieren (das Original bleibt liegen).
    /// </summary>
    static void AlteDbUebernehmen()
    {
        if (File.Exists(DbPath)) return;
        var appOrdner = Directory.GetParent(FileSystem.AppDataDirectory);
        var alt = appOrdner?.Parent?.GetDirectories("*.blutdruck")
            .Where(d => d.FullName != appOrdner.FullName)
            .Select(d => Path.Combine(d.FullName, "Data", "blutdruck.db"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (alt is null) return;
        Directory.CreateDirectory(FileSystem.AppDataDirectory);
        File.Copy(alt, DbPath);
    }
#endif

    static void Exec(SqliteConnection con, string sql, params (string, object?)[] p)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in p)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static string ZeitText(DateTime t) => t.ToString(ZeitFormat, CultureInfo.InvariantCulture);

    public static DateTime ParseZeit(string s) =>
        DateTime.ParseExact(s, ZeitFormat, CultureInfo.InvariantCulture);

    // ---------- Lesen ----------

    public static List<Eintrag> AlleEintraege()
    {
        var liste = new List<Eintrag>();
        using var con = Open();

        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT id, zeitpunkt, sys, dia, puls, notiz FROM messung";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                liste.Add(new Eintrag
                {
                    Typ = EintragTyp.Messung,
                    Id = r.GetInt64(0),
                    Zeitpunkt = ParseZeit(r.GetString(1)),
                    Sys = r.GetInt32(2),
                    Dia = r.GetInt32(3),
                    Puls = r.IsDBNull(4) ? null : r.GetInt32(4),
                    Notiz = r.IsDBNull(5) ? null : r.GetString(5),
                });
        }

        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT id, zeitpunkt, medikament, notiz FROM einnahme";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                liste.Add(new Eintrag
                {
                    Typ = EintragTyp.Einnahme,
                    Id = r.GetInt64(0),
                    Zeitpunkt = ParseZeit(r.GetString(1)),
                    Medikament = r.IsDBNull(2) ? null : r.GetString(2),
                    Notiz = r.IsDBNull(3) ? null : r.GetString(3),
                });
        }

        return liste.OrderByDescending(e => e.Zeitpunkt).ToList();
    }

    public static (int Messungen, int Einnahmen) Anzahl()
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT (SELECT COUNT(*) FROM messung), (SELECT COUNT(*) FROM einnahme)";
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt32(0), r.GetInt32(1));
    }

    // ---------- Schreiben ----------

    public static void Speichern(Eintrag e)
    {
        using var con = Open();
        var zeit = ZeitText(e.Zeitpunkt);

        if (e.Typ == EintragTyp.Messung)
        {
            var p = new (string, object?)[]
            {
                ("$id", e.Id), ("$z", zeit), ("$s", e.Sys), ("$d", e.Dia), ("$p", e.Puls), ("$n", Leer(e.Notiz)),
            };
            if (e.Id == 0)
            {
                Exec(con, "INSERT INTO messung (zeitpunkt, sys, dia, puls, notiz) VALUES ($z, $s, $d, $p, $n)", p);
            }
            else
            {
                Exec(con, "UPDATE messung SET zeitpunkt=$z, sys=$s, dia=$d, puls=$p, notiz=$n WHERE id=$id", p);
            }
        }
        else
        {
            var p = new (string, object?)[]
            {
                ("$id", e.Id), ("$z", zeit), ("$m", Leer(e.Medikament)), ("$n", Leer(e.Notiz)),
            };
            if (e.Id == 0)
            {
                Exec(con, "INSERT INTO einnahme (zeitpunkt, medikament, notiz) VALUES ($z, $m, $n)", p);
            }
            else
            {
                Exec(con, "UPDATE einnahme SET zeitpunkt=$z, medikament=$m, notiz=$n WHERE id=$id", p);
            }
        }
    }

    public static void Loeschen(Eintrag e)
    {
        using var con = Open();
        var tabelle = e.Typ == EintragTyp.Messung ? "messung" : "einnahme";
        Exec(con, $"DELETE FROM {tabelle} WHERE id=$id", ("$id", e.Id));
    }

    static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ---------- Export / Import ----------

    /// <summary>Schreibt eine konsistente Kopie der Datenbank in eine Datei (VACUUM INTO).</summary>
    public static string SnapshotErstellen()
    {
        var ziel = Path.Combine(FileSystem.CacheDirectory, $"export_{Guid.NewGuid():N}.db");
        using var con = Open();
        Exec(con, "VACUUM INTO $ziel", ("$ziel", ziel));
        return ziel;
    }

    /// <summary>
    /// Übernimmt alle Einträge aus einer anderen blutdruck.db.
    /// Einträge, die schon identisch vorhanden sind, werden übersprungen.
    /// </summary>
    public static (int Neu, int Doppelt) ImportDb(string pfad)
    {
        using var con = Open();
        Exec(con, "ATTACH DATABASE $pfad AS imp", ("$pfad", pfad));
        try
        {
            using (var check = con.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(*) FROM imp.sqlite_master WHERE type='table' AND name IN ('messung','einnahme')";
                if (Convert.ToInt32(check.ExecuteScalar()) == 0)
                    throw new InvalidDataException("Die Datei enthält keine Blutdruck-Daten.");
            }

            int vorher = Gesamt(con, "main");
            int importiert = Gesamt(con, "imp");

            using var tx = con.BeginTransaction();
            if (TabelleExistiert(con, "imp", "messung"))
                Exec(con, """
                    INSERT INTO main.messung (zeitpunkt, sys, dia, puls, notiz)
                    SELECT i.zeitpunkt, i.sys, i.dia, i.puls, i.notiz FROM imp.messung i
                    WHERE NOT EXISTS (SELECT 1 FROM main.messung m
                        WHERE m.zeitpunkt = i.zeitpunkt AND m.sys = i.sys AND m.dia = i.dia
                          AND m.puls IS i.puls)
                    """);
            if (TabelleExistiert(con, "imp", "einnahme"))
                Exec(con, """
                    INSERT INTO main.einnahme (zeitpunkt, medikament, notiz)
                    SELECT i.zeitpunkt, i.medikament, i.notiz FROM imp.einnahme i
                    WHERE NOT EXISTS (SELECT 1 FROM main.einnahme m
                        WHERE m.zeitpunkt = i.zeitpunkt AND m.medikament IS i.medikament)
                    """);
            tx.Commit();

            int neu = Gesamt(con, "main") - vorher;
            return (neu, importiert - neu);
        }
        finally
        {
            Exec(con, "DETACH DATABASE imp");
        }
    }

    /// <summary>Fügt Einträge ein, die noch nicht identisch vorhanden sind.</summary>
    public static (int Neu, int Doppelt) ImportEintraege(IEnumerable<Eintrag> eintraege)
    {
        var vorhanden = AlleEintraege().Select(Schluessel).ToHashSet();
        int neu = 0, doppelt = 0;
        foreach (var e in eintraege)
        {
            if (!vorhanden.Add(Schluessel(e)))
            {
                doppelt++;
                continue;
            }
            e.Id = 0;
            Speichern(e);
            neu++;
        }
        return (neu, doppelt);
    }

    static string Schluessel(Eintrag e) => e.Typ == EintragTyp.Messung
        ? $"M|{ZeitText(e.Zeitpunkt)}|{e.Sys}|{e.Dia}|{e.Puls}"
        : $"E|{ZeitText(e.Zeitpunkt)}|{e.Medikament}";

    static bool TabelleExistiert(SqliteConnection con, string schema, string tabelle)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {schema}.sqlite_master WHERE type='table' AND name=$t";
        cmd.Parameters.AddWithValue("$t", tabelle);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    static int Gesamt(SqliteConnection con, string schema)
    {
        int summe = 0;
        foreach (var t in new[] { "messung", "einnahme" })
        {
            if (!TabelleExistiert(con, schema, t)) continue;
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {schema}.{t}";
            summe += Convert.ToInt32(cmd.ExecuteScalar());
        }
        return summe;
    }
}

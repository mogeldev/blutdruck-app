using System.Text;
using BlutdruckApp.Data;
using CommunityToolkit.Maui.Storage;

namespace BlutdruckApp.Pages;

/// <summary>Export und Import der Daten (SQLite-Datei oder CSV).</summary>
public partial class DatenPage : ContentPage
{
    public DatenPage()
    {
        InitializeComponent();
        LblPfad.Text = Database.DbPath;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var (m, e) = Database.Anzahl();
        LblAnzahl.Text = $"{m} Messungen, {e} Tabletteneinnahmen gespeichert.";
    }

    static string Dateiname(string endung) => $"blutdruck_{DateTime.Now:yyyy-MM-dd_HHmm}.{endung}";

    async void OnExportDb(object? sender, EventArgs e)
    {
        string? snapshot = null;
        try
        {
            snapshot = Database.SnapshotErstellen();
            await using var stream = File.OpenRead(snapshot);
            await Speichern(Dateiname("db"), stream);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Export fehlgeschlagen", ex.Message, "OK");
        }
        finally
        {
            if (snapshot is not null) TryDelete(snapshot);
        }
    }

    async void OnExportCsv(object? sender, EventArgs e)
    {
        try
        {
            var csv = Csv.Schreiben(Database.AlleEintraege());
            // UTF-8 mit BOM, damit Excel Umlaute korrekt anzeigt
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            using var stream = new MemoryStream(bytes);
            await Speichern(Dateiname("csv"), stream);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Export fehlgeschlagen", ex.Message, "OK");
        }
    }

    async void OnExportPdf(object? sender, EventArgs e)
    {
        const string t30 = "Letzte 30 Tage", t90 = "Letzte 90 Tage", t365 = "Letzte 12 Monate", alles = "Alles";
        var wahl = await DisplayActionSheetAsync("Zeitraum für den Bericht", "Abbrechen", null, t30, t90, t365, alles);
        if (wahl is null or "Abbrechen") return;

        try
        {
            var alle = Database.AlleEintraege();
            var bis = DateTime.Today;
            var von = wahl switch
            {
                t30 => bis.AddDays(-29),
                t90 => bis.AddDays(-89),
                t365 => bis.AddYears(-1).AddDays(1),
                _ => alle.Count > 0 ? alle.Min(x => x.Zeitpunkt).Date : bis,
            };
            var auswahl = alle.Where(x => x.Zeitpunkt.Date >= von && x.Zeitpunkt.Date <= bis).ToList();

            using var stream = new MemoryStream();
            await Task.Run(() => PdfBericht.Schreiben(stream, auswahl, von, bis));
            stream.Position = 0;
            await Speichern(Dateiname("pdf"), stream);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Export fehlgeschlagen", ex.Message, "OK");
        }
    }

    async Task Speichern(string dateiname, Stream stream)
    {
        var res = await FileSaver.Default.SaveAsync(dateiname, stream, CancellationToken.None);
        if (res.IsSuccessful)
            await DisplayAlertAsync("Exportiert", $"Gespeichert unter:\n{res.FilePath}", "OK");
        else if (res.Exception is not null && res.Exception is not OperationCanceledException
                 && !res.Exception.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            await DisplayAlertAsync("Export fehlgeschlagen", res.Exception.Message, "OK");
    }

    async void OnImport(object? sender, EventArgs e)
    {
        FileResult? datei;
        try
        {
            datei = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Datei für Import wählen" });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Import fehlgeschlagen", ex.Message, "OK");
            return;
        }
        if (datei is null) return;

        // In den Cache kopieren: auf Android liefert der Picker oft nur einen Content-Stream
        var temp = Path.Combine(FileSystem.CacheDirectory, $"import_{Guid.NewGuid():N}");
        try
        {
            await using (var quelle = await datei.OpenReadAsync())
            await using (var ziel = File.Create(temp))
                await quelle.CopyToAsync(ziel);

            (int Neu, int Doppelt) ergebnis;
            if (IstSqlite(temp))
            {
                ergebnis = await Task.Run(() => Database.ImportDb(temp));
            }
            else
            {
                var text = await File.ReadAllTextAsync(temp, Encoding.UTF8);
                var eintraege = Csv.Lesen(text);
                ergebnis = await Task.Run(() => Database.ImportEintraege(eintraege));
            }

            OnAppearing();
            await DisplayAlertAsync("Import abgeschlossen",
                $"{ergebnis.Neu} neue Einträge übernommen.\n{ergebnis.Doppelt} bereits vorhanden oder ungültig (übersprungen).", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Import fehlgeschlagen", ex.Message, "OK");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            TryDelete(temp);
        }
    }

    static bool IstSqlite(string pfad)
    {
        var kopf = new byte[16];
        using var f = File.OpenRead(pfad);
        return f.Read(kopf, 0, 16) == 16 && Encoding.ASCII.GetString(kopf, 0, 15) == "SQLite format 3";
    }

    static void TryDelete(string pfad)
    {
        try { File.Delete(pfad); } catch { /* egal */ }
    }
}

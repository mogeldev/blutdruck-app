using BlutdruckApp.Data;
using BlutdruckApp.Models;
using BlutdruckApp.Pages;

namespace BlutdruckApp;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Laden();
    }

    void Laden()
    {
        var alle = Database.AlleEintraege();

        Liste.ItemsSource = alle
            .GroupBy(e => e.Zeitpunkt.Date)
            .Select(g => new TagGruppe(g.Key, g))
            .ToList();

        var seit = DateTime.Now.AddDays(-7);
        var woche = alle.Where(e => e.IstMessung && e.Zeitpunkt >= seit).ToList();
        if (woche.Count > 0)
        {
            var puls = woche.Where(e => e.Puls.HasValue).Select(e => e.Puls!.Value).ToList();
            LblSchnittTitel.Text = $"Ø letzte 7 Tage ({woche.Count} {(woche.Count == 1 ? "Messung" : "Messungen")})";
            LblSchnitt.Text = $"{woche.Average(e => e.Sys):0} / {woche.Average(e => e.Dia):0}"
                + (puls.Count > 0 ? $"  ·  Puls {puls.Average():0}" : "");
        }
        else
        {
            LblSchnittTitel.Text = "Ø letzte 7 Tage";
            LblSchnitt.Text = "–";
        }

        var letzte = alle.FirstOrDefault(e => e.IstEinnahme);
        LblLetzteTablette.Text = letzte is null
            ? ""
            : $"Letzte Tablette\n{BeschreibeZeit(letzte.Zeitpunkt)}";
    }

    static string BeschreibeZeit(DateTime t)
    {
        var tag = t.Date == DateTime.Today ? "heute"
            : t.Date == DateTime.Today.AddDays(-1) ? "gestern"
            : t.ToString("dd.MM.");
        return $"{tag} {t:HH:mm}";
    }

    async void OnNeueMessung(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new EintragPage(new Eintrag { Typ = EintragTyp.Messung, Zeitpunkt = DateTime.Now }));

    async void OnNeueEinnahme(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new EintragPage(new Eintrag
        {
            Typ = EintragTyp.Einnahme,
            Zeitpunkt = DateTime.Now,
            Medikament = Preferences.Default.Get("letztesMedikament", (string?)null),
        }));

    async void OnAuswahl(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Eintrag eintrag) return;
        Liste.SelectedItem = null;
        await Navigation.PushAsync(new EintragPage(eintrag));
    }
}

using BlutdruckApp.Data;
using BlutdruckApp.Models;

namespace BlutdruckApp.Pages;

/// <summary>Erfassen und Bearbeiten einer Messung oder Tabletteneinnahme.</summary>
public partial class EintragPage : ContentPage
{
    readonly Eintrag _eintrag;

    public EintragPage(Eintrag eintrag)
    {
        InitializeComponent();
        _eintrag = eintrag;

        bool neu = eintrag.Id == 0;
        Title = (eintrag.IstMessung ? "Messung" : "Tablette") + (neu ? " erfassen" : " bearbeiten");
        BereichMessung.IsVisible = eintrag.IstMessung;
        BereichEinnahme.IsVisible = eintrag.IstEinnahme;
        BtnLoeschen.IsVisible = !neu;

        Datum.Date = eintrag.Zeitpunkt.Date;
        Zeit.Time = eintrag.Zeitpunkt.TimeOfDay;
        if (eintrag.IstMessung && !neu)
        {
            Sys.Text = eintrag.Sys.ToString();
            Dia.Text = eintrag.Dia.ToString();
            Puls.Text = eintrag.Puls?.ToString();
        }
        Medikament.Text = eintrag.Medikament;
        Notiz.Text = eintrag.Notiz;

        Sys.Completed += (_, _) => Dia.Focus();
        Dia.Completed += (_, _) => Puls.Focus();
        Puls.Completed += (_, _) => Notiz.Focus();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_eintrag.IstMessung && _eintrag.Id == 0)
        {
            // kurz warten, damit die Tastatur auf Android zuverlässig erscheint
            await Task.Delay(300);
            Sys.Focus();
        }
    }

    void OnJetzt(object? sender, EventArgs e)
    {
        var jetzt = DateTime.Now;
        Datum.Date = jetzt.Date;
        Zeit.Time = new TimeSpan(jetzt.Hour, jetzt.Minute, 0);
    }

    async void OnSpeichern(object? sender, EventArgs e)
    {
        var datum = Datum.Date ?? DateTime.Today;
        var zeit = Zeit.Time ?? TimeSpan.Zero;
        var zeitpunkt = datum.Date + new TimeSpan(zeit.Hours, zeit.Minutes, 0);

        if (_eintrag.IstMessung)
        {
            if (!int.TryParse(Sys.Text, out var sys) || sys < 50 || sys > 300)
            {
                await DisplayAlertAsync("Ungültiger Wert", "Bitte einen systolischen Wert (oben) zwischen 50 und 300 eingeben.", "OK");
                Sys.Focus();
                return;
            }
            if (!int.TryParse(Dia.Text, out var dia) || dia < 30 || dia > 200)
            {
                await DisplayAlertAsync("Ungültiger Wert", "Bitte einen diastolischen Wert (unten) zwischen 30 und 200 eingeben.", "OK");
                Dia.Focus();
                return;
            }
            int? puls = null;
            if (!string.IsNullOrWhiteSpace(Puls.Text))
            {
                if (!int.TryParse(Puls.Text, out var p) || p < 20 || p > 250)
                {
                    await DisplayAlertAsync("Ungültiger Wert", "Der Puls muss zwischen 20 und 250 liegen (oder leer bleiben).", "OK");
                    Puls.Focus();
                    return;
                }
                puls = p;
            }
            _eintrag.Sys = sys;
            _eintrag.Dia = dia;
            _eintrag.Puls = puls;
        }
        else
        {
            _eintrag.Medikament = Medikament.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(_eintrag.Medikament))
                Preferences.Default.Set("letztesMedikament", _eintrag.Medikament);
        }

        _eintrag.Zeitpunkt = zeitpunkt;
        _eintrag.Notiz = Notiz.Text;
        Database.Speichern(_eintrag);
        await Navigation.PopAsync();
    }

    async void OnLoeschen(object? sender, EventArgs e)
    {
        var was = _eintrag.IstMessung ? $"die Messung {_eintrag.Sys}/{_eintrag.Dia}" : "die Tabletteneinnahme";
        if (!await DisplayAlertAsync("Löschen?", $"Soll {was} vom {_eintrag.Zeitpunkt:dd.MM.yyyy HH:mm} gelöscht werden?", "Löschen", "Abbrechen"))
            return;
        Database.Loeschen(_eintrag);
        await Navigation.PopAsync();
    }
}

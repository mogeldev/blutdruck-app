# Blutdruck-App (Android + Windows)

.NET MAUI-App zum Erfassen von Blutdruckwerten (SYS, DIA, Puls mit Zeitpunkt) und der Einnahme der Blutdrucktablette.
Die Daten liegen in einer lokalen SQLite-Datenbank (`blutdruck.db`).

## Installation

- **Android:** `Blutdruck.apk` aufs Handy kopieren und öffnen. Einmalig «Installation aus unbekannten Quellen» erlauben.
- **Windows:** `Blutdruck-Windows.zip` entpacken und `BlutdruckApp.exe` starten. Es muss nichts installiert werden.

## Funktionen

- **Verlauf:** alle Einträge nach Tag gruppiert, farbige Markierung (grün = normal, gelb = hoch-normal, orange = erhöht, rot = deutlich erhöht), dazu der Durchschnitt der letzten 7 Tage und wann die letzte Tablette genommen wurde.
- **Messung / Tablette:** Datum und Uhrzeit sind auf «jetzt» vorausgefüllt. Das zuletzt verwendete Medikament wird vorgeschlagen.
- Ein Eintrag lässt sich antippen, um ihn zu bearbeiten oder zu löschen.
- **Daten:**
  - Export als `.db` (komplette SQLite-Datenbank, z. B. zum Übertragen Handy ⇄ PC)
  - Export als `.csv` (Semikolon, UTF-8, öffnet direkt in Excel)
  - Export als `.pdf`: Protokoll für den Arzt mit Durchschnittswerten (gesamt, morgens, abends), Verlaufsdiagramm und allen Einträgen. Zeitraum wählbar: 30 Tage, 90 Tage, 12 Monate oder alles
  - Import von `.db` oder `.csv`: Die Einträge werden **zusammengeführt**. Doppelte Einträge (gleicher Zeitpunkt und gleiche Werte) werden übersprungen.

CSV-Spalten: `typ;datum;uhrzeit;sys;dia;puls;medikament;notiz` (`typ` ist `messung` oder `tablette`, das Datum `yyyy-MM-dd` oder `dd.MM.yyyy`).

## Speicherort der Datenbank

- Android: im privaten App-Ordner (`/data/user/0/ch.mogeldev.blutdruck/files/blutdruck.db`)
- Windows: `%LOCALAPPDATA%\Blutdruck\ch.mogeldev.blutdruck\Data\blutdruck.db`

## Selbst bauen

Benötigt: .NET 10 SDK mit den Workloads `maui-android` und `maui-windows`, Android SDK, JDK 17+.

```bash
dotnet publish -f net10.0-android -c Release
```

```bash
dotnet publish -f net10.0-windows10.0.19041.0 -c Release -o publish/windows
```

Liegt der Projektordner sehr tief (Pfad > ~100 Zeichen), bricht der Windows-Build am 260-Zeichen-Limit ab. Dann den Ordner mit `subst X: <Pfad>` auf ein Laufwerk abbilden und von dort bauen.

**Wichtig:** Das APK wird mit dem Standard-Debug-Schlüssel des Android SDK signiert (`~/.android/debug.keystore`), es gibt keinen eigenen Release-Schlüssel. Updates lassen sich nur über die alte Version installieren, wenn sie mit demselben Debug-Schlüssel gebaut wurden, sonst muss zuerst deinstalliert werden (Daten gehen dabei verloren). Vor einem Update die `ApplicationVersion` in `BlutdruckApp.csproj` erhöhen.

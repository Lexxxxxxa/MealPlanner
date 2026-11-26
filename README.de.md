# MealPlanner

[English](README.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

Ein Telegram-Bot für Paare (oder Familien): gemeinsame Gerichteliste mit Berechnung von Kalorien und Makronährstoffen (Eiweiß, Fett, Kohlenhydrate), Vorratserfassung,
Vorschläge „Was kann ich aus den vorhandenen Zutaten kochen?“ und ein Tagesmenü. Für eine Kursarbeit enthält das Projekt außerdem einen experimentellen Teil:
Vergleich von Empfehlungsstrategien, Ersatz von Zutaten und Menüplanung bei begrenzten Vorräten.

> Die Bot-Oberfläche ist auf Ukrainisch. Die Schaltflächen werden unten so zitiert, wie sie im Bot erscheinen, mit Übersetzung.

## Struktur

| Projekt | Zweck |
|---|---|
| `src/MealPlanner.Core` | Modelle, Parsen von Gerichtstexten, Menüalgorithmen (Greedy, Simplex), Empfehlungen, Zutatenersatz, Produktkatalog `Data/foods.csv` |
| `src/MealPlanner.App` | EF Core + SQLite (Haushalte, Gerichte), `BotUi` (Oberfläche mit Schaltflächen) und `CommandHandler` (Textbefehle), unabhängig von Telegram |
| `src/MealPlanner.BotHost` | Konsolen-Host für den Telegram-Bot (Long Polling) |
| `src/MealPlanner.Benchmark` | Experimente 0–3, Ergebnisse in `results/*.csv` |
| `tests/MealPlanner.Tests` | xUnit-Tests (Parser, Algorithmen, Bot-Befehle mit SQLite In-Memory) |

## Start

Voraussetzung: .NET 8 SDK.

```bash
dotnet build
dotnet test
```

### Bot

1. Erstelle in Telegram über `@BotFather` einen Bot und kopiere das Token.
2. Ermittle deine Telegram-ID und die deines Partners (z. B. über `@userinfobot`).
3. Starte den Bot (Windows PowerShell):

```powershell
$env:TELEGRAM_BOT_TOKEN = "123456:ABC..."
$env:ALLOWED_USER_IDS = "11111111,22222222"
dotnet run --project src/MealPlanner.BotHost
```

Das Token nicht im Repository speichern. `ALLOWED_USER_IDS` beschränkt den Zugriff auf euch beide.
Im Chat: `/start`, danach sendet der Partner `/join CODE`.

Die gesamte Bedienung erfolgt über Schaltflächen (Menü unten im Bildschirm):

- **🍳 Що приготувати** (Was kochen): vorhandene Produkte markieren (Kategorien → Produkte) oder als Liste eingeben; der Bot wählt Gerichte aus deiner Datenbank und Ideen aus den integrierten Gerichten.
- **📖 Мої блюда** (Meine Gerichte): Liste mit Kalorien, Zutaten, Löschen. In der Detailansicht:
  - **✏️ Змінити** (Bearbeiten): Produkte ändern, hinzufügen oder entfernen, Gramm (Schaltflächen −50/−10/+10/+50 oder Zahl), Anzahl der Portionen; „Зберегти“ (Speichern) aktualisiert das Rezept.
  - **👩‍🍳 Готую** (Ich koche): wie oben, aber mit den tatsächlichen Grammangaben: Der Bot vergleicht die tatsächlichen Nährwerte mit dem Rezept (Differenz pro Portion); eine Portion kann ins Tagebuch eingetragen oder das Rezept aktualisiert werden.
  - **🍽 З'їв(ла)** (Gegessen): ½, 1, 1½, 2 oder 3 Portionen ins Tagebuch eintragen.
- **📝 Рецепт** (Rezept): Jedes Gericht hat eine Zubereitungszeit und Schritte („Як готувати“, Zubereitung). Mit „📝 Рецепт“ und „🗑 Прибрати рецепт“ (Rezept entfernen) lassen sich die Schritte hinzufügen oder ändern; im Kochmodus erscheinen sie neben den Grammangaben. Die Schritte der integrierten Gerichte werden aus Vorlagen je Gerichtstyp erzeugt. Es sind Entwürfe, die geprüft und korrigiert werden sollten.
- **⏱ Zeit und Schwierigkeit:** Beim Anlegen eines Gerichts wählt man nach den Portionen die Zubereitungszeit (10–90 Min.) und die Schwierigkeit (leicht / mittel / schwer); beides ist unter „Змінити“ änderbar. In „Що приготувати“ grenzen die Filter „⚡ До 20 мин“ (bis 20 Min.) und „🔧 Складність“ (Schwierigkeit: beliebig → nicht schwer → nur leicht) die Auswahl ein.
- **💡 Що з'їсти до норми** (Was essen bis zum Tagesziel, in der Tagesansicht): Der Bot nimmt die heute noch offenen Kalorien und Makronährstoffe und wählt Portionen (½, 1, 1½, 2) aus eigenen Gerichten und integrierten Ideen, die den Rest am besten füllen. Bewertung = |Δkcal| / offene kcal + Σ über Eiweiß, Fett, Kohlenhydrate (0,5 · Überschreitung + 0,25 · Fehlmenge) / max(Rest, 10); Varianten über 110 % der offenen Kalorien werden verworfen. Eine Portion kann direkt eingetragen werden.
- **📅 Щоденник** (Tagebuch): Tagessumme der Kalorien und Makronährstoffe im Vergleich zum persönlichen Ziel (Standard: 2000 kcal), Fortschrittsbalken, Produkte in Gramm erfassen, Einträge löschen, frühere Tage ansehen, Schaltfläche „🎯 Моя ціль“ (Mein Ziel). Jedes Mitglied hat ein eigenes Tagebuch; Nährwerte werden als Momentaufnahme gespeichert, sodass eine Rezeptänderung die Historie nicht überschreibt.
- **🛒 Einkaufsmodus** in „Що приготувати“: Lässt sich aus den gewählten Produkten kein Gericht zubereiten, zeigt der Bot Gerichte, die sie verwenden, und was noch gekauft werden muss; der Schalter „Лише з моїх продуктів“ (Nur meine Produkte) stellt die strenge Auswahl wieder her.
- **📚 База блюд** (Gerichtedatenbank): rund 390 integrierte Gerichte in 15 Kategorien (Frühstück, Suppen, Salate, Fleisch, Geflügel, Fisch, Gemüse, Brei, Pasta, Hülsenfrüchte, Gebäck, Desserts, Snacks, Getränke, Schnelles). Seitenweises Blättern nach Kategorien, Suche nach Name oder Produkt („форель“, „творог яйца“), Filter „bis 20 Min.“ und nach Schwierigkeit, Schaltfläche „🎲 Випадкове“ (Zufall), Speichern in „Мої блюда“ mit einem Klick und Eintragen einer Portion.
- **🔎 Produktsuche** gibt es überall, wo Produkte gewählt werden (Ideen, Gericht anlegen oder bearbeiten, Tagebuch); Produktlisten zeigen 12 Einträge pro Seite. Der Katalog enthält 392 Produkte in 25 Kategorien (59 ursprüngliche und eine Erweiterung).
- **➕ Додати блюдо** (Gericht hinzufügen): Name → Produkte per Schaltfläche → Gramm per Schaltfläche (oder Zahl / „3 шт“ für Stück) → Portionen → Zeit → Schwierigkeit → Zubereitung (Text, ein Schritt pro Zeile, oder „Пропустити“ zum Überspringen).
- **👫 Пара** (Paar): Haushaltscode für eine gemeinsame Gerichtedatenbank.

**🎯 Моя ціль** (Mein Ziel) ist ein Assistent in 6 Schritten (Geschlecht → Alter → Größe → Gewicht → Aktivität → Ziel). Jeder Schritt zeigt einen Richtwert für den Tagesbedarf, der mit den Eingaben genauer wird; die Schaltflächen für Aktivität und Ziel zeigen den jeweiligen Wert. Berechnung: BMI (mit WHO-Kategorie), Grundumsatz nach Mifflin-St. Jeor, Aktivitätsfaktor (1,2–1,725) und Korrektur nach Ziel: abnehmen −15 % (nicht unter 1200/1500 kcal), halten 0 %, zunehmen +10 %. Die Makroanteile hängen vom Ziel ab (Eiweiß/Fett/Kohlenhydrate: abnehmen 25/30/45, halten 20/30/50, zunehmen 20/25/55). Der Wert kann auch manuell gesetzt werden. Die Ergebnisse sind Richtwerte und ersetzen keine ärztliche Beratung.

Eine vorhandene Datenbank wird beim Start automatisch um die Tagebuchtabellen (`DiaryEntries`, `MemberTargets`) ergänzt.

Vorräte werden nicht gespeichert: Produkte werden nur bei der Anfrage nach einer Idee angegeben.
Textbefehle (`/dish`, `/have`, `/cook`, `/menu` …) bleiben als versteckter Ersatzmodus erhalten.

### Architektur

- `Core`: Fachlogik ohne Abhängigkeit von Telegram und Datenbank (Modelle, Empfehlungsalgorithmen, `PortionFit`, `EnergyCalculator`, `InstructionText`); Strategien `IDishRecommender`, `IMenuPlanner`.
- `App/Services`: Services und Repositories hinter Interfaces (`IHouseholdService`, `IFoodRepository`, `IDishRepository`, `IPantryService`, `IDiaryService`), zusammengesetzt durch `AppServices.Create`. Datenbankinitialisierung: `DatabaseInitializer`.
- `App/Bot/Features`: eine Klasse pro Bereich der Oberfläche (`IBotFeature`: Callback-Präfixe und Sitzungsschritte); `BotUi` leitet nur weiter, neue Funktionen kommen ohne Änderung bestehender hinzu. Gemeinsame Hilfsfunktionen: `UiKit`. Textbefehle: Registry in `CommandHandler`.

### Experimente ausführen

```bash
dotnet run -c Release --project src/MealPlanner.Benchmark -- all        # oder menu | recommend | substitute | pantry
```

## Experimente

0. **Menü aus dem gesamten Katalog:** Greedy-Algorithmus gegen LP (Simplex) mit 59–1000 Produkten.
1. **Empfehlungen:** Random, BinaryCoverage, WeightedCoverage und Smart (IDF-Gewichtung, Abzug für Zukäufe, Kalorienabgleich) mit synthetischen Haushalten; Precision@5, Hit@1, Kalorienabweichung.
2. **Zutatenersatz:** Random, Macro, CategoryMacro, Oracle; Fehler bei Kalorien und Makronährstoffen des Gerichts.
3. **Menü aus Vorräten:** Anteil zulässiger Menüs und Kosten in Abhängigkeit von der Anzahl vorrätiger Produkte.

## Mathematisches Modell des Menüs („Diätproblem“)

```
min   Σ pᵢ·xᵢ                              (Kosten)
NB    minₖ ≤ Σ aᵢₖ·xᵢ ≤ maxₖ,  k = kcal, Eiweiß, Fett, Kohlenhydrate
      0 ≤ xᵢ ≤ min(Mᵢ, Vorratᵢ)/100
```

## Katalogdaten

`foods.csv` ist der Basissatz mit 59 Produkten (Grundlage der Experimente); `foods_extra.csv` enthält 333 weitere; `dishes.txt` enthält rund 390 Gerichte im Format `Kategorie|Name|Minuten|Schwierigkeit|Portionen|Produkt:Gramm;…`. Nährwerte je 100 g und Preise sind Näherungswerte aus gängigen Nährwerttabellen; vor wissenschaftlicher Nutzung sollten sie mit offiziellen Quellen abgeglichen werden (USDA FoodData Central, Skurichin-Tabellen). Beim Start ergänzt der Bot automatisch fehlende Produkte, daher muss die Datenbank bei Updates nicht neu erstellt werden. Der Test `DishCatalog_ResolvesEveryProduct` stellt sicher, dass jede Zutat jedes Gerichts im Katalog existiert.

## Hinweise zu den Daten

Nährwerte und Preise in `foods.csv` sind Näherungswerte zu Lernzwecken, keine amtlichen Daten.
Die Experimente 0–3 laufen mit synthetischen Daten (Gerichte und „Kühlschränke“ werden deterministisch erzeugt),
die Schlussfolgerungen gelten daher für das Modell, nicht für reales Nutzerverhalten.

using System;

namespace TaskOTime.DataLayer
{
    internal sealed class DemoTextCatalog
    {
        private static readonly string[] GermanTenants =
        {
            "München Produktteam",
            "Rhein-Ruhr Servicekreis",
            "Hamburg Planungsrunde",
            "Berlin MSBench Labor"
        };

        private static readonly string[] DutchTenants =
        {
            "Utrecht Ontwikkelteam",
            "Randstad Serviceteam",
            "Eindhoven Plangroep",
            "Amsterdam MSBench Lab"
        };

        private static readonly string[] GermanProjects =
        {
            "Kundenportal Ausbau",
            "Abrechnung Rhein-Ruhr",
            "MSBench Berichtslauf",
            "Support-Triage DACH",
            "Teamkalender Synchronisierung",
            "Zeiterfassung Pilot"
        };

        private static readonly string[] DutchProjects =
        {
            "Klantportaal Verbetering",
            "Facturatie Randstad",
            "MSBench Rapportage",
            "Supporttriage Benelux",
            "Teamagenda Synchronisatie",
            "Tijdregistratie Pilot"
        };

        private static readonly string[] GermanTasks =
        {
            "Kundenfeedback sichten",
            "Sprintplanung mit dem Team abstimmen",
            "MSBench Messlauf auswerten",
            "Buchungsmaske lokalisieren",
            "Rückfragen aus dem Stand-up klären",
            "Release-Notizen gegenlesen",
            "Projektkennzahlen für Review vorbereiten",
            "Datenimport mit Beispieldatei prüfen"
        };

        private static readonly string[] DutchTasks =
        {
            "Klantfeedback verwerken",
            "Sprintplanning met het team afstemmen",
            "MSBench meting beoordelen",
            "Boekingsscherm lokaliseren",
            "Vragen uit de stand-up opvolgen",
            "Release-notities nalezen",
            "Projectcijfers voor review voorbereiden",
            "Data-import met voorbeeldbestand testen"
        };

        private static readonly string[] GermanNotes =
        {
            "Stand-up: Lisa übernimmt die Rückfragen, Mehmet prüft die Zahlen.",
            "Bitte die Demo mit realistischen Pausen und Übergaben zeigen.",
            "MSBench-Lauf ist stabil; Ausreißer werden im Review erklärt.",
            "Kundenwunsch aus Köln: kurze Zusammenfassung für die Teamrunde.",
            "Nächster Schritt: niederländische Texte mit dem Benelux-Team validieren."
        };

        private static readonly string[] DutchNotes =
        {
            "Stand-up: Sanne pakt de vragen op, Daan controleert de cijfers.",
            "Toon de demo met realistische pauzes en overdrachten.",
            "MSBench-run is stabiel; uitschieters lichten we toe in de review.",
            "Klantwens uit Utrecht: korte samenvatting voor het teamoverleg.",
            "Volgende stap: Duitse teksten valideren met het DACH-team."
        };

        private static readonly string[] GermanTags =
        {
            "planung",
            "review",
            "kundenfrage",
            "msbench",
            "zeitbuchung",
            "team-sync"
        };

        private static readonly string[] DutchTags =
        {
            "planning",
            "review",
            "klantvraag",
            "msbench",
            "tijdboeking",
            "team-sync"
        };

        private readonly DemoTextOptions options;

        public DemoTextCatalog(DemoTextOptions options)
        {
            this.options = options ?? new DemoTextOptions();
        }

        public string TenantName(int index)
        {
            return Pick(index, GermanTenants, DutchTenants) + " " + (index + 1);
        }

        public string TenantDescription(int index)
        {
            return UsesGerman(index)
                ? "Demo-Mandant für ein deutschsprachiges Produkt- und Serviceteam."
                : "Demo-tenant voor een Nederlandstalig product- en serviceteam.";
        }

        public string ProjectName(int index)
        {
            return Pick(index, GermanProjects, DutchProjects) + " " + (index + 1);
        }

        public string ProjectDescription(int index)
        {
            return UsesGerman(index)
                ? "Teamprojekt mit realistischen Aufgaben, Notizen und MSBench-Bezug."
                : "Teamproject met realistische taken, notities en MSBench-context.";
        }

        public string TaskListName(int index)
        {
            if (UsesGerman(index))
            {
                return new[] { "Backlog", "Heute", "In Abstimmung", "Review" }[index % 4];
            }

            return new[] { "Backlog", "Vandaag", "In afstemming", "Review" }[index % 4];
        }

        public string TagName(int index)
        {
            return Pick(index, GermanTags, DutchTags) + "-" + (index + 1);
        }

        public string TagDescription(int index)
        {
            return UsesGerman(index) ? "Demo-Schlagwort für Teamarbeit." : "Demo-label voor teamwork.";
        }

        public string TaskName(int index, bool isCompleted)
        {
            var prefix = isCompleted
                ? (UsesGerman(index) ? "Erledigt" : "Afgerond")
                : (UsesGerman(index) ? "Offen" : "Open");
            return prefix + ": " + Pick(index, GermanTasks, DutchTasks);
        }

        public string TaskDescription(int index)
        {
            return UsesGerman(index)
                ? "Realistische Demo-Aufgabe aus einem mehrsprachigen Teamalltag."
                : "Realistische demotaak uit een meertalige teamdag.";
        }

        public string TaskQuickInfo(int index)
        {
            return UsesGerman(index) ? "Teamabgleich " + (index + 1) : "Teamafstemming " + (index + 1);
        }

        public string TimeShortTitle(int index)
        {
            return UsesGerman(index) ? "Fokuszeit " + (index + 1) : "Focustijd " + (index + 1);
        }

        public string TimeDescription(int index)
        {
            return UsesGerman(index) ? "Gebuchte Arbeit am Demo-Teamprojekt." : "Geboekt werk aan het demo-teamproject.";
        }

        public string EventInfo(int index)
        {
            return UsesGerman(index) ? "Demo-Buchung" : "Demo-boeking";
        }

        public string NoteText(int index)
        {
            return Pick(index, GermanNotes, DutchNotes);
        }

        public string WebLinkDescription(int index)
        {
            return UsesGerman(index) ? "Demo-Link für Projektunterlagen." : "Demo-link voor projectdocumenten.";
        }

        public string TenantLeadName(int index)
        {
            return UsesGerman(index) ? "Interessent Team Nord " + (index + 1) : "Prospect Team West " + (index + 1);
        }

        public string TenantLeadFirstName(int index)
        {
            return UsesGerman(index) ? new[] { "Hannah", "Lukas", "Mira", "Jonas" }[index % 4] : new[] { "Sanne", "Daan", "Femke", "Bram" }[index % 4];
        }

        public string TenantLeadLastName(int index)
        {
            return UsesGerman(index) ? new[] { "Weber", "Klein", "Schmitz", "Hoffmann" }[index % 4] : new[] { "Jansen", "De Vries", "Bakker", "Smit" }[index % 4];
        }

        public string TenantLeadNotes(int index)
        {
            return UsesGerman(index) ? "Möchte MSBench-Demo im nächsten Teamtermin sehen." : "Wil MSBench-demo in het volgende teamoverleg zien.";
        }

        public string LogMessage(int index)
        {
            return UsesGerman(index) ? "Demo-Aktivität im Teamverlauf " + (index + 1) : "Demo-activiteit in teamlog " + (index + 1);
        }

        private string Pick(int index, string[] germanValues, string[] dutchValues)
        {
            var values = UsesGerman(index) ? germanValues : dutchValues;
            return values[Positive(options.Seed + index) % values.Length];
        }

        private bool UsesGerman(int index)
        {
            if (options.IsGerman)
            {
                return true;
            }

            if (options.IsDutch)
            {
                return false;
            }

            var totalWeight = options.GermanWeight + options.DutchWeight;
            return Positive(options.Seed + index * 17) % totalWeight < options.GermanWeight;
        }

        private static int Positive(int value)
        {
            return value == int.MinValue ? 0 : Math.Abs(value);
        }
    }
}

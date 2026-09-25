using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace TaskOTime.Theme.CaptureHost
{
    public sealed class CaptureProjectData
    {
        public CaptureProjectData(string culture)
        {
            var german = culture == "de-DE";
            Projects = new ObservableCollection<CaptureProject>
            {
                new CaptureProject { ProjectName = german ? "Beispielprojekt" : "Sample project" },
                new CaptureProject { ProjectName = german ? "Kundenportal" : "Customer portal" },
                new CaptureProject { ProjectName = german ? "Dokumentation" : "Documentation" }
            };
            SelectedProject = Projects[0];
            ProjectName = SelectedProject.ProjectName;
            Identifier = "QC-2026";
            Description = german
                ? "Synthetische Beispieldaten zur visuellen Pruefung. Keine Produktionsdaten."
                : "Synthetic sample data for visual inspection. No production data.";
            IsActive = true;
            AssignmentText = german ? "2 Beispielaufgaben" : "2 sample tasks";
            OperationStatusText = german ? "Nur visuelle Vorschau" : "Visual preview only";
        }

        public ObservableCollection<CaptureProject> Projects { get; }
        public CaptureProject SelectedProject { get; set; }
        public string ProjectName { get; set; }
        public string Identifier { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public string AssignmentText { get; }
        public string OperationStatusText { get; }
        public ICommand NewCommand { get; } = new PreviewCommand();
        public ICommand SaveCommand { get; } = new PreviewCommand();
        public ICommand ArchiveCommand { get; } = new PreviewCommand();

        private sealed class PreviewCommand : ICommand
        {
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter) => throw new InvalidOperationException("Capture must not execute application commands.");
            public event EventHandler CanExecuteChanged { add { } remove { } }
        }
    }

    public sealed class CaptureProject
    {
        public string ProjectName { get; set; }
    }
}

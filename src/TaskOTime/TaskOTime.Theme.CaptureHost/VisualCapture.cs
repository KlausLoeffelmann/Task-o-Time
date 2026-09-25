using System;
using System.Collections.Generic;
using System.ComponentModel;
using CultureInfo = System.Globalization.CultureInfo;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;

namespace TaskOTime.Theme.CaptureHost
{
    internal static class VisualCapture
    {
        private static readonly DateTime SelectedDay = new DateTime(2026, 6, 15);
        private static readonly string[] Palettes = { "Light", "Dark", "HighContrast" };
        private static readonly string[] Cultures = { "en-US", "de-DE" };

        public static void Run(CaptureOptions options)
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            using var target = new CaptureTarget(options.ApplicationRoot);
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(LoadDictionary("ClassicDark.xaml"));
            var palette = LoadDictionary("Light.xaml");
            application.Resources.MergedDictionaries.Add(palette);

            // Directory.CreateDirectory silently reuses existing directories; capture must claim a new one.
            if (!CreateDirectory(options.OutputDirectory, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot exclusively create the requested capture directory.");

            var images = new List<CapturedImage>();
            foreach (var paletteName in Palettes)
            {
                SetContrastResources(application.Resources, paletteName == "HighContrast");
                var replacement = LoadDictionary(paletteName + ".xaml");
                application.Resources.MergedDictionaries[application.Resources.MergedDictionaries.IndexOf(palette)] = replacement;
                palette = replacement;
                foreach (var cultureName in Cultures)
                {
                    var culture = CultureInfo.GetCultureInfo(cultureName);
                    target.SetCulture(cultureName);
                    foreach (var state in new[] { "selected", "pending-range", "disabled" })
                    {
                        var calendar = new Calendar
                        {
                            DisplayDate = SelectedDay,
                            Language = XmlLanguage.GetLanguage(cultureName),
                            FirstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek,
                            SelectionMode = CalendarSelectionMode.SingleRange,
                            IsTodayHighlighted = false,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Top
                        };
                        calendar.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCalendarStyle");
                        calendar.BlackoutDates.Add(new CalendarDateRange(SelectedDay.AddDays(-5)));
                        if (state != "pending-range") calendar.SelectedDate = SelectedDay;
                        calendar.IsEnabled = state != "disabled";
                        images.Add(Render(options.OutputDirectory, paletteName, cultureName, "calendar", state,
                            calendar, 380, 330, state == "pending-range" ? () => SetPendingRange(calendar) : (Action)null));
                    }
                    foreach (var state in new[] { "normal", "disabled" })
                    {
                        var project = target.CreateProjectView();
                        project.DataContext = new CaptureProjectData(cultureName);
                        project.IsEnabled = state != "disabled";
                        images.Add(Render(options.OutputDirectory, paletteName, cultureName, "project", state,
                            project, 1060, 660, null));
                    }
                }
            }
            WriteManifest(options, target, images);
        }

        private static ResourceDictionary LoadDictionary(string name) => new ResourceDictionary
        {
            Source = new Uri("/TaskOTime.App;component/Themes/" + name, UriKind.Relative)
        };

        private static void SetContrastResources(ResourceDictionary resources, bool enabled)
        {
            var colors = new Dictionary<ResourceKey, Color>
            {
                { SystemColors.WindowColorKey, Colors.Black },
                { SystemColors.WindowTextColorKey, Colors.White },
                { SystemColors.HighlightColorKey, Colors.Yellow },
                { SystemColors.HighlightTextColorKey, Colors.Black },
                { SystemColors.GrayTextColorKey, Colors.Silver }
            };
            foreach (var pair in colors)
            {
                if (enabled) resources[pair.Key] = pair.Value;
                else resources.Remove(pair.Key);
            }
        }

        private static void SetPendingRange(Calendar calendar)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var start = typeof(Calendar).GetProperty("HoverStart", flags)
                ?? throw new MissingMemberException("Calendar.HoverStart");
            var end = typeof(Calendar).GetProperty("HoverEnd", flags)
                ?? throw new MissingMemberException("Calendar.HoverEnd");
            var refresh = typeof(Calendar).GetMethod("UpdateCellItems", flags)
                ?? throw new MissingMethodException("Calendar.UpdateCellItems");
            start.SetValue(calendar, SelectedDay);
            end.SetValue(calendar, SelectedDay.AddDays(3));
            refresh.Invoke(calendar, null);
        }

        private static CapturedImage Render(string output, string palette, string culture, string surface,
            string state, FrameworkElement content, int width, int height, Action beforeRender)
        {
            var frame = new Border
            {
                Padding = new Thickness(16),
                Width = width,
                Height = height,
                Language = XmlLanguage.GetLanguage(culture),
                Child = content
            };
            frame.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
            var window = new Window
            {
                Width = width,
                Height = height,
                Left = -32000,
                Top = -32000,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Content = frame
            };
            try
            {
                window.Show();
                Pump(frame);
                beforeRender?.Invoke();
                Pump(frame);
                if (content is Calendar calendar) ValidateCalendar(calendar, culture, state);
                else ValidateProject(content);
                if (!frame.IsVisible || !frame.IsLoaded || frame.ActualWidth <= 0 || frame.ActualHeight <= 0 || frame.Background == null)
                    throw new InvalidOperationException(
                        $"Capture surface is not ready: visible={frame.IsVisible}, loaded={frame.IsLoaded}, size={frame.RenderSize}, background={frame.Background}.");
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(frame);
                var pixels = new int[width * height];
                bitmap.CopyPixels(pixels, width * 4, 0);
                var colors = new HashSet<int>();
                foreach (var pixel in pixels)
                {
                    colors.Add(pixel);
                    if (colors.Count >= 16) break;
                }
                if (colors.Count < 16)
                    throw new InvalidOperationException($"Blank or unrendered capture: {palette}/{culture}/{surface}/{state}.");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var name = palette.ToLowerInvariant() + "-" + culture + "-" + surface + "-" + state + ".png";
                using (var stream = new FileStream(Path.Combine(output, name), FileMode.CreateNew, FileAccess.Write))
                    encoder.Save(stream);
                return new CapturedImage(name, palette, culture, surface, state, width, height);
            }
            finally
            {
                window.Close();
            }
        }

        private static void Pump(FrameworkElement element)
        {
            element.Measure(new Size(element.Width, element.Height));
            element.Arrange(new Rect(0, 0, element.Width, element.Height));
            element.UpdateLayout();
            element.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ApplicationIdle);
        }

        private static void ValidateCalendar(Calendar calendar, string cultureName, string state)
        {
            var item = Descendants<CalendarItem>(calendar).Single();
            var month = (Grid)item.Template.FindName("PART_MonthView", item);
            var headers = month.Children.Cast<FrameworkElement>().Where(child => Grid.GetRow(child) == 0)
                .OrderBy(Grid.GetColumn).ToList();
            if (headers.Count != 7) throw new InvalidOperationException("Capture requires seven weekday headings.");
            var culture = CultureInfo.GetCultureInfo(cultureName);
            for (var column = 0; column < headers.Count; column++)
            {
                var label = headers[column] as TextBlock ?? Descendants<TextBlock>(headers[column]).Single();
                var day = ((int)calendar.FirstDayOfWeek + column) % 7;
                if (!label.IsVisible || label.ActualWidth <= 0 || label.ActualHeight <= 0 ||
                    label.Text != culture.DateTimeFormat.ShortestDayNames[day])
                    throw new InvalidOperationException("Missing or incorrect rendered weekday heading in column " + column + ".");
            }
            var days = Descendants<CalendarDayButton>(calendar).ToList();
            if (!days.Single(day => Equals(day.DataContext, SelectedDay.AddDays(-5))).IsBlackedOut)
                throw new InvalidOperationException("The capture blackout date was not generated.");
            if (state == "pending-range")
            {
                var preview = days.Where(day => day.IsHighlighted).ToList();
                if (calendar.SelectedDates.Count != 0 || preview.Count != 4 ||
                    preview.Any(day => day.IsSelected || !(day.DataContext is DateTime date) ||
                        date < SelectedDay || date > SelectedDay.AddDays(3)))
                    throw new InvalidOperationException("WPF did not generate the four uncommitted preview dates.");
            }
            else if (!days.Single(day => Equals(day.DataContext, SelectedDay)).IsSelected ||
                (state == "disabled" && days.Any(day => day.IsEnabled)))
                throw new InvalidOperationException("WPF did not generate the selected/disabled calendar state.");
        }

        private static void ValidateProject(FrameworkElement project)
        {
            var sample = (CaptureProjectData)project.DataContext;
            var name = project.FindName("ProjectNameTextBox") as TextBox;
            var list = project.FindName("ProjectListView") as ListView;
            if (name?.Text != sample.ProjectName || list?.Items.Count != sample.Projects.Count ||
                list?.SelectedItem != sample.SelectedProject)
                throw new InvalidOperationException("The target ProjectView did not bind the capture sample data.");
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match) yield return match;
                foreach (var descendant in Descendants<T>(child)) yield return descendant;
            }
        }

        private static void WriteManifest(CaptureOptions options, CaptureTarget target, IEnumerable<CapturedImage> images)
        {
            using var stream = new FileStream(Path.Combine(options.OutputDirectory, "capture.xml"), FileMode.CreateNew, FileAccess.Write);
            using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true });
            writer.WriteStartElement("themeCapture");
            writer.WriteAttributeString("applicationRoot", options.ApplicationRoot);
            writer.WriteAttributeString("applicationMvid", target.ApplicationAssembly.ManifestModule.ModuleVersionId.ToString());
            writer.WriteAttributeString("viewModelMvid", target.ViewModelAssembly.ManifestModule.ModuleVersionId.ToString());
            writer.WriteAttributeString("hostFramework", typeof(VisualCapture).Assembly.GetCustomAttribute<TargetFrameworkAttribute>().FrameworkName);
            writer.WriteAttributeString("runtimeVersion", Environment.Version.ToString());
            writer.WriteAttributeString("sampleData", "true");
            writer.WriteAttributeString("highContrast", "process-local black/white/yellow; OS settings unchanged");
            foreach (var image in images)
            {
                writer.WriteStartElement("image");
                writer.WriteAttributeString("file", image.Name);
                writer.WriteAttributeString("palette", image.Palette);
                writer.WriteAttributeString("culture", image.Culture);
                writer.WriteAttributeString("surface", image.Surface);
                writer.WriteAttributeString("state", image.State);
                writer.WriteAttributeString("width", image.Width.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("height", image.Height.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDirectory(string path, IntPtr securityAttributes);

        private sealed class CapturedImage
        {
            public CapturedImage(string name, string palette, string culture, string surface, string state, int width, int height)
            {
                Name = name; Palette = palette; Culture = culture; Surface = surface; State = state; Width = width; Height = height;
            }
            public string Name { get; }
            public string Palette { get; }
            public string Culture { get; }
            public string Surface { get; }
            public string State { get; }
            public int Width { get; }
            public int Height { get; }
        }
    }

    internal sealed class CaptureTarget : IDisposable
    {
        private readonly string root;
        private readonly object localization;
        private readonly MethodInfo setCulture;
        public Assembly ApplicationAssembly { get; }
        public Assembly ViewModelAssembly { get; }

        public CaptureTarget(string root)
        {
            this.root = root;
            // This supported resolver is not AppDomain isolation; no production assembly is linked into this host.
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            ApplicationAssembly = LoadExact(File.Exists(Path.Combine(root, "TaskOTime.App.dll"))
                ? "TaskOTime.App.dll" : "TaskOTime.App.exe");
            ViewModelAssembly = LoadExact("TaskOTime.ViewModel.dll");
            var type = ViewModelAssembly.GetType("TaskOTime.ViewModel.Localization.LocalizationService", true);
            localization = type.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? throw new MissingMemberException(type.FullName, "Current");
            setCulture = type.GetMethod("SetCulture", new[] { typeof(string) })
                ?? throw new MissingMethodException(type.FullName, "SetCulture");
        }

        private Assembly LoadExact(string name)
        {
            var path = Path.GetFullPath(Path.Combine(root, name));
            var assembly = Assembly.LoadFrom(path);
            if (!StringComparer.OrdinalIgnoreCase.Equals(assembly.Location, path))
                throw new InvalidOperationException("A different target assembly was already loaded: " + assembly.Location);
            return assembly;
        }

        private Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);
            var name = requested.Name;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            var path = string.IsNullOrEmpty(requested.CultureName)
                ? Path.Combine(root, name + ".dll")
                : Path.Combine(root, requested.CultureName, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        public void SetCulture(string name) => setCulture.Invoke(localization, new object[] { name });

        public FrameworkElement CreateProjectView()
        {
            var type = ViewModelAssembly.GetType("TaskOTime.ViewModel.Views.ProjectView", true);
            return Activator.CreateInstance(type) as FrameworkElement
                ?? throw new InvalidOperationException("ProjectView is not a WPF FrameworkElement.");
        }

        public void Dispose() => AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
    }
}

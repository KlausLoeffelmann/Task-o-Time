using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Extensions.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskOTime.App;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Services;
using TaskOTime.ViewModel.Localization;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.Localization.Tests
{
    [TestClass]
    public class LocalizationTests
    {
        private static Dispatcher dispatcher;
        private static Thread uiThread;
        private static readonly LocalizationService Localizer = LocalizationService.Current;

        [AssemblyInitialize]
        public static void Initialize(TestContext context)
        {
            var ready = new ManualResetEventSlim();
            Exception failure = null;
            uiThread = new Thread(() =>
            {
                try
                {
                    // Load presentation resources only. App.OnStartup connects to SQL and must not run here.
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/TaskOTime.App;component/Themes/ClassicDark.xaml", UriKind.Relative)
                    });
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/TaskOTime.App;component/Resources/Strings.xaml", UriKind.Relative)
                    });
                    dispatcher = Dispatcher.CurrentDispatcher;
                }
                catch (Exception e) { failure = e; }
                finally { ready.Set(); }
                if (failure == null) Dispatcher.Run();
            });
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.IsBackground = true;
            uiThread.Start();
            ready.Wait();
            if (failure != null) throw failure;
        }

        [AssemblyCleanup]
        public static void Cleanup()
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            Assert.IsTrue(uiThread.Join(TimeSpan.FromSeconds(10)),
                $"The test UI dispatcher did not stop: {uiThread.ThreadState}, {dispatcher.HasShutdownStarted}, {dispatcher.HasShutdownFinished}.");
        }

        private static void OnUi(Action action) => dispatcher.Invoke(() =>
        {
            Localizer.SetCulture("en");
            try { action(); }
            finally { Localizer.SetCulture("en"); }
        });

        private static void Flush() => dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

        [TestMethod]
        public void ResourceSetsHaveExactParityAndValidFormatArguments()
        {
            var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
            var neutral = ReadResources(Path.Combine(root, "Strings.resx"));
            foreach (var language in new[] { "de", "nl", "es" })
            {
                var translated = ReadResources(Path.Combine(root, "Strings." + language + ".resx"));
                CollectionAssert.AreEquivalent(neutral.Keys.ToArray(), translated.Keys.ToArray());
                foreach (var key in neutral.Keys)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(translated[key]), key);
                    CollectionAssert.AreEqual(Placeholders(neutral[key]), Placeholders(translated[key]), key);
                }
            }
        }

        private static string[] Placeholders(string value) =>
            System.Text.RegularExpressions.Regex.Matches(value, @"\{(\d+)(?:[^}]*)\}")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();

        private static Dictionary<string, string> ReadResources(string path) =>
            XDocument.Load(path).Root.Elements("data").ToDictionary(x => (string)x.Attribute("name"), x => (string)x.Element("value"));

        [TestMethod]
        public void EveryLocalizedXamlBindingReferencesAnExistingResource()
        {
            var root = AppDomain.CurrentDomain.BaseDirectory;
            var resources = ReadResources(Path.Combine(root, "Resources", "Strings.resx"));
            foreach (var path in Directory.GetFiles(Path.Combine(root, "Views"), "*.xaml"))
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(path), @"\{loc:Loc ([^}]+)\}");
                Assert.IsTrue(matches.Count > 0, path);
                foreach (System.Text.RegularExpressions.Match match in matches)
                    Assert.IsTrue(resources.ContainsKey(match.Groups[1].Value), path + ":" + match.Value);
            }
        }

        [TestMethod]
        public void RealMicrosoftLocalizerLoadsNeutralSatellitesAndParentFallback() => OnUi(() =>
        {
            Assert.IsInstanceOfType(Localizer.Strings, typeof(ResourceManagerStringLocalizer));
            foreach (var pair in new[] { ("en", "Sign in"), ("de", "Anmelden"), ("nl", "Aanmelden"), ("es", "Iniciar sesión"), ("de-AT", "Anmelden") })
            {
                Localizer.SetCulture(pair.Item1);
                Assert.AreEqual(pair.Item2, Localizer["Login_SignIn"]);
                Assert.IsFalse(Localizer.Strings["Booking_Title"].ResourceNotFound);
                Assert.IsFalse(Localizer.Strings["Main_Heading"].ResourceNotFound);
                Assert.IsFalse(Localizer.Strings["Project_Title"].ResourceNotFound);
            }
            Localizer.SetCulture("fr-FR");
            Assert.AreEqual("en", Localizer.Culture.Name);
            Assert.AreEqual("Sign in", Localizer["Login_SignIn"]);
            Localizer.SetCulture("invalid-culture-name");
            Assert.AreEqual("en", Localizer.Culture.Name);
            Assert.IsTrue(Localizer.Strings["Missing_Key"].ResourceNotFound);
            Assert.AreEqual("Missing_Key", Localizer["Missing_Key"]);
        });

        [TestMethod]
        public void EveryResourceIsActuallyResolvableAndTranslationsAreNotEnglishCopies() => OnUi(() =>
        {
            var neutral = Localizer.Strings.GetAllStrings(true).ToDictionary(x => x.Name, x => x.Value);
            foreach (var culture in new[] { "de", "nl", "es" })
            {
                Localizer.SetCulture(culture);
                foreach (var key in neutral.Keys)
                    Assert.IsFalse(Localizer.Strings[key].ResourceNotFound, culture + ":" + key);
                foreach (var key in new[] { "Login_ChangeRequired", "Main_EmptyDay", "Booking_InvalidTime", "Project_NameRequired" })
                    Assert.AreNotEqual(neutral[key], Localizer[key], culture + ":" + key);
            }
        });

        [TestMethod]
        public void LoginErrorAndForcedPasswordStateUpdateWithoutRepeatingAuthentication() => OnUi(() =>
        {
            var authentication = new AuthenticationStub();
            var vm = new LoginViewModel(authentication);
            Assert.IsFalse(vm.Login("user", "wrong"));
            StringAssert.Contains(vm.ErrorMessage, "user name or password");
            Localizer.SetCulture("de");
            StringAssert.Contains(vm.ErrorMessage, "Benutzername");
            Assert.AreEqual(1, authentication.Calls);
            authentication.RequireChange = true;
            Assert.IsFalse(vm.Login("user", "temporary"));
            Assert.IsTrue(vm.MustChangePassword);
            Assert.IsNull(vm.Session);
            Localizer.SetCulture("es");
            StringAssert.Contains(vm.ErrorMessage, "contraseña temporal");
            Assert.IsTrue(vm.ChangeTemporaryPassword("temporary", "new"));
            Assert.IsNotNull(vm.Session);
            Assert.IsFalse(vm.MustChangePassword);
        });

        [TestMethod]
        public void OpenWindowBindingsUpdateIncludingMenusLabelsAndInheritedLanguage() => OnUi(() =>
        {
            var login = new LoginWindow(new LoginViewModel(new AuthenticationStub()), "", "Login_ModeDemo");
            var options = new OptionsDialog(new AppOptionsViewModel());
            var collection = new TimeCollectionViewModel();
            var booking = new TimeEntryEditDialog(new TimeEntryEditRequestEventArgs(DateTime.Today.AddHours(8), "Customer title", "", false, null), collection);
            var main = new MainWindow(new VmMain(collection), new DesktopServices(), new TenantUserDto { UserIdent = "test" });
            try
            {
                Localizer.SetCulture("de");
                Flush();
                Assert.AreEqual("Task-o-Time – Anmelden", login.Title);
                Assert.AreEqual("Optionen", options.Title);
                Assert.AreEqual("Zeitbuchung", booking.Title);
                Assert.AreEqual("de", booking.Language.IetfLanguageTag);
                Assert.IsTrue(Descendants(main).OfType<MenuItem>().Any(x => Equals(x.Header, "_Datei")));
                Assert.IsTrue(Descendants(booking).OfType<TextBlock>().Any(x => x.Text == "Kategorie"));
                Assert.IsTrue(Descendants(login).OfType<TextBlock>().Any(x => x.Text.Contains("Lokale SQL-Demo")));
                Localizer.SetCulture("nl");
                Flush();
                Assert.AreEqual("Tijdboeking", booking.Title);
                Assert.IsTrue(Descendants(main).OfType<MenuItem>().Any(x => Equals(x.Header, "_Bestand")));
                Assert.AreEqual("Customer title", booking.Request.Title);
            }
            finally
            {
                // Suppress placement persistence in memory while closing actual application views.
                var settings = (System.Configuration.ApplicationSettingsBase)typeof(MainWindow).Assembly
                    .GetType("TaskOTime.App.Properties.Settings").GetProperty("Default").GetValue(null);
                var restore = settings["RestoreMainWindowPlacement"];
                settings["RestoreMainWindowPlacement"] = false;
                try
                {
                    main.Close();
                    booking.Close();
                    options.Close();
                    login.Close();
                }
                finally { settings["RestoreMainWindowPlacement"] = restore; }
            }
        });

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var descendant in Descendants(child)) yield return descendant;
        }

        [TestMethod]
        public void OptionsDraftCancelAndApplyPreserveTransactionalSemantics() => OnUi(() =>
        {
            var main = new VmMain();
            var draft = main.Options.Clone();
            draft.CultureName = "de";
            Assert.AreEqual("en", Localizer.Culture.Name);
            Assert.AreEqual("en", main.Options.CultureName);
            var savedDraft = main.Options.Clone();
            savedDraft.CultureName = "nl";
            savedDraft.SaturdayIsWorkday = !main.Options.SaturdayIsWorkday;
            main.ApplyOptions(savedDraft);
            Assert.AreEqual("nl", Localizer.Culture.Name);
            Assert.AreEqual(savedDraft.SaturdayIsWorkday, main.Options.SaturdayIsWorkday);
            Assert.AreEqual("Tijdregistratie", main.TimeCollection.Heading);
        });

        [TestMethod]
        public void CultureRefreshesFormattedViewModelStateWithoutMutatingBookings() => OnUi(() =>
        {
            var main = new VmMain();
            var selected = main.TimeCollection.SelectedDayEntries.First();
            var id = selected.IDTimeItem;
            var count = main.TimeCollection.SelectedDayEntries.Count;
            var notifications = 0;
            main.PropertyChanged += (_, e) => { if (string.IsNullOrEmpty(e.PropertyName)) notifications++; };
            var englishDate = main.SelectedDateSummary;
            Localizer.SetCulture("es");
            Assert.AreNotEqual(englishDate, main.SelectedDateSummary);
            StringAssert.Contains(main.TimeCollection.DaySummary, "registros");
            Assert.IsTrue(notifications > 0);
            Assert.AreEqual(id, selected.IDTimeItem);
            Assert.AreEqual(count, main.TimeCollection.SelectedDayEntries.Count);
        });

        [TestMethod]
        public void CultureAffectsParsingFormattingAndWpfNumericBindings() => OnUi(() =>
        {
            Localizer.SetCulture("en-US");
            Assert.IsTrue(TimeInput.TryParseTime("2:30 PM", out var time));
            Assert.AreEqual(TimeSpan.FromHours(14.5), time);
            Localizer.SetCulture("de-DE");
            Assert.IsTrue(TimeInput.TryParseTime("14:30", out time));
            Assert.IsFalse(TimeInput.TryParseTime("24:01", out _));
            Assert.IsFalse(TimeInput.TryParseTime("2026-09-11", out _));
            Assert.AreEqual("1,5", 1.5.ToString(CultureInfo.CurrentCulture));
            var box = (TextBox)XamlReader.Parse(
                "<TextBox xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:loc='clr-namespace:TaskOTime.ViewModel.Localization;assembly=TaskOTime.ViewModel' Language='{loc:Culture}'/>");
            var source = new NumberSource { Value = 1.5 };
            box.SetBinding(TextBox.TextProperty, new Binding(nameof(NumberSource.Value)) { Source = source, Mode = BindingMode.TwoWay });
            Assert.AreEqual("1,5", box.Text);
            box.Text = "2,5";
            box.GetBindingExpression(TextBox.TextProperty).UpdateSource();
            Assert.AreEqual(2.5, source.Value);
            Localizer.SetCulture("en-US");
            Flush();
            Assert.AreEqual("2.5", box.Text);
        });

        public sealed class NumberSource { public double Value { get; set; } }

        [TestMethod]
        public void SelectedCultureSurvivesCallbacksWithAnOlderExecutionContext() => OnUi(() =>
        {
            Localizer.SetCulture("es");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.AreEqual("Iniciar sesión", Localizer["Login_SignIn"]);
            StringAssert.Contains(Localizer.Format("Main_WeekInMonth", 2, new DateTime(2026, 9, 11)), "septiembre");
            StringAssert.Contains(new VmMain().SelectedDateSummary, DateTime.Today.ToString("MMMM", Localizer.Culture));
            Assert.IsFalse(TimeInput.TryParseTime("2:30 PM", out _));
            Assert.AreEqual("en-US", CultureInfo.CurrentUICulture.Name);
        });

        [TestMethod]
        public void WeakCultureSubscriptionsDoNotRetainDiscardedViewModels() => OnUi(() =>
        {
            var reference = CreateDiscardedViewModel();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.IsFalse(reference.IsAlive);
            Localizer.SetCulture("de");
        });

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateDiscardedViewModel() => new WeakReference(new AppOptionsViewModel());

        private sealed class AuthenticationStub : IAuthenticationService
        {
            public int Calls;
            public bool RequireChange;
            private bool changed;
            public ServiceResult<AuthenticationResult> Authenticate(AuthenticateUserRequest request)
            {
                Calls++;
                return RequireChange || changed
                    ? ServiceResult<AuthenticationResult>.Ok(new AuthenticationResult
                    {
                        User = new TenantUserDto { IdTenant = Guid.NewGuid(), IdUser = Guid.NewGuid(), UserIdent = "user" },
                        MustChangePassword = !changed
                    })
                    : ServiceResult<AuthenticationResult>.Fail("InvalidCredentials", "Service diagnostic");
            }
            public ServiceResult<TenantUserDto> ChangeTemporaryPassword(Guid tenant, Guid user, string temporary, string password)
            {
                changed = true;
                return ServiceResult<TenantUserDto>.Ok(new TenantUserDto());
            }
            public ServiceResult<TenantUserDto> ChangePassword(Guid tenant, Guid user, string current, string password) =>
                throw new NotSupportedException();
        }
    }
}

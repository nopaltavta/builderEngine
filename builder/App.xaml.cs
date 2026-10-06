using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using builder.Settings;
using ControlzEx.Theming;

namespace builder
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            CrashLog.Startup("OnStartup begin");
            AppDomain.CurrentDomain.UnhandledException += (_, a) =>
                CrashLog.Crash("FATAL domain", a.ExceptionObject);
            DispatcherUnhandledException += (_, a) =>
            {
                CrashLog.Crash("FATAL ui", a.Exception);
                MessageBox.Show("builder crashed:\n" + a.Exception.Message +
                    "\n\nDetails in %AppData%\\BuilderStudio\\crash.log",
                    "builder engine", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            TaskScheduler.UnobservedTaskException += (_, a) =>
            {
                CrashLog.Crash("FATAL task", a.Exception);
                a.SetObserved();
            };
            // Roblox-Studio-like dark ribbon. Base colors: Dark/Light, accents: Blue, Cobalt, etc.
            // Generated at runtime by ControlzEx (Fluent.Ribbon 8+ moved ThemeManager there).
            AppSettings.Current = AppSettings.Load();
            CrashLog.Startup("settings loaded");
            CrashLog.Startup("theme: " + ApplyStudioTheme(this, AppSettings.Current.DarkTheme));
            CrashLog.Startup("theme set");
            // Exported games boot here: stash the path now; the window picks it
            // up on Loaded. (StartupUri creates MainWindow inside Run(), AFTER
            // OnStartup returns, so it doesn't exist yet at this point.)
            PendingPlayFile = PlayFileArg(e.Args) ?? BundledGameFile();
            CrashLog.Startup($"args: {e.Args.Length}, play: {PendingPlayFile ?? "<none>"}");
            base.OnStartup(e); // creates MainWindow via StartupUri
            CrashLog.Startup("base.OnStartup returned");
        }

        /// <summary>Studio accent: builder orange, falling back to Cobalt if the
        /// runtime theme pack lacks it. Returns the applied theme name (logged).</summary>
        public static string ApplyStudioTheme(Application app, bool dark)
        {
            string[] candidates = dark
                ? new[] { "Dark.Orange", "Dark.Cobalt" }
                : new[] { "Light.Orange", "Light.Cobalt" };
            foreach (string name in candidates)
            {
                try
                {
                    ThemeManager.Current.ChangeTheme(app, name);
                    return name;
                }
                catch { /* try the fallback */ }
            }
            return "none";
        }

        /// <summary>Place file the fresh window must boot into player mode with.
        /// One-shot: MainWindow clears it on Loaded.</summary>
        public static string? PendingPlayFile { get; set; }

        /// <summary>--play &lt;place.bp&gt; (or --play=&lt;place.bp&gt;). Null = normal studio.</summary>
        private static string? PlayFileArg(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--play" && i + 1 < args.Length) return args[i + 1];
                if (args[i].StartsWith("--play=", StringComparison.OrdinalIgnoreCase))
                    return args[i]["--play=".Length..];
            }
            return null;
        }

        /// <summary>Exported games carry a player.mode marker: boot game.bp
        /// next to the exe with no args needed. Never present in studio installs.</summary>
        private static string? BundledGameFile()
        {
            try
            {
                string dir = AppContext.BaseDirectory;
                if (!File.Exists(Path.Combine(dir, "player.mode"))) return null;
                string game = Path.Combine(dir, "game.bp");
                return File.Exists(game) ? game : null;
            }
            catch { return null; }
        }
    }
}

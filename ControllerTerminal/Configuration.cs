using PanelController.Controller;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Timers;
using System.Xml;

namespace ControllerTerminal
{
    public class Configuration
    {
        public static readonly string WorkingDirectory = Environment.CurrentDirectory;

        public static readonly string ExtensionsFolder = "Extensions";

        public static readonly DirectoryInfo ExtensionsDirectory = new(Path.Combine(WorkingDirectory, ExtensionsFolder));

        public static readonly string PanelsInfoFolder = "PanelsInfo";

        public static readonly DirectoryInfo PanelsInfoDirectory = new(Path.Combine(WorkingDirectory, PanelsInfoFolder));

        public static readonly string ProfilesFolder = "Profiles";

        public static readonly DirectoryInfo ProfilesDirectory = new(Path.Combine(WorkingDirectory, ProfilesFolder));

        public static readonly string ConfigurationFileName = "cfg.json";

        public static readonly FileInfo ConfigurationFile = new(ConfigurationFileName);

        public static readonly Assembly ControllerAsssembly = typeof(Main).Assembly;

        public XmlWriterSettings _xmlWriterSettings = new() { Indent = true, Encoding = System.Text.Encoding.UTF8, IndentChars = "\t" };

        public JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };

        private static Configuration s_config = new();


        private readonly System.Timers.Timer _autoSaveTimer = new()
        {
            AutoReset = true,
            Interval = 30000
        };

        public bool AutoSave
        {
            get => _autoSaveTimer.Enabled;
            set => _autoSaveTimer.Enabled = value;
        }

        public double AutoSaveSecondsInterval
        {
            get => (double)_autoSaveTimer.Interval / 1000.0;
            set => _autoSaveTimer.Interval = value * 1000;
        }

        public static Configuration Config { get => s_config; set => s_config = value; }

        public Configuration()
        {
            _autoSaveTimer.Elapsed += AutoSaveTimer_Elapsed;
        }

        private void AutoSaveTimer_Elapsed(object? sender, ElapsedEventArgs e) => Terminal.SaveAll();
    
        public class Serializable
        {
            public double? AutoSaveSecondsInterval { get; set; } = null;

            public Serializable()
            { }

            public Serializable(Configuration config)
            {
                AutoSaveSecondsInterval = config.AutoSaveSecondsInterval;
            }
        }
    }
}

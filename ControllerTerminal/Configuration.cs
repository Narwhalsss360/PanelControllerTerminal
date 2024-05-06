using PanelController.Controller;
using PanelController.PanelObjects.Properties;
using System.Data;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Timers;
using System.Xml;
using System.Xml.Serialization;

namespace ControllerTerminal
{
    public class Configuration
    {
        public delegate object ObjectConstructor(Type type, object?[] constructArguments);

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

        private ObjectConstructor _constructor = DefaultConfigurationObjectConstructor;

        [JsonIgnore]
        public ObjectConstructor Constructor
        {
            get => _constructor;
            set => _constructor = value;
        }

        [UserProperty]
        public string ConstructorMethodName
        {
            get => $"{Constructor.Method.DeclaringType}.{Constructor.Method.Name}";
        }

        private static Configuration s_config = new();

        private readonly System.Timers.Timer _autoSaveTimer = new()
        {
            AutoReset = true,
            Interval = 30000
        };

        [UserProperty]
        public bool AutoSave
        {
            get => _autoSaveTimer.Enabled;
            set => _autoSaveTimer.Enabled = value;
        }

        [UserProperty]
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

        [JsonConstructor]
        public Configuration(string? ConstructorMethodName)
        {
            if (FindObjectConstructor(ConstructorMethodName) is ObjectConstructor objectConstructor)
                Constructor = objectConstructor;
        }

        public object Construct(Type type, object?[] constructArguments) => Constructor(type, constructArguments);

        public T Construct<T>(object?[] constructArguments) => (T)(Construct(typeof(T), constructArguments) ?? throw new NullReferenceException($"Construction created a null reference"));

        private void AutoSaveTimer_Elapsed(object? sender, ElapsedEventArgs e) => Terminal.SaveAll();

        public static object DefaultConfigurationObjectConstructor(Type  type, object?[] constructArguments)
        {
            if (Activator.CreateInstance(type, constructArguments) is object constructed)
                return constructed;
            throw new NullReferenceException("Constructed object would create null reference");
        }

        public static ObjectConstructor? FindObjectConstructor(string? fullName)
        {
            if (fullName is null)
                return null;

            if (!fullName.Contains('.'))
                return null;

            string typeName = fullName[..fullName.LastIndexOf('.')];
            string methodName = fullName[(fullName.LastIndexOf('.') + 1)..];

            if (Type.GetType(typeName) is not Type type)
                return null;

            if (type.GetMethod(methodName) is not MethodInfo method)
                return null;

            if (!method.IsStatic)
                return null;

            try
            {
                return method.CreateDelegate<ObjectConstructor>();
            }
            catch (Exception)
            {
                return null;
            }
        }

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

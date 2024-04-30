using CLIApplication;
using PanelController.Controller;
using PanelController.Profiling;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace ControllerTerminal
{
    public static class Terminal
    {
        public static readonly string WorkingDirectory = Environment.CurrentDirectory;

        public static readonly string ExtensionsFolder = "Extensions";

        public static readonly DirectoryInfo ExtensionsDirectory = new DirectoryInfo(Path.Combine(WorkingDirectory, ExtensionsFolder));

        public static readonly string PanelsInfoFolder = "PanelsInfo";

        public static readonly DirectoryInfo PanelsInfoDirectory = new DirectoryInfo(Path.Combine(WorkingDirectory, PanelsInfoFolder));

        public static readonly string ProfilesFolder = "Profiles";

        public static readonly DirectoryInfo ProfilesDirectory = new DirectoryInfo(Path.Combine(WorkingDirectory, ProfilesFolder));

        private static XmlWriterSettings _xmlWriterSettings = new() { Indent = true, Encoding = System.Text.Encoding.UTF8, IndentChars = "\t" };

        private static CLIInterpreter? _interpreter;

        private static Dispatcher? _dispatcher;

        private static Action? _quitRequestDelegate = null;

        private static readonly InvalidProgramException _uninitializedControllerTerminalException = new("");

        public static CLIInterpreter Interpreter
        {
            get
            {
                if (_interpreter is null)
                {
                    throw _uninitializedControllerTerminalException;
                }

                return _interpreter;
            }
        }

        public static Dispatcher Dispatcher
        {
            get
            {
                if (_dispatcher is null)
                {
                    throw _uninitializedControllerTerminalException;
                }

                return _dispatcher;
            }
        }

        public static IList? SelectedContainer = null;
        
        public static object? SelectedObject = null;

        private static void Log(string message, Logger.Levels level)
        {
            Logger.Log(message, level, "Controller Terminal");
        }

        public static Assembly? TryLoadAssembly(string path)
        {
            try
            {
                return Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }

        public static void LoadExtensions()
        {
            if (!ExtensionsDirectory.Exists)
                return;
            Log($"Loading extensions from {ExtensionsDirectory.FullName}.", Logger.Levels.Info);

            foreach (FileInfo file in ExtensionsDirectory.GetFiles())
            {
                if (file.Extension.ToLower() != ".dll")
                    continue;
                if (TryLoadAssembly(file.FullName) is not Assembly assembly)
                    continue;
                Extensions.Load(assembly);
            }
        }

        public static void LoadPanels()
        {
            if (!PanelsInfoDirectory.Exists)
                return;

            XmlSerializer serializer = new(typeof(PanelInfo));
            foreach (FileInfo file in PanelsInfoDirectory.GetFiles())
            {
                if (file.Extension.ToLower() != ".xml")
                    continue;

                using FileStream stream = file.OpenRead();
                using XmlReader reader = XmlReader.Create(stream);

                try
                {
                    if (!serializer.CanDeserialize(reader))
                        continue;
                }
                catch (XmlException)
                {
                    continue;
                }

                if (serializer.Deserialize(reader) is not PanelInfo panelInfo)
                    continue;
                Main.PanelsInfo.Add(panelInfo);
            }
        }

        public static void LoadProfiles()
        {
            if (!ProfilesDirectory.Exists)
                return;

            XmlSerializer serializer = new XmlSerializer(typeof(Profile.SerializableProfile));
            foreach (FileInfo file in ProfilesDirectory.GetFiles())
            {
                if (file.Extension.ToLower() != ".xml")
                    continue;

                using FileStream stream = file.OpenRead();
                using XmlReader reader = XmlReader.Create(stream);

                try
                {
                    if (serializer.CanDeserialize(reader))
                        continue;
                }
                catch (XmlException)
                {
                    continue;
                }

                if (serializer.Deserialize(reader) is not Profile.SerializableProfile serializable)
                    continue;

                Main.Profiles.Add(new(serializable));
            }
        }

        public static void LoadCommands()
        {
            if (!ExtensionsDirectory.Exists)
                return;

            foreach (FileInfo file in ExtensionsDirectory.GetFiles())
            {
                if (file.Extension.ToLower() != ".dll")
                    continue;
                if (TryLoadAssembly(file.FullName) is not Assembly assembly)
                    continue;
                if (assembly.GetCustomAttribute<TerminalExtension>() is null)
                    continue;
                foreach (Type type in assembly.GetTypes())
                {
                    foreach (MethodInfo method in type.GetMethods())
                    {
                        if (method.GetCustomAttribute<TerminalExtension>() is null)
                            continue;
                        if (!method.IsStatic)
                        {
                            Log($"Method {method.Name} is non-static, TerminalExtensions commands must be static.", Logger.Levels.Error);
                            Interpreter.Out.WriteLine($"Method {method.Name} is non-static, TerminalExtensions commands must be static.");
                            continue;
                        }
                        if (Interpreter.Commands.Contains(new(method)))
                            continue;
                        Interpreter.Commands.Add(new(method));
                    }
                }
            }
        }

        public static void LoadAll()
        {
            LoadExtensions();
            LoadPanels();
            LoadProfiles();
            LoadCommands();
        }

        public static void SavePanels()
        {
            if (!PanelsInfoDirectory.Exists)
                PanelsInfoDirectory.Create();

            XmlSerializer serializer = new(typeof(PanelInfo));
            foreach (PanelInfo panelInfo in Main.PanelsInfo)
            {
                using FileStream file = File.Open(Path.Combine(PanelsInfoDirectory.FullName, $"{panelInfo.PanelGuid}.xml"), FileMode.Create);
                using XmlWriter writer = XmlWriter.Create(file, _xmlWriterSettings);
                serializer.Serialize(writer, panelInfo);
            }

            foreach (FileInfo file in PanelsInfoDirectory.GetFiles())
            {
                if (Main.PanelsInfo.Any(panelInfo => $"{panelInfo.PanelGuid}.xml" == file.Name))
                    continue;
                file.Delete();
            }
        }

        public static void SaveProfiles()
        {
            if (!ProfilesDirectory.Exists)
                ProfilesDirectory.Create();

            XmlSerializer serializer = new(typeof(Profile.SerializableProfile));
            foreach (Profile profile in Main.Profiles)
            {
                using FileStream file = File.Open(Path.Combine(ProfilesDirectory.FullName, $"{profile.Name}.xml"), FileMode.Create);
                using XmlWriter writer = XmlWriter.Create(file, _xmlWriterSettings);
                serializer.Serialize(writer, new Profile.SerializableProfile(profile));
            }

            foreach (FileInfo file in ProfilesDirectory.GetFiles())
            {
                if (Main.Profiles.Any(profile => $"{profile.Name}.xml" == file.Name))
                    continue;
                file.Delete();
            }
        }

        public static void SaveAll()
        {
            SavePanels();
            SaveProfiles();
        }

        private static void ControllerInitialized()
        {
            LoadAll();
            Interpreter.InterfaceName = "Controller Terminal";
            Interpreter.Commands.Add(new(SaveAll));
            Interpreter.Commands.Add(new(BuiltIns.ShowCommand.Show));
            Interpreter.Commands.Add(new(BuiltIns.Clear));
            Interpreter.Commands.Add(new(BuiltIns.Dump));
            Interpreter.Commands.Add(new(BuiltIns.Quit));
        }

        private static void Init(CLIInterpreter interpreter, Dispatcher dispatcher, Action quitRequestDelegate)
        {
            _interpreter = interpreter;
            _dispatcher = dispatcher;
            _quitRequestDelegate = quitRequestDelegate;
            if (Main.IsInitialized)
            {
                ControllerInitialized();
            }
            else
            {
                Main.Initialized += (sender, args) => ControllerInitialized();
            }
        }

        public static class BuiltIns
        {
            public static class ShowCommand
            {
                private delegate void ShowDelegate();

                public static void LoadedExtensions()
                {

                }

                public static void Profiles()
                {

                }
                
                public static void Mappings()
                {

                }

                public static void Panels()
                {

                }

                public static void Properties()
                {

                }

                public static void Selected()
                {

                }

                public static void All()
                {

                }

                public enum Categories
                {
                    [Description("PanelController Extensions.")]
                    LoadedExtensions,
                    Profiles,
                    [Description("Mappings in Selected Profile.")]
                    Mappings,
                    Panels,
                    [Description("Properties in Selected Object. Fields if Object isn't IPanelObject.")]
                    Properties,
                    Selected,
                    All
                }

                private static Dictionary<Categories, ShowDelegate> _optionsSwitch = new()
                {
                   { Categories.LoadedExtensions, LoadedExtensions },
                   { Categories.Profiles, Profiles },
                   { Categories.Mappings, Mappings },
                   { Categories.Panels, Panels },
                   { Categories.Properties, Properties },
                   { Categories.Selected, Selected },
                   { Categories.All, All }
                };

                [Description("Show information")]
                public static void Show([Description("Select from which category to show.")] Categories category = Categories.All) => _optionsSwitch[category]();
            }

            public static void Dump(string format = "/T [/L][/F] /M")
            {
                foreach (Logger.HistoricalLog log in Logger.Logs)
                    Console.WriteLine(log.ToString(format));
            }

            public static void Clear()
            {
                Console.Clear();
            }

            public static void Quit()
            {
                if (_quitRequestDelegate is null)
                {
                    Main.Deinitialize();
                }
                else
                {
                    _quitRequestDelegate.DynamicInvoke();
                }
            }
        }
    }
}

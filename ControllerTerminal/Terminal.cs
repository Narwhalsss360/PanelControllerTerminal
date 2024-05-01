﻿using CLIApplication;
using PanelController.Controller;
using PanelController.PanelObjects;
using PanelController.PanelObjects.Properties;
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
            Interpreter.Commands.AddRange(BuiltIns.Commands);
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
            public static readonly CLIInterpreter.Command[] Commands = new CLIInterpreter.Command[]
            {
                new(SaveAll),
                new(ShowCommand.Show),
                new(Clear),
                new(Dump),
                new(Quit)
            };

            public static class ShowCommand
            {
                private delegate void ShowDelegate();

                public static void LoadedExtensions()
                {
                    if (Extensions.AllExtensions.Length == 0)
                        return;
                    Interpreter.Out.WriteLine("Loaded Extensions:");
                    foreach (Type extension in Extensions.AllExtensions)
                        Interpreter.Out.WriteLine($"    {extension.GetItemName()} {extension.FullName}");
                }

                public static void Profiles()
                {
                    if (Main.Profiles.Count == 0)
                        return;
                    Interpreter.Out.WriteLine("Profiles");
                    foreach (Profile profile in Main.Profiles)
                        Interpreter.Out.WriteLine($"    {profile} {(ReferenceEquals(Main.CurrentProfile, profile) ? "SELECTED" : "")}");
                }
                
                public static void Mappings()
                {
                    if (Main.CurrentProfile is null)
                        return;
                    if (Main.CurrentProfile.Mappings.Length == 0)
                        return;

                    Interpreter.Out.WriteLine("Mappings:");
                    foreach (Mapping mapping in Main.CurrentProfile.Mappings)
                    {
                        Interpreter.Out.WriteLine($"    {mapping}");
                        foreach (Mapping.MappedObject mapped in mapping.Objects)
                            Interpreter.Out.WriteLine($"        {mapped.Object}:{mapped.Object.Status} {mapped.Delay} {mapped.Value}");
                    }
                }

                public static void Panels()
                {
                    if (Main.PanelsInfo.Count == 0)
                        return;

                    Interpreter.Out.WriteLine("Panels:");
                    foreach (PanelInfo info in Main.PanelsInfo)
                    {
                        Interpreter.Out.WriteLine($"    {info.Name} {info.PanelGuid} {(info.IsConnected ? "CONNECTED" : "DISCONNECTED")}");
                        Interpreter.Out.WriteLine($"        Digital Count:{info.DigitalCount}");
                        Interpreter.Out.WriteLine($"        Analog Count:{info.AnalogCount}");
                        Interpreter.Out.WriteLine($"        Display Count:{info.DisplayCount}");
                    }
                }

                public static void Properties()
                {
                    if (SelectedObject is null)
                        return;

                    Type[] knownTypes = new Type[]
                    {
                        typeof(Profile),
                        typeof(PanelInfo),
                        typeof(Mapping)
                    };

                    if (SelectedObject is not IPanelObject @object)
                    {
                        if (!knownTypes.Contains(SelectedObject.GetType()))
                            Interpreter.Out.WriteLine($"WARNING: Listing of non-IPanelObject, listing {SelectedObject.GetType().Name} {SelectedObject}");

                        Interpreter.Out.WriteLine("Properties:");
                        foreach (PropertyInfo property in SelectedObject.GetType().GetProperties())
                        {
                            Interpreter.Out.Write($"    {(property.IsUserProperty() ? "*" : "")}{property.PropertyType.Name} {property.Name} = ");
                            try
                            {
                                Interpreter.Out.WriteLine(property.GetValue(SelectedObject)?.ToString());
                            }
                            catch (Exception e)
                            {
                                Interpreter.Out.WriteLine($"Exception thrown trying to read: {e}");
                            }
                        }

                        Interpreter.Out.WriteLine("Fields:");
                        foreach (FieldInfo field in SelectedObject.GetType().GetFields())
                        {
                            Interpreter.Out.Write($"    {field.FieldType.Name} {field.Name} = ");
                            try
                            {
                                Interpreter.Out.WriteLine(field.GetValue(SelectedObject)?.ToString());
                            }
                            catch (Exception e)
                            {
                                Interpreter.Out.WriteLine($"Exception thrown trying to read: {e}");
                            }
                        }
                        return;
                    }

                    PropertyInfo[] properties = @object.GetUserProperties();
                    if (properties.Length == 0)
                        return;

                    Interpreter.Out.WriteLine($"{@object.GetItemName()}:");
                    foreach (PropertyInfo property in properties)
                        Interpreter.Out.WriteLine($"    {property.PropertyType.Name} {property.Name} = {property.GetValue(@object)}");
                }

                public static void Selected()
                {
                    if (SelectedObject is null)
                        return;

                    Interpreter.Out.WriteLine("Currently Selected:");
                    Interpreter.Out.WriteLine($"    Containing Collection:{SelectedContainer}");
                    Interpreter.Out.WriteLine($"    Selected Object:{SelectedObject}");
                    Interpreter.Out.WriteLine($"Current Profile: {Main.CurrentProfile?.Name}");
                }

                public static void All()
                {
                    Selected();
                    LoadedExtensions();
                    Profiles();
                    Mappings();
                    Panels();
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

            [Description("Dump PanelController logs to output window.")]
            public static void Dump([Description("/T -> Time, /L -> Level, /F -> From, /M -> Message")] string format = "/T [/L][/F] /M")
            {
                foreach (Logger.HistoricalLog log in Logger.Logs)
                    Console.WriteLine(log.ToString(format));
            }

            [Description("Clear the output window.")]
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

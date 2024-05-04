using CLIApplication;
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

        public static readonly Assembly ControllerAsssembly = typeof(Main).Assembly;

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
                    if (!serializer.CanDeserialize(reader))
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

        public static object AskWhich<T>(this IList<T> list, string listName = "") where T : class
        {
            int index = -1;

            if (list.Count == 0)
                return $"{(listName == "" ? "list" : listName)} is empty.";

            Interpreter.Out.WriteLine("Select Index:");
            for (int i = 0; i < list.Count; i++)
                Interpreter.Out.WriteLine($"    {i}: {list[i]}");

            if (!int.TryParse(Interpreter.In.ReadLine(), out index))
                return "Not a number.";

            return index == -1 ? "Selection Cancelled" : list[index];
        }

        public static object MatchElseAsk<T>(this IList<T> list, Predicate<T>? predicate = null, string listName = "", Action? noMatch = null) where T : class
        {
            if (predicate is null)
                return list.AskWhich();

            foreach (T item in list)
                if (predicate(item))
                    return item;

            if (noMatch is not null)
                noMatch();

            return list.AskWhich();
        }

        public static T ValidateSelection<T>(this object selection)
        {
            if (selection is T selectedObject)
                return selectedObject;
            throw new InvalidProgramException($"{nameof(AskWhich)} should always return T or string.");
        }

        public static string[] DefaultNullFlags(this string[]? flags, bool? newCase = null)
        {
            if (flags is null)
                return new string[0];

            if (!newCase.HasValue)
                return flags;
            flags = Array.ConvertAll(flags, flag => newCase.Value ? flag.ToLower() : flag.ToUpper());
            return flags;
        }

        public static string[] RemoveFlagMarkers(this string[] flags) => Array.ConvertAll(flags, flag => flag.Remove(0, Interpreter.FlagMarker.Length));
        public static object ExtensionSearch(string name)
        {
            IEnumerable<Type> nameMatch = Extensions.AllExtensions.Where(extension => extension.Name == name);
            if (nameMatch.Count() > 1)
                return $"More than one extension with name {name} matches";

            if (nameMatch.Count() == 1)
                return nameMatch.ElementAt(0);

            return name.FindType() is Type type ? type : $"No extension with name/qualified-name with {name} matches";
        }

        public static class BuiltIns
        {
            public static readonly CLIInterpreter.Command[] Commands = new CLIInterpreter.Command[]
            {
#if DEBUG
                new(Break),
#endif
                new(SaveAll),
                new(ShowCommand.Show),
                new(SelectCommand.Select),
                new(Remove),
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

            public static class SelectCommand
            {
                public static void Generic()
                {
                    object selection = Extensions.Objects.AskWhich("Objects");
                    if (selection is string selectionError)
                    {
                        Interpreter.Error.WriteLine(selectionError);
                        return;
                    }

                    IPanelObject selectedPanelObject = selection.ValidateSelection<IPanelObject>();

                    SelectedContainer = Extensions.Objects;
                    SelectedObject = selectedPanelObject;
                }

                public static void Panel(string? panelName = null)
                {
                    object selection = panelName is null ? Main.PanelsInfo.AskWhich("Panels") : Main.PanelsInfo.MatchElseAsk(panel => panel.Name == panelName, "Panels", () => Interpreter.Error.WriteLine($"No panel with name {panelName}."));
                    if (selection is string selectionError)
                    {
                        Interpreter.Error.WriteLine(selectionError);
                        return;
                    }

                    SelectedObject = selection.ValidateSelection<PanelInfo>();
                    SelectedContainer = Main.PanelsInfo;
                }

                public static void Profile(string? profileName)
                {
                    object selection = profileName is null ? Main.Profiles.AskWhich("Profiles") : Main.Profiles.MatchElseAsk(profile => profile.Name == profileName, "Profiles", () => Interpreter.Error.WriteLine($"No profile with name {profileName}."));
                    if (selection is string selectionError)
                    {
                        Interpreter.Error.WriteLine(selectionError);
                        return;
                    }

                    SelectedObject = selection.ValidateSelection<Profile>();
                    SelectedContainer = Main.Profiles;
                }

                public static void Mapping(string? mappingName)
                {
                    if (Main.CurrentProfile is null)
                    {
                        Interpreter.Error.WriteLine("No current profile, cannot select mapping.");
                        return;
                    }

                    SelectedObject = mappingName is null ? Main.CurrentProfile.Mappings.AskWhich("Mappings") : Main.CurrentProfile.Mappings.MatchElseAsk(mapping => mapping.Name == mappingName, "Mappings", () => Interpreter.Error.WriteLine($"No mapping with name {mappingName}."));
                    SelectedContainer = Main.CurrentProfile.Mappings;
                }

                public static void MappedObject(bool? inner = null)
                {
                    inner ??= false;
                    if (SelectedObject is Mapping.MappedObject mappedObject)
                    {
                        if (!inner.Value)
                        {
                            Interpreter.Error.WriteLine("Inner already selected.");
                            return;
                        }
                        SelectedObject = mappedObject;
                        SelectedContainer = null;
                        return;
                    }

                    if (SelectedObject is not Mapping mapping)
                    {
                        Interpreter.Error.WriteLine("Must select Mapping before selecting MappedObject.");
                        return;
                    }

                    object selection = mapping.Objects.AskWhich("Mappings");
                    if (inner.Value)
                    {
                        SelectedObject = selection.ValidateSelection<Mapping.MappedObject>().Object;
                        SelectedContainer = null;
                    }
                    else
                    {
                        SelectedObject = selection.ValidateSelection<Mapping>();
                        SelectedContainer = mapping.Objects;
                    }
                }

                public enum Categories
                {
                    [Description("Select from list")]
                    Generic,
                    [Description("Name, else select from list")]
                    Panel,
                    [Description("Name, else select from list")]
                    Profile,
                    [Description("Name, else select from list")]
                    Mapping,
                    [Description("Select inner from already selected MappedObject OR Select from list of MappedObject from already selected Mapping. Provide --inner flag to select inner object")]
                    MappedObject
                }

                [Description("Select a program object.")]
                public static void Select([Description("Category to select from")] Categories category, [Description("Name to select (if applicable)")] string? name = null, string[]? flags = null)
                {
                    flags = flags.DefaultNullFlags().RemoveFlagMarkers();
                    switch (category)
                    {
                        case Categories.Generic:
                            Generic();
                            break;
                        case Categories.Panel:
                            Panel(name);
                            break;
                        case Categories.Profile:
                            Profile(name);
                            break;
                        case Categories.Mapping:
                            Mapping(name);
                            break;
                        case Categories.MappedObject:
                            MappedObject(flags.Contains("inner"));
                            break;
                        default:
                            break;
                    }
                }
            }

            public static class EditCommand
            {
                public const string ForcePanelObjectName = "/NAME";

                [Description("Edit a property of an object.")]
                public static void Edit([Description($"Name of property, use {ForcePanelObjectName} to force property to be ItemName")] string property, [Description("New value")] string value)
                {
                    if (SelectedObject is null)
                    {
                        Interpreter.Out.WriteLine("No selected object to edit.");
                        return;
                    }

                    if (property == ForcePanelObjectName)
                    {
                        if (SelectedObject is not IPanelObject panelObject)
                        {
                            Interpreter.Error.WriteLine("Selected object is not IPanelObject, no ItemName");
                            return;
                        }

                        panelObject.TrySetItemName(value);
                        return;
                    }

                    if (ControllerAsssembly.DefinedTypes.Contains(SelectedObject.GetType()))
                    {
                        if (property != "Name")
                        {
                            Interpreter.Error.WriteLine("Can only edit name of a Controller Type");
                            return;
                        }

                        if (SelectedObject.GetType().GetProperty("Name") is not PropertyInfo nameProp)
                        {
                            throw new InvalidProgramException("All selectable PanelController assembly types should have a property named 'Name'");
                        }

                        nameProp.SetValue(SelectedObject, value);
                        return;
                    }

                    if (SelectedObject is not IPanelObject @object)
                    {
                        Interpreter.Out.WriteLine("Support for editting properties is only for IPanelObjects");
                        return;
                    }

                    if (Array.Find(@object.GetUserProperties(), prop => prop.Name == property) is not PropertyInfo propertyInfo)
                    {
                        Interpreter.Error.WriteLine($"Property {property} not found.");
                        return;
                    }

                    if (!ParameterInfoExtensions.IsSupported(propertyInfo.PropertyType))
                    {
                        Interpreter.Error.WriteLine($"Property type {propertyInfo.PropertyType} is not supported.");
                        return;
                    }

                    if (value.ParseAs(propertyInfo.PropertyType) is not object parsedValue)
                    {
                        Interpreter.Error.WriteLine($"There was an error trying to parse {value} as {propertyInfo.PropertyType}.");
                        return;
                    }

                    propertyInfo.SetValue(@object, parsedValue);
                }
            }

            public static void Remove(string[]? flags = null)
            {
                if (SelectedContainer is null)
                {
                    Interpreter.Error.WriteLine("Selected object has no container, cannot remove.");
                    return;
                }

                flags = flags.DefaultNullFlags().RemoveFlagMarkers();
                if (!flags.Contains("y"))
                {
                    Interpreter.Out.Write("Confirm (yes/no):");

                    if (Interpreter.In.ReadLine() is not string result)
                        return;

                    if (!result.ToLower().StartsWith("y"))
                        return;
                }
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

#if DEBUG
            public static void Break() { ; }
#endif
        }
    }
}

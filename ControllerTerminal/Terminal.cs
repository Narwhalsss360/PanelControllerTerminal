﻿using CLIApplication;
using PanelController.Controller;
using PanelController.PanelObjects;
using PanelController.PanelObjects.Properties;
using PanelController.Profiling;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace ControllerTerminal
{
    public static class Terminal
    {
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

        private static object? s_selectedContainer = null;

        private static object? s_selectedObject = null;

        public static object? SelectedContainer { get => s_selectedContainer; set => s_selectedContainer = value; }

        public static object? SelectedObject { get => s_selectedObject; set => s_selectedObject = value; }

        private static void Log(string message, Logger.Levels level, bool output = false)
        {
            Logger.Log(message, level, "Controller Terminal");
            if (output)
            {
                if (level == Logger.Levels.Error)
                    Interpreter.Error.WriteLine(message);
                else
                    Interpreter.Out.WriteLine(message);
            }
        }

        public static Assembly? TryLoadAssembly(string path)
        {
            try
            {
                return Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException e)
            {
                Log($"Cannot load assembly from {path}, incorrect DLL type. {e.Message}.", Logger.Levels.Error, true);
            }
            catch (PlatformNotSupportedException e)
            {
                Log($"Cannot load assembly from {path}, wrong platform. {e.Message}.", Logger.Levels.Error, true);
            }
            catch (FileNotFoundException)
            {
                Interpreter.Error.WriteLine($"Cannot load {path}, file does not exist.");
            }
            catch (Exception e)
            {
                Log($"Cannot load assembly from {path}, {e}. {e.Message}.", Logger.Levels.Error, true);
            }

            return null;
        }

        public static void LoadExtensions()
        {
            if (!Configuration.ExtensionsDirectory.Exists)
                return;
            Log($"Loading extensions from {Configuration.ExtensionsDirectory.FullName}.", Logger.Levels.Info);

            foreach (FileInfo file in Configuration.ExtensionsDirectory.GetFiles())
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
            if (!Configuration.PanelsInfoDirectory.Exists)
                return;

            XmlSerializer serializer = new(typeof(PanelInfo));
            foreach (FileInfo file in Configuration.PanelsInfoDirectory.GetFiles())
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
                catch (XmlException e)
                {
                    Log($"There was an error loading {file.Name}: {e.Message}", Logger.Levels.Error);
                    continue;
                }

                if (serializer.Deserialize(reader) is not PanelInfo panelInfo)
                {
                    Log($"There was an unkown error loading {file.Name}", Logger.Levels.Error);
                    continue;
                }
                Main.PanelsInfo.Add(panelInfo);
            }
        }

        public static void LoadProfiles()
        {
            if (!Configuration.ProfilesDirectory.Exists)
                return;

            XmlSerializer serializer = new(typeof(Profile.SerializableProfile));
            foreach (FileInfo file in Configuration.ProfilesDirectory.GetFiles())
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
                catch (XmlException e)
                {
                    Log($"There was an error loading {file.Name}: {e.Message}", Logger.Levels.Error);
                    continue;
                }

                if (serializer.Deserialize(reader) is not Profile.SerializableProfile serializable)
                {
                    Log($"There was an error loading {file.Name}", Logger.Levels.Error);
                    continue;
                }

                Main.Profiles.Add(new(serializable));
            }
        }

        public static void LoadCommands()
        {
            if (!Configuration.ExtensionsDirectory.Exists)
                return;

            foreach (FileInfo file in Configuration.ExtensionsDirectory.GetFiles())
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
                        Log($"Loaded command {method.Name} from {assembly.FullName}...{type.Name}", Logger.Levels.Info);
                    }
                }
            }
        }

        public static void LoadConfig()
        {
            if (!Configuration.ConfigurationFile.Exists)
                return;
            using FileStream file = Configuration.ConfigurationFile.Open(FileMode.Open);
            try
            {
                if (JsonSerializer.Deserialize<Configuration>(file) is not Configuration deserialized)
                    throw new JsonException();
                Configuration.Config = deserialized;
            }
            catch (JsonException e)
            {
                Log($"There was an error loading {file.Name}: {e.Message}", Logger.Levels.Error);
                return;
            }
            Log($"Loaded ControllerTerminal configuration.", Logger.Levels.Info, true);
        }

        public static void LoadAll()
        {
            LoadExtensions();
            LoadPanels();
            LoadProfiles();
            LoadCommands();
            LoadConfig();
        }

        public static void SavePanels()
        {
            if (!Configuration.PanelsInfoDirectory.Exists)
                Configuration.PanelsInfoDirectory.Create();

            XmlSerializer serializer = new(typeof(PanelInfo));
            foreach (PanelInfo panelInfo in Main.PanelsInfo)
            {
                using FileStream file = File.Open(Path.Combine(Configuration.PanelsInfoDirectory.FullName, $"{panelInfo.PanelGuid}.xml"), FileMode.Create);
                using XmlWriter writer = XmlWriter.Create(file, Configuration.Config._xmlWriterSettings);
                serializer.Serialize(writer, panelInfo);
            }

            foreach (FileInfo file in Configuration.PanelsInfoDirectory.GetFiles())
            {
                if (Main.PanelsInfo.Any(panelInfo => $"{panelInfo.PanelGuid}.xml" == file.Name))
                    continue;
                file.Delete();
            }
            Log("Panels Saved", Logger.Levels.Info);
        }

        public static void SaveProfiles()
        {
            if (!Configuration.ProfilesDirectory.Exists)
                Configuration.ProfilesDirectory.Create();

            XmlSerializer serializer = new(typeof(Profile.SerializableProfile));
            foreach (Profile profile in Main.Profiles)
            {
                using FileStream file = File.Open(Path.Combine(Configuration.ProfilesDirectory.FullName, $"{profile.Name}.xml"), FileMode.Create);
                using XmlWriter writer = XmlWriter.Create(file, Configuration.Config._xmlWriterSettings);
                serializer.Serialize(writer, new Profile.SerializableProfile(profile));
            }

            foreach (FileInfo file in Configuration.ProfilesDirectory.GetFiles())
            {
                if (Main.Profiles.Any(profile => $"{profile.Name}.xml" == file.Name))
                    continue;
                file.Delete();
            }
            Log("Panels Saved", Logger.Levels.Info);
        }

        public static void SaveConfig()
        {
            using FileStream file = Configuration.ConfigurationFile.Open(FileMode.Create);
            using StreamWriter writer = new(file);
            writer.Write(JsonSerializer.Serialize(Configuration.Config, Configuration.Config._jsonSerializerOptions));
            Log("Config Saved", Logger.Levels.Info);
        }

        public static void SaveAll()
        {
            SavePanels();
            SaveProfiles();
            SaveConfig();
        }

        private static void ControllerInitialized()
        {
            LoadAll();
            Interpreter.InterfaceName = "Controller Terminal";
            Interpreter.Commands.AddRange(BuiltIns.Commands);
            ObjectsManager.Initialize();
        }

        private static void ControllerDeinitialized()
        {
            Extensions.Objects.Clear();
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
            Main.Deinitialized += (sender, args) => ControllerDeinitialized();
        }

        public static object AskWhich<T>(this IList<T> list, string listName = "") where T : class
        {
            if (list.Count == 0)
                return $"{(listName == "" ? "list" : listName)} is empty.";

            Interpreter.Out.WriteLine("Select Index:");
            for (int i = 0; i < list.Count; i++)
                Interpreter.Out.WriteLine($"    {i}: {list[i]}");

            if (!int.TryParse(Interpreter.In.ReadLine(), out int index))
                return "Not a number.";

            return index < 0 ? "Selection Cancelled" : list[index];
        }

        public static object MatchElseAsk<T>(this IList<T> list, Predicate<T>? predicate = null, string listName = "", Action? noMatch = null) where T : class
        {
            if (predicate is null)
                return list.AskWhich(listName);

            foreach (T item in list)
                if (predicate(item))
                    return item;

            if (noMatch is not null)
                noMatch();

            return list.AskWhich(listName);
        }

        public static T ValidateSelection<T>(this object selection)
        {
            if (selection is T selectedObject)
                return selectedObject;
            throw new InvalidProgramException($"{nameof(AskWhich)} should always return T or string.");
        }

        public const bool DFLT_FLAGS_TO_UPPER = true;

        public const bool DFLT_FLAGS_TO_LOWER = true;

        public static string[] DefaultNullFlags(this string[]? flags, bool? caseState = null)
        {
            if (flags is null)
                return Array.Empty<string>();

            if (!caseState.HasValue)
                return flags;
            flags = Array.ConvertAll(flags, flag => caseState.Value ? flag.ToUpper() : flag.ToLower());
            return flags;
        }

        public static string[] RemoveFlagMarkers(this string[] flags) => Array.ConvertAll(flags, flag => flag.Remove(0, Interpreter.FlagMarker.Length));

        public static string PythonListPrint<T>(this IEnumerable<T> enumerable, Func<T, string>? stringifer = null)
        {
            if (stringifer is null)
                stringifer = (T obj) => $"{obj}";
            string output = "[";

            foreach (T item in enumerable)
                output += stringifer(item);

            if (output != "[")
                output.Remove(output.Length - 1 - ", ".Length);
            output += "]";
            return output;
        }

        public static string PythonDictionaryPrint<TKey, TValue>(this IDictionary<TKey, TValue> dict, Func<TKey, string>? keyStringifer = null, Func<TValue, string>? valueStringifer = null)
        {
            if (keyStringifer is null)
                keyStringifer = (TKey key) => $"{key}";
            if (valueStringifer is null)
                valueStringifer = (TValue value) => $"{value}";

            string output = "{";
            TKey[] keys = dict.Keys.ToArray();
            for (int i = 0; i < keys.Length; i++)
            {
                output += $"{keys[i]}:{dict[keys[i]]}";
                if (i != keys.Length - 1)
                    output += ", ";
            }
            output += "}";
            return output;
        }

        public static string TypeNameNoGenericMangling(this Type type)
        {
            string name = type.Name;
            int index = name.IndexOf('`');
            return index == -1 ? name : name.Substring(0, index);
        }

        public static string NiceTypeName(this Type type)
        {
            if (type.IsGenericType)
            {
                string niceName = $"{type.TypeNameNoGenericMangling()}<";
                Type[] genericArguments = type.GetGenericArguments();
                for (int i = 0; i < genericArguments.Length; i++)
                {
                    niceName += genericArguments[i].NiceTypeName();
                    if (i != genericArguments.Length - 1)
                        niceName += ", ";
                }
                niceName += ">";
                return niceName;
            }
            return type.Name;
        }

        public static object ExtensionSearch(string name)
        {
            IEnumerable<Type> nameMatch = Extensions.AllExtensions.Where(extension => extension.Name == name);
            if (nameMatch.Count() > 1)
                return $"More than one extension with name {name} matches";

            if (nameMatch.Count() == 1)
                return nameMatch.ElementAt(0);

            return name.FindType() is Type type ? type : $"No extension with name/qualified-name with {name} matches";
        }

        public static string AvoidNameConfict(this string name)
        {
            string[] deliminated = name.Split('(');
            if (deliminated.Length == 1)
                return $"{name}(1)";

            if (deliminated.Last().Last() == ')')
                deliminated[^1] = deliminated.Last()[..^1];

            if (!uint.TryParse(deliminated.Last(), out uint num))
                return $"{name}(1)";


            return $"{name[..^$"({num})".Length]}({num + 1})";
        }

        public static ConstructorInfo? GetConstructorForArgs(this Type type, object[] args)
        {
            ConstructorInfo matchedCtor;
            try
            {
                matchedCtor = (from ctor in type.GetConstructors()
                        where ctor.GetParameters().Length == args.Length
                        && ctor.GetParameters().All(
                            param => ParameterInfoExtensions.IsSupported(param.ParameterType)
                            )
                        select ctor).First();
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            return (from param in matchedCtor.GetParameters()
                    select param.ParameterType).
                    Zip(
                        from arg in args
                        select arg.GetType()).
                    All(tup => tup.First == tup.Second || tup.Second == typeof(string))
                    ? matchedCtor : null;
        }

        public static object[] ParseEnums(this ParameterInfo[] parameters, object[] args)
        {
            if (parameters.Length != args.Length)
                throw new InvalidOperationException($"`{nameof(parameters)}` and `{nameof(args)}` must be same length");

            for (int i = 0; i < parameters.Length; i++)
            {
                Type paramType = parameters[i].ParameterType;
                object arg = args[i];
                if (arg.GetType() == paramType)
                    continue;
                if (!paramType.IsEnum || arg is not string argString)
                {
                    Interpreter.Error.WriteLine($"{arg} could not be parsed as {paramType.NiceTypeName()}");
                    break;
                }
                if (argString.ParseAs(paramType) is not object parsed)
                {
                    Interpreter.Error.WriteLine($"{arg} could not be parsed as {paramType.NiceTypeName()}");
                    break;
                }
                args[i] = parsed;
            }
            return args;
        }

        public static object[] GlobalSearch(this string query)
        {
            query = query.ToLower();
            List<object> objects = new();

            bool ObjectQueryMatched(object @object)
            {
                return
                    @object.GetItemName().ToLower().Contains(query) ||
                    @object.GetItemDescription().ToLower().Contains(query) ||
                    (@object.GetType().FullName?.ToLower().Contains(query) ?? false) ||
                    (@object.ToString()?.ToLower().Contains(query) ?? false);
            }

            objects.AddRange(from extension in Extensions.AllExtensions where ObjectQueryMatched(extension) select extension);
            objects.AddRange(from profile in Main.Profiles where ObjectQueryMatched(profile) select profile);
            objects.AddRange(from panelInfo in Main.PanelsInfo where ObjectQueryMatched(panelInfo) select panelInfo);
            objects.AddRange(from cmd in Interpreter.Commands where ObjectQueryMatched(cmd) select cmd);

            return objects.ToArray();
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
                new(EditCommand.Edit),
                new(CreateCommand.Create),
                new(Open),
                new(ConfigCommand.Config),
                new(Detail),
                new(Search),
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

                    if (Extensions.Objects.Count == 0)
                        return;
                    Interpreter.Out.WriteLine("Instantiated Extensions:");
                    foreach (IPanelObject panelObject in Extensions.Objects)
                        Interpreter.Out.WriteLine($"    {panelObject.GetItemName()}: {panelObject.GetType().NiceTypeName()}");
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

                    string FormatPropertyOrField(object propOrField, object? from)
                    {
                        object? value = null;
                        if (propOrField is PropertyInfo prop)
                        {
                            try
                            {
                                value = prop.GetValue(from);
                            }
                            catch (Exception e)
                            {
                                return $"Exception thrown trying to read: {e}";
                            }
                        }
                        else if (propOrField is FieldInfo field)
                        {
                            try
                            {
                                value = field.GetValue(from);
                            }
                            catch (Exception e)
                            {
                                return $"Exception thrown trying to read: {e}";
                            }
                        }
                        else
                            return $"Cannot print {propOrField}: {propOrField.GetType().NiceTypeName()}";

                        if (value is IEnumerable<object> enumerable)
                            return PythonListPrint(enumerable);
                        else if (value is IDictionary<object, object> dictionary)
                            return PythonDictionaryPrint(dictionary);
                        else
                            return value is null ? "null" : $"{value}";
                    }

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
                            Interpreter.Out.WriteLine($"    {(property.IsUserProperty() ? "*" : "")}{property.PropertyType.NiceTypeName()} {property.Name} = {FormatPropertyOrField(property, SelectedObject)}");

                        Interpreter.Out.WriteLine("Fields:");
                        foreach (FieldInfo field in SelectedObject.GetType().GetFields())
                            Interpreter.Out.WriteLine($"    {field.FieldType.NiceTypeName()} {field.Name} = {FormatPropertyOrField(field, SelectedObject)}");
                        return;
                    }

                    PropertyInfo[] properties = @object.GetUserProperties();
                    if (properties.Length == 0)
                        return;

                    Interpreter.Out.WriteLine($"{@object.GetItemName()}:");
                    foreach (PropertyInfo property in properties)
                        Interpreter.Out.WriteLine($"    {property.PropertyType.NiceTypeName()} {property.Name} = {FormatPropertyOrField(property, SelectedObject)}");
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

                private static readonly Dictionary<Categories, ShowDelegate> _optionsSwitch = new()
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

                    Profile selectedProfile = selection.ValidateSelection<Profile>();
                    SelectedObject = selectedProfile;
                    SelectedContainer = Main.Profiles;
                    Main.SelectedProfileIndex = Main.Profiles.IndexOf(selectedProfile);
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

                    if (selection is string selectionError)
                    {
                        Interpreter.Out.WriteLine(selectionError);
                        return;
                    }

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
                    flags = flags.DefaultNullFlags(DFLT_FLAGS_TO_LOWER).RemoveFlagMarkers();
                    
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

                public static void Property(PropertyInfo property, string? value)
                {
                    if (SelectedObject is null)
                        return;

                    if (!ParameterInfoExtensions.IsSupported(property.PropertyType))
                    {
                        Interpreter.Error.WriteLine($"Type {property.PropertyType.NiceTypeName()} is not supported");
                        return;
                    }

                    object? parsed = null;

                    if (value is null)
                    {
                        Interpreter.Out.Write($"Did you mena to set {property.Name} as null?");
                        if (Interpreter.In.ReadLine() is not string entry)
                            return;
                        if (!entry.StartsWith('y'))
                            return;
                    }
                    else if (value.ParseAs(property.PropertyType) is object parsedValue)
                    {
                        parsed = parsedValue;
                    }
                    else
                    {
                        Interpreter.Error.WriteLine($"There was an error parsing {value} as {value}");
                        return;
                    }

                    property.SetValue(SelectedObject, parsed);
                }

                public static void List(PropertyInfo property, ContainerActions action, string? idxOrValue)
                {
                    if (SelectedObject is null)
                        return;

                    if (!property.PropertyType.IsAssignableTo(typeof(IList)) || property.GetValue(SelectedObject) is not IList list)
                    {
                        Interpreter.Error.WriteLine($"{property.Name} is of type {property.PropertyType.NiceTypeName()} and is not assignable to IList");
                        return;
                    }

                    if (action == ContainerActions.Remove)
                    {
                        if (!int.TryParse(idxOrValue, out int idx))
                        {
                            Interpreter.Error.WriteLine($"Could not parse {idxOrValue} as a number");
                            return;
                        }
                        if (list.Count == 0)
                        {
                            Interpreter.Error.WriteLine($"{property.Name} is empty");
                            return;
                        }
                        if (idx < 0 || list.Count <= idx)
                        {
                            Interpreter.Error.WriteLine($"{idx} is out of range: [{0}, {list.Count}]");
                            return;
                        }
                        list.RemoveAt(idx);
                        return;
                    }

                    Type parseAs = typeof(string);

                    if (property.PropertyType.IsGenericType)
                    {
                        if (property.PropertyType.GenericTypeArguments.Length != 0)
                            parseAs = property.PropertyType.GenericTypeArguments[0];
                    }

                    if (!ParameterInfoExtensions.IsSupported(parseAs))
                    {
                        Interpreter.Error.WriteLine($"Type {parseAs.NiceTypeName()} is not supported");
                        return;
                    }

                    object? parsed = null;

                    if (idxOrValue is null)
                    {
                        Interpreter.Out.Write($"Did you mena to set {property.Name} as null?");
                        if (Interpreter.In.ReadLine() is not string entry)
                            return;
                        if (!entry.StartsWith('y'))
                            return;
                    }
                    else if (idxOrValue.ParseAs(parseAs) is object parsedValue)
                    {
                        parsed = parsedValue;
                    }
                    else
                    {
                        Interpreter.Error.WriteLine($"There was an error parsing {idxOrValue} as {parseAs.NiceTypeName()}");
                        return;
                    }

                    list.Add(parsed);
                }

                public static void Dictionary(PropertyInfo property, ContainerActions action, string key, string? value = null)
                {
                    if (SelectedObject is null)
                        return;

                    if (!property.PropertyType.IsAssignableTo(typeof(IDictionary)) || property.GetValue(SelectedObject) is not IDictionary dictionary)
                    {
                        Interpreter.Error.WriteLine($"{property.Name} is of type {property.PropertyType.NiceTypeName()} and is not assignable to IDictionary");
                        return;
                    }

                    Type keyType = typeof(string);
                    Type valueType = typeof(string);

                    if (property.PropertyType.IsGenericType)
                    {
                        if (property.PropertyType.GenericTypeArguments.Length > 0)
                            keyType = property.PropertyType.GenericTypeArguments[0];
                        if (property.PropertyType.GenericTypeArguments.Length > 1)
                            valueType = property.PropertyType.GenericTypeArguments[1];
                    }

                    if (!ParameterInfoExtensions.IsSupported(keyType))
                    {
                        Interpreter.Error.WriteLine($"Type {keyType.NiceTypeName()} is not supported");
                        return;
                    }

                    if (key.ParseAs(keyType) is not object parsedKey)
                    {
                        Interpreter.Error.WriteLine($"There was an error parsing key {key} as {keyType.NiceTypeName()}");
                        return;
                    }

                    if (action == ContainerActions.Remove)
                    {
                        if (!dictionary.Contains(parsedKey))
                        {
                            Interpreter.Error.WriteLine($"Key {parsedKey} not in {property.Name}");
                            return;
                        }
                        dictionary.Remove(parsedKey);
                        return;
                    }

                    object? parsedValue = null;

                    if (value is null)
                    {
                        Interpreter.Out.Write($"Did you mena to set {property.Name} as null?");
                        if (Interpreter.In.ReadLine() is not string entry)
                            return;
                        if (!entry.StartsWith('y'))
                            return;
                    }
                    else if (value.ParseAs(valueType) is object parsed)
                    {
                        parsedValue = parsed;
                    }
                    else
                    {
                        Interpreter.Error.WriteLine($"There was an error parsing {value} as {valueType.NiceTypeName()}");
                        return;
                    }

                    if (!ParameterInfoExtensions.IsSupported(valueType))
                    {
                        Interpreter.Error.WriteLine($"Type {valueType.NiceTypeName()} is not supported");
                        return;
                    }

                    dictionary.Add(parsedKey, parsedValue);
                }

                public enum EditTypes
                {
                    [Description("Single Property")]
                    Property,
                    [Description("1-Dimensional list")]
                    List,
                    [Description("Key-Value pairs")]
                    Dictionary
                }

                public enum ContainerActions
                {
                    [Description("List: Add `value` | Dictionary: Add `key` and `value`")]
                    Add,
                    [Description("List: Remove at index `value` | Dictionary: Remove `key`")]
                    Remove
                }

                [Description("Edit a property of an object.")]
                public static void Edit(EditTypes editType, [Description($"Name of property, use {ForcePanelObjectName} to force property to be ItemName")] string property, [Description("New value")] string? value = null, [Description("Key (for dictionary)")] string? key = null, ContainerActions? action = null)
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

                        if (value is null)
                        {
                            return;
                        }

                        panelObject.TrySetItemName(value);
                    }

                    if (Configuration.ControllerAsssembly.DefinedTypes.Contains(SelectedObject.GetType()))
                    {
                        if (SelectedObject.GetType().GetField(property) is not FieldInfo controllerField)
                        {
                            throw new InvalidProgramException("All selectable PanelController assembly types should have a property named 'Name'");
                        }

                        if (!ParameterInfoExtensions.IsSupported(controllerField.FieldType))
                        {
                            Interpreter.Error.WriteLine($"{controllerField.FieldType.NiceTypeName()} is an unsupported type");
                            return;
                        }

                        if (value?.ParseAs(controllerField.FieldType) is not object parsed)
                        {
                            Interpreter.Error.WriteLine($"There was an error parsing {value} as {controllerField.FieldType.NiceTypeName()}");
                            return;
                        }

                        controllerField.SetValue(SelectedObject, parsed);
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

                    switch (editType)
                    {
                        case EditTypes.Property:
                            Property(propertyInfo, value);
                            break;
                        case EditTypes.List:
                            if (!action.HasValue)
                            {
                                Interpreter.Error.WriteLine($"Must specify {nameof(ContainerActions)}");
                                return;
                            }
                            List(propertyInfo, action.Value, value);
                            break;
                        case EditTypes.Dictionary:
                            if (key is null)
                            {
                                Interpreter.Error.WriteLine("Must specify key");
                                return;
                            }
                            if (!action.HasValue)
                            {
                                Interpreter.Error.WriteLine($"Must specify {nameof(ContainerActions)}");
                                return;
                            }
                            Dictionary(propertyInfo, action.Value, key, value);
                            break;
                        default:
                            break;
                    }
                }
            }

            public static class CreateCommand
            {
                public delegate void SelectableCreator(bool select, string name, object[] constructArgs);

                public static void Generic(bool select, string name, object[] constructArgs)
                {
                    object searchResult = ExtensionSearch(name);
                    if (searchResult is string errorMessage)
                    {
                        Interpreter.Error.WriteLine(errorMessage);
                        return;
                    }

                    if (searchResult is not Type type)
                        throw new InvalidProgramException("ExtensionSearch should always return type of string or Type");

                    if (type.GetExtensionCategory() != Extensions.ExtensionCategories.Generic)
                    {
                        Interpreter.Error.WriteLine($"Extension {type.GetItemName()} is not a {Extensions.ExtensionCategories.Generic} type.");
                        return;
                    }

                    if (type.GetConstructorForArgs(constructArgs) is not ConstructorInfo ctor)
                    {
                        Interpreter.Error.WriteLine("Constructor with specified arguments do not exist");
                        return;
                    }

                    IPanelObject instance;
                    try
                    {
                        instance = Configuration.Config.Construct<IPanelObject>(type, ctor.GetParameters().ParseEnums(constructArgs));
                    }
                    catch (Exception e)
                    {
                        Interpreter.Error.WriteLine($"There was an error constructing object {e}: {e.Message}.");
                        return;
                    }

                    Extensions.Objects.Add(instance);

                    if (select)
                    {
                        SelectedObject = instance;
                        SelectedContainer = Extensions.Objects;
                    }
                }

                public static void Channel(string name, object[] constructArgs)
                {
                    object searchResult = ExtensionSearch(name);
                    if (searchResult is string errorMessage)
                    {
                        Interpreter.Error.WriteLine(errorMessage);
                        return;
                    }

                    if (searchResult is not Type type)
                        throw new InvalidProgramException("ExtensionSearch should always return type of string or Type");

                    if (type.GetExtensionCategory() != Extensions.ExtensionCategories.Channel)
                    {
                        Interpreter.Error.WriteLine($"Extension {type.GetItemName()} is not a  {Extensions.ExtensionCategories.Channel}  type.e.");
                        return;
                    }

                    if (type.GetConstructorForArgs(constructArgs) is not ConstructorInfo ctor)
                    {
                        Interpreter.Error.WriteLine("Constructor with specified arguments do not exist");
                        return;
                    }

                    IChannel instance;
                    try
                    {
                        instance = Configuration.Config.Construct<IChannel>(type, ctor.GetParameters().ParseEnums(constructArgs));
                    }
                    catch (Exception e)
                    {
                        Interpreter.Error.WriteLine($"There was an error constructing object {e}: {e.Message}.");
                        return;
                    }

                    _ = Main.HandshakeAsync(instance);
                }

                public static void Profile(bool select, string name, object[] constructArgs)
                {
                    if (Main.Profiles.Any(profile => profile.Name == name))
                    {
                        Profile(select, name.AvoidNameConfict(), constructArgs);
                        return;
                    }

                    Profile newProfile = new() { Name = name };
                    Main.Profiles.Add(newProfile);
                    if (select)
                        Main.SelectedProfileIndex = Main.Profiles.IndexOf(newProfile);
                }

                public static void Mapping(bool select, string name, bool? onPress, object[] constructArgs)
                {
                    if (Main.CurrentProfile is null)
                    {
                        Interpreter.Error.WriteLine("No current profile.");
                        return;
                    }

                    if (Main.CurrentProfile.Mappings.Any(mapping => mapping.Name == name))
                    {
                        Mapping(select, name.AvoidNameConfict(), onPress, constructArgs);
                        return;
                    }

                    Mapping newMapping = new() { Name = name, InterfaceOption = onPress };

                    if (constructArgs.Length > 0)
                    {
                        if (constructArgs[0].GetType() != typeof(string))
                        {
                            Interpreter.Error.WriteLine("First argument after typeName must be InterfaceType");
                        }
                        else if ((constructArgs[0] as string)?.ParseAs(typeof(InterfaceTypes)) is not InterfaceTypes interfaceType)
                        {
                            Interpreter.Error.WriteLine("First argument after typeName must be InterfaceType");
                        }
                        else
                        {
                            newMapping.InterfaceType = interfaceType;
                        }
                    }

                    if (constructArgs.Length > 1)
                    {
                        if (((uint?)constructArgs[1]) is not uint interfaceID)
                        {
                            Interpreter.Error.WriteLine("Second argument after typeName must be uint");
                        }
                        else
                        {
                            newMapping.InterfaceID = interfaceID;
                        }
                    }

                    if (constructArgs.Length > 2)
                    {
                        if (constructArgs[2] is not string panelName)
                        {
                            Interpreter.Error.WriteLine("Third argument after typeName must be string");
                        }
                        else if (Main.PanelsInfo.Find(panel => panel.Name == panelName) is PanelInfo panelInfo)
                        {
                            newMapping.PanelGuid = panelInfo.PanelGuid;
                        }
                        else
                        {
                            Interpreter.Error.WriteLine($"Panel by {panelName} was not found.");
                        }
                    }

                    if (Main.CurrentProfile.FindMapping(newMapping) is not null)
                    {
                        Interpreter.Error.WriteLine("Mapping already exists.");
                        return;
                    }

                    Main.CurrentProfile.AddMapping(newMapping);

                    if (select)
                    {
                        SelectedObject = newMapping;
                        SelectedContainer = Main.CurrentProfile;
                    }
                }

                public static void MappedObject(bool select, string typeName, object[] constructArgs)
                {
                    if (SelectedObject is not Mapping mapping)
                    {
                        Interpreter.Error.WriteLine("Must have a preselected Mapping to attach MappedObject to.");
                        return;
                    }

                    object search = ExtensionSearch(typeName);

                    if (search is string searchErrorMessage)
                    {
                        Interpreter.Error.WriteLine(searchErrorMessage);
                        return;
                    }

                    if (search is not Type mappable)
                        throw new InvalidProgramException($"ExtensionSearch should always return string or Type");

                    if (mappable.GetConstructorForArgs(constructArgs) is not ConstructorInfo ctor)
                    {
                        Interpreter.Error.WriteLine("Constructor with specified arguments do not exist");
                        return;
                    }

                    IPanelObject instance;
                    try
                    {
                        instance = Configuration.Config.Construct<IPanelObject>(mappable, ctor.GetParameters().ParseEnums(constructArgs));
                    }
                    catch (Exception e)
                    {
                        Interpreter.Error.WriteLine($"There was an error constructing object {e}: {e.Message}.");
                        return;
                    }

                    mapping.Objects.Add(new(instance, TimeSpan.Zero, null));
                }

                public static void PanelInfo(bool select, string name, object[] constructArgs)
                {
                    if (Main.PanelsInfo.Any(panel => panel.Name == name))
                    {
                        PanelInfo(select, name.AvoidNameConfict(), constructArgs);
                        return;
                    }

                    PanelInfo newPanelInfo = new();

                    if (constructArgs.Length > 0)
                    {
                        if (((uint?)constructArgs[0]) is not uint DigitalCount)
                        {
                            Interpreter.Error.WriteLine("First argument after typeName must be uint");
                        }
                        else
                        {
                            newPanelInfo.DigitalCount = DigitalCount;
                        }
                    }

                    if (constructArgs.Length > 1)
                    {
                        if (((uint?)constructArgs[1]) is not uint AnalogCount)
                        {
                            Interpreter.Error.WriteLine("Second argument after typeName must be uint");
                        }
                        else
                        {
                            newPanelInfo.AnalogCount = AnalogCount;
                        }
                    }

                    if (constructArgs.Length > 2)
                    {
                        if (((uint?)constructArgs[2]) is not uint DisplayCount)
                        {
                            Interpreter.Error.WriteLine("Third argument after typeName must be uint");
                        }
                        else
                        {
                            newPanelInfo.DisplayCount = DisplayCount;
                        }
                    }

                    Main.PanelsInfo.Add(newPanelInfo);

                    if (select)
                    {
                        SelectedObject = newPanelInfo;
                        SelectedContainer = Main.PanelsInfo;
                    }
                }

                public enum CreationType
                {
                    [Description("(typeName, Constructor Arguments...)")]
                    Generic,
                    [Description("(typeName, Constructor Arguments...)")]
                    Channel,
                    [Description("(profileName)")]
                    Profile,
                    [Description("(mappingName, InterfaceType, InterfaceID, PanelName?)")]
                    Mapping,
                    [Description("(typeName, Constructor Arguments...)")]
                    MappedObject,
                    [Description("(panelName, digitalCount, analogCount, displayCount)")]
                    PanelInfo
                }

                private static readonly Dictionary<CreationType, SelectableCreator> SelectableCreators = new()
                {
                    { CreationType.Generic, Generic },
                    { CreationType.Profile, Profile },
                    { CreationType.MappedObject, MappedObject },
                    { CreationType.PanelInfo, PanelInfo }
                };

                public static void Create(CreationType type, string[]? flags = null, params object[] args)
                {
                    flags = flags.DefaultNullFlags(DFLT_FLAGS_TO_LOWER).RemoveFlagMarkers();
                    if (args.Length == 0)
                    {
                        Interpreter.Error.WriteLine($"Must enter typeName argument when creating {type} type.");
                        return;
                    }

                    string name = args[0].ToString() ?? "";
                    args = args[1..args.Length];

                    if (type == CreationType.Channel)
                        Channel(name, args);
                    else if (type == CreationType.Mapping)
                        Mapping(flags.Contains("select"), name, flags.Contains("on-press") ? true : (flags.Contains("on-release") ? false : null), args);
                    else
                        SelectableCreators[type](flags.Contains("select"), name, args);
                }
            }

            public static class ConfigCommand
            {
                public static void Set(string setting, string value, int index = -1)
                {
                    if (typeof(Configuration).GetProperty(setting) is not PropertyInfo property)
                    {
                        Interpreter.Error.WriteLine($"Property {setting} does not exist.");
                        return;
                    }

                    if (!ParameterInfoExtensions.IsSupported(property.PropertyType) || property.GetCustomAttribute<UserPropertyAttribute>() is null)
                    {
                        throw new InvalidProgramException($"All properties of {nameof(Configuration)} must be a supported (parsable) type.");
                    }

                    object? parsedValue = value.ParseAs(property.PropertyType);

                    if (parsedValue is null)
                    {
                        Interpreter.Error.WriteLine($"There was an error parsing \"{value}\" as {property.PropertyType.Name}.");
                        return;
                    }

                    if (property.PropertyType.IsArray)
                    {
                        if (index < 0)
                        {
                            Interpreter.Error.WriteLine($"Must enter index of array {setting}.");
                            return;
                        }
                        property.SetValue(Configuration.Config, parsedValue, new object?[] { index });
                    }
                    else
                    {
                        property.SetValue(Configuration.Config, parsedValue);
                        Interpreter.Out.WriteLine($"{setting}:{property.GetValue(Configuration.Config)}");
                    }
                }

                public static void Get(string setting, int index = -1)
                {
                    if (typeof(Configuration).GetProperty(setting) is not PropertyInfo property || property.GetCustomAttribute<UserPropertyAttribute>() is null)
                    {
                        Interpreter.Error.WriteLine($"Property {setting} does not exist.");
                        return;
                    }

                    if (property.PropertyType.IsArray)
                    {
                        if (index < 0)
                        {
                            Interpreter.Error.WriteLine($"Must enter index of array {setting}.");
                            return;
                        }
                    }
                    else
                    {
                        Interpreter.Out.WriteLine($"{setting}:{property.GetValue(Configuration.Config)}");
                    }
                }

                public static void List()
                {
                    foreach (PropertyInfo property in typeof(Configuration).GetProperties())
                    {
                        if (property.GetCustomAttribute<UserPropertyAttribute>() is null)
                            continue;

                        Interpreter.Out.Write($"{property.PropertyType.Name} {property.Name}:");
                        if (property.PropertyType.IsArray)
                        {
                            if (property.GetValue(Configuration.Config) is not IList list)
                            {
                                Interpreter.Out.WriteLine($"{null}");
                                continue;
                            }

                            for (int i = 0; i < list.Count; i++)
                                Interpreter.Out.WriteLine($"    [{i}]:{list[i]}");
                        }
                        else
                        {
                            Interpreter.Out.WriteLine(property.GetValue(Configuration.Config));
                        }
                    }
                }

                public static void Dump(string path)
                {
                    try
                    {
                        using FileStream file = File.Open(path, FileMode.OpenOrCreate);
                        using StreamWriter writer = new(file);
                        writer.Write(JsonSerializer.Serialize(Configuration.Config, Configuration.Config._jsonSerializerOptions));
                    }
                    catch (Exception e)
                    {
                        Interpreter.Error.WriteLine($"There was an error dumping s_config: {e}, {e.Message}.");
                    }
                }

                public static void Load(string path)
                {
                    try
                    {
                        using FileStream file = File.Open(path, FileMode.OpenOrCreate);
                        using StreamReader reader = new(file);
                        if (JsonSerializer.Deserialize<Configuration>(file) is not Configuration config)
                            throw new Exception($"Deserialization Error");
                        Configuration.Config = config;
                    }
                    catch (Exception e)
                    {
                        Interpreter.Error.WriteLine($"There was an error loading s_config: {e}, {e.Message}.");
                        return;
                    }

                    Interpreter.Out.WriteLine($"Loaded s_config from {path}.");
                }

                public static void Default()
                {
                    Interpreter.Out.WriteLine("Are you sure you want to set configuration to defaults?(yes/no)");
                    if (Interpreter.In.ReadLine() is not string ans)
                        return;
                    if (ans.ToLower().StartsWith('y'))
                        return;
                    Configuration.Config = new Configuration();
                }

                public enum ConfigPropertyAction
                {
                    [Description("Set the value of a property | name: Name of property, value: New value")]
                    Set,
                    [Description("Get the value of a property | name: Name of property")]
                    Get,
                    [Description("Get all properties and values")]
                    List,
                    [Description("Serialize configuration and save to specified path | name: Output path")]
                    Dump,
                    [Description("Deserialize and load configuration from specified path | name: Input path")]
                    Load,
                    [Description("Set configuration to defaults")]
                    Defaults
                }

                [Description("Manage ControllerTerminal configuation.")]
                public static void Config(ConfigPropertyAction action, string name = "", string value = "", [Description("Enter index for managing single value of a 1-dimensional collection")] int index = -1)
                {
                    switch (action)
                    {
                        case ConfigPropertyAction.Set:
                            Set(name, value, index);
                            break;
                        case ConfigPropertyAction.Get:
                            Get(name, index);
                            break;
                        case ConfigPropertyAction.List:
                            List();
                            break;
                        case ConfigPropertyAction.Dump:
                            Dump(name);
                            break;
                        case ConfigPropertyAction.Load:
                            Load(name);
                            break;
                        case ConfigPropertyAction.Defaults:
                            Default();
                            break;
                        default:
                            break;
                    }
                }
            }

            public static void Detail(int treeLevel = 3, bool properties = true, bool fields = true, bool onlyPanelControllerTypes = true)
            {
                void Recurse(object? current, int currentLevel, string name)
                {
                    if (current is null)
                        return;
                    if (currentLevel == treeLevel)
                        return;

                    if (!Configuration.ControllerAsssembly.GetTypes().
                            Any(t => current.GetType().IsAssignableTo(t)) &&
                        onlyPanelControllerTypes &&
                        current.GetType() != typeof(Type) || current.GetType().IsPrimitive)
                        return;


                    string levelIndent = "";
                    for (int i = 0; i < currentLevel; i++)
                        levelIndent += '\t';

                    Interpreter.Out.WriteLine($"{levelIndent}{name}:{current} -> {current.GetType().NiceTypeName()} | {current.GetItemDescription()}");

                    if (current.GetType().IsPrimitive)
                        return;

                    if (properties)
                    {
                        foreach (PropertyInfo prop in  current.GetType().GetProperties())
                        {
                            try
                            {
                                Recurse(prop.GetValue(current), currentLevel + 1, prop.Name);
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }

                    if (fields)
                    {
                        foreach (FieldInfo field in current.GetType().GetFields())
                        {
                            try
                            {
                                Recurse(field.GetValue(current), currentLevel + 1, field.Name);
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }

                Recurse(SelectedObject, 0, SelectedObject.GetItemName());
            }

            [Description("Global program search")]
            public static void Search(string query, string[]? flags = null)
            {
                flags = DefaultNullFlags(flags, false).RemoveFlagMarkers();
                object[] results = GlobalSearch(query);
                if (flags.Contains("select"))
                {
                    object selection = AskWhich(results, "Results");
                    if (selection is string errorMessage)
                    {
                        Interpreter.Error.WriteLine(errorMessage);
                        return;
                    }
                    SelectedObject = selection;
                    SelectedContainer = null;
                }
                else
                {
                    foreach (object obj in results)
                        Interpreter.Out.WriteLine($"{obj} -> {obj.GetType().NiceTypeName()}");
                }
            }

            public static void Open(string name, string[]? flags = null, params object[] constructArgs)
            {
                flags = flags.DefaultNullFlags(DFLT_FLAGS_TO_LOWER).RemoveFlagMarkers();

                object searchResult = ExtensionSearch(name);
                if (searchResult is string errorMessage)
                {
                    Interpreter.Error.WriteLine(errorMessage);
                    return;
                }

                if (searchResult is not Type type)
                    throw new InvalidProgramException("ExtensionSearch should always return type of string or Type");

                if (type.GetConstructorForArgs(constructArgs) is not ConstructorInfo ctor)
                {
                    Interpreter.Error.WriteLine("Constructor with specified arguments do not exist");
                    return;
                }

                if (type.GetExtensionCategory() == Extensions.ExtensionCategories.Generic)
                {
                    if (!type.IsAssignableTo(typeof(Window)))
                    {
                        Interpreter.Error.WriteLine($"The type {type.GetItemName()} is not a window.");
                        return;
                    }

                    IPanelObject instance;
                    try
                    {
                        instance = Configuration.Config.Construct<IPanelObject>(type, ctor.GetParameters().ParseEnums(constructArgs));
                    }
                    catch (Exception e)
                    {
                        Interpreter.Error.WriteLine($"There was an error constructing object {e}: {e.Message}.");
                        return;
                    }

                    Extensions.Objects.Add(instance);

                    if (flags.Contains("select"))
                    {
                        SelectedObject = instance;
                        SelectedContainer = Extensions.Objects;
                    }
                }
                else if (type.GetExtensionCategory() == Extensions.ExtensionCategories.Channel)
                {
                    IChannel instance;
                    try
                    {
                        instance = Configuration.Config.Construct<IChannel>(type, ctor.GetParameters().ParseEnums(constructArgs));
                    }
                    catch (Exception e)
                    {
                        Interpreter.Error.WriteLine($"There was an error constructing object {e}: {e.Message}.");
                        return;
                    }

                    _ = Main.HandshakeAsync(instance);
                }
                else
                {
                    Interpreter.Error.WriteLine("Can only open a Generic (Window) type or a channel.");
                    return;
                }
            }

            public static void Remove(string[]? flags = null)
            {
                if (SelectedContainer is null)
                {
                    Interpreter.Error.WriteLine("Selected object has no container, cannot remove.");
                    return;
                }

                flags = flags.DefaultNullFlags(DFLT_FLAGS_TO_LOWER).RemoveFlagMarkers();
                if (!flags.Contains("y"))
                {
                    Interpreter.Out.Write("Confirm (yes/no):");

                    if (Interpreter.In.ReadLine() is not string result)
                        return;

                    if (!result.ToLower().StartsWith("y"))
                        return;
                }

                if (SelectedContainer is IList list)
                {
                    list.Remove(SelectedObject);
                }
                else if (SelectedContainer is Profile profile)
                {
                    if (SelectedObject is not Mapping selectedMapping)
                    {
                        Interpreter.Error.WriteLine("Unkown type in Profile Container");
                        return;
                    }
                    profile.RemoveMapping(selectedMapping);
                }
                else
                {
                    Interpreter.Error.WriteLine("Unkown container");
                    return;
                }

                SelectedObject = null;
                SelectedContainer = null;
            }

            [Description("Dump PanelController logs to output window.")]
            public static void Dump([Description("/T -> Time, /L -> Level, /F -> From, /M -> Message")] string format = "/T [/L][/F] /M", string fileOutput = "")
            {
                TextWriter writer;
                Action scopeExit;

                if (fileOutput != "")
                {
                    FileInfo file = new FileInfo(fileOutput);
                    if (!file.Directory?.Exists ?? false)
                        return;
                    if (!file.Exists)
                        file.Create();
                    FileStream stream = file.Open(FileMode.Create);
                    StreamWriter streamWriter = new StreamWriter(stream);
                    scopeExit = () =>
                    {
                        streamWriter.Close();
                        stream.Close();
                    };
                    writer = streamWriter;
                }
                else
                {
                    writer = Interpreter.Out;
                    scopeExit = () => { };
                }

                foreach (Logger.HistoricalLog log in Logger.Logs)
                    writer.WriteLine(log.ToString(format));
                scopeExit();
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
            public static void Break()
            { }
#endif
        }
    }
}

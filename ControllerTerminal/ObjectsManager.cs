using PanelController.Controller;
using PanelController.PanelObjects;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows;

namespace ControllerTerminal
{
    public static class ObjectsManager
    {
        private static List<IPanelObject>? s_objects;

        private static List<IPanelObject> Objects { get => s_objects ?? throw new InvalidProgramException($"Uninitialied {nameof(ObjectsManager)}"); }

        private static List<Predicate<IPanelObject>> s_watchObject = new();

        private static List<Delegate> s_disposers = new();

        public static void Initialize()
        {
            if (s_objects is not null)
                return;
            s_objects = new();
            Extensions.Objects.CollectionChanged += ObjectsChanged;
            AddWatch(WatchDisposables);
            AddWatch(WatchWindows);
            AddDisposer(DisposeDisposable);
            AddDisposer(DisposeWindow);
        }

        private static void ObjectsChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.Action == NotifyCollectionChangedAction.Move)
                return;

            if (args.NewItems is not null)
                Objects.AddRange(args.NewItems.Cast<IPanelObject>().Where(@object => Watch(@object)));

            foreach (IPanelObject @object in Objects.Where(@object => (args.OldItems is not null ? args.OldItems.Contains(@object) : false) || args.Action == NotifyCollectionChangedAction.Reset))
            {
                if (GetDisposerFor(@object) is not Delegate disposer)
                    throw new InvalidProgramException($"All watched objects must have a disposer");
                try
                {
                    disposer.DynamicInvoke(@object);
                }
                catch (Exception e)
                {
                    Logger.Log($"An exception was thrown trying to dispose {@object}, {e}: {e.Message}.", Logger.Levels.Error, $"TerminalController {nameof(ObjectsManager)}");
                }
            }
            
        }

        public static void AddWatch(Predicate<IPanelObject> watch) => s_watchObject.Add(watch);

        public static void AddDisposer(Delegate disposer)
        {
            MethodInfo methodInfo = disposer.GetMethodInfo();
            ParameterInfo[] parameters = methodInfo.GetParameters();

            if (parameters.Length != 1)
                throw new ArgumentException($"{nameof(disposer)}: Disposer must have 1 parameter");

            if (parameters[0].GetType() == typeof(IPanelObject))
                throw new ArgumentException($"{nameof(disposer)}: Disposer parameter cannot be IPanelObject, it encapsulates all objects");

            s_disposers.Add(disposer);
        }

        public static Delegate? GetDisposerFor(IPanelObject @object)
        {
            foreach (Delegate disposer in s_disposers)
                if (@object.GetType().IsAssignableTo(disposer.GetMethodInfo().GetParameters()[0].ParameterType))
                    return disposer;
            return null;
        }

        public static bool Watch(IPanelObject @object) => s_watchObject.Any(watch => watch(@object) && GetDisposerFor(@object) is not null);

        private static bool WatchDisposables(IPanelObject @object) => @object.GetType().IsAssignableTo(typeof(IDisposable));

        private static void DisposeDisposable(IDisposable disposable) => disposable.Dispose();

        private static bool WatchWindows(IPanelObject @object) => @object.GetType().IsAssignableTo(typeof(Window));

        private static void DisposeWindow(Window window) => window.Dispatcher.Invoke(() => window.Close());
    }
}

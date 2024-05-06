using CLIApplication;
using ControllerTerminal;
using System.Windows.Threading;
using System.Reflection;

namespace TerminalHost
{
    public static class Program
    {
        private static readonly string TerminalControllerInitializerMethodName = "Init";

        private static readonly CLIInterpreter _interpreter = new()
        {
            InterfaceName = "PanelController"
        };

        private static readonly Thread _interpreterThread = new(() => { _interpreter.Run(); });

        private static void InitializeTerminalController()
        {
            MethodInfo? initializer = typeof(Terminal).GetMethod(TerminalControllerInitializerMethodName, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidProgramException("ControllerTerminal initializer method not found");

            object[] initializerArguments = new object[]
            {
                _interpreter,
                Dispatcher.CurrentDispatcher,
                (Action)QuitRequest
            };

            try
            {
                initializer.Invoke(null, initializerArguments);
            }
            catch (Exception)
            {
                Console.WriteLine($"An exception was thrown trying to invoke ControllerTerminal initializer.");
                throw;
            }
        }

        private static void Initialized(object? sender, EventArgs args)
        {
            _interpreterThread.Start();
            Dispatcher.Run();
        }

        private static void Deinitialized(object? sender, EventArgs args)
        {
            Terminal.Dispatcher.Invoke(() => { Terminal.Dispatcher.DisableProcessing(); });
            Dispatcher.ExitAllFrames();
            _interpreter.Stop();
            Environment.Exit(0);
        }

        private static void QuitRequest() => PanelController.Controller.Main.Deinitialize();

        [STAThread]
        public static void Main()
        {
            InitializeTerminalController();
            PanelController.Controller.Main.Initialized += Initialized;
            PanelController.Controller.Main.Deinitialized += Deinitialized;
            PanelController.Controller.Main.Initialize();
        }
    }
}

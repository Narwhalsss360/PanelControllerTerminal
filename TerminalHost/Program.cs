using CLIApplication;
using ControllerTerminal;
using System.Windows.Threading;

namespace TerminalHost
{
    public static class Program
    {
        private static CLIInterpreter _interpreter = new();

        private static Thread _interpreterThread = new(() => { _interpreter.Run(); });

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

        [STAThread]
        public static void Main(string[] args)
        {
            _ = (typeof(Terminal).
                GetMethod("Init", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.
                Invoke(null, new object[] { _interpreter, Dispatcher.CurrentDispatcher, (object)Deinitialized }));
            PanelController.Controller.Main.Initialized += Initialized;
            PanelController.Controller.Main.Deinitialized += Deinitialized;
            PanelController.Controller.Main.Initialize();
        }
    }
}

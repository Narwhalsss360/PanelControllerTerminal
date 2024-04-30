using CLIApplication;
using PanelController.Controller;
using System.Windows.Threading;

namespace ControllerTerminal
{
    public static class Terminal
    {
        public static readonly string WorkingDirectory = Environment.CurrentDirectory;

        public static readonly string ExtensionsFolder = "Extensions";

        public static readonly string PanelsInfoFolder = "PanelsInfo";

        private static CLIInterpreter? _interpreter;

        private static Dispatcher? _dispatcher;

        public static CLIInterpreter Interpreter
        {
            get
            {
                if (_interpreter is null)
                {
                    throw new InvalidProgramException();
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
                    throw new InvalidProgramException();
                }

                return _dispatcher;
            }
        }

        public static void LoadExtensions()
        {

        }

        public static void LoadPanels()
        {

        }

        public static void LoadProfiles()
        {

        }

        private static void ControllerInitialized()
        {
            
        }

        private static void Init(CLIInterpreter interpreter, Dispatcher dispatcher)
        {
            _interpreter = interpreter;
            _dispatcher = dispatcher;
            if (Main.IsInitialized)
            {
                ControllerInitialized();
            }
            else
            {
                Main.Initialized += (sender, args) => ControllerInitialized();
            }
        }
    }
}

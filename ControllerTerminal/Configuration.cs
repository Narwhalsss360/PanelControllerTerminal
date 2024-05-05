using System.Timers;

namespace ControllerTerminal
{
    public class Configuration
    {
        private System.Timers.Timer _autoSaveTimer = new()
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

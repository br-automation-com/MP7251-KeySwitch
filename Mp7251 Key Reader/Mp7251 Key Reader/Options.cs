using log4net.Core;

namespace Mp7251_Key_Reader
{
    public class Options
    {
        private Level _logLevel = Level.Error;

        public string LogLevel {
            get => _logLevel.Name;
            set { switch (value.ToLower())
                {
                    case "debug":
                        _logLevel = Level.Debug;
                        break;
                    case "info":
                        _logLevel = Level.Info;
                        break;
                    case "error":
                        _logLevel = Level.Error;
                        break;
                    default:
                        _logLevel = Level.Error;
                        break;
                }
            } 
        }
        public double UpdateInterval { get; set; } = 100;

        public Level GetLogLevel() { return _logLevel; }

    }

}

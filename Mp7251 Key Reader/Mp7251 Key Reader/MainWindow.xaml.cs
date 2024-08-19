using BR.Adi.Interop;
using log4net;
using log4net.Appender;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;
using System.Windows.Input;

namespace Mp7251_Key_Reader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private Options _options;
        private PLC _plc;
        private PLCVariables _plcVariables;
        private OpcUaComm _opcUaComm;

        private Timer updateTimer;
        private byte oldKeySwitches = 255;
        private bool[] oldKeyMatrix;
        private void SetupLogger()
        {
            Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository();

            PatternLayout patternLayout = new PatternLayout()
            {
                ConversionPattern = "%date %level %logger{1} - %message%n",
            };
            patternLayout.ActivateOptions();


            RollingFileAppender roller = new RollingFileAppender()
            {
                AppendToFile = false,
                File = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EventLog.log"),
                Layout = patternLayout,
                MaxSizeRollBackups = 5,
                MaximumFileSize = "5MB",
                RollingStyle = RollingFileAppender.RollingMode.Size,
                StaticLogFileName = true,
                LockingModel = new FileAppender.MinimalLock(),
            };
            roller.ActivateOptions();

            var buffer = new BufferingForwardingAppender()
            {
                BufferSize = 256,
            };
            buffer.AddAppender(roller);
            buffer.ActivateOptions();
            hierarchy.Root.AddAppender(buffer);

            hierarchy.Root.Level = _options.GetLogLevel();
            hierarchy.Configured = true;
        }


        public MainWindow()
        {
            InitializeComponent();
            ReadConfigurationFile();
            SetupLogger();
            WriteOutput("Opening");

            this.Loaded += MainWindow_Loaded;

            updateTimer = new Timer();

            try
            {
                _opcUaComm = new OpcUaComm(_plc.IP_Address, _plc.Port);
                _opcUaComm.Connected += _opcUaComm_Connected;
                _opcUaComm.Disconnected += _opcUaComm_Disconnected;
                _opcUaComm.Connect();
            }
            catch (Exception ex)
            {
                log.Error(ex.Message);
                WriteOutput(ex.Message);
            }

        }

        private void _opcUaComm_Disconnected(object sender, EventArgs e)
        {
            Error("Disconnected from OpcUa server");
        }

        private void _opcUaComm_Connected(object sender, EventArgs e)
        {
            WriteOutput("Connected to OpcUa server");
            log.Debug("Connected to OpcUa server");
            oldKeySwitches = 255;
            ReadKeySwitch();

            oldKeyMatrix = new bool[_plcVariables.KeyMatrix.Count];
            ReadKeyMatrix();
            foreach (var key in _plcVariables.KeyMatrix)
                WriteKey(key, oldKeyMatrix[_plcVariables.KeyMatrix.IndexOf(key)]);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            updateTimer.Interval = _options.UpdateInterval;
            updateTimer.Elapsed += UpdateTimer_Elapsed;
            updateTimer.AutoReset = true;
            updateTimer.Enabled = true;
            WindowState = WindowState.Minimized;
        }

        private void ReadConfigurationFile()
        {
            var configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile("config.json");
            var configuration = configurationBuilder.Build();

            _options = new Options();
            _plc = new PLC();
            _plcVariables = new PLCVariables();
            configuration.GetSection(nameof(Options)).Bind(_options);
            configuration.GetSection(nameof(PLC)).Bind(_plc);
            configuration.GetSection(nameof(PLCVariables)).Bind(_plcVariables);
        }

        private void UpdateTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            SelectPanel();
            ReadKeySwitch();
            ReadKeyMatrix();
        }

        private void WriteOutput(string output)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                OutputWindow.Text += output;
                OutputWindow.Text += Environment.NewLine;
                OutputWindowScrollViewer.ScrollToEnd();
            }));
        }

        private void Error(string message)
        {
            log.Error(message);
            WriteOutput(message);
        }

        private void SelectPanel()
        {
            try
            {
                if (!NativeMethods.AdiSelectPanel(0))
                {
                    Error($"can't select panel (error {Marshal.GetLastWin32Error()})");
                    return;
                }
            }
            catch (Exception ex)
            {
                Error("Error Loading DLL, is the ADI installed?");
                updateTimer.Enabled = false;
                return;
            }

        }

        private void ReadKeySwitch()
        {
            if (!NativeMethods.AdiGetKeyCfgValue(KeyCfgValue.State, out bool keyCfgState))
            {
                Error($"can't get key configuration state (error {Marshal.GetLastWin32Error()})");
                return;
            }

            if (!NativeMethods.AdiGetKeySwitches(out byte keySwitches))
            {
                Error($"can't get key switches (error {Marshal.GetLastWin32Error()})");
                return;
            }
            if (keySwitches != oldKeySwitches)
            {
                WriteKeySwitch(keySwitches);
            }

            oldKeySwitches = keySwitches;

        }


        private void WriteKeySwitch(UInt16 keySwitches)
        {
            WriteOutput($"Key switches: {keySwitches:X2}h");
            log.Debug($"Key switches: {keySwitches:X2}h");
            try
            {
                _opcUaComm.Write(_plcVariables.KeySwitch, keySwitches);
            }
            catch (Exception e)
            {
                log.Error(e.Message);
                WriteOutput(e.Message);
            }
        }

        private void ReadKeyMatrix()
        {
            foreach (var key in _plcVariables.KeyMatrix)
            {
                if (!NativeMethods.AdiGetKey(key.KeyNumber, out bool value))
                {
                    Error($"can't get key matrix (error {Marshal.GetLastWin32Error()})");
                    continue;
                }
                if (value != oldKeyMatrix[_plcVariables.KeyMatrix.IndexOf(key)])
                {
                    WriteKey(key, value);
                    oldKeyMatrix[_plcVariables.KeyMatrix.IndexOf(key)] = value;
                }
            }
        }

        private void WriteKey(PLCVariable var, bool value)
        {
            WriteOutput($"Key {var.KeyNumber} : {value}");
            log.Debug($"Key  {var.KeyNumber} : {value}");
            try
            {
                _opcUaComm.Write(var, value);
            }
            catch (Exception e)
            {
                log.Error(e.Message);
                WriteOutput(e.Message);
            }
        }

    }
}

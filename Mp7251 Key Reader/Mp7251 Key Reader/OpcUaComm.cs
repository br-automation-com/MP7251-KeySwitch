using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System;
using System.Linq;
using System.Runtime.Remoting;
using System.Threading.Tasks;
using System.IO;

namespace Mp7251_Key_Reader
{
    public class OpcUaComm
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private readonly ConfiguredEndpoint _endpoint;
        private Session _session = null;
        private readonly UserIdentity _userIdentity;
        private readonly ApplicationConfiguration _appConfig;
        private const int ReconnectPeriod = 10000;
        private readonly object _lock = new object();
        private SessionReconnectHandler _reconnectHandler;
        private const string AppName = "MP7251";

        #region Event Handlers
        public event EventHandler Disconnected;
        public event EventHandler Connected;

        protected virtual void OnDisconnected(EventArgs e)
        {
            Disconnected?.Invoke(this, e);
        }
        protected virtual void OnConnected(EventArgs e)
        {
            Connected?.Invoke(this, e);
        }
        #endregion

        public OpcUaComm(string ipAddress, int port)
        {
            String path = Path.Combine(Directory.GetCurrentDirectory(), "Certificates");
            Directory.CreateDirectory(path);
            String hostName = System.Net.Dns.GetHostName();

            _userIdentity = new UserIdentity();
            _appConfig = new ApplicationConfiguration
            {
                ApplicationName = "MP7251",
                ApplicationUri = Utils.Format(@"urn:{0}" + AppName, hostName),
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StorePath = Path.Combine(path, "Application"),
                        SubjectName = $"CN={AppName}, DC={hostName}"
                    },
                    AutoAcceptUntrustedCertificates = true,
                    AddAppCertToTrustedStore = true,
                    RejectSHA1SignedCertificates = false
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 20000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 5000 },
                TraceConfiguration = new TraceConfiguration
                {
                    DeleteOnLoad = true,
                },
                DisableHiResClock = false
            };
            _appConfig.Validate(ApplicationType.Client).GetAwaiter().GetResult();

            if (_appConfig.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                _appConfig.CertificateValidator.CertificateValidation += (s, ee) =>
                {
                    ee.Accept = (ee.Error.StatusCode == StatusCodes.BadCertificateUntrusted);
                };
            }

            var application = new ApplicationInstance
            {
                ApplicationName = AppName,
                ApplicationType = ApplicationType.Client,
                ApplicationConfiguration = _appConfig
            };
            Utils.SetTraceMask(0);
            application.CheckApplicationInstanceCertificate(true, 2048).GetAwaiter().GetResult();

            var endpointDescription = CoreClientUtils.SelectEndpoint(_appConfig, $@"opc.tcp://{ipAddress}:{port}", false);
            var endpointConfig = EndpointConfiguration.Create(_appConfig);
            _endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfig);
            
        }

        public void Connect(uint timeOut = 5)
        {
            this.Disconnect();

            this._session =
                Task.Run(
                    async () => await Session.Create(_appConfig, _endpoint, false, _appConfig.ApplicationName,
                        timeOut * 1000, _userIdentity, null)).GetAwaiter().GetResult();
            
            this._session.KeepAlive += this.KeepAlive;

            if (this._session == null || !this._session.Connected)
            {
                throw new ServerException("Error creating a session on the server");
            }

            OnConnected(EventArgs.Empty);
            CreateSubscription();
        }

        private void CreateSubscription()
        {
            Subscription subscription = new Subscription(_session.DefaultSubscription)
            {
                DisplayName = "Console ReferenceClient Subscription",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                LifetimeCount = 0,
            };

            _session.AddSubscription(subscription);

            // Create the subscription on Server side
            subscription.Create();
            log.Debug($"New Subscription created with SubscriptionId = {subscription.Id}.");

            MonitoredItem serverStatus = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = new NodeId("ns=0;i=2258"),
                AttributeId = Attributes.Value,
                DisplayName = "Server Status",
                SamplingInterval = 1000,
                QueueSize = 10,
                DiscardOldest = true,
            };
            serverStatus.Notification += ServerStatus_Notification;

            subscription.AddItem(serverStatus);
            subscription.ApplyChanges();
        }

        private void ServerStatus_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            MonitoredItemNotification notification = e.NotificationValue as MonitoredItemNotification;
            //log.Debug($"Current time is now {notification.Value}");
        }

        private void KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            try
            {
                if (!ServiceResult.IsBad(e.Status)) return;
                
                log.Error($"Disconnected from OpcUa Server {e.Status} {session.OutstandingRequestCount}");
                OnDisconnected(EventArgs.Empty);
                lock (this._lock)
                {
                    if (this._reconnectHandler != null) return;
                    this._reconnectHandler = new SessionReconnectHandler(true);
                    this._reconnectHandler.BeginReconnect(this._session, ReconnectPeriod, this.Reconnect);
                }
            }
            catch (Exception ex)
            {
                // ignored
            }
        }

        private void Reconnect(object sender, EventArgs e)
        {
            if (!ReferenceEquals(sender, this._reconnectHandler))
            {
                return;
            }

            lock (this._lock)
            {
                if (this._reconnectHandler.Session != null)
                {
                    this._session = (Session) _reconnectHandler.Session;
                    log.Error("Reconnected from OpcUa Server");
                }

                this._reconnectHandler.Dispose();
                this._reconnectHandler = null;
            }

            OnConnected(EventArgs.Empty);

        }

        public void Write<T>(PLCVariable variable, T value)
        {
            WriteValueCollection writeValues = new WriteValueCollection();

            var writeValue = new WriteValue
            {
                NodeId = new NodeId(variable.Name, variable.NameSpaceIndex),
                AttributeId = Attributes.Value,
                Value = new DataValue 
                {
                    Value = value
                }
            };
            log.Debug($@"Writing {variable.Name} namespace {variable.NameSpaceIndex} value = {value}");

            writeValues.Add(writeValue);
            this._session.Write(null, writeValues, out StatusCodeCollection statusCodes,
                out DiagnosticInfoCollection diagnosticInfo);
            if (!StatusCode.IsGood(statusCodes[0]))
            {
                throw new Exception("Error writing value. Code: 0x" + statusCodes[0].Code.ToString("X8"));
            }
        }

        private void Disconnect()
        {
            if (_session == null) return;
            if (_session.Connected)
            {
                if (this._session.Subscriptions != null && this._session.Subscriptions.Any())
                {
                    foreach (var subscription in this._session.Subscriptions)
                    {
                        subscription.Delete(true);
                    }
                }

                this._session.Close();
                this._session.Dispose();
                this._session = null;
            }

            OnDisconnected(EventArgs.Empty);

        }

    }
}

namespace Stwalkerster.IrcClient
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Security;
    using System.Text;
    using System.Threading;
    using Microsoft.Extensions.Logging;
    using Network;
    using Prometheus;
    using Stwalkerster.IrcClient.Events;
    using Stwalkerster.IrcClient.Exceptions;
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Messages;
    using Stwalkerster.IrcClient.Model;
    using Stwalkerster.IrcClient.Model.Interfaces;

    /// <summary>
    /// The IRC client.
    /// </summary>
    public class IrcClient : IIrcClient, IDisposable
    {
        private const string PingPrefix = "GNU Terry Pratchett ";

        private static readonly Counter PrivateMessagesReceived = Metrics.CreateCounter(
            "ircclient_privmsg_received_total",
            "Number of PRIVMSG messages received",
            new CounterConfiguration
            {
                LabelNames = new[] {"client"}
            });

        private static readonly Gauge ChannelsJoined = Metrics.CreateGauge(
            "ircclient_channels_joined",
            "Current number of channels joined",
            new GaugeConfiguration
            {
                LabelNames = new[] {"client"}
            });     
        
        private static readonly Gauge UsersKnown = Metrics.CreateGauge(
            "ircclient_users_known",
            "Current number of users known",
            new GaugeConfiguration
            {
                LabelNames = new[] {"client"}
            });
        
        private static readonly Gauge MyStatusFlags = Metrics.CreateGauge(
            "ircclient_my_statusflags",
            "The number of status flags I currently hold",
            new GaugeConfiguration
            {
                LabelNames = new[] {"client", "flag"}
            });

        private static readonly Gauge ChannelUsers = Metrics.CreateGauge(
            "ircclient_channel_users",
            "The number of users in a channel",
            new GaugeConfiguration
            {
                LabelNames = new[] {"client", "channel"}
            });
        
        private static readonly Gauge PingDuration = Metrics.CreateGauge(
            "ircclient_ping_duration_seconds",
            "Current ping duration to the server",
            new GaugeConfiguration
            {
                SuppressInitialValue = true,
                LabelNames = new[] {"client"}
            });
        
        private static readonly Counter PingsMissed = Metrics.CreateCounter(
            "ircclient_pings_missed_total",
            "Current ping duration to the server",
            new CounterConfiguration
            {
                LabelNames = new[] {"client"}
            });

        #region Fields

        /// <summary>
        /// Authenticate to services?
        /// </summary>
        private readonly bool authToServices;

        /// <summary>
        /// The channels.
        /// </summary>
        private readonly Dictionary<string, IrcChannel> channels;

        /// <summary>
        /// The client's possible capabilities.
        /// </summary>
        private readonly List<string> clientCapabilities;
        private readonly List<string> serverCapabilities = new List<string>();

        /// <summary>
        /// The connection registration semaphore.
        /// </summary>
        private readonly Semaphore connectionRegistrationSemaphore;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger<IrcClient> logger;

        /// <summary>
        /// The network client.
        /// </summary>
        private INetworkClient networkClient;

        /// <summary>
        /// The password.
        /// </summary>
        private readonly string serverPassword;

        /// <summary>
        /// The support helper.
        /// </summary>
        private readonly ISupportHelper supportHelper;

        /// <summary>
        /// The real name.
        /// </summary>
        private readonly string realName;

        /// <summary>
        /// The user cache.
        /// </summary>
        private readonly Dictionary<string, IrcUser> userCache;

        /// <summary>
        /// The lock object for operations on the user/channel lists.
        /// </summary>
        private readonly object userOperationLock = new object();

        /// <summary>
        /// The username.
        /// </summary>
        private readonly string username;

        /// <summary>
        /// The prefixes.
        /// </summary>
        private readonly IDictionary<string, string> prefixes = new Dictionary<string, string>();

        /// <summary>
        /// The destination flags.
        /// </summary>
        private readonly IList<string> destinationFlags = new List<string>();

        private bool capExtendedJoin;
        private bool capSasl;
        private bool capMultiPrefix;
        private bool capChghost;
        private bool capAccountNotify;
        private bool capCapNotify;
        private bool capAccountTag;
        private bool capAwayNotify;

        /// <summary>
        /// The data interception function.
        /// </summary>
        private bool connectionRegistered;

        /// <summary>
        /// The nick tracking valid.
        /// </summary>
        private bool nickTrackingValid = true;

        /// <summary>
        /// The nickname.
        /// </summary>
        private string nickname;
        
        private string intendedNickname;

        /// <summary>
        /// The server prefix.
        /// </summary>
        private string serverPrefix;

        /// <summary>
        /// Is the client logged in to a nickserv account?
        /// </summary>
        private bool servicesLoggedIn;

        private bool pingThreadAlive;
        private string expectedPingMessage;
        private readonly AutoResetEvent pingReplyEvent;
        private readonly Thread pingThread;
        private readonly AutoResetEvent pingThreadTimerWait = new AutoResetEvent(false);
        
        private int lagTimer;
        private readonly bool restartOnHeavyLag;
        private readonly bool reclaimNickFromServices;
        
        private readonly string servicesUsername;
        private readonly string servicesPassword;
        private string servicesCert;
        private readonly int missedPingLimit;
        private readonly string onConnectModes;

        #endregion

        #region Constructors and Destructors

        private void Setup(INetworkClient client)
        {
            this.networkClient = client;
            this.networkClient.DataReceived += this.NetworkClientOnDataReceived;
            this.networkClient.Disconnected += this.NetworkClientDisconnected;
            this.networkClient.Connect();

            if (!this.authToServices)
            {
                this.logger.LogWarning("Services authentication is disabled!");

                this.clientCapabilities.Remove("sasl");
            }
            
            this.RegisterConnection(null);
        }

        public IrcClient(ILoggerFactory loggerFactory, IIrcConfiguration configuration, ISupportHelper supportHelper)
            : this(NetworkClientFactory.Create(configuration, loggerFactory), loggerFactory.CreateLogger<IrcClient>(), configuration, supportHelper)
        {
        }

        public IrcClient(INetworkClient client, ILogger<IrcClient> logger, IIrcConfiguration configuration, ISupportHelper supportHelper)
        {
            this.nickname = configuration.Nickname;
            this.intendedNickname = configuration.Nickname;
            this.username = configuration.Username;
            this.realName = configuration.RealName;
            this.serverPassword = configuration.ServerPassword;
            
            this.authToServices = configuration.AuthToServices;
            this.servicesUsername = configuration.ServicesUsername;
            this.servicesPassword = configuration.ServicesPassword;
            this.servicesCert = client is SslNetworkClient && configuration.AuthToServices ? configuration.ServicesCertificate : null;
            
            this.restartOnHeavyLag = configuration.RestartOnHeavyLag;
            this.reclaimNickFromServices = configuration.ReclaimNickFromServices;

            this.onConnectModes = configuration.ConnectModes;

            this.supportHelper = supportHelper;
            this.ClientName = configuration.ClientName;
            this.logger = logger;

            this.ReceivedIrcMessage += this.OnIrcMessageReceivedIrcEvent;

            this.clientCapabilities = new List<string>
            {
                "sasl", "account-notify", "extended-join", "multi-prefix", "chghost", "cap-notify", "account-tag",
                "away-notify"
            };
            
            this.userCache = new Dictionary<string, IrcUser>();
            this.channels = new Dictionary<string, IrcChannel>();
            
            this.pingReplyEvent = new AutoResetEvent(false);
            this.pingThread = new Thread(this.PingThreadWorker);
            this.PingInterval = configuration.PingInterval;
            this.PingTimeout = configuration.PingInterval;
            this.missedPingLimit = configuration.MissedPingLimit;

            this.connectionRegistrationSemaphore = new Semaphore(0, 1);

            this.Setup(client);
        }

        #endregion

        #region Public Events

        /// <summary>
        /// The invite received event.
        /// </summary>
        public event EventHandler<InviteEventArgs> InviteReceivedEvent;

        /// <summary>
        /// The join received event.
        /// </summary>
        public event EventHandler<JoinEventArgs> JoinReceivedEvent;

        public event EventHandler<JoinEventArgs> PartReceivedEvent;
        public event EventHandler<QuitEventArgs> QuitReceivedEvent;
        public event EventHandler<KickEventArgs> KickReceivedEvent;

        /// <summary>
        /// The received message.
        /// </summary>
        public event EventHandler<IrcMessageReceivedEventArgs> ReceivedIrcMessage;

        public event EventHandler<MessageReceivedEventArgs> ReceivedMessage;

        public event EventHandler<KickedEventArgs> WasKickedEvent;

        public event EventHandler<ModeEventArgs> ModeReceivedEvent;

        public event EventHandler<NickEventArgs> NickReceivedEvent;

        public event EventHandler<ChannelUserModeEventArgs> ChannelUserModeEvent;

        public event EventHandler<EndOfWhoEventArgs> EndOfWhoEvent;

        /// <summary>
        /// Raised when the client disconnects from IRC.
        /// </summary>
        public event EventHandler DisconnectedEvent;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the channels.
        /// </summary>
        public Dictionary<string, IrcChannel> Channels
        {
            get { return this.channels; }
        }

        /// <summary>
        /// Gets a value indicating whether the nick tracking is valid.
        /// </summary>
        public bool NickTrackingValid
        {
            get { return this.nickTrackingValid; }
        }

        /// <summary>
        /// Gets or sets the nickname.
        /// </summary>
        public string Nickname
        {
            get { return this.nickname; }

            set
            {
                this.nickname = value;
                this.intendedNickname = value;
                this.Send(new Message("NICK", value));
            }
        }

        /// <summary>
        /// Gets a value indicating whether the client logged in to a nickserv account
        /// </summary>
        public bool ServicesLoggedIn
        {
            get { return this.servicesLoggedIn; }
        }

        /// <summary>
        /// Gets the user cache.
        /// </summary>
        public Dictionary<string, IrcUser> UserCache
        {
            get { return this.userCache; }
        }

        public bool NetworkConnected
        {
            get { return this.networkClient.Connected; }
        }

        public string ClientName { get; private set; }
        
        public string ExtBanDelimiter { get; private set; }
        public string ExtBanTypes { get; private set; }
        
        public int PingTimeout { get; set; }
        public int PingInterval { get; set; }

        public ReadOnlyCollection<string> ServerCapabilities => new ReadOnlyCollection<string>(this.serverCapabilities);

        /// <summary>
        /// The cap extended join.
        /// </summary>
        public bool CapExtendedJoin => this.capExtendedJoin;

        /// <summary>
        /// The SASL capability.
        /// </summary>
        public bool CapSasl => this.capSasl;

        public bool CapMultiPrefix => this.capMultiPrefix;

        public bool CapChghost => this.capChghost;

        public bool CapAccountNotify => this.capAccountNotify;
        public bool CapAccountTag => this.capAccountTag;

        public bool CapCapNotify => this.capCapNotify;
        public bool CapAwayNotify => this.capAwayNotify;

        public int PrivmsgReceived => (int)PrivateMessagesReceived.WithLabels(this.ClientName).Value;
        public double Latency => PingDuration.WithLabels(this.ClientName).Value;
        public IList<string> StatusMsgDestinationFlags => new List<string>(this.destinationFlags);
        
        public string[] ChannelModeTypes { get; private set; }
        
        #endregion

        #region Public Methods and Operators

        /// <summary>
        /// The dispose.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Don't use this.
        /// Injects a raw string into the network stream.
        /// Everything should use Send(IMessage) instead.
        /// </summary>
        /// <param name="message">
        /// The raw data to inject into the network stream
        /// </param>
        public void Inject(string message)
        {
            this.networkClient.Send(message);
        }

        /// <summary>
        /// Blocks until the connection is registered.
        /// </summary>
        public void WaitOnRegistration()
        {
            if (connectionRegistered)
            {
                return;
            }
            
            this.connectionRegistrationSemaphore.WaitOne();
            this.connectionRegistrationSemaphore.Release();
        }

        /// <summary>
        /// The join.
        /// </summary>
        /// <param name="channel">
        /// The channel.
        /// </param>
        public void JoinChannel(string channel)
        {
            if (channel == "0")
            {
                throw new SecurityException("Not allowed to part all with JOIN 0");
            }

            this.connectionRegistrationSemaphore.WaitOne();
            this.connectionRegistrationSemaphore.Release();

            // request to join
            this.Send(new Message("JOIN", channel));
        }

        /// <summary>
        /// The lookup user.
        /// </summary>
        /// <param name="prefix">
        /// The prefix.
        /// </param>
        /// <returns>
        /// The <see cref="IrcUser"/>.
        /// </returns>
        public IrcUser LookupUser(string prefix)
        {
            var parsedUser = IrcUser.FromPrefix(prefix, this);

            lock (this.userOperationLock)
            {
                // attempt to load from cache
                if (this.nickTrackingValid && this.userCache.ContainsKey(parsedUser.Nickname))
                {
                    parsedUser = this.userCache[parsedUser.Nickname];
                }
            }

            return parsedUser;
        }

        /// <summary>
        /// The part channel.
        /// </summary>
        /// <param name="channel">
        /// The channel.
        /// </param>
        /// <param name="message">
        /// The message.
        /// </param>
        public void PartChannel(string channel, string message)
        {
            // request to join
            this.Send(new Message("PART", new[] {channel, message}));
        }

        /// <summary>
        /// The send.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        public void Send(IMessage message)
        {
            try
            {
                this.networkClient.Send(message.ToString());
            }
            catch (Exception ex)
            {
                this.logger.LogError("Could not send message to network." + ex.Message);
            }
        }

        public void PrioritySend(IMessage message)
        {
            try
            {
                this.networkClient.PrioritySend(message.ToString());
            }
            catch (Exception ex)
            {
                this.logger.LogError("Could not send message to network." + ex.Message);
            }
        }

        /// <summary>
        /// The send message.
        /// </summary>
        /// <param name="destination">
        /// The destination.
        /// </param>
        /// <param name="message">
        /// The message.
        /// </param>
        /// <param name="destinationFlag">
        /// The destination flag.
        /// </param>
        /// <param name="priority">
        /// Whether to treat this as a priority message
        /// </param>
        public void SendMessage(string destination, string message, DestinationFlags destinationFlag, bool priority)
        {
            this.WaitOnRegistration();

            var computedDestination = destination;
            
            if (destinationFlag != null)
            {
                if (this.destinationFlags.Contains(destinationFlag.Flag))
                {
                    computedDestination = destinationFlag.Flag + computedDestination;
                }
                else
                {
                    throw new OperationNotSupportedException(
                        "Message send requested with destination flag, but destination flag is not supported by this server.");
                }
            }

            var builtMessage = new Message("PRIVMSG", new[] { computedDestination, message });
            if (priority)
            {
                this.PrioritySend(builtMessage);
            }
            else
            {
                this.Send(builtMessage);
            }
        }

        public void SendMessage(string destination, string message, DestinationFlags destinationFlag)
        {
            this.WaitOnRegistration();
            
            this.SendMessage(destination, message, destinationFlag, false);
        }
        
        /// <summary>
        /// The send message.
        /// </summary>
        /// <param name="destination">
        /// The destination.
        /// </param>
        /// <param name="message">
        /// The message.
        /// </param>
        public void SendMessage(string destination, string message)
        {
            this.WaitOnRegistration();
            
            this.SendMessage(destination, message, null, false);
        }

        /// <summary>
        /// The send notice.
        /// </summary>
        /// <param name="destination">
        /// The destination.
        /// </param>
        /// <param name="message">
        /// The message.
        /// </param>
        /// <param name="destinationFlag">
        /// The destination Flag.
        /// </param>
        public void SendNotice(string destination, string message, DestinationFlags destinationFlag)
        {
            var computedDestination = destination;
            
            if (destinationFlag != null)
            {
                if (this.destinationFlags.Contains(destinationFlag.Flag))
                {
                    computedDestination = destinationFlag.Flag + computedDestination;
                }
                else
                {
                    throw new OperationNotSupportedException(
                        "Message send requested with destination flag, but destination flag is not supported by this server.");
                }
            }

            this.Send(new Message("NOTICE", new[] { computedDestination, message }));
        }

        public void SendNotice(string destination, string message)
        {
            this.SendNotice(destination, message, null);
        }
        
        public void Mode(string target, string changes)
        {
            this.networkClient.Send(string.Format("MODE {0} {1}", target, changes));
        }

        #endregion

        #region Methods

        /// <summary>
        /// The dispose.
        /// </summary>
        /// <param name="disposing">
        /// The disposing.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.networkClient.Disconnect();
                this.networkClient.Dispose();
                ((IDisposable) this.connectionRegistrationSemaphore).Dispose();
            }
        }
        
        private void HandleWhoXReply(IMessage message)
        {
            try
            {
                if (message.Command != Numerics.WhoXReply)
                {
                    throw new ArgumentException("Expected WHOX reply message", "message");
                }

                var parameters = message.Parameters.ToList();
                if (parameters.Count != 8)
                {
                    throw new ArgumentException("Expected 8 WHOX parameters.", "message");
                }

                /* >> :holmes.freenode.net 354 stwalkerster 001 #wikipedia-en-accounts ChanServ services.           ChanServ       H@  0
                 * >> :holmes.freenode.net 354 stwalkerster 001 #wikipedia-en-accounts ~jamesur wikimedia/Jamesofur Jamesofur|away G  jamesofur
                 *                             .            t   c                      u        h                   n              f  a
                 *     prefix              cmd    0         1   2                      3        4                   5              6  7
                 */
                var channel = parameters[2];
                var user = parameters[3];
                var host = parameters[4];
                var nick = parameters[5];
                var flags = parameters[6];
                var away = flags[0] == 'G'; // H (here) / G (gone)
                var modes = flags.Substring(1);
                var account = parameters[7];

                lock (this.userOperationLock)
                {
                    var ircUser = new IrcUser(this);
                    if (this.userCache.ContainsKey(nick))
                    {
                        ircUser = this.userCache[nick];
                    }
                    else
                    {
                        ircUser.Nickname = nick;
                        ircUser.SkeletonStatus = IrcUserSkeletonStatus.NickOnly;
                        this.userCache.Add(nick, ircUser);
                        UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
                    }

                    ircUser.Account = account;
                    ircUser.Username = user;
                    ircUser.Hostname = host;
                    ircUser.Away = away;
                    ircUser.SkeletonStatus = IrcUserSkeletonStatus.Full;

                    if (this.channels[channel].Users.ContainsKey(ircUser.Nickname))
                    {
                        var channelUser = this.channels[channel].Users[ircUser.Nickname];
                        channelUser.Operator = modes.Contains("@") && this.prefixes.Values.Contains("@");
                        channelUser.Voice = modes.Contains("+") && this.prefixes.Values.Contains("+");
                    }
                    else
                    {
                        var channelUser = new IrcChannelUser(ircUser, channel)
                        {
                            Operator = modes.Contains("@") && this.prefixes.Values.Contains("@"),
                            Voice = modes.Contains("+") && this.prefixes.Values.Contains("+")
                        };

                        this.channels[channel].Users.Add(ircUser.Nickname, channelUser);
                        ChannelUsers.WithLabels(this.ClientName, channel).Set(this.channels[channel].Users.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                this.nickTrackingValid = false;
                this.logger.LogError(ex, "Nick tracking for authentication is no longer valid");
                throw;
            }
        }
        
        private void HandleEndOfWhoXReply(IMessage message)
        {
            // nick, channel, comment
            var messageParameters = message.Parameters.ToList();
            var channel = messageParameters[1];
            
            this.logger.LogDebug(message: "End of /WHOX for {Channel}", channel);
            
            var whoEvent = this.EndOfWhoEvent;
            if (whoEvent != null)
            {
                whoEvent(this, new EndOfWhoEventArgs(message, channel, this));
            }
        }

        private void NetworkClientDisconnected(object sender, EventArgs e)
        {
            this.Disconnect("Network client connection lost.");
        }
        
        private void NetworkClientOnDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            var message = Message.Parse(dataReceivedEventArgs.Data);

            if (message.Command == "ERROR")
            {
                var errorMessage = message.Parameters.First();
                this.Disconnect(errorMessage);
            }

            if (message.Command == "PING")
            {
                this.Send(new Message("PONG", message.Parameters));
            }

            if (message.Command == "PONG")
            {
                if (message.Parameters.Contains(this.expectedPingMessage))
                {
                    var endTime = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds;
                    var pingMessage = message.Parameters.Last().Substring(PingPrefix.Length);
                    if (double.TryParse(pingMessage, out var beginTime))
                    {
                        PingDuration.WithLabels(this.ClientName).Set((endTime - beginTime) / 1000);
                    }
                    
                    this.pingReplyEvent.Set();
                }
            }

            if (this.HandleCapabilityNegotiation(message))
            {
                return;
            }

            if (this.connectionRegistered)
            {
                this.RaiseDataEvent(message);
            }
            else
            {
                this.RegisterConnection(message);
            }
        }

        void Disconnect(string errorMessage)
        {
            this.pingThreadAlive = false;
            this.pingThreadTimerWait.Set();

            this.logger.LogError("Disconnecting: {DisconnectReason}", errorMessage);
            this.networkClient.Disconnect();

            // Invoke the disconnected event
            var temp = this.DisconnectedEvent;
            if (temp != null)
            {
                temp(this, EventArgs.Empty);
            }
        }

        protected virtual void OnBotKickedEvent(KickedEventArgs e)
        {
            var handler = this.WasKickedEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        /// <summary>
        /// The on account message received.
        /// </summary>
        /// <param name="e">
        /// The e.
        /// </param>
        /// <param name="user">
        /// The user.
        /// </param>
        private void OnAccountMessageReceived(IrcMessageReceivedEventArgs e, IUser user)
        {
            var parameters = e.Message.Parameters.ToList();

            lock (this.userOperationLock)
            {
                this.logger.LogDebug("Seen {User} change account name to {Parameters}", user, parameters[0]);
                if (this.userCache.ContainsKey(user.Nickname))
                {
                    var cachedUser = this.userCache[user.Nickname];
                    cachedUser.Account = parameters[0];

                    // flesh out the skeleton
                    if (cachedUser.SkeletonStatus < ((IrcUser) user).SkeletonStatus)
                    {
                        cachedUser.Username = user.Username;
                        cachedUser.Hostname = user.Hostname;
                        
                        cachedUser.SkeletonStatus = IrcUserSkeletonStatus.Account;
                    }
                }
                else
                {
                    this.userCache.Add(user.Nickname, (IrcUser) user);
                    UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
                    user.Account = parameters[0];
                }
            }
        }

        private void OnChannelModeReceived(List<string> parameters, IUser actingUser)
        {
            // Channel Mode message
            var channel = parameters[0];
            var modechange = parameters[1];

            var addMode = true;
            var position = 2;

            foreach (var c in modechange)
            {
                if (c == '-')
                {
                    addMode = false;
                }

                if (c == '+')
                {
                    addMode = true;
                }

                if (this.prefixes.ContainsKey(c.ToString()))
                {
                    var nick = parameters[position];

                    IrcChannelUser channelUser;
                    lock (this.userOperationLock)
                    {
                        channelUser = this.channels[channel].Users[nick];

                        this.logger.LogInformation("Seen {AddMode}{Mode} on {User} by {ActingUser}", addMode ? "+" : "-", c, channelUser, actingUser);

                        if (addMode)
                        {
                            channelUser.SetPrefix(c.ToString(), this.prefixes[c.ToString()]);
                        }
                        else
                        {
                            channelUser.RemovePrefix(c.ToString());
                        }

                        position++;
                    }

                    var channelUserModeEvent = this.ChannelUserModeEvent;
                    if (channelUserModeEvent != null)
                    {
                        channelUserModeEvent(this, new ChannelUserModeEventArgs(channelUser.User, channel, c.ToString(), addMode, actingUser, this));
                    }
                }

                // type a + b must always have a parameter
                if (this.ChannelModeTypes[0].Contains(c) || this.ChannelModeTypes[1].Contains(c))
                {
                    position++;
                }
                
                // type c only has parameter on set.
                if (this.ChannelModeTypes[2].Contains(c) && addMode)
                {
                    position++;
                }
                
                // type d never has a parameter
            }
            
            this.SyncStatusFlagsMetrics();
        }

        private void SyncStatusFlagsMetrics()
        {
            try
            {
                MyStatusFlags.WithLabels(this.ClientName, "v")
                    .Set(this.channels.Aggregate(0, (t, c) => t + (c.Value.Users[this.nickname].Voice ? 1 : 0)));
                MyStatusFlags.WithLabels(this.ClientName, "o")
                    .Set(this.channels.Aggregate(0, (t, c) => t + (c.Value.Users[this.nickname].Operator ? 1 : 0)));
            }
            catch (KeyNotFoundException ex)
            {
                this.logger.LogDebug(ex, "Unable to update metrics due to missing username in channel info - are we joining an empty channel?");
            }
        }

        /// <summary>
        /// The on join received.
        /// </summary>
        /// <param name="e">
        /// The e.
        /// </param>
        /// <param name="eventUser">
        /// The user.
        /// </param>
        private void OnJoinReceived(IrcMessageReceivedEventArgs e, IUser eventUser)
        {
            // this is a client join to a channel.
            // :stwalkerster!stwalkerst@wikimedia/stwalkerster JOIN ##stwalkerster
            var parametersList = e.Message.Parameters.ToList();

            var user = eventUser as IrcUser;
            if (user == null)
            {
                throw new ArgumentException("Unsupported user type");
            }
                       
            lock (this.userOperationLock)
            {
                if (this.userCache.ContainsKey(user.Nickname))
                {
                    var cachedUser = this.userCache[user.Nickname];

                    // flesh out the skeleton
                    if (cachedUser.SkeletonStatus < IrcUserSkeletonStatus.PrefixOnly)
                    {
                        cachedUser.Hostname = user.Hostname;
                        cachedUser.Username = user.Username;
                        cachedUser.SkeletonStatus = IrcUserSkeletonStatus.PrefixOnly;
                    }

                    user = cachedUser;
                }
                else
                {
                    this.userCache.Add(user.Nickname, user);
                    UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
                }
            }

            if (this.capExtendedJoin)
            {
                // :stwalkerster!stwalkerst@wikimedia/stwalkerster JOIN ##stwalkerster accountname :realname
                if (user.SkeletonStatus < IrcUserSkeletonStatus.Account)
                {
                    user.Account = parametersList[1];
                    user.SkeletonStatus = IrcUserSkeletonStatus.Account;
                }
            }
            
            if (this.capAccountTag && e.Message.Tags.ContainsKey("account"))
            {
                // @account=stwalkerster :stwalkerster!stwalkerst@wikimedia/stwalkerster JOIN ##stwalkerster
                if (user.SkeletonStatus < IrcUserSkeletonStatus.Account)
                {
                    user.Account = e.Message.Tags["account"];
                    user.SkeletonStatus = IrcUserSkeletonStatus.Account;
                }
            }

            var channelName = parametersList[0];
            if (user.Nickname == this.Nickname)
            {
                // we're joining this, so rate-limit from here.
                this.logger.LogInformation("Joining channel {Channel}", channelName);
                this.logger.LogDebug("Requesting WHOX a information for {Channel}", channelName);
                this.Send(new Message("WHO", new[] {channelName, "%uhnatfc,001"}));

                this.logger.LogDebug("Requesting MODE information for {Channel}", channelName);
                this.Send(new Message("MODE", new[] {channelName}));

                lock (this.userOperationLock)
                {
                    // add the channel to the list of channels I'm in.
                    this.logger.LogDebug("Adding {Channel} to the list of channels I'm in", channelName);
                    this.Channels.Add(channelName, new IrcChannel(channelName));
                    ChannelsJoined.WithLabels(this.ClientName).Set(this.channels.Count);
                }
            }
            else
            {
                this.logger.LogInformation("Seen {User} join channel {Channel}", user, channelName);

                lock (this.userOperationLock)
                {
                    if (!this.Channels[channelName].Users.ContainsKey(user.Nickname))
                    {
                        this.logger.LogDebug("Adding user {User} to the list of users in channel {Channel}", user, channelName);
                        this.Channels[channelName]
                            .Users.Add(
                                user.Nickname,
                                new IrcChannelUser(user, channelName));
                        ChannelUsers.WithLabels(this.ClientName, channelName).Set(this.channels[channelName].Users.Count);
                    }
                    else
                    {
                        this.logger.LogError("Nickname tracking no longer valid");
                        this.nickTrackingValid = false;
                    }
                }
            }

            var temp = this.JoinReceivedEvent;
            if (temp != null)
            {
                temp(this, new JoinEventArgs(e.Message, user, channelName, this));
            }
        }

        /// <summary>
        /// The on message received event.
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="e">
        /// The e.
        /// </param>
        private void OnIrcMessageReceivedIrcEvent(object sender, IrcMessageReceivedEventArgs e)
        {
            IUser user = null;
            if (e.Message.Prefix != null)
            {
                if (e.Message.Prefix == this.serverPrefix)
                {
                    user = new ServerUser();
                }
                else
                {
                    // parse it into something reasonable
                    user = IrcUser.FromPrefix(e.Message.Prefix, this);

                    lock (this.userOperationLock)
                    {
                        // attempt to load from cache
                        if (this.nickTrackingValid && this.userCache.ContainsKey(user.Nickname))
                        {
                            var cachedUser = this.userCache[user.Nickname];

                            if (cachedUser.SkeletonStatus < IrcUserSkeletonStatus.PrefixOnly 
                                && user.Username != null && user.Hostname != null)
                            {
                                cachedUser.Username = user.Username;
                                cachedUser.Hostname = user.Hostname;
                                cachedUser.SkeletonStatus = IrcUserSkeletonStatus.PrefixOnly;
                            }

                            if (this.capAccountTag && e.Message.Tags.ContainsKey("account"))
                            {
                                cachedUser.Account = e.Message.Tags["account"];
                                cachedUser.SkeletonStatus =
                                    cachedUser.SkeletonStatus == IrcUserSkeletonStatus.PrefixOnly
                                        ? IrcUserSkeletonStatus.Account
                                        : cachedUser.SkeletonStatus;
                            }

                            user = cachedUser;
                        }
                    }
                }
            }

            if (e.Message.Command == "PRIVMSG" || e.Message.Command == "NOTICE")
            {
                var messageReceivedEvent = this.ReceivedMessage;
                if (messageReceivedEvent != null)
                {
                    var newEvent = new MessageReceivedEventArgs(
                        user,
                        e.Message.Parameters.First(),
                        e.Message.Parameters.Skip(1).First(),
                        e.Message.Command == "NOTICE",
                        e.Client
                    );
                    
                    PrivateMessagesReceived.WithLabels(this.ClientName).Inc();
                    messageReceivedEvent(this, newEvent);
                }
            }
            
            if ((e.Message.Command == "JOIN") && (user != null))
            {
                this.OnJoinReceived(e, user);
            }

            if (e.Message.Command == Numerics.NameReply)
            {
                this.OnNameReplyReceived(e);
            }

            if (e.Message.Command == Numerics.ISupport)
            {
                this.OnISupportMessageReceived(e);
            }

            if (e.Message.Command == Numerics.WhoXReply)
            {
                this.logger.LogDebug("WHOX Reply:{WhoX}", string.Join(" ", e.Message.Parameters));
                this.HandleWhoXReply(e.Message);
            }

            if (e.Message.Command == Numerics.EndOfWho)
            {
                this.HandleEndOfWhoXReply(e.Message);
            }

            if ((e.Message.Command == "QUIT") && (user != null))
            {
                this.OnQuitMessageReceived(e, user);
            }

            if ((e.Message.Command == "MODE") && (user != null))
            {
                var parameters = e.Message.Parameters.ToList();
                var target = parameters[0];
                if (target.StartsWith("#"))
                {
                    this.logger.LogDebug("Received channel mode message");
                    this.OnChannelModeReceived(parameters, user);
                }
                else
                {
                    // User mode message
                    this.logger.LogDebug("Received user mode message - not processing");
                }

                var modeEvent = this.ModeReceivedEvent;
                if (modeEvent != null)
                {
                    modeEvent(this, new ModeEventArgs(e.Message, user, parameters[0], parameters.Skip(1).ToList(), this));
                }
            }

            if ((e.Message.Command == Numerics.CurrentChannelMode) && (user != null))
            {
                var parameters = e.Message.Parameters.Skip(1).ToList();

                this.OnChannelModeReceived(parameters, user);

                var modeEvent = this.ModeReceivedEvent;
                if (modeEvent != null)
                {
                    modeEvent(this, new ModeEventArgs(e.Message, user, parameters[0], parameters.Skip(1).ToList(), this));
                }
            }

            if ((e.Message.Command == "PART") && (user != null))
            {
                this.OnPartMessageReceived(e, user);
            }

            if (e.Message.Command == "KICK")
            {
                this.OnKickMessageReceived(e, user);
            }

            if ((e.Message.Command == "ACCOUNT") && (user != null))
            {
                this.OnAccountMessageReceived(e, user);
            }
            
            if ((e.Message.Command == "CHGHOST") && (user != null))
            {
                this.OnChghostMessageReceived(e, user);
            }
            
            if ((e.Message.Command == "NICK") && (user != null))
            {
                this.OnNickChangeReceived(e, user);
            }

            if (e.Message.Command == "INVITE")
            {
                var inviteReceivedEvent = this.InviteReceivedEvent;
                if (inviteReceivedEvent != null)
                {
                    var parameters = e.Message.Parameters.ToList();
                    inviteReceivedEvent(this, new InviteEventArgs(e.Message, user, parameters[1], parameters[0], this));
                }
            }

            if (this.capAwayNotify && e.Message.Command == "AWAY")
            {
                var ircuser = (user as IrcUser);
                if (ircuser != null)
                {
                    ircuser.Away = e.Message.Parameters.Any();
                }
            }
        }

        private void OnChghostMessageReceived(IrcMessageReceivedEventArgs e, IUser user)
        {
            var parameters = e.Message.Parameters.ToList();
            var newUser = parameters[0];
            var newHost = parameters[1];
            
            var oldNickname = user.Nickname;
            
            this.logger.LogInformation("Changing user/host of {OldNick} to {NewUser}@{NewHost} in nick tracking database", oldNickname, newUser, newHost);

            try
            {
                lock (this.userOperationLock)
                {
                    // update the user cache.
                    var ircUser = this.userCache[oldNickname];

                    ircUser.Username = newUser;
                    ircUser.Hostname = newHost;
                    
                    // flesh out the skeleton
                    if (ircUser.SkeletonStatus < IrcUserSkeletonStatus.PrefixOnly)
                    {
                        ircUser.SkeletonStatus = IrcUserSkeletonStatus.PrefixOnly;
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Nickname tracking is no longer valid");
                this.nickTrackingValid = false;
            }
        }

        /// <summary>
        /// To be finished
        /// </summary>
        /// <remarks>
        /// Remove code analysis suppression when finished implementation
        /// </remarks>
        /// <param name="e"></param>
        [SuppressMessage("ReSharper", "UnusedVariable")]
        private void OnISupportMessageReceived(IrcMessageReceivedEventArgs e)
        {
            // User modes in channels
            var prefixMessage = e.Message.Parameters.FirstOrDefault(x => x.StartsWith("PREFIX="));
            if (prefixMessage != null)
            {
                this.supportHelper.HandlePrefixMessageSupport(prefixMessage, this.prefixes);
            }

            // status message for voiced/opped users only
            var statusMessage = e.Message.Parameters.FirstOrDefault(x => x.StartsWith("STATUSMSG="));
            if (statusMessage != null)
            {
                this.supportHelper.HandleStatusMessageSupport(statusMessage, this.destinationFlags);
            }

            var extbans = e.Message.Parameters.FirstOrDefault(x => x.StartsWith("EXTBAN="));
            if (extbans != null)
            {
                var extBanData = extbans.Split('=')[1].Split(',');
                this.ExtBanDelimiter = extBanData[0];
                this.ExtBanTypes = extBanData[1];
            }

            // TODO: finish me
            
            // Channel mode types
            var channelModes = e.Message.Parameters.FirstOrDefault(x => x.StartsWith("CHANMODES="));
            if (channelModes != null)
            {
                this.ChannelModeTypes = channelModes.Split('=')[1].Split(',');
            }
            
            // Max mode changes in one command
            var modeLimit = e.Message.Parameters.FirstOrDefault(x => x.StartsWith("MODES="));

            // Channel type prefixes
            var channelTypes = e.Message.Parameters.FirstOrDefault(x => x.StartsWith("CHANTYPES="));

            // max channels:
            var chanLimit = e.Message.Parameters.FirstOrDefault(x => x.StartsWith("CHANLIMIT="));

            // whox
        }

        /// <summary>
        /// The on name reply received.
        /// </summary>
        /// <param name="e">
        /// The e.
        /// </param>
        private void OnNameReplyReceived(IrcMessageReceivedEventArgs e)
        {
            var parameters = e.Message.Parameters.ToList();

            var channel = parameters[2];
            var names = parameters[3];

            this.logger.LogDebug("Names on {Channel}: {Names}", channel, names);

            foreach (var name in names.Split(' '))
            {
                var parsedName = name;
                var voice = false;
                var op = false;
                
                if (parsedName.StartsWith("@"))
                {
                    parsedName = parsedName.Substring(1);
                    op = true;
                }
                
                if (parsedName.StartsWith("+"))
                {
                    parsedName = parsedName.Substring(1);
                    voice = true;
                }

                lock (this.userOperationLock)
                {
                    if (this.channels[channel].Users.ContainsKey(parsedName))
                    {
                        var channelUser = this.channels[channel].Users[parsedName];
                        channelUser.Operator = op;
                        channelUser.Voice = voice;
                    }
                    else
                    {
                        var ircUser = new IrcUser(this) { Nickname = parsedName, SkeletonStatus = IrcUserSkeletonStatus.NickOnly };
                        if (this.userCache.ContainsKey(parsedName))
                        {
                            ircUser = this.userCache[parsedName];
                        }
                        else
                        {
                            this.userCache.Add(parsedName, ircUser);
                            UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
                        }

                        var channelUser = new IrcChannelUser(ircUser, channel) { Voice = voice, Operator = op };

                        this.channels[channel].Users.Add(parsedName, channelUser);
                        ChannelUsers.WithLabels(this.ClientName, channel).Set(this.channels[channel].Users.Count);
                    }
                }
            }
        }

        /// <summary>
        /// The on nick change received.
        /// </summary>
        /// <param name="e">
        /// The e.
        /// </param>
        /// <param name="user">
        /// The user.
        /// </param>
        private void OnNickChangeReceived(IrcMessageReceivedEventArgs e, IUser user)
        {
            var parameters = e.Message.Parameters.ToList();
            var newNickname = parameters[0];
            var oldNickname = user.Nickname;

            if (this.nickname == oldNickname)
            {
                this.logger.LogInformation("Updating my nickname from {OldNick} to {NewNick}", oldNickname, newNickname);
                this.nickname = newNickname;
            }
            
            this.logger.LogInformation("Changing {OldNick} to {NewNick} in nick tracking database", oldNickname, newNickname);

            try
            {
                lock (this.userOperationLock)
                {
                    // firstly, update the user cache.
                    var ircUser = this.userCache[oldNickname];
                    ircUser.Nickname = newNickname;

                    // flesh out the skeleton
                    if (ircUser.SkeletonStatus < IrcUserSkeletonStatus.PrefixOnly)
                    {
                        ircUser.Username = user.Username;
                        ircUser.Hostname = user.Hostname;
                        ircUser.SkeletonStatus = IrcUserSkeletonStatus.PrefixOnly;
                    }
                    
                    if (this.capAccountTag && e.Message.Tags.ContainsKey("account"))
                    {
                        if (ircUser.SkeletonStatus < IrcUserSkeletonStatus.Account)
                        {
                            user.Account = e.Message.Tags["account"];
                            ircUser.SkeletonStatus = IrcUserSkeletonStatus.Account;
                        }
                    }

                    try
                    {
                        this.userCache.Remove(oldNickname);
                        this.userCache.Add(newNickname, ircUser);
                        
                        UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
                    }
                    catch (ArgumentException)
                    {
                        this.logger.LogWarning(
                            "Couldn't add the new entry to the dictionary - nick tracking is no longer valid");
                        this.nickTrackingValid = false;
                        throw;
                    }

                    // secondly, update the channels this user is in.
                    foreach (var channelPair in this.channels)
                    {
                        if (channelPair.Value.Users.ContainsKey(oldNickname))
                        {
                            var channelUser = channelPair.Value.Users[oldNickname];

                            if (!channelUser.User.Equals(ircUser))
                            {
                                this.logger.LogError(
                                    "Channel user {ChannelUser} doesn't match irc user {IrcUser} for NICK in {Channel} - ick tracking is no longer valid",
                                    channelUser.User,
                                    ircUser,
                                    channelPair.Value.Name);

                                this.nickTrackingValid = false;

                                throw new Exception("Channel user doesn't match irc user");
                            }

                            try
                            {
                                channelPair.Value.Users.Remove(oldNickname);
                                channelPair.Value.Users.Add(newNickname, channelUser);
                            }
                            catch (ArgumentException)
                            {
                                this.logger.LogWarning(
                                    "Couldn't add the new entry to the dictionary - nick tracking is no longer valid");
                                this.nickTrackingValid = false;
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                this.logger.LogError(exception, "Nick tracking is no longer valid");
                this.nickTrackingValid = false;
            }

            var nickEvent = this.NickReceivedEvent;
            if (nickEvent != null)
            {
                nickEvent(this, new NickEventArgs(e.Message, user, oldNickname, this));
            }
        }

        /// <summary>
        /// The on part message received.
        /// </summary>
        /// <param name="e">
        /// The e.
        /// </param>
        /// <param name="user">
        /// The user.
        /// </param>
        private void OnPartMessageReceived(IrcMessageReceivedEventArgs e, IUser user)
        {
            var parameters = e.Message.Parameters.ToList();
            var channel = parameters[0];
            if (user.Nickname == this.Nickname)
            {
                this.logger.LogInformation("{Nickname} Leaving channel {Channel}", user, channel);

                lock (this.userOperationLock)
                {
                    var channelUsers = this.channels[channel].Users.Select(x => x.Key);
                    foreach (var u in channelUsers.Where(u => this.channels.Count(x => x.Value.Users.ContainsKey(u)) == 0))
                    {
                        this.logger.LogDebug("{User} is no longer in any channel I'm in, removing them from tracking", u);
                        this.userCache.Remove(u);
                    }
                    
                    ChannelUsers.WithLabels(this.ClientName, channel).Unpublish();
                    UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
                    this.channels.Remove(channel);
                    ChannelsJoined.WithLabels(this.ClientName).Set(this.channels.Count);
                    
                    this.SyncStatusFlagsMetrics();
                }
            }
            else
            {
                lock (this.userOperationLock)
                {
                    this.channels[channel].Users.Remove(user.Nickname);

                    this.logger.LogInformation("{User} has left channel {Channel}", user, channel);

                    if (this.channels.Count(x => x.Value.Users.ContainsKey(user.Nickname)) == 0)
                    {
                        this.logger.LogDebug("{User} has left all channels I'm in, removing them from tracking", user);
                        this.userCache.Remove(user.Nickname);
                        
                        UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
                    }
                    
                    ChannelUsers.WithLabels(this.ClientName, channel).Set(this.channels[channel].Users.Count);
                }
            }

            var onPartReceivedEvent = this.PartReceivedEvent;
            if (onPartReceivedEvent != null)
            {
                onPartReceivedEvent(this, new JoinEventArgs(e.Message, user, channel, this));
            }
        }

        /// <summary>
        /// The on kick message received.
        /// </summary>
        /// <param name="e">
        ///     The e.
        /// </param>
        /// <param name="user"></param>
        private void OnKickMessageReceived(IrcMessageReceivedEventArgs e, IUser user)
        {
            // Kick format is:
            // :n!u@h KICK #chan nick :reason
            var parameters = e.Message.Parameters.ToList();
            var channel = parameters[0];
            if (parameters[1] == this.Nickname)
            {
                this.logger.LogWarning("Kicked from channel {Channel}", channel);

                lock (this.userOperationLock)
                {
                    var channelUsers = this.channels[channel].Users.Select(x => x.Key);
                    var channelsWithUser = channelUsers.Where(u => this.channels.Count(x => x.Value.Users.ContainsKey(u)) == 0);
                    foreach (var u in channelsWithUser)
                    {
                        this.logger.LogDebug("{User} is no longer in any channel I'm in, removing them from tracking", u);
                        this.userCache.Remove(u);
                    }
                    
                    UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);

                    this.logger.LogDebug("Removing {Channel} from channel list", channel);
                    this.channels.Remove(channel);
                    ChannelsJoined.WithLabels(this.ClientName).Set(this.channels.Count);
                    ChannelUsers.WithLabels(this.ClientName, channel).Unpublish();
                    
                    this.SyncStatusFlagsMetrics();
                }

                this.OnBotKickedEvent(new KickedEventArgs(channel));
            }
            else
            {
                var cachedUser = this.userCache[parameters[1]];
                
                lock (this.userOperationLock)
                {
                    this.channels[channel].Users.Remove(parameters[1]);

                    this.logger.LogInformation("{User} has been kicked from channel {Channel}", parameters[1], channel);

                    if (this.channels.Count(x => x.Value.Users.ContainsKey(parameters[1])) == 0)
                    {
                        this.logger.LogDebug("{User} has left all channels I'm in, removing them from tracking", parameters[1]);
                        this.userCache.Remove(parameters[1]);
                        UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
                    }
                    
                    ChannelUsers.WithLabels(this.ClientName, channel).Set(this.channels[channel].Users.Count);
                }
                
                var onKickReceivedEvent = this.KickReceivedEvent;
                if (onKickReceivedEvent != null)
                {
                    onKickReceivedEvent(this, new KickEventArgs(e.Message, user, channel, cachedUser, this));
                }
            }
        }

        private void OnQuitMessageReceived(IrcMessageReceivedEventArgs e, IUser user)
        {
            this.logger.LogInformation("{User} has left IRC", user);

            lock (this.userOperationLock)
            {
                this.userCache.Remove(user.Nickname);

                foreach (var channel in this.channels)
                {
                    channel.Value.Users.Remove(user.Nickname);
                    ChannelUsers.WithLabels(this.ClientName, channel.Key).Set(channel.Value.Users.Count);
                }
                
                UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
            }
            
            var onQuitReceivedEvent = this.QuitReceivedEvent;
            if (onQuitReceivedEvent != null)
            {
                onQuitReceivedEvent(this, new QuitEventArgs(e.Message, user, this));
            }
        }

        /// <summary>
        /// The raise data event.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        private void RaiseDataEvent(IMessage message)
        {
            var receivedMessageEvent = this.ReceivedIrcMessage;
            if (receivedMessageEvent != null)
            {
                receivedMessageEvent(this, new IrcMessageReceivedEventArgs(message, this));
            }
        }

        /// <summary>
        /// The register connection.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        private void RegisterConnection(IMessage message)
        {
            // initial request
            if (message == null)
            {
                if (this.clientCapabilities.Count == 0)
                {
                    // we don't support capabilities, so don't go through the CAP cycle.
                    this.logger.LogInformation("I support no capabilities");

                    this.Send(new Message("CAP", "END"));
                    this.Send1459Registration();
                }
                else
                {
                    // we support capabilities, use them!
                    this.Send(new Message("CAP", new[] {"LS", "302"}));
                    this.serverCapabilities.Clear();
                }

                return;
            }

            if (message.Command == "NOTICE")
            {
                // do nothing, we don't care about these messages during registration.
                return;
            }

            // welcome to IRC!
            if (message.Command == Numerics.Welcome)
            {
                this.logger.LogInformation("Connection registration succeeded");
                this.serverPrefix = message.Prefix;

                this.pingThread.Start();

                // send initial connection modes
                if (this.onConnectModes != null)
                {
                    this.Send(new Message("MODE", new[] { this.Nickname, this.onConnectModes }));
                }

                // Add myself to nicktracking immediately
                this.userCache.Add(
                    this.Nickname,
                    new IrcUser(this) { Nickname = this.Nickname, SkeletonStatus = IrcUserSkeletonStatus.NickOnly });
                
                this.connectionRegistered = true;
                this.connectionRegistrationSemaphore.Release();

                this.RaiseDataEvent(message);
                return;
            }

            // nickname in use
            if ((message.Command == Numerics.NicknameInUse) || (message.Command == Numerics.UnavailableResource))
            {
                this.logger.LogWarning("Nickname {Nick} in use, retrying...", this.nickname);
                this.nickname = this.nickname + "_";
                this.Send(new Message("NICK", this.nickname));
                return;
            }

            // do sasl auth
            if (message.Command == "AUTHENTICATE")
            {
                this.SaslAuth(message);
                return;
            }

            if (message.Command == Numerics.SaslLoggedIn)
            {
                var strings = message.Parameters.ToArray();
                this.logger.LogInformation("You are now logged in as {0} ({1})", strings[2], strings[1]);
                this.servicesLoggedIn = true;
                return;
            }

            if (message.Command == Numerics.SaslSuccess)
            {
                this.logger.LogInformation("SASL Login succeeded");

                // logged in, continue with registration
                this.Send(new Message("CAP", "END"));
                this.Send1459Registration();
                return;
            }

            if (message.Command == Numerics.SaslAuthFailed)
            {
                this.logger.LogError("SASL Login failed");

                // not logged in, cancel sasl auth.
                this.Send(new Message("QUIT"));
                return;
            }

            if (message.Command == Numerics.SaslAborted)
            {
                this.logger.LogWarning("SASL Login aborted");

                // not logged in, cancel sasl auth.
                this.Send(new Message("CAP", "END"));
                this.Send1459Registration();
                return;
            }

            if (message.Command == Numerics.VisibleHost)
            {
                // no-op
                return;
            }

            this.logger.LogError("How did I get here? ({Command} received)", message.Command);
        }

        private bool HandleCapabilityNegotiation(IMessage message)
        {
            // we've recieved a reply to our CAP commands
            if (message.Command == "CAP")
            {
                var list = message.Parameters.ToList();

                if (list[1] == "LS")
                {
                    if (list[2] == "*")
                    {
                        this.serverCapabilities.AddRange(list[3].Split(' '));
                        return true;
                    }

                    this.serverCapabilities.AddRange(list[2].Split(' '));
                    this.logger.LogDebug("Server Capabilities: {Capabilities}", string.Join(", ", this.ServerCapabilities));
                    this.logger.LogDebug("Client Capabilities: {Capabilities}", string.Join(", ", this.clientCapabilities));

                    var parsedServerCaps =
                        this.serverCapabilities.Select(x => x.Contains("=") ? x.Substring(0, x.IndexOf("=", StringComparison.Ordinal)) : x);
                    
                    var caps = parsedServerCaps.Intersect(this.clientCapabilities).ToList();

                    // We don't support one without the other!
                    if (caps.Intersect(new[] {"account-notify", "extended-join"}).Count() == 1)
                    {
                        this.logger.LogWarning("Dropping account-notify and extended-join support since server only supports one of them");
                        caps.Remove("account-notify");
                        caps.Remove("extended-join");
                    }

                    var saslCap = this.serverCapabilities.FirstOrDefault(x => x.StartsWith("sasl"));
                    if (saslCap!= null && saslCap.Contains("="))
                    {
                        var mechanisms = saslCap.Substring(saslCap.IndexOf("=", StringComparison.Ordinal)+1).Split(',');

                        if (!mechanisms.Contains("EXTERNAL") && !string.IsNullOrWhiteSpace(this.servicesCert))
                        {
                            this.logger.LogWarning("Configured to use EXTERNAL auth, but unsupported by server");
                            this.servicesCert = null;
                        }
                        
                        if (!mechanisms.Contains("PLAIN") && this.servicesCert == null)
                        {
                            // We only support PLAIN.
                            caps.Remove("sasl");
                        }
                    }

                    if (caps.Count == 0)
                    {
                        // nothing is suitable for us, so downgrade to 1459
                        this.logger.LogInformation("Requesting no capabilities");

                        this.Send(new Message("CAP", "END"));
                        this.Send1459Registration();

                        return true;
                    }

                    this.logger.LogDebug("Requesting capabilities: {Capabilities}", string.Join(", ", caps));

                    this.Send(new Message("CAP", new[] {"REQ", string.Join(" ", caps)}));

                    return true;
                }

                if (list[1] == "ACK")
                {
                    var caps = list[2].Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries);
                    this.logger.LogInformation("Acknowledged capabilities: {Capabilities}", string.Join(", ", caps));

                    foreach (var cap in caps)
                    {
                        // are we adding a capability, or removing it? ACK acknowledges both.
                        var adding = true;
                        var parsedCap = cap;
                        if (cap[0] == '-')
                        {
                            adding = false;
                            parsedCap = cap.Substring(1);
                        }

                        switch (parsedCap)
                        {
                            case "sasl":
                                this.capSasl = adding;
                                break;
                            case "extended-join":
                                this.capExtendedJoin = adding;
                                break;
                            case "account-notify":
                                this.capAccountNotify = adding;
                                break;
                            case "account-tag":
                                this.capAccountTag = adding;
                                break;
                            case "multi-prefix":
                                this.capMultiPrefix = adding;
                                break;
                            case "chghost":
                                this.capChghost = adding;
                                break;
                            case "cap-notify":
                                this.capCapNotify = adding;
                                break;
                            case "away-notify":
                                this.capAwayNotify = adding;
                                break;
                        }
                    }

                    if (this.capSasl)
                    {
                        this.SaslAuth(null);
                    }
                    else
                    {
                        this.Send(new Message("CAP", "END"));
                        if (!this.connectionRegistered)
                        {
                            this.Send1459Registration();
                        }
                    }

                    return true;
                }

                if (list[1] == "NAK")
                {
                    // something went wrong, so downgrade to 1459.
                    var caps = list[2].Split(' ');
                    this.logger.LogWarning("NOT Acked capabilities: {Capabilities}", string.Join(", ", caps));

                    this.Send(new Message("CAP", "END"));
                    this.Send1459Registration();
                    return true;
                }

                if (list[1] == "NEW")
                {
                    this.serverCapabilities.AddRange(list[2].Split(' '));
                }

                if (list[1] == "DEL")
                {
                    var removedCapabilities = list[2].Split(' ');

                    foreach (var capability in removedCapabilities)
                    {
                        this.serverCapabilities.Remove(capability);

                        switch (capability)
                        {
                            case "sasl":
                                this.capSasl = false;
                                break;
                            case "multi-prefix":
                                this.capMultiPrefix = false;
                                break;
                            case "extended-join":
                                this.capExtendedJoin = false;
                                break;
                            case "account-notify":
                                this.capAccountNotify = false;
                                break;
                            case "account-tag":
                                this.capAccountTag = false;
                                break;
                            case "chghost":
                                this.capChghost = false;
                                break;
                            case "cap-notify":
                                this.capCapNotify = false;
                                break;
                            case "away-notify":
                                this.capAwayNotify = false;
                                break;
                        }
                    }

                    if (this.capAccountNotify && !this.capExtendedJoin)
                    {
                        //mismatch between capabilities, disable both
                        this.Send(new Message("CAP", new[] {"REQ", "-account-notify"}));
                    }
                    else if (!this.capAccountNotify && this.capExtendedJoin)
                    {
                        //mismatch between capabilities, disable both
                        this.Send(new Message("CAP", new[] {"REQ", "-extended-join"}));
                    }
                }
            }

            return false;
        }

        private void PingThreadWorker()
        {
            this.pingThreadAlive = true;

            while (this.pingThreadAlive)
            {
                var set = this.pingThreadTimerWait.WaitOne(TimeSpan.FromSeconds(this.PingInterval));
                
                if (set)
                {
                    // we've been told to check our current status immediately.
                    this.logger.LogDebug("Ping thread skipping wait");
                    continue;
                }

                this.PingThreadHandlePing();
                this.PingThreadHandleNickChange();
            }
            
            this.logger.LogDebug("Ping thread exited!");
        }

        private void PingThreadHandlePing()
        {
            var totalMilliseconds = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds;
            this.expectedPingMessage = string.Format("{1}{0}", totalMilliseconds, PingPrefix);

            this.networkClient.PrioritySend(string.Format("PING :{0}", this.expectedPingMessage));
            var result = this.pingReplyEvent.WaitOne(TimeSpan.FromSeconds(this.PingTimeout));

            if (!result)
            {
                this.logger.LogWarning("Ping reply not received!");
                this.lagTimer++;
                PingsMissed.WithLabels(this.ClientName).Inc();

                if (this.lagTimer >= this.missedPingLimit && this.restartOnHeavyLag)
                {
                    this.networkClient.PrioritySend("QUIT :Unexpected heavy lag, restarting...");
                    this.Disconnect($"Heavy lag, {this.missedPingLimit} ping replies not received.");
                }
            }
            else
            {
                this.lagTimer = 0;
            }
        }

        private void PingThreadHandleNickChange()
        {
            if (this.intendedNickname != this.nickname && this.reclaimNickFromServices)
            {
                if (this.servicesLoggedIn)
                {
                    this.SendMessage("NickServ", $"REGAIN {this.intendedNickname}");
                }
                else
                {
                    this.Send(new Message("NICK", this.intendedNickname));
                }
            }
        }
        
        /// <summary>
        /// The SASL authentication.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        private void SaslAuth(IMessage message)
        {
            if (message == null)
            {
                if (!string.IsNullOrWhiteSpace(this.servicesCert))
                {
                    this.Send(new Message("AUTHENTICATE", "EXTERNAL"));
                }
                else
                {
                    this.Send(new Message("AUTHENTICATE", "PLAIN"));
                }
                return;
            }

            var list = message.Parameters.ToList();
            if (list[0] == "+")
            {
                if (!string.IsNullOrWhiteSpace(this.servicesCert))
                {
                    this.Send(new Message("AUTHENTICATE", "+"));
                }
                else
                {
                    var authdata = string.Format("\0{0}\0{1}", this.servicesUsername, this.servicesPassword);
                    authdata = Convert.ToBase64String(Encoding.UTF8.GetBytes(authdata));
                    this.Send(new Message("AUTHENTICATE", authdata));
                }
            }
        }

        /// <summary>
        /// The send 1459 registration.
        /// </summary>
        private void Send1459Registration()
        {
            if (!this.capSasl && !string.IsNullOrEmpty(this.servicesPassword) && this.authToServices && string.IsNullOrEmpty(this.serverPassword))
            {
                this.Send(new Message("PASS", this.servicesPassword));
            } 
            else if (!string.IsNullOrEmpty(this.serverPassword))
            {
                this.Send(new Message("PASS", this.serverPassword));
            }

            this.Send(new Message("USER", new[] {this.username, "*", "*", this.realName}));

            this.Send(new Message("NICK", this.nickname));
        }

        #endregion
    }
}
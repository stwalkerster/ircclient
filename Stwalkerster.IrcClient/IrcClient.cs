namespace Stwalkerster.IrcClient
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Security;
    using System.Text;
    using System.Threading;

    using Castle.Core.Logging;
    using Prometheus;
    using Stwalkerster.IrcClient.Events;
    using Stwalkerster.IrcClient.Exceptions;
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Messages;
    using Stwalkerster.IrcClient.Model;
    using Stwalkerster.IrcClient.Model.Interfaces;
    using Stwalkerster.IrcClient.Network;

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
        
        /// <summary>
        /// The mode mapping.
        /// </summary>
        private readonly Dictionary<string, Action<IrcChannelUser, bool>> modeMapping =
            new Dictionary<string, Action<IrcChannelUser, bool>>
            {
                { "v", (x, flag) => x.Voice = flag },
                { "o", (x, flag) => x.Operator = flag },
            };

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

        /// <summary>
        /// The connection registration semaphore.
        /// </summary>
        private readonly Semaphore connectionRegistrationSemaphore;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger logger;

        /// <summary>
        /// The network client.
        /// </summary>
        private INetworkClient networkClient;

        /// <summary>
        /// The password.
        /// </summary>
        private readonly string password;

        /// <summary>
        /// The support helper.
        /// </summary>
        private readonly ISupportHelper supportHelper;

        /// <summary>
        /// The real name.
        /// </summary>
        private readonly string realName;

        /// <summary>
        /// The sync logger.
        /// </summary>
        private readonly ILogger syncLogger;

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

        /// <summary>
        /// The cap extended join.
        /// </summary>
        private bool capExtendedJoin;

        /// <summary>
        /// The SASL capability.
        /// </summary>
        private bool capSasl;

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
        private int lagTimer;
        private readonly bool restartOnHeavyLag;
        private readonly bool reclaimNickFromServices;

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
                this.logger.Warn("Services authentication is disabled!");

                this.clientCapabilities.Remove("sasl");
            }

            this.RegisterConnection(null);
        }

        public IrcClient(ILogger logger, IIrcConfiguration configuration, ISupportHelper supportHelper)
            : this(null, logger, configuration, supportHelper)
        {
        }

        public IrcClient(INetworkClient client, ILogger logger, IIrcConfiguration configuration, ISupportHelper supportHelper)
        {
            this.nickname = configuration.Nickname;
            this.intendedNickname = configuration.Nickname;
            this.username = configuration.Username;
            this.realName = configuration.RealName;
            this.password = configuration.Password;
            this.authToServices = configuration.AuthToServices;
            this.restartOnHeavyLag = configuration.RestartOnHeavyLag;
            this.reclaimNickFromServices = configuration.ReclaimNickFromServices;

            this.supportHelper = supportHelper;
            this.ClientName = configuration.ClientName;
            this.logger = logger.CreateChildLogger(this.ClientName);

            this.syncLogger = this.logger.CreateChildLogger("Sync");
            this.ReceivedIrcMessage += this.OnIrcMessageReceivedIrcEvent;

            this.clientCapabilities = new List<string> {"sasl", "account-notify", "extended-join", "multi-prefix"};
            
            this.userCache = new Dictionary<string, IrcUser>();
            this.channels = new Dictionary<string, IrcChannel>();
            
            this.pingReplyEvent = new AutoResetEvent(false);
            this.pingThread = new Thread(this.PingThreadWorker);
            this.PingInterval = 15;
            this.PingTimeout = 15;

            this.connectionRegistrationSemaphore = new Semaphore(0, 1);
            this.syncLogger.Debug("ctor() acquired connectionRegistration semaphore.");

            if (client == null)
            {
                if (configuration.Ssl)
                {
                    client = new SslNetworkClient(
                        configuration.Hostname,
                        configuration.Port,
                        this.logger.CreateChildLogger("NetworkClient"));
                }
                else
                {
                    client = new NetworkClient(
                        configuration.Hostname,
                        configuration.Port,
                        this.logger.CreateChildLogger("NetworkClient"));
                }
            }

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

        /// <summary>
        /// The received message.
        /// </summary>
        public event EventHandler<IrcMessageReceivedEventArgs> ReceivedIrcMessage;

        public event EventHandler<MessageReceivedEventArgs> ReceivedMessage;

        public event EventHandler<KickedEventArgs> WasKickedEvent;

        public event EventHandler<ModeEventArgs> ModeReceivedEvent;    

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

            this.syncLogger.DebugFormat("Join({0}) waiting on connectionRegistration semaphore.", channel);
            this.connectionRegistrationSemaphore.WaitOne();
            this.syncLogger.DebugFormat("Join({0}) acquired on connectionRegistration semaphore.", channel);
            this.connectionRegistrationSemaphore.Release();
            this.syncLogger.DebugFormat("Join({0}) released connectionRegistration semaphore.", channel);

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
            this.networkClient.Send(message.ToString());
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
        public void SendMessage(string destination, string message, DestinationFlags destinationFlag)
        {
            this.WaitOnRegistration();
            
            if (destinationFlag != null && !this.destinationFlags.Contains(destinationFlag.Flag))
            {
                throw new OperationNotSupportedException("Message send requested with destination flag, but destination flag is not supported by this server.");
            }

            this.Send(new Message("PRIVMSG", new[] { destination, message }));
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
            
            this.SendMessage(destination, message, null);
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
            if (destinationFlag != null && !this.destinationFlags.Contains(destinationFlag.Flag))
            {
                throw new OperationNotSupportedException("Message send requested with destination flag, but destination flag is not supported by this server.");
            }

            this.Send(new Message("NOTICE", new[] { destination, message }));
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

        /// <summary>
        /// The handle who x reply.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        private void HandleWhoXReply(IMessage message)
        {
            try
            {
                if (message.Command != Numerics.WhoXReply)
                {
                    throw new ArgumentException("Expected WHOX reply message", "message");
                }

                var parameters = message.Parameters.ToList();
                if (parameters.Count() != 8)
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
                    }
                }
            }
            catch (Exception ex)
            {
                this.nickTrackingValid = false;
                this.logger.Error("Nick tracking for authentication is no longer valid.", ex);
                throw;
            }
        }

        private void NetworkClientDisconnected(object sender, EventArgs e)
        {
            this.Disconnect("Network client connection lost.");
        }
        
        /// <summary>
        /// The network client on data received.
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="dataReceivedEventArgs">
        /// The data received event args.
        /// </param>
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
                        PingDuration.Set((endTime - beginTime) / 1000);
                    }
                    
                    this.pingReplyEvent.Set();
                }
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

        private void Disconnect(string errorMessage)
        {
            this.pingThreadAlive = false;

            if (!this.pingThread.Equals(Thread.CurrentThread))
            {
                // we're not on this thread, so forcibly abort it now.
                this.pingThread.Abort();
            }

            this.logger.Fatal(errorMessage);
            this.networkClient.Disconnect();

            // Invoke the disconnected event
            var temp = this.DisconnectedEvent;
            if (temp != null)
            {
                temp(this, new EventArgs());
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
                this.logger.DebugFormat("Seen {0} change account name to {1}", user, parameters[0]);
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

        /// <summary>
        /// The on channel mode received.
        /// </summary>
        /// <param name="parameters">
        /// The parameters.
        /// </param>
        private void OnChannelModeReceived(List<string> parameters)
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

                    lock (this.userOperationLock)
                    {
                        var channelUser = this.channels[channel].Users[nick];

                        this.logger.InfoFormat("Seen {0}{2} on {1}.", addMode ? "+" : "-", channelUser, c);

                        this.modeMapping[c.ToString()](channelUser, addMode);

                        position++;
                    }
                }

                if ("eIbqkflj".Contains(c))
                {
                    position++;
                }
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

            var channelName = parametersList[0];
            if (user.Nickname == this.Nickname)
            {
                // we're joining this, so rate-limit from here.
                this.logger.InfoFormat("Joining channel {0}", channelName);
                this.logger.Debug("Requesting WHOX a information");
                this.Send(new Message("WHO", new[] {channelName, "%uhnatfc,001"}));

                this.logger.Debug("Requesting MODE information");
                this.Send(new Message("MODE", new[] {channelName}));

                lock (this.userOperationLock)
                {
                    // add the channel to the list of channels I'm in.
                    this.logger.DebugFormat("Adding {0} to the list of channels I'm in.", channelName);
                    this.Channels.Add(channelName, new IrcChannel(channelName));
                    ChannelsJoined.WithLabels(this.ClientName).Set(this.channels.Count);
                }
            }
            else
            {
                this.logger.InfoFormat("Seen {0} join channel {1}.", user, channelName);

                lock (this.userOperationLock)
                {
                    if (!this.Channels[channelName].Users.ContainsKey(user.Nickname))
                    {
                        this.logger.DebugFormat("Adding user {0} to the list of users in channel {1}", user, channelName);
                        this.Channels[channelName]
                            .Users.Add(
                                user.Nickname,
                                new IrcChannelUser(user, channelName));
                    }
                    else
                    {
                        this.logger.Error("Nickname tracking no longer valid.");
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

                            if (cachedUser.SkeletonStatus < IrcUserSkeletonStatus.PrefixOnly)
                            {
                                cachedUser.Username = user.Username;
                                cachedUser.Hostname = user.Hostname;
                                cachedUser.SkeletonStatus = IrcUserSkeletonStatus.PrefixOnly;
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
                this.logger.DebugFormat("WHOX Reply:{0}", string.Join(" ", e.Message.Parameters));
                this.HandleWhoXReply(e.Message);
            }

            if (e.Message.Command == Numerics.EndOfWho)
            {
                this.logger.Debug("End of who list.");
            }

            if ((e.Message.Command == "QUIT") && (user != null))
            {
                this.OnQuitMessageReceived(user);
            }

            if ((e.Message.Command == "MODE") && (user != null))
            {
                var parameters = e.Message.Parameters.ToList();
                var target = parameters[0];
                if (target.StartsWith("#"))
                {
                    this.logger.Debug("Received channel mode message");
                    this.OnChannelModeReceived(parameters);
                }
                else
                {
                    // User mode message
                    this.logger.Debug("Received user mode message. Not processing.");
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

                this.OnChannelModeReceived(parameters);

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
                this.OnKickMessageReceived(e);
            }

            if ((e.Message.Command == "ACCOUNT") && (user != null))
            {
                this.OnAccountMessageReceived(e, user);
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

            this.logger.DebugFormat("Names on {0}: {1}", channel, names);

            foreach (var name in names.Split(' '))
            {
                var parsedName = name;
                var voice = false;
                var op = false;

                if (parsedName.StartsWith("+"))
                {
                    parsedName = parsedName.Substring(1);
                    voice = true;
                }

                if (parsedName.StartsWith("@"))
                {
                    parsedName = parsedName.Substring(1);
                    op = true;
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
                this.logger.InfoFormat("Updating my nickname from {0} to {1}!", oldNickname, newNickname);
                this.nickname = newNickname;
            }
            
            this.logger.InfoFormat("Changing {0} to {1} in nick tracking database.", oldNickname, newNickname);

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

                    try
                    {
                        this.userCache.Remove(oldNickname);
                        this.userCache.Add(newNickname, ircUser);
                        
                        UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
                    }
                    catch (ArgumentException)
                    {
                        this.logger.Warn(
                            "Couldn't add the new entry to the dictionary. Nick tracking is no longer valid.");
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
                                this.logger.ErrorFormat(
                                    "Channel user {0} doesn't match irc user {1} for NICK in {2}",
                                    channelUser.User,
                                    ircUser,
                                    channelPair.Value.Name);

                                this.logger.Error("Nick tracking is no longer valid.");
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
                                this.logger.Warn(
                                    "Couldn't add the new entry to the dictionary. Nick tracking is no longer valid.");
                                this.nickTrackingValid = false;
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                this.logger.Error("Nickname tracking is no longer valid.", exception);
                this.nickTrackingValid = false;
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
                this.logger.InfoFormat("Leaving channel {1}.", user, channel);

                lock (this.userOperationLock)
                {
                    var channelUsers = this.channels[channel].Users.Select(x => x.Key);
                    foreach (var u in channelUsers.Where(u => this.channels.Count(x => x.Value.Users.ContainsKey(u)) == 0))
                    {
                        this.logger.InfoFormat(
                            "{0} is no longer in any channel I'm in, removing them from tracking",
                            u,
                            channel);

                        this.userCache.Remove(u);
                    }
                    
                    UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
                    this.channels.Remove(channel);
                    ChannelsJoined.WithLabels(this.ClientName).Set(this.channels.Count);
                }
            }
            else
            {
                lock (this.userOperationLock)
                {
                    this.channels[channel].Users.Remove(user.Nickname);

                    this.logger.InfoFormat("{0} has left channel {1}.", user, channel);

                    if (this.channels.Count(x => x.Value.Users.ContainsKey(user.Nickname)) == 0)
                    {
                        this.logger.InfoFormat(
                            "{0} has left all channels I'm in, removing them from tracking",
                            user,
                            channel);

                        this.userCache.Remove(user.Nickname);
                        
                        UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
                    }
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
        /// The e.
        /// </param>
        private void OnKickMessageReceived(IrcMessageReceivedEventArgs e)
        {
            // Kick format is:
            // :n!u@h KICK #chan nick :reason
            var parameters = e.Message.Parameters.ToList();
            var channel = parameters[0];
            if (parameters[1] == this.Nickname)
            {
                this.logger.WarnFormat("Kicked from channel {0}.", channel);

                lock (this.userOperationLock)
                {
                    var channelUsers = this.channels[channel].Users.Select(x => x.Key);
                    var channelsWithUser = channelUsers.Where(u => this.channels.Count(x => x.Value.Users.ContainsKey(u)) == 0);
                    foreach (var u in channelsWithUser)
                    {
                        this.logger.InfoFormat(
                            "{0} is no longer in any channel I'm in, removing them from tracking",
                            u,
                            channel);

                        this.userCache.Remove(u);
                    }
                    
                    UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);

                    this.logger.DebugFormat("Removing {0} from channel list", channel);
                    this.channels.Remove(channel);
                    ChannelsJoined.WithLabels(this.ClientName).Set(this.channels.Count);
                }

                this.OnBotKickedEvent(new KickedEventArgs(channel));
            }
            else
            {
                lock (this.userOperationLock)
                {
                    this.channels[channel].Users.Remove(parameters[1]);

                    this.logger.InfoFormat("{0} has beem kicked from channel {1}.", parameters[1], channel);

                    if (this.channels.Count(x => x.Value.Users.ContainsKey(parameters[1])) == 0)
                    {
                        this.logger.InfoFormat(
                            "{0} has left all channels I'm in, removing them from tracking",
                            parameters[1],
                            channel);

                        this.userCache.Remove(parameters[1]);
                        UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
                    }
                }
            }
        }

        /// <summary>
        /// The on quit message received.
        /// </summary>
        /// <param name="user">
        /// The user.
        /// </param>
        private void OnQuitMessageReceived(IUser user)
        {
            this.logger.InfoFormat("{0} has left IRC.", user);

            lock (this.userOperationLock)
            {
                this.userCache.Remove(user.Nickname);

                foreach (var channel in this.channels)
                {
                    channel.Value.Users.Remove(user.Nickname);
                }
                
                UsersKnown.WithLabels(this.ClientName).Set(this.userCache.Count);
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
                    this.logger.InfoFormat("I support no capabilities.");

                    this.Send(new Message("CAP", "END"));
                    this.Send1459Registration();
                }
                else
                {
                    // we support capabilities, use them!
                    this.Send(new Message("CAP", "LS"));
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
                this.logger.Info("Connection registration succeeded");
                this.serverPrefix = message.Prefix;

                this.pingThread.Start();

                // block forwarding
                this.Send(new Message("MODE", new[] {this.Nickname, "+Q"}));
                
                this.connectionRegistered = true;
                this.connectionRegistrationSemaphore.Release();
                this.syncLogger.Debug("RegisterConnection() released connectionRegistration semaphore.");

                this.RaiseDataEvent(message);
                return;
            }

            // nickname in use
            if ((message.Command == Numerics.NicknameInUse) || (message.Command == Numerics.UnavailableResource))
            {
                this.logger.Warn("Nickname in use, retrying.");
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

            // we've recieved a reply to our CAP commands
            if (message.Command == "CAP")
            {
                var list = message.Parameters.ToList();

                if (list[1] == "LS")
                {
                    var serverCapabilities = list[2].Split(' ');
                    this.logger.DebugFormat("Server Capabilities: {0}", string.Join(", ", serverCapabilities));
                    this.logger.DebugFormat("Client Capabilities: {0}", string.Join(", ", this.clientCapabilities));

                    var caps = serverCapabilities.Intersect(this.clientCapabilities).ToList();

                    // We don't support one without the other!
                    if (caps.Intersect(new[] { "account-notify", "extended-join" }).Count() == 1)
                    {
                        this.logger.Warn(
                            "Dropping account-notify and extended-join support since server only supports one of them!");
                        caps.Remove("account-notify");
                        caps.Remove("extended-join");
                    }

                    if (caps.Count == 0)
                    {
                        // nothing is suitable for us, so downgrade to 1459
                        this.logger.InfoFormat("Requesting no capabilities.");

                        this.Send(new Message("CAP", "END"));
                        this.Send1459Registration();

                        return;
                    }

                    this.logger.InfoFormat("Requesting capabilities: {0}", string.Join(", ", caps));
                    
                    this.Send(new Message("CAP", new[] {"REQ", string.Join(" ", caps)}));

                    return;
                }

                if (list[1] == "ACK")
                {
                    var caps = list[2].Split(' ');
                    this.logger.InfoFormat("Acknowledged capabilities: {0}", string.Join(", ", caps));

                    foreach (var cap in caps)
                    {
                        if (cap == "sasl")
                        {
                            this.capSasl = true;
                        }

                        if (cap == "extended-join")
                        {
                            // This includes account-notify since both are required.
                            this.capExtendedJoin = true;
                        }

                        // We don't care about multi-prefix, since the code to 
                        // handle it works nicely for those without it.
                    }

                    if (this.capSasl)
                    {
                        this.SaslAuth(null);
                    }
                    else
                    {
                        this.Send(new Message("CAP", "END"));
                        this.Send1459Registration();
                    }

                    return;
                }

                if (list[1] == "NAK")
                {
                    // something went wrong, so downgrade to 1459.
                    var caps = list[2].Split(' ');
                    this.logger.WarnFormat("NOT Acked capabilities: {0}", string.Join(", ", caps));

                    this.Send(new Message("CAP", "END"));
                    this.Send1459Registration();
                    return;
                }
            }

            if (message.Command == Numerics.SaslLoggedIn)
            {
                var strings = message.Parameters.ToArray();
                this.logger.InfoFormat("You are now logged in as {1} ({0})", strings[1], strings[2]);
                this.servicesLoggedIn = true;
                return;
            }

            if (message.Command == Numerics.SaslSuccess)
            {
                this.logger.InfoFormat("SASL Login succeeded.");

                // logged in, continue with registration
                this.Send(new Message("CAP", "END"));
                this.Send1459Registration();
                return;
            }

            if (message.Command == Numerics.SaslAuthFailed)
            {
                this.logger.Fatal("SASL Login failed.");

                // not logged in, cancel sasl auth.
                this.Send(new Message("QUIT"));
                return;
            }

            if (message.Command == Numerics.SaslAborted)
            {
                this.logger.WarnFormat("SASL Login aborted.");

                // not logged in, cancel sasl auth.
                this.Send(new Message("CAP", "END"));
                this.Send1459Registration();
                return;
            }

            this.logger.ErrorFormat("How did I get here? ({0} recieved)", message.Command);
        }

        private void PingThreadWorker()
        {
            this.pingThreadAlive = true;
            var timerWait = new AutoResetEvent(false);

            while (this.pingThreadAlive)
            {
                timerWait.WaitOne(TimeSpan.FromSeconds(this.PingInterval));

                this.PingThreadHandlePing();
                this.PingThreadHandleNickChange();
            }
        }

        private void PingThreadHandlePing()
        {
            var totalMilliseconds = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds;
            this.expectedPingMessage = string.Format("{1}{0}", totalMilliseconds, PingPrefix);

            this.networkClient.PrioritySend(string.Format("PING :{0}", this.expectedPingMessage));
            var result = this.pingReplyEvent.WaitOne(TimeSpan.FromSeconds(this.PingTimeout));

            if (!result)
            {
                this.logger.Warn("Ping reply not received!");
                this.lagTimer++;
                PingsMissed.WithLabels(this.ClientName).Inc();

                if (this.lagTimer >= 3 && this.restartOnHeavyLag)
                {
                    this.networkClient.PrioritySend("QUIT :Unexpected heavy lag, restarting...");
                    this.Disconnect("Heavy lag, three ping replies not recieved.");
                }
            }
            else
            {
                this.lagTimer = 0;
            }
        }

        private void PingThreadHandleNickChange()
        {
            if (this.intendedNickname != this.nickname)
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
                this.Send(new Message("AUTHENTICATE", "PLAIN"));
                return;
            }

            var list = message.Parameters.ToList();
            if (list[0] == "+")
            {
                var authdata = string.Format("\0{0}\0{1}", this.username, this.password);
                authdata = Convert.ToBase64String(Encoding.UTF8.GetBytes(authdata));
                this.Send(new Message("AUTHENTICATE", authdata));
            }
        }

        /// <summary>
        /// The send 1459 registration.
        /// </summary>
        private void Send1459Registration()
        {
            if (!this.capSasl && !string.IsNullOrEmpty(this.password) && this.authToServices)
            {
                this.Send(new Message("PASS", this.password));
            }

            this.Send(new Message("USER", new[] {this.username, "*", "*", this.realName}));

            this.Send(new Message("NICK", this.nickname));
        }

        #endregion
    }
}
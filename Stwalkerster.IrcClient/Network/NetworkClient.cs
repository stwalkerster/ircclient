namespace Stwalkerster.IrcClient.Network
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Sockets;
    using System.Threading;
    using Castle.Core.Logging;
    using Prometheus;
    using Stwalkerster.IrcClient.Events;
    using Stwalkerster.IrcClient.Interfaces;

    /// <summary>
    ///     The TCP client.
    /// </summary>
    /// <para>
    ///     This is an event-based asynchronous TCP client
    /// </para>
    public class NetworkClient : INetworkClient
    {
        private static readonly Counter MessagesReceived = Metrics.CreateCounter(
            "ircclient_network_messages_received_total",
            "Number of messages received",
            new CounterConfiguration {LabelNames = new[] {"endpoint"}});
        
        private static readonly Counter MessagesSent = Metrics.CreateCounter(
            "ircclient_network_messages_sent_total",
            "Number of messages sent",
            new CounterConfiguration {LabelNames = new[] {"endpoint"}});
        
        private static readonly Counter MessageBytesReceived = Metrics.CreateCounter(
            "ircclient_network_messages_received_bytes_total",
            "Number of messages received",
            new CounterConfiguration {LabelNames = new[] {"endpoint"}});
        
        private static readonly Counter MessageBytesSent = Metrics.CreateCounter(
            "ircclient_network_messages_sent_bytes_total",
            "Number of messages sent",
            new CounterConfiguration {LabelNames = new[] {"endpoint"}});

        private static readonly Gauge SendQueueLength = Metrics.CreateGauge(
            "ircclient_network_sendqueue_length",
            "Number of messages in the send queue",
            new GaugeConfiguration {LabelNames = new[] {"endpoint"}});
        
        /// <inheritdoc />
        public event EventHandler<DataReceivedEventArgs> DataReceived;
        public event EventHandler<EventArgs> Disconnected;

        private readonly TcpClient client;

        private readonly ILogger inboundLogger;
        private readonly ILogger outboundLogger;

        private readonly LinkedList<string> sendQueue;
        private readonly object sendQueueLock = new object();

        private readonly AutoResetEvent writerThreadResetEvent;
        private readonly Thread readerThread;
        private readonly Thread writerThread;

        private bool disconnectedFired;
        private readonly object disconnectedLock = new object();

        /// <summary>
        ///     Initialises a new instance of the <see cref="NetworkClient" /> class.
        /// </summary>
        /// <param name="hostname">
        ///     The hostname.
        /// </param>
        /// <param name="port">
        ///     The port.
        /// </param>
        /// <param name="logger">
        ///     The logger.
        /// </param>
        public NetworkClient(string hostname, int port, ILogger logger)
        {
            this.Hostname = hostname;
            this.Port = port;
            this.Logger = logger;
            this.inboundLogger = logger.CreateChildLogger("Inbound");
            this.outboundLogger = logger.CreateChildLogger("Outbound");

            this.sendQueue = new LinkedList<string>();
            this.client = new TcpClient();
            
            this.readerThread = new Thread(this.ReaderThreadTask);
            this.writerThread = new Thread(this.WriterThreadTask);

            this.writerThreadResetEvent = new AutoResetEvent(true);
        }
        
        protected TcpClient Client
        {
            get { return this.client; }
        }

        /// <inheritdoc />
        public bool Connected
        {
            get { return this.client.Connected; }
        }
        
        /// <inheritdoc />
        public string Hostname { get; private set; }

        /// <inheritdoc />
        public int Port { get; private set; }

        protected StreamReader Reader { get; set; }
        protected StreamWriter Writer { get; set; }

        protected ILogger Logger { get; private set; }

        /// <inheritdoc />
        public void Disconnect()
        {
            this.Logger.Info("Disconnecting network socket.");
            
            try
            {
                this.Writer.Flush();
                this.Writer.Close();
            }
            catch (IOException ex)
            {
                this.Logger.Info("Error disposing writer and stream.", ex);
            }

            try
            {
                this.client.Close();
            }
            catch (IOException ex)
            {
                this.Logger.Info("Error closing socket.", ex);
            }
        }

        /// <inheritdoc />
        public virtual void Connect()
        {
            this.Connect(true);
        }

        protected void Connect(bool startThreads)
        {
            this.Logger.InfoFormat("Connecting to socket tcp://{0}:{1}/ ...", this.Hostname, this.Port);

            this.client.Connect(this.Hostname, this.Port);

            this.Reader = new StreamReader(this.client.GetStream());
            this.Writer = new StreamWriter(this.client.GetStream());

            if (startThreads)
            {
                this.StartThreads();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public void Send(string message)
        {
            lock (this.sendQueueLock)
            {
                this.sendQueue.AddLast(message);
                SendQueueLength.WithLabels($"{Hostname}:{Port}").Set(this.sendQueue.Count);
            }

            this.writerThreadResetEvent.Set();
        }

        /// <inheritdoc />
        public void PrioritySend(string message)
        {
            lock (this.sendQueueLock)
            {
                this.sendQueue.AddFirst(message);
                SendQueueLength.WithLabels($"{Hostname}:{Port}").Set(this.sendQueue.Count);
            }

            this.writerThreadResetEvent.Set();
        }

        /// <inheritdoc />
        public void Send(IEnumerable<string> messages)
        {
            lock (this.sendQueueLock)
            {
                foreach (var message in messages)
                {
                    this.sendQueue.AddLast(message);
                }
                
                SendQueueLength.WithLabels($"{Hostname}:{Port}").Set(this.sendQueue.Count);
            }

            this.writerThreadResetEvent.Set();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.Reader.Dispose();
                this.Writer.Dispose();
                ((IDisposable) this.writerThreadResetEvent).Dispose();
            }
        }

        protected virtual void OnDataReceived(DataReceivedEventArgs e)
        {
            var handler = this.DataReceived;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected void StartThreads()
        {
            this.Logger.InfoFormat("Initialising reader/writer threads");

            this.readerThread.Start();
            this.writerThread.Start();
        }

        private void ReaderThreadTask()
        {
            try
            {
                while (this.client.Connected)
                {
                    var data = this.Reader.ReadLine();

                    if (data != null)
                    {
                        this.inboundLogger.Debug(data);
                        
                        MessagesReceived.WithLabels($"{Hostname}:{Port}").Inc();
                        MessageBytesReceived.WithLabels($"{Hostname}:{Port}").Inc(data.Length);
                        
                        this.OnDataReceived(new DataReceivedEventArgs(data));
                    }
                }
                
                this.Logger.Debug("Reader thread no longer connected");    
            }
            catch (IOException ex)
            {
                this.Logger.Error("IO error on read from network stream", ex);
            }
            catch (SocketException ex)
            {
                this.Logger.Error("Socket error on read from network stream", ex);
            }
            catch (ObjectDisposedException ex)
            {
                this.Logger.Fatal("Object disposed", ex);
            }
            catch (Exception ex)
            {
                this.Logger.Warn("Unhandled reader thread exception", ex);
                throw;
            }
            finally
            {
                this.writerThread.Abort();
                this.client.Close();

                this.Logger.Debug("Firing disconnect event from reader thread!");    
                this.FireDisconnectedEvent();
            }
        }

        private void WriterThreadTask()
        {
            try
            {
                while (this.client.Connected)
                {
                    string item = null;

                    // grab an item from the queue if we can
                    lock (this.sendQueueLock)
                    {
                        if (this.sendQueue.Count > 0)
                        {
                            item = this.sendQueue.First.Value;
                            this.sendQueue.RemoveFirst();
                        }

                        SendQueueLength.WithLabels($"{Hostname}:{Port}").Set(this.sendQueue.Count);
                    }

                    if (item == null)
                    {
                        // Wait here for an item to be added to the queue
                        this.writerThreadResetEvent.WaitOne(500);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(item))
                        {
                            continue;
                        }

                        this.outboundLogger.Debug(item);

                        MessagesSent.WithLabels($"{Hostname}:{Port}").Inc();
                        MessageBytesSent.WithLabels($"{Hostname}:{Port}").Inc(item.Length);

                        this.Writer.WriteLine(item);
                        this.Writer.Flush();

                        // Flood protection
                        Thread.Sleep(500);
                    }
                }

                this.Logger.Debug("Writer thread no longer connected");
            }
            catch (IOException ex)
            {
                this.Logger.Error("IO error on read from network stream", ex);
            }
            catch (SocketException ex)
            {
                this.Logger.Error("Socket error on read from network stream", ex);
            }
            catch (ObjectDisposedException ex)
            {
                this.Logger.Fatal("Object disposed", ex);
            }
            catch (Exception ex)
            {
                this.Logger.Warn("Unhandled writer thread exception", ex);
                throw;
            }
            finally
            {            
                this.readerThread.Abort();
                this.client.Close();

                this.Logger.Debug("Firing disconnect event from writer thread!");    
                this.FireDisconnectedEvent();
            }
        }

        private void FireDisconnectedEvent()
        {   
            var fireDisconnected = false;
            
            lock (this.disconnectedLock)
            {
                if (!this.disconnectedFired)
                {
                    this.disconnectedFired = true;
                    fireDisconnected = true;
                }
            }

            if (fireDisconnected)
            {
                var tmp = this.Disconnected;
                if (tmp != null)
                {
                    tmp(this, EventArgs.Empty);
                }
            }
        }
    }
}
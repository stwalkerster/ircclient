namespace Stwalkerster.IrcClient.Interfaces
{
    using System;
    using System.Collections.Generic;
    using Stwalkerster.IrcClient.Events;

    /// <summary>
    /// The NetworkClient interface.
    /// </summary>
    public interface INetworkClient : IDisposable
    {
        bool Connected { get; }

        /// <summary>
        /// Raised when an event is received from the network stream
        /// </summary>
        event EventHandler<DataReceivedEventArgs> DataReceived;

        /// <summary>
        /// Gets the hostname to connect to
        /// </summary>
        string Hostname { get; }

        /// <summary>
        /// Gets the port to connect to.
        /// </summary>
        int Port { get; }

        /// <summary>
        /// Enqueues a line of text to be sent to the network socket
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        void Send(string message);

        /// <summary>
        /// Enqueues a priority line of text to be sent to the network socket
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        void PrioritySend(string message);

        /// <summary>
        /// Enqueues a collection of lines of text to be sent to the network socket
        /// </summary>
        /// <param name="messages">
        /// The messages.
        /// </param>
        void Send(IEnumerable<string> messages);

        /// <summary>
        /// Disconnects the network socket
        /// </summary>
        void Disconnect();
        
        /// <summary>
        /// Opens the network socket and initialises the events
        /// </summary>
        void Connect();

        event EventHandler<EventArgs> Disconnected;
    }
}
﻿namespace Stwalkerster.IrcClient.Events
{
    using System;
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Messages;

    /// <summary>
    /// The message received event args.
    /// </summary>
    public class IrcMessageReceivedEventArgs : EventArgs
    {
        #region Fields

        /// <summary>
        /// The message.
        /// </summary>
        private readonly IMessage message;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        /// Initialises a new instance of the <see cref="IrcMessageReceivedEventArgs"/> class.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        /// <param name="client">
        /// The client.
        /// </param>
        public IrcMessageReceivedEventArgs(IMessage message, IIrcClient client)
        {
            this.Client = client;
            this.message = message;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the client.
        /// </summary>
        public IIrcClient Client { get; private set; }

        /// <summary>
        /// Gets the message.
        /// </summary>
        public IMessage Message
        {
            get
            {
                return this.message;
            }
        }
        
        public byte[] RawData { get; set; }

        #endregion
    }
}
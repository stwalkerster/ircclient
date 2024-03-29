﻿namespace Stwalkerster.IrcClient.Interfaces
{
    using System;
    using System.Collections.Generic;
    using Stwalkerster.IrcClient.Events;
    using Stwalkerster.IrcClient.Messages;
    using Stwalkerster.IrcClient.Model;

    /// <summary>
    /// The IRC Client interface.
    /// </summary>
    public interface IIrcClient
    {
        #region Public Events

        /// <summary>
        /// The invite received event.
        /// </summary>
        event EventHandler<InviteEventArgs> InviteReceivedEvent;

        /// <summary>
        /// The join received event.
        /// </summary>
        event EventHandler<JoinEventArgs> JoinReceivedEvent;

        /// <summary>
        /// Fired whenevner a raw IRC message is received 
        /// </summary>
        event EventHandler<IrcMessageReceivedEventArgs> ReceivedIrcMessage;
        
        /// <summary>
        /// Fired on PRIVMSG/NOTICE
        /// </summary>
        event EventHandler<MessageReceivedEventArgs> ReceivedMessage;

        /// <summary>
        /// Raised when a part is detected - either someone else or this client.
        /// </summary>
        event EventHandler<JoinEventArgs> PartReceivedEvent;
        
        /// <summary>
        /// Raised when a mode change is detected
        /// </summary>
        event EventHandler<ModeEventArgs> ModeReceivedEvent;
        
        /// <summary>
        /// Raised when this client was kicked from a channel
        /// </summary>
        event EventHandler<KickedEventArgs> WasKickedEvent;
        
        /// <summary>
        /// Raised when the client disconnects from IRC.
        /// </summary>
        event EventHandler DisconnectedEvent;
        
        event EventHandler<QuitEventArgs> QuitReceivedEvent;
        
        /// <summary>
        /// Raised when another user was kicked from a channel
        /// </summary>
        event EventHandler<KickEventArgs> KickReceivedEvent;
        event EventHandler<NickEventArgs> NickReceivedEvent;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the channels.
        /// </summary>
        Dictionary<string, IrcChannel> Channels { get; }

        /// <summary>
        /// Gets a value indicating whether the nick tracking is valid.
        /// </summary>
        bool NickTrackingValid { get; }

        /// <summary>
        /// Gets or sets the nickname.
        /// </summary>
        string Nickname { get; set; }

        /// <summary>
        /// Gets a value indicating whether the client logged in to a nickserv account
        /// </summary>
        bool ServicesLoggedIn { get; }

        /// <summary>
        /// Gets the user cache.
        /// </summary>
        Dictionary<string, IrcUser> UserCache { get; }

        bool NetworkConnected { get; }
        string ClientName { get; }
        string ExtBanDelimiter { get; }
        string ExtBanTypes { get; }
        int PrivmsgReceived { get; }
        double Latency { get; }
        IList<string> StatusMsgDestinationFlags { get; }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        /// The join.
        /// </summary>
        /// <param name="channel">
        /// The channel.
        /// </param>
        void JoinChannel(string channel);

        /// <summary>
        /// The lookup user.
        /// </summary>
        /// <param name="prefix">
        /// The prefix.
        /// </param>
        /// <returns>
        /// The <see cref="IrcUser"/>.
        /// </returns>
        IrcUser LookupUser(string prefix);

        /// <summary>
        /// The part channel.
        /// </summary>
        /// <param name="channel">
        /// The channel.
        /// </param>
        /// <param name="message">
        /// The message.
        /// </param>
        void PartChannel(string channel, string message);

        /// <summary>
        /// The send.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        void Send(IMessage message);

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
        /// The destination flags.
        /// </param>
        void SendMessage(string destination, string message, DestinationFlags destinationFlag);
        void SendMessage(string destination, string message);

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
        /// The destination flags.
        /// </param>
        void SendNotice(string destination, string message, DestinationFlags destinationFlag);
        void SendNotice(string destination, string message);

        /// <summary>
        /// Blocks until the connection is registered.
        /// </summary>
        void WaitOnRegistration();
        
        /// <summary>
        /// Changes the mode of the target
        /// </summary>
        /// <param name="target"></param>
        /// <param name="changes"></param>
        void Mode(string target, string changes);

        #endregion

        event EventHandler<ChannelUserModeEventArgs> ChannelUserModeEvent;
        event EventHandler<EndOfWhoEventArgs> EndOfWhoEvent;
        void PrioritySend(IMessage message);
        void SendMessage(string destination, string message, DestinationFlags destinationFlag, bool priority);
    }
}
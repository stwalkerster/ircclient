namespace Stwalkerster.IrcClient.Events
{
    using System;
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Model.Interfaces;

    public class ChannelUserModeEventArgs : EventArgs
    {
        public IUser AffectedUser { get; }
        public string Channel { get; }
        public string ModeFlag { get; }
        public bool Adding { get; }
        public IUser ActingUser { get; }
        public IIrcClient Client { get; }

        public ChannelUserModeEventArgs(IUser affectedUser, string channel, string modeFlag, bool adding, IUser actingUser, IIrcClient client)
        {
            this.AffectedUser = affectedUser;
            this.Channel = channel;
            this.ModeFlag = modeFlag;
            this.Adding = adding;
            this.ActingUser = actingUser;
            this.Client = client;
        }
        
    }
}
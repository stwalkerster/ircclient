namespace Stwalkerster.IrcClient.Events
{
    using System;
    using Stwalkerster.IrcClient.Model.Interfaces;

    public class KickedEventArgs : EventArgs
    {
        public KickedEventArgs(string channel, IUser user, string reason)
        {
            this.Channel = channel;
            this.User = user;
            this.Reason = reason;
        }

        public string Channel { get; }
        public IUser User { get; }
        public string Reason { get; }
    }
}
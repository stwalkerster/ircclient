namespace Stwalkerster.IrcClient.Events
{
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Messages;
    using Stwalkerster.IrcClient.Model.Interfaces;

    public class KickEventArgs : UserEventArgsBase
    {
        public string Channel { get; }
        public IUser KickedUser { get; }
        public string Reason { get; }

        public KickEventArgs(
            IMessage message,
            IUser user,
            string channel,
            IUser kickedUser,
            IIrcClient client,
            string reason)
            : base(message, user, client)
        {
            this.Channel = channel;
            this.KickedUser = kickedUser;
            this.Reason = reason;
        }
    }
}
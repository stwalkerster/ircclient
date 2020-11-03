namespace Stwalkerster.IrcClient.Events
{
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Messages;
    using Stwalkerster.IrcClient.Model.Interfaces;

    public class KickEventArgs : UserEventArgsBase
    {
        public string Channel { get; }
        public string KickedNickname { get; }

        public KickEventArgs(IMessage message, IUser user, string channel, string kickedNickname, IIrcClient client)
            : base(message, user, client)
        {
            this.Channel = channel;
            this.KickedNickname = kickedNickname;
        }
    }
}
namespace Stwalkerster.IrcClient.Events
{
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Messages;
    using Stwalkerster.IrcClient.Model.Interfaces;

    public class NickEventArgs : UserEventArgsBase
    {
        public string OldNick { get; }

        public NickEventArgs(IMessage message, IUser user, string oldNick, IIrcClient client)
            : base(message, user, client)
        {
            this.OldNick = oldNick;
        }
    }
}
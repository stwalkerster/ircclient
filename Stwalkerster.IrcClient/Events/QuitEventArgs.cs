namespace Stwalkerster.IrcClient.Events
{
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Messages;
    using Stwalkerster.IrcClient.Model.Interfaces;

    public class QuitEventArgs : UserEventArgsBase
    {
        public QuitEventArgs(IMessage message, IUser user, IIrcClient client)
            : base(message, user, client)
        {
        }
    }
}
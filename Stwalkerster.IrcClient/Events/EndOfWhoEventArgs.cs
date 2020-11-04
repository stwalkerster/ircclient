namespace Stwalkerster.IrcClient.Events
{
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Messages;

    public class EndOfWhoEventArgs : IrcMessageReceivedEventArgs
    {
        public string Channel { get; }

        public EndOfWhoEventArgs(IMessage message, string channel, IIrcClient client) : base(message, client)
        {
            this.Channel = channel;
        }
    }
}
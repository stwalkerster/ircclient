namespace Stwalkerster.IrcClient.Events
{
    using System;
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Model.Interfaces;

    public class MessageReceivedEventArgs : EventArgs
    {
        public MessageReceivedEventArgs(
            IUser user,
            string target,
            string message,
            bool isNotice,
            IIrcClient client,
            byte[] rawData)
        {
            this.User = user;
            this.Target = target;
            this.Message = message;
            this.IsNotice = isNotice;
            this.Client = client;
            this.RawData = rawData;
        }

        public bool IsNotice { get; private set; }
        public IUser User { get; private set; }
        public string Target { get; private set; }
        public string Message { get; private set; }
        public IIrcClient Client { get; private set; }
        public byte[] RawData { get; set; }
    }
}
namespace Stwalkerster.IrcClient.Testbot.Startup
{
    using Castle.Core;
    using Castle.MicroKernel.Registration;
    using Castle.Windsor;
    using Castle.Windsor.Installer;
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Messages;

    public class Launch : IStartable
    {
        public static void Main()
        {
            var container = new WindsorContainer();

            container.Register(
                Component.For<IIrcConfiguration>()
                    .Instance(
                        new IrcConfiguration(
                            hostname: "irc.freenode.net",
                            port: 6667,
                            authToServices: false,
                            nickname: "stwtestbot",
                            username: "stwtestbot",
                            realName: "stwtestbot",
                            ssl: false,
                            clientName: "TestClient"
                        )));
            
            container.Install(FromAssembly.This());
            
            var app = container.Resolve<Launch>();
        }

        public Launch(IIrcClient client)
        {
            client.JoinChannel("##stwalkerster-development");
            client.ReceivedMessage += (sender, args) =>
            {
                if (args.Message.Command == "PRIVMSG")
                {
                    var message = new Message(args.Message.Command, args.Message.Parameters);
                    args.Client.Send(message);
                }
            };
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }
    }
}
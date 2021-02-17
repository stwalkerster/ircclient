namespace Stwalkerster.IrcClient.Testbot.Startup
{
    using Castle.Core;
    using Castle.MicroKernel.Registration;
    using Castle.Windsor;
    using Prometheus;
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Messages;

    public class Launch : IStartable
    {
        public static void Main()
        {
            var server = new MetricServer(9101);
            server.Start();
            
            var container = new WindsorContainer();

            container.Register(
                Component.For<IIrcConfiguration>()
                    .Instance(
                        new IrcConfiguration(
                            hostname: "niven.freenode.net",
                            port: 9003,
                            authToServices: true,
                            nickname: "stwtestbot",
                            username: "stwtestbot",
                            realName: "stwtestbot",
                            password: "stwtestbot",
                            ssl: true,
                            clientName: "TestClient",
                            restartOnHeavyLag: false
                        )));
            
            container.Install(new Installer());
            
            var app = container.Resolve<Launch>();
        }

        public Launch(IIrcClient client)
        {
            client.JoinChannel("##stwalkerster-development");
            client.ReceivedMessage += (sender, args) =>
            {
                if (!args.IsNotice)
                {
                    if (args.Message == ".quit")
                    {
                        (args.Client as IrcClient)?.Inject("QUIT :*waves*");
                    }
                    
                    var message = new Message(
                        "PRIVMSG",
                        new[] {"##stwalkerster-development", args.User.ToString() + " -> " + args.Client.Nickname});
                    args.Client.Send(message);
                }
                else
                {
                    if (args.Target == "##stwalkerster-development")
                    {
                        (args.Client as IrcClient)?.Inject(args.Message);
                    }
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
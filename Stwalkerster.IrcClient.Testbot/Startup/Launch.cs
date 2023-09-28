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
            var server = new MetricServer(9102);
            server.Start();
            
            var container = new WindsorContainer();

            var libera = new IrcConfiguration(
                hostname: "irc.libera.chat",
                port: 6697,
                authToServices: false,
                nickname: "stwtestbot",
                username: "stwtestbot",
                realName: "stwtestbot",
                servicesUsername: "stwtestbot",
                servicesPassword: "stwtestbot",
                ssl: true,
                clientName: "TestClient",
                restartOnHeavyLag: false,
                connectModes: null
            );

            
            container.Register(
                Component.For<IIrcConfiguration>()
                    .Instance(libera));
            
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
                        new[] {"##stwalkerster-development", args.User.ToString() + " -> " + args.Client.Latency + "s lag, " + args.Client.PrivmsgReceived +" messages"});
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
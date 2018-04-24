namespace Stwalkerster.IrcClient.Testbot.Startup
{
    using Castle.Facilities.EventWiring;
    using Castle.Facilities.Logging;
    using Castle.Facilities.Startable;
    using Castle.MicroKernel.Registration;
    using Castle.MicroKernel.SubSystems.Configuration;
    using Castle.Services.Logging.Log4netIntegration;
    using Castle.Windsor;
    using Stwalkerster.IrcClient.Interfaces;

    public class Installer : IWindsorInstaller
    {
        public void Install(IWindsorContainer container, IConfigurationStore store)
        {
            container.AddFacility<LoggingFacility>(f => f.LogUsing<Log4netFactory>().WithConfig("log4net.xml"));
            container.AddFacility<EventWiringFacility>();
            container.AddFacility<StartableFacility>(f => f.DeferredStart());

            container.Install(new Stwalkerster.IrcClient.Installer());
            container.Register(Component.For<IIrcClient>().ImplementedBy<IrcClient>());
            container.Register(Component.For<Launch>().ImplementedBy<Launch>());
        }
    }
}
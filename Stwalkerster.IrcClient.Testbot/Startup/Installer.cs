namespace Stwalkerster.IrcClient.Testbot.Startup
{
    using Castle.Facilities.Startable;
    using Castle.MicroKernel.Registration;
    using Castle.MicroKernel.SubSystems.Configuration;
    using Castle.Windsor;
    using Microsoft.Extensions.Logging;
    using Stwalkerster.IrcClient.Interfaces;

    public class Installer : IWindsorInstaller
    {
        public void Install(IWindsorContainer container, IConfigurationStore store)
        {
            var loggerFactory = new LoggerFactory().AddLog4Net("log4net.xml");
            
            container.AddFacility<StartableFacility>(f => f.DeferredStart());

            container.Register(Component.For<ILoggerFactory>().Instance(loggerFactory));
            container.Register(Component.For<ILogger<SupportHelper>>().UsingFactoryMethod(loggerFactory.CreateLogger<SupportHelper>));
            
            
            container.Register(Component.For<ISupportHelper>().ImplementedBy<SupportHelper>());
            container.Register(Component.For<IIrcClient>().ImplementedBy<IrcClient>());
            container.Register(Component.For<Launch>().ImplementedBy<Launch>());
        }
    }
}
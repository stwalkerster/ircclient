namespace Stwalkerster.IrcClient
{
    using Castle.MicroKernel.Registration;
    using Castle.MicroKernel.SubSystems.Configuration;
    using Castle.Windsor;
    using Stwalkerster.IrcClient.Interfaces;

    public class Installer : IWindsorInstaller
    {
        public void Install(IWindsorContainer container, IConfigurationStore store)
        {
            container.Register(
                Component.For<ISupportHelper>().ImplementedBy<SupportHelper>()
            );
        }
    }
}
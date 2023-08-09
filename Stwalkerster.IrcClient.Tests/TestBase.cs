namespace Stwalkerster.IrcClient.Tests
{
    using Microsoft.Extensions.Logging;
    using NSubstitute;
    using NUnit.Framework;
    using Stwalkerster.IrcClient.Interfaces;

    /// <summary>
    /// The base class for all the unit tests.
    /// </summary>
    public abstract class TestBase
    {
        /// <summary>
        /// Gets or sets the IRC configuration.
        /// </summary>
        protected IIrcConfiguration IrcConfiguration { get; set; }

        /// <summary>
        /// Gets or sets the logger.
        /// </summary>
        protected ILogger<IrcClient> Logger { get; set; }
        
        /// <summary>
        /// The common setup.
        /// </summary>
        [OneTimeSetUp]
        public void CommonSetup()
        {
            this.Logger = Substitute.For<ILogger<IrcClient>>();
            this.IrcConfiguration = Substitute.For<IIrcConfiguration>();

            this.LocalSetup();
        }

        /// <summary>
        /// The local setup.
        /// </summary>
        public virtual void LocalSetup()
        {
        }
    }
}

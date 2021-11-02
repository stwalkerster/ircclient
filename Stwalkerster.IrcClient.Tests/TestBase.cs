namespace Stwalkerster.IrcClient.Tests
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Extensions.Logging;
    using Moq;
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
        protected Mock<IIrcConfiguration> IrcConfiguration { get; set; }

        /// <summary>
        /// Gets or sets the logger.
        /// </summary>
        protected Mock<ILogger<IrcClient>> Logger { get; set; }
        
        /// <summary>
        /// The SupportHelper mock
        /// </summary>
        protected Mock<ISupportHelper> SupportHelper { get; set; }

        /// <summary>
        /// The common setup.
        /// </summary>
        [OneTimeSetUp]
        public void CommonSetup()
        {
            this.Logger = new Mock<ILogger<IrcClient>>();
            // ReSharper disable TemplateIsNotCompileTimeConstantProblem
            this.Logger.Setup(x => x.LogError(It.IsAny<string>())).Callback<string>((s) => Assert.Fail("Logger recorded error - " + s));
            this.Logger.Setup(x => x.LogError(It.IsAny<Exception>(), It.IsAny<string>())).Callback(() => Assert.Fail("Logger recorded error."));
            this.Logger.Setup(x => x.LogError(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<IEnumerable<object>>())).Callback(() => Assert.Fail("Logger recorded error."));
            this.Logger.Setup(x => x.LogError(It.IsAny<string>(), It.IsAny<IEnumerable<object>>())).Callback(() => Assert.Fail("Logger recorded error.")); 
            
            // ReSharper restore TemplateIsNotCompileTimeConstantProblem

            this.IrcConfiguration = new Mock<IIrcConfiguration>();
            
            this.SupportHelper = new Mock<ISupportHelper>();

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

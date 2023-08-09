namespace Stwalkerster.IrcClient.Tests.IRC.Model
{
    using NSubstitute;
    using NUnit.Framework;
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Model;

    /// <summary>
    /// The IRC user tests.
    /// </summary>
    [TestFixture]
    public class IrcUserTests : TestBase
    {
        private IIrcClient client;
        
        [OneTimeSetUp]
        public void FixtureSetup()
        {
            this.client = Substitute.For<IIrcClient>();
        }
        
        /// <summary>
        /// The should create from prefix.
        /// </summary>
        [Test]
        public void ShouldCreateFromPrefix()
        {
            // arrange
            const string Prefix = "Yetanotherx|afk!~Yetanothe@mcbouncer.com";
            var expected = new IrcUser(this.client)
                               {
                                   Hostname = "mcbouncer.com",
                                   Username = "~Yetanothe",
                                   Nickname = "Yetanotherx|afk"
                               };

            // act
            var actual = IrcUser.FromPrefix(Prefix, this.client);

            // assert
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// The should create from prefix 2.
        /// </summary>
        [Test]
        public void ShouldCreateFromPrefix2()
        {
            // arrange
            const string Prefix = "stwalkerster@foo.com";
            var expected = new IrcUser(this.client)
            {
                Hostname = "foo.com",
                Nickname = "stwalkerster"
            };

            // act
            var actual = IrcUser.FromPrefix(Prefix, this.client);

            // assert
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// The should create from prefix 3.
        /// </summary>
        [Test]
        public void ShouldCreateFromPrefix3()
        {
            // arrange
            const string Prefix = "stwalkerster";
            var expected = new IrcUser(this.client)
            {
                Nickname = "stwalkerster"
            };

            // act
            var actual = IrcUser.FromPrefix(Prefix, this.client);

            // assert
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// The should create from prefix.
        /// </summary>
        [Test]
        public void ShouldCreateFromPrefix4()
        {
            // arrange
            const string Prefix = "nick!user@host";
            var expected = new IrcUser(this.client)
            {
                Hostname = "host",
                Username = "user",
                Nickname = "nick"
            };

            // act
            var actual = IrcUser.FromPrefix(Prefix, this.client);

            // assert
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// The should create from prefix.
        /// </summary>
        [Test]
        public void ShouldCreateFromPrefix5()
        {
            // arrange
            const string Prefix = "ChanServ!ChanServ@services.";
            var expected = new IrcUser(this.client)
            {
                Hostname = "services.",
                Username = "ChanServ",
                Nickname = "ChanServ"
            };

            // act
            var actual = IrcUser.FromPrefix(Prefix, this.client);

            // assert
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}

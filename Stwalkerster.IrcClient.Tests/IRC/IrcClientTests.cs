namespace Stwalkerster.IrcClient.Tests.IRC
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using Microsoft.Extensions.Logging;
    using Moq;
    using NUnit.Framework;
    using Stwalkerster.IrcClient.Events;
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Model;
    using Stwalkerster.IrcClient.Model.Interfaces;

    /// <summary>
    /// The irc client tests.
    /// </summary>
    [TestFixture]
    public class IrcClientTests : TestBase
    {
        /// <summary>
        /// The test join processed correctly.
        /// </summary>
        [Test]
        public void TestJoinProcessedCorrectly()
        {
            var network = new Mock<INetworkClient>();
            this.IrcConfiguration.Setup(x => x.Nickname).Returns("nickname");
            this.IrcConfiguration.Setup(x => x.Username).Returns("username");
            this.IrcConfiguration.Setup(x => x.RealName).Returns("real name");         
            this.IrcConfiguration.Setup(x => x.ClientName).Returns("client");

            var client = new IrcClient(network.Object, this.Logger.Object, this.IrcConfiguration.Object, this.SupportHelper.Object);

            // init IRC
            // Setup capabilities
            network.Raise(
                x => x.DataReceived += null, 
                new DataReceivedEventArgs(":testnet CAP * ACK :account-notify extended-join multi-prefix"));

            // Complete registration
            network.Raise(x => x.DataReceived += null, new DataReceivedEventArgs(":testnet 001 nickname :Welcome"));

            // Join a channel
            network.Raise(
                x => x.DataReceived += null, 
                new DataReceivedEventArgs(":nickname!username@hostname JOIN #channel * :real name"));

            // Grab the actual user out when a JOIN event is raised
            IUser actualUser = null;
            client.JoinReceivedEvent += (sender, args) => actualUser = args.User;

            // get ChanServ to join the channel
            network.Raise(
                x => x.DataReceived += null, 
                new DataReceivedEventArgs(":ChanServ!ChanServ@services. JOIN #channel * :Channel Services"));

            // Double check we got it
            Assert.That(actualUser, Is.Not.Null);
            Assert.That(actualUser.Nickname, Is.EqualTo("ChanServ"));
            Assert.That(actualUser.Username, Is.EqualTo("ChanServ"));
            Assert.That(actualUser.Hostname, Is.EqualTo("services."));
            Assert.That(actualUser.Account, Is.Null);
        }

        /// <summary>
        /// HMB-169
        /// </summary>
        [Test]
        public void TestUserFleshedOnJoin()
        {
            var network = new Mock<INetworkClient>();
            this.IrcConfiguration.Setup(x => x.Nickname).Returns("nickname");
            this.IrcConfiguration.Setup(x => x.Username).Returns("username");
            this.IrcConfiguration.Setup(x => x.RealName).Returns("real name");
            this.IrcConfiguration.Setup(x => x.ClientName).Returns("client");

            var client = new IrcClient(network.Object, this.Logger.Object, this.IrcConfiguration.Object, this.SupportHelper.Object);

            // init IRC
            // Setup capabilities
            network.Raise(
                x => x.DataReceived += null,
                new DataReceivedEventArgs(":testnet CAP * ACK :account-notify extended-join multi-prefix"));

            // Complete registration
            network.Raise(x => x.DataReceived += null, new DataReceivedEventArgs(":testnet 001 nickname :Welcome"));

            var data = new[]
                           {
                               ":nickname!username@hostname JOIN #wikipedia-en-helpers * :real name",
                               ":testnet 332 nickname #wikipedia-en-helpers :Channel topic here",
                               ":testnet 333 nickname #wikipedia-en-helpers Matthew_!~Matthewrb@wikimedia/matthewrbowker 1453362294",
                               ":testnet 353 nickname = #wikipedia-en-helpers :nickname FastLizard4",
                               ":testnet 366 nickname #wikipedia-en-helpers :End of /NAMES list."
                           };

            // Join a channel
            foreach (var s in data)
            {
                network.Raise(x => x.DataReceived += null, new DataReceivedEventArgs(s));
            }

            Assert.That(client.UserCache.ContainsKey("FastLizard4"));

            // OK, Flizzy should still be a skeleton.
            Assert.That(client.UserCache["FastLizard4"].SkeletonStatus, Is.EqualTo(IrcUserSkeletonStatus.NickOnly));

            // ... and stwalkerster shouldn't exist.
            Assert.That(client.UserCache.ContainsKey("stwalkerster"), Is.False);

            // stwalkerster joins the channel
            var join = ":stwalkerster!~stwalkers@wikimedia/stwalkerster JOIN #wikipedia-en-helpers stwalkerster :realname";
            network.Raise(x => x.DataReceived += null, new DataReceivedEventArgs(join));

            // ... and stwalkerster should now exist as a real user
            Assert.That(client.UserCache.ContainsKey("stwalkerster"));
            Assert.That(client.UserCache["stwalkerster"].SkeletonStatus, Is.EqualTo(IrcUserSkeletonStatus.Account));
            Assert.That(client.UserCache["stwalkerster"].Username, Is.EqualTo("~stwalkers"));
            Assert.That(client.UserCache["stwalkerster"].Hostname, Is.EqualTo("wikimedia/stwalkerster"));
            Assert.That(client.UserCache["stwalkerster"].Account, Is.EqualTo("stwalkerster"));

            // Flizzy does a /nick
            var nick = ":FastLizard4!fastlizard@wikipedia/pdpc.active.FastLizard4 NICK :werelizard";
            network.Raise(x => x.DataReceived += null, new DataReceivedEventArgs(nick));

            // ... and werelizard should now exist as a real user, but not Flizzy
            Assert.That(client.UserCache.ContainsKey("FastLizard4"), Is.False);
            Assert.That(client.UserCache.ContainsKey("werelizard"), Is.True);
            Assert.That(client.UserCache["werelizard"].SkeletonStatus, Is.EqualTo(IrcUserSkeletonStatus.PrefixOnly));
            Assert.That(client.UserCache["werelizard"].Username, Is.EqualTo("fastlizard"));
            Assert.That(client.UserCache["werelizard"].Hostname, Is.EqualTo("wikipedia/pdpc.active.FastLizard4"));
            Assert.That(client.UserCache["werelizard"].Nickname, Is.EqualTo("werelizard"));
        }

        [Test]
        public void TestDisconnectEventRaised()
        {
            var logger = new Mock<ILogger<IrcClient>>();
            
            // arrange
            var networkClient = new Mock<INetworkClient>();
            this.IrcConfiguration.Setup(x => x.Nickname).Returns("nick");
            this.IrcConfiguration.Setup(x => x.Username).Returns("username");
            this.IrcConfiguration.Setup(x => x.RealName).Returns("real name");
            this.IrcConfiguration.Setup(x => x.ClientName).Returns("client");
            this.IrcConfiguration.Setup(x => x.RestartOnHeavyLag).Returns(false);
            this.SupportHelper
                .Setup(x => x.HandlePrefixMessageSupport(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
                .Callback(
                    (string s, IDictionary<string, string> r) =>
                    {
                        r.Add("v", "+");
                        r.Add("o", "@");
                    });
            var client = new IrcClient(networkClient.Object, logger.Object, this.IrcConfiguration.Object, this.SupportHelper.Object);

            // shortcuts
            Action<string> i = data => networkClient.Raise(
                x => x.DataReceived += null,
                networkClient.Object,
                new DataReceivedEventArgs(data));
            Action<string> o = data => networkClient.Verify(x => x.Send(data));
            
            o("CAP LS 302");
            i(":orwell.freenode.net CAP * LS :account-notify extended-join identify-msg multi-prefix sasl");
            o("CAP REQ :account-notify extended-join multi-prefix");
            i(":orwell.freenode.net CAP * ACK :account-notify extended-join multi-prefix ");
            o("CAP END");
            o("USER username * * :real name");
            o("NICK nick");
            i(":orwell.freenode.net 001 nick :Welcome to the freenode Internet Relay Chat Network nick");
            i(":stwtestbot MODE nick :+i");
            o("MODE nick +Q");
            i(":nick MODE nick :+Q");

            bool fired = false;
            client.DisconnectedEvent += (sender, args) => { fired = true; }; 
            
            networkClient.Raise(x => x.Disconnected += null, networkClient, new EventArgs());

            networkClient.Verify(x => x.Disconnect(), Times.Once());
            Assert.True(fired);
        }
        
        [Ignore("Failing due to lag disconnect firing fatal log message, which appears to be unintentionally caught")]
        [Test]
        public void TestDisconnectEventRaisedOnTimeout()
        {
            var logger = new Mock<ILogger<IrcClient>>();
            
            // arrange
            var networkClient = new Mock<INetworkClient>();
            this.IrcConfiguration.Setup(x => x.Nickname).Returns("nick");
            this.IrcConfiguration.Setup(x => x.Username).Returns("username");
            this.IrcConfiguration.Setup(x => x.RealName).Returns("real name");
            this.IrcConfiguration.Setup(x => x.ClientName).Returns("client");
            this.IrcConfiguration.Setup(x => x.RestartOnHeavyLag).Returns(true);
            this.IrcConfiguration.Setup(x => x.MissedPingLimit).Returns(3);
            this.SupportHelper
                .Setup(x => x.HandlePrefixMessageSupport(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
                .Callback(
                    (string s, IDictionary<string, string> r) =>
                    {
                        r.Add("v", "+");
                        r.Add("o", "@");
                    });
            var client = new IrcClient(networkClient.Object, logger.Object, this.IrcConfiguration.Object, this.SupportHelper.Object);
            client.PingTimeout = 1;
            client.PingInterval = 1;
            
            // shortcuts
            Action<string> i = data => networkClient.Raise(
                x => x.DataReceived += null,
                networkClient.Object,
                new DataReceivedEventArgs(data));
            Action<string> o = data => networkClient.Verify(x => x.Send(data));
            Action<string> op = data => networkClient.Verify(x => x.PrioritySend(It.Is<string>(s => s.StartsWith(data))));
            
            o("CAP LS 302");
            i(":orwell.freenode.net CAP * LS :account-notify extended-join identify-msg multi-prefix sasl");
            o("CAP REQ :account-notify extended-join multi-prefix");
            i(":orwell.freenode.net CAP * ACK :account-notify extended-join multi-prefix ");
            o("CAP END");
            o("USER username * * :real name");
            o("NICK nick");
            i(":orwell.freenode.net 001 nick :Welcome to the freenode Internet Relay Chat Network nick");
            i(":stwtestbot MODE nick :+i");
            o("MODE nick +Q");
            i(":nick MODE nick :+Q");
            
            bool fired = false;
            client.DisconnectedEvent += (sender, args) => { fired = true; };
            
            Thread.Sleep(1000);
            op("PING ");
            Thread.Sleep(2000);
            Assert.False(fired);
            op("PING ");
            Assert.False(fired);
            Thread.Sleep(2000);
            Assert.False(fired);
            op("PING ");
            Assert.False(fired);
            Thread.Sleep(2000);
            Assert.True(fired);

            networkClient.Verify(x => x.Disconnect(), Times.Once());
            Assert.True(fired);
        }
    }
}
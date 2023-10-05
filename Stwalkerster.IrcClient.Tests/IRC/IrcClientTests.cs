namespace Stwalkerster.IrcClient.Tests.IRC
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using Exceptions;
    using Microsoft.Extensions.Logging;
    using NSubstitute;
    using NUnit.Framework;
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Model;
    using Stwalkerster.IrcClient.Model.Interfaces;
    using DataReceivedEventArgs = Events.DataReceivedEventArgs;

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
            var network = Substitute.For<INetworkClient>();
            var supportHelper = Substitute.For<ISupportHelper>();
            
            this.IrcConfiguration.Nickname.Returns("nickname");
            this.IrcConfiguration.Username.Returns("username");
            this.IrcConfiguration.RealName.Returns("real name");
            this.IrcConfiguration.ClientName.Returns("client");

            var client = new IrcClient(network, this.Logger, this.IrcConfiguration, supportHelper);

            // init IRC
            // Setup capabilities
            network.DataReceived +=
                Raise.EventWith(new DataReceivedEventArgs(":testnet CAP * ACK :account-notify extended-join multi-prefix"));
            
            // Complete registration
            network.DataReceived +=
                Raise.EventWith(new DataReceivedEventArgs(":testnet 001 nickname :Welcome"));

            // Join a channel
            network.DataReceived +=
                Raise.EventWith(new DataReceivedEventArgs(":nickname!username@hostname JOIN #channel * :real name"));

            // Grab the actual user out when a JOIN event is raised
            IUser actualUser = null;
            client.JoinReceivedEvent += (sender, args) => actualUser = args.User;

            // get ChanServ to join the channel
            network.DataReceived +=
                Raise.EventWith(new DataReceivedEventArgs(":ChanServ!ChanServ@services. JOIN #channel * :Channel Services"));

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
            var network = Substitute.For<INetworkClient>();
            var supportHelper = Substitute.For<ISupportHelper>();
            
            this.IrcConfiguration.Nickname.Returns("nickname");
            this.IrcConfiguration.Username.Returns("username");
            this.IrcConfiguration.RealName.Returns("real name");
            this.IrcConfiguration.ClientName.Returns("client");

            var client = new IrcClient(network, this.Logger, this.IrcConfiguration, supportHelper);

            // init IRC
            // Setup capabilities
            network.DataReceived +=
                Raise.EventWith(new DataReceivedEventArgs(":testnet CAP * ACK :account-notify extended-join multi-prefix"));

            // Complete registration
            network.DataReceived +=
                Raise.EventWith(new DataReceivedEventArgs(":testnet 001 nickname :Welcome"));

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
                network.DataReceived += Raise.EventWith(new DataReceivedEventArgs(s));
            }

            Assert.That(client.UserCache.ContainsKey("FastLizard4"));

            // OK, Flizzy should still be a skeleton.
            Assert.That(client.UserCache["FastLizard4"].SkeletonStatus, Is.EqualTo(IrcUserSkeletonStatus.NickOnly));

            // ... and stwalkerster shouldn't exist.
            Assert.That(client.UserCache.ContainsKey("stwalkerster"), Is.False);

            // stwalkerster joins the channel
            var join = ":stwalkerster!~stwalkers@wikimedia/stwalkerster JOIN #wikipedia-en-helpers stwalkerster :realname";
            network.DataReceived += Raise.EventWith(new DataReceivedEventArgs(join));

            // ... and stwalkerster should now exist as a real user
            Assert.That(client.UserCache.ContainsKey("stwalkerster"));
            Assert.That(client.UserCache["stwalkerster"].SkeletonStatus, Is.EqualTo(IrcUserSkeletonStatus.Account));
            Assert.That(client.UserCache["stwalkerster"].Username, Is.EqualTo("~stwalkers"));
            Assert.That(client.UserCache["stwalkerster"].Hostname, Is.EqualTo("wikimedia/stwalkerster"));
            Assert.That(client.UserCache["stwalkerster"].Account, Is.EqualTo("stwalkerster"));

            // Flizzy does a /nick
            var nick = ":FastLizard4!fastlizard@wikipedia/pdpc.active.FastLizard4 NICK :werelizard";
            network.DataReceived += Raise.EventWith(new DataReceivedEventArgs(nick));

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
            var logger = Substitute.For<ILogger<IrcClient>>();
            var supportHelper = Substitute.For<ISupportHelper>();

            // arrange
            var networkClient = Substitute.For<INetworkClient>();
            this.IrcConfiguration.Nickname.Returns("nick");
            this.IrcConfiguration.Username.Returns("username");
            this.IrcConfiguration.RealName.Returns("real name");
            this.IrcConfiguration.ClientName.Returns("client");
            this.IrcConfiguration.RestartOnHeavyLag.Returns(false);
            supportHelper.HandlePrefixMessageSupport(
                Arg.Any<string>(),
                Arg.Do<IDictionary<string, string>>(
                    r =>
                    {
                        r.Add("v", "+");
                        r.Add("o", "@");
                    }));
            var client = new IrcClient(networkClient, logger, this.IrcConfiguration, supportHelper);

            // shortcuts
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => networkClient.Received().Send(data);
            
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
            
            networkClient.Disconnected += Raise.Event();

            networkClient.Received(1).Disconnect();
            Assert.True(fired);
        }
        
        [Ignore("Failing due to lag disconnect firing fatal log message, which appears to be unintentionally caught")]
        [Test]
        public void TestDisconnectEventRaisedOnTimeout()
        {
            var logger = Substitute.For<ILogger<IrcClient>>();
            var supportHelper = Substitute.For<ISupportHelper>();
            
            // arrange
            var networkClient = Substitute.For<INetworkClient>();
            this.IrcConfiguration.Nickname.Returns("nick");
            this.IrcConfiguration.Username.Returns("username");
            this.IrcConfiguration.RealName.Returns("real name");
            this.IrcConfiguration.ClientName.Returns("client");
            this.IrcConfiguration.RestartOnHeavyLag.Returns(true);
            this.IrcConfiguration.MissedPingLimit.Returns(3);

            supportHelper.HandlePrefixMessageSupport(
                Arg.Any<string>(),
                Arg.Do<IDictionary<string, string>>(
                    r =>
                    {
                        r.Add("v", "+");
                        r.Add("o", "@");
                    }));
            
            var client = new IrcClient(networkClient, logger, this.IrcConfiguration, supportHelper);
            client.PingTimeout = 1;
            client.PingInterval = 1;
            
            // shortcuts
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => networkClient.Received().Send(data);
            Action<string> op = data => networkClient.Received().PrioritySend(Arg.Is<string>(s => s.StartsWith(data)));
            
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

            networkClient.Received(1).Disconnect();
            Assert.True(fired);
        }

        public static IEnumerable StatusMsgTestData
        {
            get
            {
                yield return new TestCaseData(null, "PRIVMSG #channel :test message");
                yield return new TestCaseData(DestinationFlags.VoicedUsers, "PRIVMSG +#channel :test message");
                yield return new TestCaseData(DestinationFlags.ChannelOperators, "PRIVMSG @#channel :test message");
                yield return new TestCaseData(DestinationFlags.FromChar("%"), "PRIVMSG %#channel :test message");
            }
        }


        [Test, TestCaseSource(typeof(IrcClientTests), nameof(StatusMsgTestData))]
        public void TestStatusMsgPrefix(DestinationFlags flag, string expectedMessage)
        {
            var logger = Substitute.For<ILogger<IrcClient>>();
            var supportHelper = Substitute.For<ISupportHelper>();

            // arrange
            var networkClient = Substitute.For<INetworkClient>();
            this.IrcConfiguration.Nickname.Returns("nick");
            this.IrcConfiguration.Username.Returns("username");
            this.IrcConfiguration.RealName.Returns("real name");
            this.IrcConfiguration.ClientName.Returns("client");
            this.IrcConfiguration.RestartOnHeavyLag.Returns(false);
            supportHelper.HandlePrefixMessageSupport(
                Arg.Any<string>(),
                Arg.Do<IDictionary<string, string>>(
                    r =>
                    {
                        r.Add("v", "+");
                        r.Add("o", "@");
                        r.Add("h", "%");
                    }));
            supportHelper.HandleStatusMessageSupport(
                Arg.Any<string>(),
                Arg.Do<IList<string>>(
                    list =>
                    {
                        list.Add("+");
                        list.Add("@");
                        list.Add("%");
                    }));
            var client = new IrcClient(networkClient, logger, this.IrcConfiguration, supportHelper);

            // shortcuts
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => networkClient.Received().Send(data);
            
            o("CAP LS 302");
            i(":copper.libera.chat CAP * LS :account-notify extended-join multi-prefix");
            o("CAP REQ :account-notify extended-join multi-prefix");
            i(":copper.libera.chat CAP * ACK :account-notify extended-join multi-prefix ");
            o("CAP END");
            o("USER username * * :real name");
            o("NICK nick");
            i(":copper.libera.chat 001 stwtest :Welcome to the Libera.Chat Internet Relay Chat Network stwtest");
            i(":copper.libera.chat 005 stwtest CHANMODES=eIbq,k,flj,CFLMPQSTcgimnprstuz CHANLIMIT=#:250 PREFIX=(ovh)@+% MAXLIST=bqeI:100 MODES=4 NETWORK=stwalkerster.net STATUSMSG=@+% CASEMAPPING=rfc1459 NICKLEN=16 MAXNICKLEN=16 CHANNELLEN=50 TOPICLEN=390 :are supported by this server");
            i(":stwtestbot MODE nick :+Ziw");
            o("MODE nick +Q");
            i(":nick MODE nick :+Q");

            Assert.That(client.StatusMsgDestinationFlags, Contains.Item("@"));
            Assert.That(client.StatusMsgDestinationFlags, Contains.Item("+"));
            Assert.That(client.StatusMsgDestinationFlags, Contains.Item("%"));
            
            networkClient.ClearReceivedCalls();
            
            client.SendMessage("#channel", "test message", flag);
            o(expectedMessage);
        }
        
        [Test]
        public void TestBadStatusMsgPrefix()
        {
            var logger = Substitute.For<ILogger<IrcClient>>();
            var supportHelper = Substitute.For<ISupportHelper>();

            // arrange
            var networkClient = Substitute.For<INetworkClient>();
            this.IrcConfiguration.Nickname.Returns("nick");
            this.IrcConfiguration.Username.Returns("username");
            this.IrcConfiguration.RealName.Returns("real name");
            this.IrcConfiguration.ClientName.Returns("client");
            this.IrcConfiguration.ConnectModes.Returns("+Q");
            this.IrcConfiguration.RestartOnHeavyLag.Returns(false);
            supportHelper.HandlePrefixMessageSupport(
                Arg.Any<string>(),
                Arg.Do<IDictionary<string, string>>(
                    r =>
                    {
                        r.Add("v", "+");
                        r.Add("o", "@");
                    }));
            supportHelper.HandleStatusMessageSupport(
                Arg.Any<string>(),
                Arg.Do<IList<string>>(
                    list =>
                    {
                        list.Add("+");
                        list.Add("@");
                    }));
            
            var client = new IrcClient(networkClient, logger, this.IrcConfiguration, supportHelper);

            // shortcuts
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => networkClient.Received().Send(data);
            
            o("CAP LS 302");
            i(":copper.libera.chat CAP * LS :account-notify extended-join multi-prefix");
            o("CAP REQ :account-notify extended-join multi-prefix");
            i(":copper.libera.chat CAP * ACK :account-notify extended-join multi-prefix ");
            o("CAP END");
            o("USER username * * :real name");
            o("NICK nick");
            i(":copper.libera.chat 001 stwtest :Welcome to the Libera.Chat Internet Relay Chat Network stwtest");
            i(":copper.libera.chat 005 stwtest CHANMODES=eIbq,k,flj,CFLMPQSTcgimnprstuz CHANLIMIT=#:250 PREFIX=(ovh)@+% MAXLIST=bqeI:100 MODES=4 NETWORK=stwalkerster.net STATUSMSG=@+% CASEMAPPING=rfc1459 NICKLEN=16 MAXNICKLEN=16 CHANNELLEN=50 TOPICLEN=390 :are supported by this server");
            i(":stwtestbot MODE nick :+Ziw");
            o("MODE nick +Q");
            i(":nick MODE nick :+Q");

            Assert.That(client.StatusMsgDestinationFlags, Contains.Item("@"));
            Assert.That(client.StatusMsgDestinationFlags, Contains.Item("+"));
            
            networkClient.ClearReceivedCalls();
            
            Assert.Throws<OperationNotSupportedException>(
                () =>
                    client.SendMessage("#channel", "test message", DestinationFlags.FromChar("%"))
            );
        }

        [Test]
        public void TestInspircdChannelModes()
        {
            var logger = Substitute.For<ILogger<IrcClient>>();
            var supportLogger = Substitute.For<ILogger<SupportHelper>>();
            var supportHelper = new SupportHelper(supportLogger);

            // arrange
            var networkClient = Substitute.For<INetworkClient>();
            this.IrcConfiguration.Nickname.Returns("HMBDebug");
            this.IrcConfiguration.Username.Returns("stwtestbot");
            this.IrcConfiguration.RealName.Returns("stwtestbot");
            this.IrcConfiguration.ClientName.Returns("client");
            this.IrcConfiguration.RestartOnHeavyLag.Returns(false);
            
            var client = new IrcClient(networkClient, logger, this.IrcConfiguration, supportHelper);

            // shortcuts
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => networkClient.Received().Send(data);
            
            // act
            o("CAP LS 302");
            i(":jasper.lizardirc.org CAP 930AAG9WU LS :away-notify extended-join account-notify multi-prefix sasl tls");
            o("CAP REQ :away-notify extended-join account-notify multi-prefix");
            i(":jasper.lizardirc.org CAP 930AAG9WU ACK :away-notify extended-join account-notify multi-prefix");
            o("CAP END");
            o("USER stwtestbot * * stwtestbot");
            o("NICK HMBDebug");
            i(":jasper.lizardirc.org 001 HMBDebug :Welcome to the LizardIRC IRC Network HMBDebug!~stwtestbot@livi-07-b2-v4wan-165258-cust132.vm6.cable.virginm.net");
            i(":jasper.lizardirc.org 005 HMBDebug AWAYLEN=200 CALLERID=g CASEMAPPING=rfc1459 CHANMODES=IXZbegw,k,FHJLVdfjl,ABCKMOPRSTcimnprstuz CHANNELLEN=64 CHANTYPES=# CHARSET=ascii ELIST=MU EXCEPTS=e EXTBAN=,ABCORSTUcjmrz FNC INVEX=I KICKLEN=255 :are supported by this server");
            i(":jasper.lizardirc.org 005 HMBDebug MAP MAXBANS=60 MAXCHANNELS=200 MAXPARA=32 MAXTARGETS=20 MODES=20 NAMESX NETWORK=LizardIRC NICKLEN=64 OVERRIDE PREFIX=(Yqaohv)!~&@%+ REMOVE SECURELIST :are supported by this server");
            i(":jasper.lizardirc.org 005 HMBDebug SSL=[::]:6697 STARTTLS STATUSMSG=!~&@%+ TOPICLEN=80000 USERIP VBANLIST WALLCHOPS WALLVOICES WATCH=1024 :are supported by this server");
            i(":HMBDebug!~stwtestbot@lizardirc/user/stwalkerster/bot JOIN ##stwalkerster-development stwbot :stwtestbot");
            i(":jasper.lizardirc.org 353 HMBDebug @ ##stwalkerster-development :@HMBDebug ");
            i(":jasper.lizardirc.org 366 HMBDebug ##stwalkerster-development :End of /NAMES list.");
            o("WHO ##stwalkerster-development %uhnatfc,001");
            i(":jasper.lizardirc.org 352 HMBDebug ##stwalkerster-development ~stwtestbot lizardirc/user/stwalkerster/bot jasper.lizardirc.org HMBDebug H@ :0 stwtestbot");
            i(":jasper.lizardirc.org 315 HMBDebug ##stwalkerster-development :End of /WHO list.");
            o("MODE ##stwalkerster-development");
            i(":jasper.lizardirc.org 324 HMBDebug ##stwalkerster-development +nst");
            i(":jasper.lizardirc.org 329 HMBDebug ##stwalkerster-development 1695929082");
            i(":stwalkerster!~stwalkerster@lizardirc/staff/stwalkerster JOIN ##stwalkerster-development stwalkerster :stwalkerster");
            i(":ChanServ!ChanServ@services.lizardirc JOIN ##stwalkerster-development * :Channel Services");
            i(":services.lizardirc MODE ##stwalkerster-development +o ChanServ");
            
            i(":ChanServ!ChanServ@services.lizardirc MODE ##stwalkerster-development +qo HMBDebug HMBDebug");

            Assert.IsTrue(client.Channels["##stwalkerster-development"].Users["HMBDebug"].ToString().Contains(" @~ "));

        }
        
        [Test]
        public void TestInspircdNoCapChghost()
        {
            var logger = Substitute.For<ILogger<IrcClient>>();
            var supportLogger = Substitute.For<ILogger<SupportHelper>>();
            var supportHelper = new SupportHelper(supportLogger);

            // arrange
            var networkClient = Substitute.For<INetworkClient>();
            this.IrcConfiguration.Nickname.Returns("boopbot");
            this.IrcConfiguration.Username.Returns("boopbot");
            this.IrcConfiguration.RealName.Returns("stwtestbot");
            this.IrcConfiguration.ClientName.Returns("client");
            this.IrcConfiguration.RestartOnHeavyLag.Returns(false);
            
            var client = new IrcClient(networkClient, logger, this.IrcConfiguration, supportHelper);

            // shortcuts
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => networkClient.Received().Send(data);
            
            // act
            o("CAP LS 302");
            i(":jasper.lizardirc.org CAP 930AAG9WU LS :away-notify extended-join account-notify multi-prefix sasl tls");
            o("CAP REQ :away-notify extended-join account-notify multi-prefix");
            i(":jasper.lizardirc.org CAP 930AAG9WU ACK :away-notify extended-join account-notify multi-prefix");
            o("CAP END");
            o("USER boopbot * * stwtestbot");
            o("NICK boopbot");
            i(":jasper.lizardirc.org 001 boopbot :Welcome to the LizfardIRC IRC Network boopbot!~stwtestbot@stwalkerster.co.uk");
            i(":jasper.lizardirc.org 005 boopbot AWAYLEN=200 CALLERID=g CASEMAPPING=rfc1459 CHANMODES=IXZbegw,k,FHJLVdfjl,ABCKMOPRSTcimnprstuz CHANNELLEN=64 CHANTYPES=# CHARSET=ascii ELIST=MU EXCEPTS=e EXTBAN=,ABCORSTUcjmrz FNC INVEX=I KICKLEN=255 :are supported by this server");
            i(":jasper.lizardirc.org 005 boopbot MAP MAXBANS=60 MAXCHANNELS=200 MAXPARA=32 MAXTARGETS=20 MODES=20 NAMESX NETWORK=LizardIRC NICKLEN=64 OVERRIDE PREFIX=(Yqaohv)!~&@%+ REMOVE SECURELIST :are supported by this server");
            i(":jasper.lizardirc.org 005 boopbot SSL=[::]:6697 STARTTLS STATUSMSG=!~&@%+ TOPICLEN=80000 USERIP VBANLIST WALLCHOPS WALLVOICES WATCH=1024 :are supported by this server");
            i(":boopbot!~boopbot@lizardirc/staff/stwalkerster JOIN #opers stwalkerster :Boop!");
            i(":jasper.lizardirc.org 353 boopbot @ #opers :@ChanServ &@stwalkerster NetOpsBot boopbot");
            i(":jasper.lizardirc.org 366 boopbot #opers :End of /NAMES list.");
            i(":boopbot!~boopbot@lizardirc/staff/stwalkerster JOIN #operchat stwalkerster :Boop!");
            i(":jasper.lizardirc.org 353 boopbot @ #operchat :stwalkerster @NetOpsBot boopbot");
            i(":jasper.lizardirc.org 366 boopbot #operchat :End of /NAMES list.");
            i(":boopbot!~boopbot@lizardirc/staff/stwalkerster JOIN #opers-verbose stwalkerster :Boop!");
            i(":jasper.lizardirc.org 353 boopbot @ #opers-verbose :@ChanServ &@stwalkerster NetOpsBot boopbot");
            i(":jasper.lizardirc.org 366 boopbot #opers-verbose :End of /NAMES list.");
            i(":ChanServ!ChanServ@services.lizardirc MODE #opers +ao boopbot boopbot");
            i(":ChanServ!ChanServ@services.lizardirc MODE #opers-verbose +ao boopbot boopbot");
            o("WHO #opers %uhnatfc,001");
            i(":jasper.lizardirc.org 352 boopbot #opers ChanServ services.lizardirc services.lizardirc ChanServ H@ :0 Channel Services");
            i(":jasper.lizardirc.org 352 boopbot #opers ~stwalkerster stwalkerster.co.uk emerald.lizardirc.org stwalkerster H*&@ :0 stwalkerster");
            i(":jasper.lizardirc.org 352 boopbot #opers fastlizard4 fastlizard4.org diamond.lizardirc.org NetOpsBot H* :0 Network Operations Bot");
            i(":jasper.lizardirc.org 352 boopbot #opers ~boopbot stwalkerster.co.uk jasper.lizardirc.org boopbot H*&@ :0 Boop!");
            i(":jasper.lizardirc.org 315 boopbot #opers :End of /WHO list.");
            o("MODE #opers");
            i(":jasper.lizardirc.org 324 boopbot #opers +COPnst");
            o("WHO #operchat %uhnatfc,001");
            i(":jasper.lizardirc.org 352 boopbot #operchat ~stwalkerster stwalkerster.co.uk emerald.lizardirc.org stwalkerster H* :0 stwalkerster");
            i(":jasper.lizardirc.org 352 boopbot #operchat fastlizard4 fastlizard4.org diamond.lizardirc.org NetOpsBot H*@ :0 Network Operations Bot");
            i(":jasper.lizardirc.org 352 boopbot #operchat ~boopbot stwalkerster.co.uk jasper.lizardirc.org boopbot H* :0 Boop!");
            i(":jasper.lizardirc.org 315 boopbot #operchat :End of /WHO list.");
            o("MODE #operchat");
            i(":jasper.lizardirc.org 324 boopbot #operchat +COPnst");
            o("WHO #opers-verbose %uhnatfc,001");
            i(":jasper.lizardirc.org 352 boopbot #opers-verbose ChanServ services.lizardirc services.lizardirc ChanServ H@ :0 Channel Services");
            i(":jasper.lizardirc.org 352 boopbot #opers-verbose ~stwalkerster stwalkerster.co.uk emerald.lizardirc.org stwalkerster H*&@ :0 stwalkerster");
            i(":jasper.lizardirc.org 352 boopbot #opers-verbose fastlizard4 fastlizard4.org diamond.lizardirc.org NetOpsBot H* :0 Network Operations Bot");
            i(":jasper.lizardirc.org 352 boopbot #opers-verbose ~boopbot stwalkerster.co.uk jasper.lizardirc.org boopbot H*&@ :0 Boop!");
            i(":jasper.lizardirc.org 315 boopbot #opers-verbose :End of /WHO list.");
            o("MODE #opers-verbose");
            i(":jasper.lizardirc.org 324 boopbot #opers-verbose +COPnst");

            i(":NetOpsBot!fastlizard4@lizardirc/utility-bot/NetOpsBot QUIT :Quit: Caught SIGINT/2 or SIGTERM/15");
            i(":NetOpsBot!fastlizard4@ridley.fastlizard4.org JOIN #opers * :Network Operations Bot");
            i(":NetOpsBot!fastlizard4@ridley.fastlizard4.org JOIN #opers-verbose * :Network Operations Bot");
            i(":NetOpsBot!fastlizard4@ridley.fastlizard4.org ACCOUNT NetOpsBot");
            i(":NetOpsBot!fastlizard4@ridley.fastlizard4.org QUIT :Changing host");
            
            // This is invalid - CAP extended-join is enabled, so this should contain two more fields.
            // However, this is what InspIRCd2 gives us.
            i(":NetOpsBot!fastlizard4@lizardirc/utility-bot/NetOpsBot JOIN #opers");
        }

    }
}
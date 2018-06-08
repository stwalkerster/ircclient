namespace Stwalkerster.IrcClient.Tests.IRC
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Moq;
    using NUnit.Framework;
    using Stwalkerster.IrcClient.Events;
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Model;

    /// <summary>
    /// The IRC client state tests.
    /// </summary>
    [TestFixture]
    public class IrcClientStateTests : TestBase
    {
        /// <summary>
        /// The client.
        /// </summary>
        private IrcClient client;

        /// <summary>
        /// The network client.
        /// </summary>
        private Mock<INetworkClient> networkClient;

        [Test]
        public void TestAccountTrackingWithExistingUsers()
        {
            // initial client setup
            this.DoSetup("stwtestbot");

            // shortcut functions for logs
            Action<string> i = data => this.networkClient.Raise(
                x => x.DataReceived += null,
                this.networkClient.Object,
                new DataReceivedEventArgs(data));
            Action<string> o = data => this.networkClient.Verify(x => x.Send(data));

            // Initial setup
            o("CAP LS");
            i(":kornbluth.freenode.net CAP * LS :account-notify extended-join identify-msg multi-prefix sasl");
            o("CAP REQ :account-notify extended-join multi-prefix");
            i(":kornbluth.freenode.net CAP * ACK :account-notify extended-join multi-prefix");
            o("CAP END");
            o("USER username * * :real name");
            o("NICK stwtestbot");
            i(":kornbluth.freenode.net 001 stwtestbot :Welcome to the freenode Internet Relay Chat Network stwtestbot");
            i(":stwtestbot MODE stwtestbot :+i");
            o("MODE stwtestbot +Q");
            i(":stwtestbot MODE stwtestbot :+Q");

            // Join channel
            this.client.JoinChannel("##stwalkerster-development");
            o("JOIN ##stwalkerster-development");
            i(":stwtestbot!~stwtestbo@cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net JOIN ##stwalkerster-development * :stwtestbot");
            i(":kornbluth.freenode.net 353 stwtestbot = ##stwalkerster-development :stwtestbot @stwalkerster @ChanServ");
            i(":kornbluth.freenode.net 366 stwtestbot ##stwalkerster-development :End of /NAMES list.");

            Assert.AreEqual(3, this.client.UserCache.Count);
            Assert.IsTrue(this.client.UserCache.ContainsKey("ChanServ"));
            Assert.AreEqual(null, this.client.UserCache["ChanServ"].Hostname);
            Assert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            Assert.IsTrue(this.client.UserCache.ContainsKey("stwalkerster"));
            Assert.AreEqual(null, this.client.UserCache["stwalkerster"].Hostname);
            Assert.AreEqual(null, this.client.UserCache["stwalkerster"].Account);

            // Sync channel
            o("WHO ##stwalkerster-development %uhnatfc,001");
            i(":kornbluth.freenode.net 354 stwtestbot 001 ##stwalkerster-development ~stwtestbo cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net stwtestbot H 0");
            i(":kornbluth.freenode.net 354 stwtestbot 001 ##stwalkerster-development stwalkerst wikimedia/stwalkerster stwalkerster H@ stwalkerster");
            i(":kornbluth.freenode.net 354 stwtestbot 001 ##stwalkerster-development ChanServ services. ChanServ H@ 0");
            i(":kornbluth.freenode.net 315 stwtestbot ##stwalkerster-development :End of /WHO list.");
            o("MODE ##stwalkerster-development");
            i(":kornbluth.freenode.net 324 stwtestbot ##stwalkerster-development +ntf ##stwalkerster");
            i(":kornbluth.freenode.net 329 stwtestbot ##stwalkerster-development 1364176563");

            Assert.AreEqual(3, this.client.UserCache.Count);
            Assert.IsTrue(this.client.UserCache.ContainsKey("ChanServ"));
            Assert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            Assert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            Assert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            Assert.IsTrue(this.client.UserCache.ContainsKey("stwalkerster"));
            Assert.AreEqual("wikimedia/stwalkerster", this.client.UserCache["stwalkerster"].Hostname);
            Assert.AreEqual("stwalkerster", this.client.UserCache["stwalkerster"].Account);
            Assert.AreEqual(false, this.client.UserCache["stwalkerster"].Away);

            // setup message handling
            this.client.ReceivedMessage += (sender, args) =>
                args.Client.SendMessage("##stwalkerster-development", args.User.ToString());

            // Send message
            i(":stwalkerster!stwalkerst@wikimedia/stwalkerster PRIVMSG ##stwalkerster-development :test");

            Assert.AreEqual(3, this.client.UserCache.Count);
            Assert.IsTrue(this.client.UserCache.ContainsKey("ChanServ"));
            Assert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            Assert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            Assert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            Assert.IsTrue(this.client.UserCache.ContainsKey("stwalkerster"));
            Assert.AreEqual("wikimedia/stwalkerster", this.client.UserCache["stwalkerster"].Hostname);
            Assert.AreEqual("stwalkerster", this.client.UserCache["stwalkerster"].Account);
            Assert.AreEqual(false, this.client.UserCache["stwalkerster"].Away);

            // Verify correct return
            o(
                "PRIVMSG ##stwalkerster-development :" + new IrcUser(this.client)
                {
                    Nickname = "stwalkerster",
                    Username = "stwalkerst",
                    Hostname = "wikimedia/stwalkerster",
                    Account = "stwalkerster",
                    Away = false
                });
        }

        /// <summary>
        /// The do setup.
        /// </summary>
        /// <param name="nickName">
        /// The nick Name.
        /// </param>
        public void DoSetup(string nickName)
        {
            this.networkClient = new Mock<INetworkClient>();
            this.IrcConfiguration.Setup(x => x.Nickname).Returns(nickName);
            this.IrcConfiguration.Setup(x => x.Username).Returns("username");
            this.IrcConfiguration.Setup(x => x.RealName).Returns("real name");
            this.IrcConfiguration.Setup(x => x.RestartOnHeavyLag).Returns(false);
            this.SupportHelper
                .Setup(x => x.HandlePrefixMessageSupport(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
                .Callback(
                    (string s, IDictionary<string, string> r) =>
                    {
                        r.Add("v", "+");
                        r.Add("o", "@");
                    });
            this.client = new IrcClient(this.networkClient.Object, this.Logger.Object, this.IrcConfiguration.Object, this.SupportHelper.Object);
        }

        [Test]
        public void TestAccountTrackingWithJoiningUsers()
        {
            // initial client setup
            this.DoSetup("stwtestbot");

            // shortcut functions for logs
            Action<string> i = data => this.networkClient.Raise(
                x => x.DataReceived += null,
                this.networkClient.Object,
                new DataReceivedEventArgs(data));
            Action<string> o = data => this.networkClient.Verify(x => x.Send(data));
            
            o("CAP LS");
            i(":orwell.freenode.net CAP * LS :account-notify extended-join identify-msg multi-prefix sasl");
            o("CAP REQ :account-notify extended-join multi-prefix");
            i(":orwell.freenode.net CAP * ACK :account-notify extended-join multi-prefix ");
            o("CAP END");
            o("USER username * * :real name");
            o("NICK stwtestbot");
            i(":orwell.freenode.net 001 stwtestbot :Welcome to the freenode Internet Relay Chat Network stwtestbot");
            i(":stwtestbot MODE stwtestbot :+i");
            o("MODE stwtestbot +Q");
            i(":stwtestbot MODE stwtestbot :+Q");
            
            this.client.JoinChannel("##stwalkerster-development");
            o("JOIN ##stwalkerster-development");
            i(":stwtestbot!~stwtestbo@cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net JOIN ##stwalkerster-development * :stwtestbot");
            i(":orwell.freenode.net 353 stwtestbot = ##stwalkerster-development :stwtestbot @ChanServ");
            i(":orwell.freenode.net 366 stwtestbot ##stwalkerster-development :End of /NAMES list.");

            Assert.AreEqual(2, this.client.UserCache.Count);
            Assert.IsTrue(this.client.UserCache.ContainsKey("ChanServ"));
            Assert.AreEqual(null, this.client.UserCache["ChanServ"].Hostname);
            Assert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            
            o("WHO ##stwalkerster-development %uhnatfc,001");
            i(":orwell.freenode.net 354 stwtestbot 001 ##stwalkerster-development ~stwtestbo cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net stwtestbot H 0");
            i(":orwell.freenode.net 354 stwtestbot 001 ##stwalkerster-development ChanServ services. ChanServ H@ 0");
            i(":orwell.freenode.net 315 stwtestbot ##stwalkerster-development :End of /WHO list.");
            o("MODE ##stwalkerster-development");
            i(":orwell.freenode.net 324 stwtestbot ##stwalkerster-development +ntf ##stwalkerster");
            i(":orwell.freenode.net 329 stwtestbot ##stwalkerster-development 1364176563");

            Assert.AreEqual(2, this.client.UserCache.Count);
            Assert.IsTrue(this.client.UserCache.ContainsKey("ChanServ"));
            Assert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            Assert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            Assert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            
            i(":stwalkerster!stwalkerst@wikimedia/stwalkerster JOIN ##stwalkerster-development stwalkerster :Simon Walker (fearow.lon.stwalkerster.net)");
            i(":ChanServ!ChanServ@services. MODE ##stwalkerster-development +o stwalkerster");

            Assert.AreEqual(3, this.client.UserCache.Count);
            Assert.IsTrue(this.client.UserCache.ContainsKey("ChanServ"));
            Assert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            Assert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            Assert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            Assert.IsTrue(this.client.UserCache.ContainsKey("stwalkerster"));
            Assert.AreEqual("wikimedia/stwalkerster", this.client.UserCache["stwalkerster"].Hostname);
            Assert.AreEqual("stwalkerster", this.client.UserCache["stwalkerster"].Account);
            
            // setup message handling
            this.client.ReceivedMessage += (sender, args) =>
                args.Client.SendMessage("##stwalkerster-development", args.User.ToString());
            
            i(":stwalkerster!stwalkerst@wikimedia/stwalkerster PRIVMSG ##stwalkerster-development :test");
            o(
                "PRIVMSG ##stwalkerster-development :" + new IrcUser(this.client)
                {
                    Nickname = "stwalkerster",
                    Username = "stwalkerst",
                    Hostname = "wikimedia/stwalkerster",
                    Account = "stwalkerster",
                    Away = false
                });

            // todo verify state
        }
        
        /// <summary>
        /// Tests read from WHO and my JOIN.
        /// </summary>
        [Test]
#if ! PARSERTESTS
        [Ignore("Parser tests disabled.")]
#endif
        public void ParserTest0()
        {
            // run the test file from ninetales local disk
            this.RunTestFile(@"parsertests/test0.log", "stwalker|test");

            // assert
            this.networkClient.Verify(x => x.Send("WHO ##stwalkerster %uhnatfc,001"));
            this.networkClient.Verify(x => x.Send("WHO ##stwalkerster-development %uhnatfc,001"));

            var channels = this.client.Channels;
            Assert.That(channels.Count, Is.EqualTo(2));
            Assert.That(channels.ContainsKey("##stwalkerster-development"), Is.True);
            Assert.That(channels.ContainsKey("##stwalkerster"), Is.True);

            Assert.That(channels["##stwalkerster"].Users.Count, Is.EqualTo(3));
            Assert.That(channels["##stwalkerster-development"].Users.Count, Is.EqualTo(4));

            Assert.That(channels["##stwalkerster-development"].Users["ChanServ"].Operator, Is.True);
            Assert.That(channels["##stwalkerster-development"].Users["ChanServ"].User.Account, Is.Null);
            Assert.That(channels["##stwalkerster-development"].Users["Helpmebot"].Voice, Is.True);
            Assert.That(channels["##stwalkerster-development"].Users["Helpmebot"].User.Account, Is.EqualTo("helpmebot"));
        }

        /// <summary>
        /// The parser test for QUIT and MODE +o
        /// </summary>
        [Test]
#if ! PARSERTESTS
        [Ignore("Parser tests disabled.")]
#endif
        public void ParserTest1()
        {
            // run the test file from ninetales local disk
            this.RunTestFile(@"parsertests/test1.log", "stwalker|test");

            // assert
            this.networkClient.Verify(x => x.Send("WHO ##stwalkerster %uhnatfc,001"));
            this.networkClient.Verify(x => x.Send("WHO ##stwalkerster-development %uhnatfc,001"));

            var channels = this.client.Channels;
            Assert.That(channels.Count, Is.EqualTo(2));
            Assert.That(channels.ContainsKey("##stwalkerster-development"), Is.True);
            Assert.That(channels.ContainsKey("##stwalkerster"), Is.True);

            var stw = channels["##stwalkerster"];
            var stwdev = channels["##stwalkerster-development"];

            Assert.That(stw.Users.Count, Is.EqualTo(2));
            Assert.That(stwdev.Users.Count, Is.EqualTo(3));

            Assert.That(stwdev.Users.ContainsKey("stwalkerster"), Is.False);
            Assert.That(stw.Users.ContainsKey("stwalkerster"), Is.False);

            Assert.That(stwdev.Users["ChanServ"].Operator, Is.True);
            Assert.That(stwdev.Users["ChanServ"].User.Account, Is.Null);
            Assert.That(stwdev.Users["Helpmebot"].Voice, Is.True);
            Assert.That(stwdev.Users["Helpmebot"].User.Account, Is.EqualTo("helpmebot"));
            Assert.That(stwdev.Users["stwalker|test"].Operator, Is.EqualTo(true));
            Assert.That(stwdev.Users["stwalker|test"].Voice, Is.EqualTo(true));
        }

        /// <summary>
        /// Just throwing data at this...
        /// </summary>
        [Test]
#if ! PARSERTESTS
        [Ignore("Parser tests disabled.")]
#endif
        public void ParserTest2()
        {
            // run the test file from ninetales local disk
            this.RunTestFile(@"parsertests/test2.log", "stwalkerster___");
        }

        /// <summary>
        /// Just throwing data at this...
        /// </summary>
        [Test]
#if ! PARSERTESTS
        [Ignore("Parser tests disabled.")]
#endif
        public void ParserTest3()
        {
            // run the test file from ninetales local disk
            this.RunTestFile(@"parsertests/test3.log", "stwalkerster___");
        }

        /// <summary>
        /// Just throwing data at this... - nick test
        /// </summary>
        [Test]
#if ! PARSERTESTS
        [Ignore("Parser tests disabled.")]
#endif
        public void ParserTest4()
        {
            // run the test file from ninetales local disk
            this.RunTestFile(@"parsertests/test4.log", "stwalkerster___");

            Assert.That(this.client.Channels["#wikipedia-en"].Users.ContainsKey("FunPika_"), Is.False);
            Assert.That(this.client.Channels["#wikipedia-en"].Users.ContainsKey("FunPikachu"), Is.True);
        }

        /// <summary>
        /// Just throwing data at this...
        /// </summary>
        [Test]
#if ! PARSERTESTS
        [Ignore("Parser tests disabled.")]
#endif
        public void ParserTest5()
        {
            // run the test file from ninetales local disk
            this.RunTestFile(@"parsertests/test5.log", "stwalkerster___");
        }

        /// <summary>
        /// Just throwing data at this...
        /// </summary>
        [Test]
#if ! PARSERTESTS
        [Ignore("Parser tests disabled.")]
#endif
        public void ParserTest6()
        {
            // run the test file from ninetales local disk
            this.RunTestFile(@"parsertests/test6.log", "stwalkerster___");
        }

        /// <summary>
        /// Just throwing data at this...
        /// </summary>
        [Test]
#if ! PARSERTESTS
        [Ignore("Parser tests disabled.")]
#endif
        public void ParserTest7()
        {
            // run the test file from ninetales local disk
            this.RunTestFile(@"parsertests/test7.log", "stwalkerster___");
        }

        /// <summary>
        /// Just throwing data at this...
        /// </summary>
        [Test]
#if ! PARSERTESTS
        [Ignore("Parser tests disabled.")]
#endif
        public void ParserTestNickChange()
        {
            // run the test file from ninetales local disk
            this.RunTestFile(@"parsertests/test0.log", "stwalker|test");

            var channels = this.client.Channels;

            Assert.That(channels.ContainsKey("##stwalkerster-development"), Is.True);
            Assert.That(channels.ContainsKey("##stwalkerster"), Is.True);

            Assert.That(channels["##stwalkerster"].Users.Count, Is.EqualTo(3));
            Assert.That(channels["##stwalkerster-development"].Users.Count, Is.EqualTo(4));

            this.RaiseEvent(":Aranda56_!~chatzilla@c-98-242-146-227.hsd1.fl.comcast.net JOIN ##stwalkerster-development * :New Now Know How");
            this.RaiseEvent(":Aranda56_!~chatzilla@c-98-242-146-227.hsd1.fl.comcast.net JOIN ##stwalkerster * :New Now Know How");

            Assert.That(channels["##stwalkerster"].Users.Count, Is.EqualTo(4));
            Assert.That(channels["##stwalkerster-development"].Users.Count, Is.EqualTo(5));
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56_"), Is.True);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56_"), Is.True);
            Assert.That(this.client.UserCache.ContainsKey("Aranda56_"), Is.True);

            this.RaiseEvent(":Aranda56_!~chatzilla@c-98-242-146-227.hsd1.fl.comcast.net NICK :Aranda56");

            Assert.That(channels["##stwalkerster"].Users.Count, Is.EqualTo(4));
            Assert.That(channels["##stwalkerster-development"].Users.Count, Is.EqualTo(5));
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56_"), Is.False);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56_"), Is.False);

            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56"), Is.True);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56"), Is.True);

            Assert.That(this.client.UserCache.ContainsKey("Aranda56"), Is.True);
            Assert.That(this.client.UserCache.ContainsKey("Aranda56_"), Is.False);

            Assert.That(this.client.UserCache["Aranda56"].Account, Is.Null);
            Assert.That(channels["##stwalkerster"].Users["Aranda56"].User.Account, Is.Null);
            Assert.That(channels["##stwalkerster-development"].Users["Aranda56"].User.Account, Is.Null);

            this.RaiseEvent(":Aranda56!~chatzilla@c-98-242-146-227.hsd1.fl.comcast.net ACCOUNT Aranda56");
            
            Assert.That(this.client.UserCache["Aranda56"].Account, Is.EqualTo("Aranda56"));
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56"), Is.True);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56"), Is.True);
            Assert.That(channels["##stwalkerster"].Users["Aranda56"].User.Account, Is.EqualTo("Aranda56"));
            Assert.That(channels["##stwalkerster-development"].Users["Aranda56"].User.Account, Is.EqualTo("Aranda56"));
        }

        /// <summary>
        /// Fixes for HMB-85
        /// </summary>
        [Test]
#if ! PARSERTESTS
        [Ignore("Parser tests disabled.")]
#endif
        public void ParserTestNickTracking()
        {
            // run the test file from ninetales local disk
            this.RunTestFile(@"parsertests/test0.log", "stwalker|test");

            var channels = this.client.Channels;

            Assert.That(channels.ContainsKey("##stwalkerster-development"), Is.True);
            Assert.That(channels.ContainsKey("##stwalkerster"), Is.True);

            Assert.That(channels["##stwalkerster"].Users.Count, Is.EqualTo(3));
            Assert.That(channels["##stwalkerster-development"].Users.Count, Is.EqualTo(4));

            Assert.That(this.client.UserCache.ContainsKey("Aranda56_"), Is.False);
            Assert.That(this.client.UserCache.ContainsKey("Aranda56"), Is.False);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56_"), Is.False);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56"), Is.False);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56_"), Is.False);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56"), Is.False);

            this.RaiseEvent(":Aranda56_!~chatzilla@c-98-242-146-227.hsd1.fl.comcast.net JOIN ##stwalkerster-development * :New Now Know How");
            Assert.That(this.client.UserCache.ContainsKey("Aranda56_"), Is.True);
            Assert.That(this.client.UserCache.ContainsKey("Aranda56"), Is.False);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56_"), Is.True);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56"), Is.False);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56_"), Is.False);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56"), Is.False);

            this.RaiseEvent(":Aranda56_!~chatzilla@c-98-242-146-227.hsd1.fl.comcast.net JOIN ##stwalkerster * :New Now Know How");
            Assert.That(this.client.UserCache.ContainsKey("Aranda56_"), Is.True);
            Assert.That(this.client.UserCache.ContainsKey("Aranda56"), Is.False);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56_"), Is.True);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56"), Is.False);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56_"), Is.True);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56"), Is.False);

            this.RaiseEvent(":Aranda56_!~chatzilla@c-98-242-146-227.hsd1.fl.comcast.net NICK Aranda56");
            Assert.That(this.client.UserCache.ContainsKey("Aranda56_"), Is.False);
            Assert.That(this.client.UserCache.ContainsKey("Aranda56"), Is.True);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56_"), Is.False);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56"), Is.True);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56_"), Is.False);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56"), Is.True);

            this.RaiseEvent(":Aranda56!~chatzilla@c-98-242-146-227.hsd1.fl.comcast.net PART ##stwalkerster-development :Parting");
            Assert.That(this.client.UserCache.ContainsKey("Aranda56_"), Is.False);
            Assert.That(this.client.UserCache.ContainsKey("Aranda56"), Is.True);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56_"), Is.False);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56"), Is.False);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56_"), Is.False);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56"), Is.True);

            this.RaiseEvent(":Aranda56!~chatzilla@c-98-242-146-227.hsd1.fl.comcast.net PART ##stwalkerster :quitting");
            Assert.That(this.client.UserCache.ContainsKey("Aranda56_"), Is.False);
            Assert.That(this.client.UserCache.ContainsKey("Aranda56"), Is.False);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56_"), Is.False);
            Assert.That(channels["##stwalkerster-development"].Users.ContainsKey("Aranda56"), Is.False);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56_"), Is.False);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56"), Is.False);
        }

        /// <summary>
        /// Fixes for HMB-97
        /// </summary>
        [Test]
#if ! PARSERTESTS
        [Ignore("Parser tests disabled.")]
#endif
        public void ParserTestKickTracking()
        {
            // run the test file from ninetales local disk
            this.RunTestFile(@"parsertests/test0.log", "stwalker|test");

            var channels = this.client.Channels;

            Assert.That(this.client.UserCache.ContainsKey("Aranda56"), Is.False);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56"), Is.False);

            this.RaiseEvent(":Aranda56!~chatzilla@c-98-242-146-227.hsd1.fl.comcast.net JOIN ##stwalkerster * :New Now Know How");
            Assert.That(this.client.UserCache.ContainsKey("stwalker|test"), Is.True);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("stwalker|test"), Is.True);
            Assert.That(this.client.UserCache.ContainsKey("Aranda56"), Is.True);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56"), Is.True);

            this.RaiseEvent(":stwalker|test!~stwalkers@wikimedia/stwalkerster KICK ##stwalkerster Aranda56 :Aranda56");
            Assert.That(this.client.UserCache.ContainsKey("Aranda56"), Is.False);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("Aranda56"), Is.False);
            Assert.That(this.client.UserCache.ContainsKey("stwalker|test"), Is.True);
            Assert.That(channels["##stwalkerster"].Users.ContainsKey("stwalker|test"), Is.True);
        }

        /// <summary>
        /// The run test file.
        /// </summary>
        /// <param name="fileName">
        /// The file name.
        /// </param>
        /// <param name="nickname">
        /// The nickname.
        /// </param>
        private void RunTestFile(string fileName, string nickname)
        {
            this.DoSetup(nickname);

            var lines = File.ReadAllLines(fileName)
                .Where(x => x.StartsWith("> "))
                .Select(x => x.Substring(2));

            foreach (string line in lines)
            {
                this.RaiseEvent(line);
            }

            Assert.That(this.client.NickTrackingValid, Is.True);
        }

        /// <summary>
        /// The raise event.
        /// </summary>
        /// <param name="line">
        /// The line.
        /// </param>
        private void RaiseEvent(string line)
        {
            this.networkClient.Raise(x => x.DataReceived += null, new DataReceivedEventArgs(line));
        }
    }
}

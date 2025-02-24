﻿namespace Stwalkerster.IrcClient.Tests.IRC
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using NSubstitute;
    using NUnit.Framework;
    using NUnit.Framework.Legacy;
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
        /// The network client mock
        /// </summary>
        private INetworkClient networkClient;

        private ISupportHelper supportHelper;

        public void DoSetup(string nickName, string password = null, string saslUsername = "username")
        {
            this.networkClient = Substitute.For<INetworkClient>();
            this.supportHelper = Substitute.For<ISupportHelper>();
            
            this.IrcConfiguration.Nickname.Returns(nickName);
            this.IrcConfiguration.Username.Returns("username");
            this.IrcConfiguration.RealName.Returns("real name");
            this.IrcConfiguration.ClientName.Returns("client");
            this.IrcConfiguration.RestartOnHeavyLag.Returns(false);
            this.IrcConfiguration.ServicesPassword.Returns(password);
            this.IrcConfiguration.ServicesUsername.Returns(saslUsername);

            this.IrcConfiguration.ConnectModes.Returns("+Q");
            
            if (!string.IsNullOrEmpty(password))
            {
                this.IrcConfiguration.AuthToServices.Returns(true);
            }
            else
            {
                this.IrcConfiguration.AuthToServices.Returns(false);
            }
            
            this.supportHelper.HandlePrefixMessageSupport(
                Arg.Any<string>(),
                Arg.Do<IDictionary<string, string>>(
                    r =>
                    {
                        r.Add("v", "+");
                        r.Add("o", "@");
                    }));
            this.client = new IrcClient(this.networkClient, this.Logger, this.IrcConfiguration, this.supportHelper);
        }
        
        [Test]
        public void TestAccountTrackingWithExistingUsers()
        {
            // initial client setup
            this.DoSetup("stwtestbot");

            // shortcut functions for logs
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => this.networkClient.Received().Send(Arg.Is(data));

            // Initial setup
            o("CAP LS 302");
            i(":kornbluth.freenode.net CAP * LS :account-notify extended-join identify-msg multi-prefix sasl");
            o("CAP REQ :account-notify extended-join multi-prefix");
            i(":kornbluth.freenode.net CAP * ACK :account-notify extended-join multi-prefix");
            o("CAP END");
            o("USER username * * :real name");
            o("NICK stwtestbot");
            i(":kornbluth.freenode.net 001 stwtestbot :Welcome to the freenode Internet Relay Chat Network stwtestbot");
            i(":orwell.freenode.net 005 stwtestbot CHANMODES=eIbq,k,flj,CFLMPQSTcgimnprstuz CHANLIMIT=#:250 PREFIX=(ovh)@+% MAXLIST=bqeI:100 MODES=4 WHOX NETWORK=stwalkerster.net STATUSMSG=@+% CASEMAPPING=rfc1459 NICKLEN=16 MAXNICKLEN=16 CHANNELLEN=50 TOPICLEN=390 :are supported by this server");
            i(":stwtestbot MODE stwtestbot :+i");
            o("MODE stwtestbot +Q");
            i(":stwtestbot MODE stwtestbot :+Q");

            // Join channel
            this.client.JoinChannel("##stwalkerster-development");
            o("JOIN ##stwalkerster-development");
            i(":stwtestbot!~stwtestbo@cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net JOIN ##stwalkerster-development * :stwtestbot");
            i(":kornbluth.freenode.net 353 stwtestbot = ##stwalkerster-development :stwtestbot @stwalkerster @ChanServ");
            i(":kornbluth.freenode.net 366 stwtestbot ##stwalkerster-development :End of /NAMES list.");

            ClassicAssert.AreEqual(3, this.client.UserCache.Count);
            Assert.That(this.client.UserCache.ContainsKey("ChanServ"), Is.True);
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Hostname);
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            Assert.That(this.client.UserCache.ContainsKey("stwalkerster"), Is.True);
            ClassicAssert.AreEqual(null, this.client.UserCache["stwalkerster"].Hostname);
            ClassicAssert.AreEqual(null, this.client.UserCache["stwalkerster"].Account);

            // Sync channel
            o("WHO ##stwalkerster-development %uhnatfc,001");
            i(":kornbluth.freenode.net 354 stwtestbot 001 ##stwalkerster-development ~stwtestbo cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net stwtestbot H 0");
            i(":kornbluth.freenode.net 354 stwtestbot 001 ##stwalkerster-development stwalkerst wikimedia/stwalkerster stwalkerster H@ stwalkerster");
            i(":kornbluth.freenode.net 354 stwtestbot 001 ##stwalkerster-development ChanServ services. ChanServ H@ 0");
            i(":kornbluth.freenode.net 315 stwtestbot ##stwalkerster-development :End of /WHO list.");
            o("MODE ##stwalkerster-development");
            i(":kornbluth.freenode.net 324 stwtestbot ##stwalkerster-development +ntf ##stwalkerster");
            i(":kornbluth.freenode.net 329 stwtestbot ##stwalkerster-development 1364176563");

            ClassicAssert.AreEqual(3, this.client.UserCache.Count);
            Assert.That(this.client.UserCache.ContainsKey("ChanServ"), Is.True);
            ClassicAssert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            // ReSharper disable once HeuristicUnreachableCode
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            ClassicAssert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            Assert.That(this.client.UserCache.ContainsKey("stwalkerster"), Is.True);
            ClassicAssert.AreEqual("wikimedia/stwalkerster", this.client.UserCache["stwalkerster"].Hostname);
            ClassicAssert.AreEqual("stwalkerster", this.client.UserCache["stwalkerster"].Account);
            ClassicAssert.AreEqual(false, this.client.UserCache["stwalkerster"].Away);

            // setup message handling
            this.client.ReceivedMessage += (sender, args) =>
                args.Client.SendMessage("##stwalkerster-development", args.User.ToString());

            // Send message
            i(":stwalkerster!stwalkerst@wikimedia/stwalkerster PRIVMSG ##stwalkerster-development :test");

            ClassicAssert.AreEqual(3, this.client.UserCache.Count);
            Assert.That(this.client.UserCache.ContainsKey("ChanServ"), Is.True);
            ClassicAssert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            ClassicAssert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            Assert.That(this.client.UserCache.ContainsKey("stwalkerster"), Is.True);
            ClassicAssert.AreEqual("wikimedia/stwalkerster", this.client.UserCache["stwalkerster"].Hostname);
            ClassicAssert.AreEqual("stwalkerster", this.client.UserCache["stwalkerster"].Account);
            ClassicAssert.AreEqual(false, this.client.UserCache["stwalkerster"].Away);

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

        [Test]
        public void TestSelfNickTracking()
        {
            // initial client setup
            this.DoSetup("stwtestbot");

            // shortcut functions for logs
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => this.networkClient.Received().Send(Arg.Is(data));
            
            o("CAP LS 302");
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
            
            ClassicAssert.AreEqual("stwtestbot", this.client.Nickname);
            
            i(":stwtestbot!~stwtestbo@cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net NICK :testytest");
            
            ClassicAssert.AreEqual("testytest", this.client.Nickname);
        }
        
        [Test]
        public void TestAccountTrackingWithJoiningUsers()
        {
            // initial client setup
            this.DoSetup("stwtestbot");

            // shortcut functions for logs
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => this.networkClient.Received().Send(Arg.Is(data));
            
            o("CAP LS 302");
            i(":orwell.freenode.net CAP * LS :account-notify extended-join identify-msg multi-prefix sasl");
            o("CAP REQ :account-notify extended-join multi-prefix");
            i(":orwell.freenode.net CAP * ACK :account-notify extended-join multi-prefix ");
            o("CAP END");
            o("USER username * * :real name");
            o("NICK stwtestbot");
            i(":orwell.freenode.net 001 stwtestbot :Welcome to the freenode Internet Relay Chat Network stwtestbot");
            i(":orwell.freenode.net 005 stwtestbot CHANMODES=eIbq,k,flj,CFLMPQSTcgimnprstuz WHOX CHANLIMIT=#:250 PREFIX=(ovh)@+% MAXLIST=bqeI:100 MODES=4 NETWORK=stwalkerster.net STATUSMSG=@+% CASEMAPPING=rfc1459 NICKLEN=16 MAXNICKLEN=16 CHANNELLEN=50 TOPICLEN=390 :are supported by this server");
            i(":stwtestbot MODE stwtestbot :+i");
            o("MODE stwtestbot +Q");
            i(":stwtestbot MODE stwtestbot :+Q");
            
            this.client.JoinChannel("##stwalkerster-development");
            o("JOIN ##stwalkerster-development");
            i(":stwtestbot!~stwtestbo@cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net JOIN ##stwalkerster-development * :stwtestbot");
            i(":orwell.freenode.net 353 stwtestbot = ##stwalkerster-development :stwtestbot @ChanServ");
            i(":orwell.freenode.net 366 stwtestbot ##stwalkerster-development :End of /NAMES list.");

            ClassicAssert.AreEqual(2, this.client.UserCache.Count);
            Assert.That(this.client.UserCache.ContainsKey("ChanServ"), Is.True);
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Hostname);
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            
            o("WHO ##stwalkerster-development %uhnatfc,001");
            i(":orwell.freenode.net 354 stwtestbot 001 ##stwalkerster-development ~stwtestbo cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net stwtestbot H 0");
            i(":orwell.freenode.net 354 stwtestbot 001 ##stwalkerster-development ChanServ services. ChanServ H@ 0");
            i(":orwell.freenode.net 315 stwtestbot ##stwalkerster-development :End of /WHO list.");
            o("MODE ##stwalkerster-development");
            i(":orwell.freenode.net 324 stwtestbot ##stwalkerster-development +ntf ##stwalkerster");
            i(":orwell.freenode.net 329 stwtestbot ##stwalkerster-development 1364176563");

            ClassicAssert.AreEqual(2, this.client.UserCache.Count);
            Assert.That(this.client.UserCache.ContainsKey("ChanServ"), Is.True);
            ClassicAssert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            // ReSharper disable once HeuristicUnreachableCode
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            ClassicAssert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            
            i(":stwalkerster!stwalkerst@wikimedia/stwalkerster JOIN ##stwalkerster-development stwalkerster :Simon Walker (fearow.lon.stwalkerster.net)");
            i(":ChanServ!ChanServ@services. MODE ##stwalkerster-development +o stwalkerster");

            ClassicAssert.AreEqual(3, this.client.UserCache.Count);
            Assert.That(this.client.UserCache.ContainsKey("ChanServ"), Is.True);
            ClassicAssert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            ClassicAssert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            Assert.That(this.client.UserCache.ContainsKey("stwalkerster"), Is.True);
            ClassicAssert.AreEqual("wikimedia/stwalkerster", this.client.UserCache["stwalkerster"].Hostname);
            ClassicAssert.AreEqual("stwalkerster", this.client.UserCache["stwalkerster"].Account);
            
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

        [Test]
        public void TestChghostCapability()
        {
            // initial client setup
            this.DoSetup("stwtestbot");

            // shortcut functions for logs
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => this.networkClient.Received().Send(Arg.Is(data));
            
            o("CAP LS 302");
            i(":orwell.freenode.net CAP * LS :account-notify extended-join identify-msg multi-prefix sasl chghost");
            o("CAP REQ :account-notify extended-join multi-prefix chghost");
            i(":orwell.freenode.net CAP * ACK :account-notify extended-join multi-prefix chghost");
            o("CAP END");
            o("USER username * * :real name");
            o("NICK stwtestbot");
            i(":orwell.freenode.net 001 stwtestbot :Welcome to the freenode Internet Relay Chat Network stwtestbot");
            i(":orwell.freenode.net 005 stwtestbot WHOX CHANMODES=eIbq,k,flj,CFLMPQSTcgimnprstuz CHANLIMIT=#:250 PREFIX=(ovh)@+% MAXLIST=bqeI:100 MODES=4 NETWORK=stwalkerster.net STATUSMSG=@+% CASEMAPPING=rfc1459 NICKLEN=16 MAXNICKLEN=16 CHANNELLEN=50 TOPICLEN=390 :are supported by this server");
            i(":stwtestbot MODE stwtestbot :+i");
            o("MODE stwtestbot +Q");
            i(":stwtestbot MODE stwtestbot :+Q");
            
            this.client.JoinChannel("##stwalkerster-development");
            o("JOIN ##stwalkerster-development");
            i(":stwtestbot!~stwtestbo@cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net JOIN ##stwalkerster-development * :stwtestbot");
            i(":orwell.freenode.net 353 stwtestbot = ##stwalkerster-development :stwtestbot @ChanServ");
            i(":orwell.freenode.net 366 stwtestbot ##stwalkerster-development :End of /NAMES list.");

            ClassicAssert.AreEqual(2, this.client.UserCache.Count);
            Assert.That(this.client.UserCache.ContainsKey("ChanServ"), Is.True);
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Hostname);
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            
            o("WHO ##stwalkerster-development %uhnatfc,001");
            i(":orwell.freenode.net 354 stwtestbot 001 ##stwalkerster-development ~stwtestbo cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net stwtestbot H 0");
            i(":orwell.freenode.net 354 stwtestbot 001 ##stwalkerster-development ChanServ services. ChanServ H@ 0");
            i(":orwell.freenode.net 315 stwtestbot ##stwalkerster-development :End of /WHO list.");
            o("MODE ##stwalkerster-development");
            i(":orwell.freenode.net 324 stwtestbot ##stwalkerster-development +ntf ##stwalkerster");
            i(":orwell.freenode.net 329 stwtestbot ##stwalkerster-development 1364176563");

            ClassicAssert.AreEqual(2, this.client.UserCache.Count);
            Assert.That(this.client.UserCache.ContainsKey("ChanServ"), Is.True);
            ClassicAssert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            // ReSharper disable once HeuristicUnreachableCode
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            ClassicAssert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            
            i(":stwalkerster!stwalkerst@spearow.lon.stwalkerster.net JOIN ##stwalkerster-development stwalkerster :Simon Walker (fearow.lon.stwalkerster.net)");
            i(":ChanServ!ChanServ@services. MODE ##stwalkerster-development +o stwalkerster");
            
            ClassicAssert.AreEqual(3, this.client.UserCache.Count);
            Assert.That(this.client.UserCache.ContainsKey("ChanServ"), Is.True);
            ClassicAssert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            ClassicAssert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            Assert.That(this.client.UserCache.ContainsKey("stwalkerster"), Is.True);
            ClassicAssert.AreEqual("spearow.lon.stwalkerster.net", this.client.UserCache["stwalkerster"].Hostname);
            ClassicAssert.AreEqual("stwalkerst", this.client.UserCache["stwalkerster"].Username);
            ClassicAssert.AreEqual("stwalkerster", this.client.UserCache["stwalkerster"].Account);

            i(":stwalkerster!stwalkerst@spearow.lon.stwalkerster.net CHGHOST stwalkerst wikimedia/stwalkerster");
            
            ClassicAssert.AreEqual(3, this.client.UserCache.Count);
            Assert.That(this.client.UserCache.ContainsKey("ChanServ"), Is.True);
            ClassicAssert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            ClassicAssert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            Assert.That(this.client.UserCache.ContainsKey("stwalkerster"), Is.True);
            ClassicAssert.AreEqual("wikimedia/stwalkerster", this.client.UserCache["stwalkerster"].Hostname);
            ClassicAssert.AreEqual("stwalkerst", this.client.UserCache["stwalkerster"].Username);
            ClassicAssert.AreEqual("stwalkerster", this.client.UserCache["stwalkerster"].Account);
            
            i(":stwalkerster!stwalkerst@wikimedia/stwalkerster CHGHOST potato wikimedia/stwalkerster");
            
            ClassicAssert.AreEqual(3, this.client.UserCache.Count);
            Assert.That(this.client.UserCache.ContainsKey("ChanServ"), Is.True);
            ClassicAssert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            ClassicAssert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            Assert.That(this.client.UserCache.ContainsKey("stwalkerster"), Is.True);
            ClassicAssert.AreEqual("wikimedia/stwalkerster", this.client.UserCache["stwalkerster"].Hostname);
            ClassicAssert.AreEqual("potato", this.client.UserCache["stwalkerster"].Username);
            ClassicAssert.AreEqual("stwalkerster", this.client.UserCache["stwalkerster"].Account);
            
            Assert.That(this.client.UserCache.ContainsKey("stwtestbot"), Is.True);
            ClassicAssert.AreEqual("cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net", this.client.UserCache["stwtestbot"].Hostname);
            ClassicAssert.AreEqual("~stwtestbo", this.client.UserCache["stwtestbot"].Username);
            Assert.That(this.client.UserCache["stwtestbot"].Account, Is.Null);
            
            i(":stwtestbot!~stwtestbo@cpc104826-sgyl39-2-0-cust295.18-2.cable.virginm.net CHGHOST testbot testbot/testytest");

            ClassicAssert.AreEqual(3, this.client.UserCache.Count);
            Assert.That(this.client.UserCache.ContainsKey("ChanServ"), Is.True);
            ClassicAssert.AreEqual("services.", this.client.UserCache["ChanServ"].Hostname);
            ClassicAssert.AreEqual(null, this.client.UserCache["ChanServ"].Account);
            ClassicAssert.AreEqual(false, this.client.UserCache["ChanServ"].Away);
            
            Assert.That(this.client.UserCache.ContainsKey("stwalkerster"), Is.True);
            ClassicAssert.AreEqual("wikimedia/stwalkerster", this.client.UserCache["stwalkerster"].Hostname);
            ClassicAssert.AreEqual("potato", this.client.UserCache["stwalkerster"].Username);
            ClassicAssert.AreEqual("stwalkerster", this.client.UserCache["stwalkerster"].Account);
            
            Assert.That(this.client.UserCache.ContainsKey("stwtestbot"), Is.True);
            ClassicAssert.AreEqual("testbot/testytest", this.client.UserCache["stwtestbot"].Hostname);
            ClassicAssert.AreEqual("testbot", this.client.UserCache["stwtestbot"].Username);
            Assert.That(this.client.UserCache["stwtestbot"].Account, Is.Null);
        }

        [Test]
        public void TestCapabilityNegotiation()
        {
            // initial client setup
            this.DoSetup("stwtestbot");

            // shortcut functions for logs
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => this.networkClient.Received().Send(Arg.Is(data));
            
            o("CAP LS 302");
            i(":orwell.freenode.net CAP * LS * :account-notify extended-join cap-notify identify-msg multi-prefix chghost");
            i(":orwell.freenode.net CAP * LS :foo bar baz sasl=PLAIN,EXTERNAL draft/foobar");
            o("CAP REQ :account-notify extended-join cap-notify multi-prefix chghost");
            i(":orwell.freenode.net CAP * ACK :account-notify extended-join multi-prefix chghost cap-notify");
            o("CAP END");
            o("USER username * * :real name");
            o("NICK stwtestbot");
            i(":orwell.freenode.net 001 stwtestbot :Welcome to the freenode Internet Relay Chat Network stwtestbot");

            ClassicAssert.AreEqual(11, this.client.ServerCapabilities.Count);
            ClassicAssert.Contains("account-notify", this.client.ServerCapabilities);
            ClassicAssert.Contains("chghost", this.client.ServerCapabilities);
            ClassicAssert.Contains("cap-notify", this.client.ServerCapabilities);
            ClassicAssert.Contains("foo", this.client.ServerCapabilities);
            ClassicAssert.Contains("bar", this.client.ServerCapabilities);
            ClassicAssert.Contains("draft/foobar", this.client.ServerCapabilities);
            ClassicAssert.Contains("sasl=PLAIN,EXTERNAL", this.client.ServerCapabilities);
            Assert.That(this.client.ServerCapabilities.Contains("*"), Is.False);
            Assert.That(this.client.ServerCapabilities.Contains("qux"), Is.False);
            Assert.That(this.client.ServerCapabilities.Contains("draft"), Is.False);
            
            Assert.That(this.client.CapChghost, Is.True);
            Assert.That(this.client.CapAccountNotify, Is.True);
            Assert.That(this.client.CapExtendedJoin, Is.True);
            Assert.That(this.client.CapMultiPrefix, Is.True);
            Assert.That(this.client.CapCapNotify, Is.True);

            i(":orwell.freenode.net CAP stwtestbot DEL :account-notify");
            o("CAP REQ -extended-join");
            i(":orwell.freenode.net CAP * ACK :-extended-join");

            Assert.That(this.client.CapChghost, Is.True);
            Assert.That(this.client.CapAccountNotify, Is.False);
            Assert.That(this.client.CapExtendedJoin, Is.False);
            Assert.That(this.client.CapMultiPrefix, Is.True);
            Assert.That(this.client.CapCapNotify, Is.True);
        }
        
        [Test]
        public void TestSasl302Negotiation()
        {
            // initial client setup
            this.DoSetup("stwtestbot", "stwtestbot");

            // shortcut functions for logs
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => this.networkClient.Received().Send(Arg.Is(data));
            
            o("CAP LS 302");
            i(":orwell.freenode.net CAP * LS :sasl=PLAIN,EXTERNAL cap-notify");
            o("CAP REQ :sasl cap-notify");
            i(":orwell.freenode.net CAP * ACK :sasl cap-notify");
            o("AUTHENTICATE PLAIN");
            i("AUTHENTICATE +");
            o("AUTHENTICATE AHVzZXJuYW1lAHN0d3Rlc3Rib3Q=");
            i(":orwell.freenode.net 900 * *!unknown@example.net stwtestbot :You are now logged in as stwtestbot");
            i(":orwell.freenode.net 903 * :SASL authentication successful");
            o("CAP END");
            o("USER username * * :real name");
            o("NICK stwtestbot");
            i(":orwell.freenode.net 001 stwtestbot :Welcome to the freenode Internet Relay Chat Network stwtestbot");
        }
        
        [Test]
        public void TestSasl302LongNegotiation()
        {
            // initial client setup
            this.DoSetup("stwtestbot", 
                "passwordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpassword",
                "usernameusername");

            // shortcut functions for logs
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => this.networkClient.Received().Send(Arg.Is(data));
            
            o("CAP LS 302");
            i(":orwell.freenode.net CAP * LS :sasl=PLAIN,EXTERNAL cap-notify");
            o("CAP REQ :sasl cap-notify");
            i(":orwell.freenode.net CAP * ACK :sasl cap-notify");
            o("AUTHENTICATE PLAIN");
            i("AUTHENTICATE +");
            o("AUTHENTICATE AHVzZXJuYW1ldXNlcm5hbWUAcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBh");
            o("AUTHENTICATE c3N3b3Jk");
            i(":orwell.freenode.net 900 * *!unknown@example.net usernameusername :You are now logged in as usernameusername");
            i(":orwell.freenode.net 903 * :SASL authentication successful");
        }
        
        [Test]
        public void TestSasl302LengthBoundaryNegotiation()
        {
            // initial client setup
            this.DoSetup("stwtestbot", 
                "passwordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpasswordpassword");

            // shortcut functions for logs
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => this.networkClient.Received().Send(Arg.Is(data));
            
            o("CAP LS 302");
            i(":orwell.freenode.net CAP * LS :sasl=PLAIN,EXTERNAL cap-notify");
            o("CAP REQ :sasl cap-notify");
            i(":orwell.freenode.net CAP * ACK :sasl cap-notify");
            o("AUTHENTICATE PLAIN");
            i("AUTHENTICATE +");
            o("AUTHENTICATE AHVzZXJuYW1lAHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZHBhc3N3b3JkcGFzc3dvcmRwYXNzd29yZA==");
            o("AUTHENTICATE +");
            i(":orwell.freenode.net 900 * *!unknown@example.net username :You are now logged in as username");
            i(":orwell.freenode.net 903 * :SASL authentication successful");
        }

        [Test]
        public void TestSasl302NoMechNegotiation()
        {
            // initial client setup
            this.DoSetup("stwtestbot", "stwtestbot");

            // shortcut functions for logs
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => this.networkClient.Received().Send(Arg.Is(data));
            
            o("CAP LS 302");
            i(":orwell.freenode.net CAP * LS :sasl=EXTERNAL cap-notify");
            o("CAP REQ cap-notify");
            i(":orwell.freenode.net CAP * ACK :cap-notify");
            o("PASS stwtestbot");
            o("USER username * * :real name");
            o("NICK stwtestbot");
            i(":orwell.freenode.net 001 stwtestbot :Welcome to the freenode Internet Relay Chat Network stwtestbot");
        }

        [Test]
        public void TestSasl301Negotiation()
        {
            // initial client setup
            this.DoSetup("stwtestbot", "stwtestbot");

            // shortcut functions for logs
            Action<string> i = data => networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(data));
            Action<string> o = data => this.networkClient.Received().Send(Arg.Is(data));
            
            o("CAP LS 302");
            i(":orwell.freenode.net CAP * LS :sasl cap-notify");
            o("CAP REQ :sasl cap-notify");
            i(":orwell.freenode.net CAP * ACK :sasl cap-notify");
            o("AUTHENTICATE PLAIN");
            i("AUTHENTICATE +");
            o("AUTHENTICATE AHVzZXJuYW1lAHN0d3Rlc3Rib3Q=");
            i(":orwell.freenode.net 900 * *!unknown@example.net stwtestbot :You are now logged in as stwtestbot");
            i(":orwell.freenode.net 903 * :SASL authentication successful");
            o("CAP END");
            o("USER username * * :real name");
            o("NICK stwtestbot");
            i(":orwell.freenode.net 001 stwtestbot :Welcome to the freenode Internet Relay Chat Network stwtestbot");
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
            this.networkClient.Received().Send(Arg.Is("WHO ##stwalkerster %uhnatfc,001"));
            this.networkClient.Received().Send(Arg.Is("WHO ##stwalkerster-development %uhnatfc,001"));

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
            this.networkClient.Received().Send(Arg.Is("WHO ##stwalkerster %uhnatfc,001"));
            this.networkClient.Received().Send(Arg.Is("WHO ##stwalkerster-development %uhnatfc,001"));

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
            this.networkClient.DataReceived += Raise.EventWith(new DataReceivedEventArgs(line));
        }
    }
}

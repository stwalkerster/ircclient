namespace Stwalkerster.IrcClient.Tests.IRC.Messages
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using Stwalkerster.IrcClient.Messages;

    /// <summary>
    /// The message tests.
    /// </summary>
    [TestFixture]
    public class MessageTests
    {
        /// <summary>
        /// The should parse correctly.
        /// </summary>
        [Test]
        public void ShouldParseCorrectly()
        { 
            // arrange
            string message = ":server 001 :Welcome!";
            var expected = new Message("server", "001", new[] { "Welcome!" });

            this.DoParseTest(message, expected);
        }

        /// <summary>
        /// The should parse correctly 2.
        /// </summary>
        [Test]
        public void ShouldParseCorrectly2()
        {
            // arrange
            string message = ":bla MODE foo bar :do something!";
            var expected = new Message("bla", "MODE", new List<string> { "foo", "bar", "do something!" });

            this.DoParseTest(message, expected);
        }

        /// <summary>
        /// The should parse correctly 3.
        /// </summary>
        [Test]
        public void ShouldParseCorrectly3()
        {
            // arrange
            string message = "PRSDS";
            var expected = new Message("PRSDS");

            this.DoParseTest(message, expected);
        }

        /// <summary>
        /// The should parse correctly 4.
        /// </summary>
        [Test]
        public void ShouldParseCorrectly4()
        {
            // arrange
            string message = "PRSDS foo bar baz";
            var expected = new Message("PRSDS", new[] { "foo", "bar", "baz" });

            this.DoParseTest(message, expected);
        }

        /// <summary>
        /// The should parse correctly 4.
        /// </summary>
        [Test]
        public void ShouldParseCorrectly5()
        {
            // arrange
            string message = ":hobana.freenode.net 354 stwalker|test 001 #wikipedia-en ~Marco 2a00:1158:2:7700::16 Gnumarcoo_ G 0";
            var expected = new Message(
                "hobana.freenode.net",
                "354",
                new List<string>
                    {
                        "stwalker|test",
                        "001",
                        "#wikipedia-en",
                        "~Marco",
                        "2a00:1158:2:7700::16",
                        "Gnumarcoo_",
                        "G",
                        "0"
                    });

            this.DoParseTest(message, expected);
        }

        /// <summary>
        /// The should parse correctly 6.
        /// </summary>
        [Test]
        public void ShouldParseCorrectly6()
        {
            // arrange
            string message = ":wolfe.freenode.net 005 hmbv7 CHANTYPES=# EXCEPTS INVEX CHANMODES=eIbq,k,flj,CFLMPQScgimnprstz CHANLIMIT=#:120 PREFIX=(ov)@+ MAXLIST=bqeI:100 MODES=4 NETWORK=freenode KNOCK STATUSMSG=@+ CALLERID=g :are supported by this server";
            var expected = new Message(
                "wolfe.freenode.net",
                "005",
                new List<string>
                    {
                        "hmbv7",
                        "CHANTYPES=#",
                        "EXCEPTS",
                        "INVEX",
                        "CHANMODES=eIbq,k,flj,CFLMPQScgimnprstz",
                        "CHANLIMIT=#:120",
                        "PREFIX=(ov)@+",
                        "MAXLIST=bqeI:100",
                        "MODES=4",
                        "NETWORK=freenode",
                        "KNOCK",
                        "STATUSMSG=@+",
                        "CALLERID=g",
                        "are supported by this server"
                    });

            this.DoParseTest(message, expected);
        }
        
        [Test]
        public void ShouldParseCorrectlyTags1()
        {
            // arrange
            string message = "@account=stwalkerster :stwalkerster!~stwalkers@wikerpedier/stwalkerster PRIVMSG ##stwalkerster :foo";
            var expected = new Message(
                "stwalkerster!~stwalkers@wikerpedier/stwalkerster",
                "PRIVMSG",
                new List<string>
                {
                    "##stwalkerster",
                    "foo"
                },
                new Dictionary<string, string>{{"account", "stwalkerster"}});

            this.DoParseTest(message, expected);
        }
        
        [Test]
        public void ShouldParseCorrectlyTags2()
        {
            // arrange
            string message = "@account=stwalkerster;foo=bar :stwalkerster!~stwalkers@wikerpedier/stwalkerster PRIVMSG ##stwalkerster :foo";
            var expected = new Message(
                "stwalkerster!~stwalkers@wikerpedier/stwalkerster",
                "PRIVMSG",
                new List<string>
                {
                    "##stwalkerster",
                    "foo"
                },
                new Dictionary<string, string> {{"account", "stwalkerster"}, {"foo", "bar"}});

            this.DoParseTest(message, expected);
        }
        
        [Test]
        public void ShouldParseCorrectlyTags3()
        {
            // arrange
            string message = "@account=stwalkerster\\spotato;foo=bar\\\\baz :stwalkerster!~stwalkers@wikerpedier/stwalkerster PRIVMSG ##stwalkerster :foo";
            var expected = new Message(
                "stwalkerster!~stwalkers@wikerpedier/stwalkerster",
                "PRIVMSG",
                new List<string>
                {
                    "##stwalkerster",
                    "foo"
                },
                new Dictionary<string, string> {{"account", "stwalkerster potato"}, {"foo", "bar\\baz"}});

            this.DoParseTest(message, expected);
        }

        /// <summary>
        /// The do parse test.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        /// <param name="expected">
        /// The expected.
        /// </param>
        private void DoParseTest(string message, Message expected)
        {
            // act
            var actual = (Message)Message.Parse(message);

            // assert
            Assert.That(actual.Prefix, Is.EqualTo(expected.Prefix));
            Assert.That(actual.Command, Is.EqualTo(expected.Command));
            Assert.That(actual.Parameters, Is.EqualTo(expected.Parameters));
            
            Assert.That(actual.Tags.Count, Is.EqualTo(expected.Tags.Count));
            foreach (var expectedKvp in expected.Tags)
            {
                Assert.That(actual.Tags.ContainsKey(expectedKvp.Key), Is.True);
                Assert.That(actual.Tags[expectedKvp.Key], Is.EqualTo(expectedKvp.Value));
            }
        }
    }
}

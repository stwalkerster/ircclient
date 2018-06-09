namespace Stwalkerster.IrcClient.Tests.IRC.Model
{
    using System.Collections;

    using Moq;

    using NUnit.Framework;

    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Model;

    [TestFixture]
    public class IrcUserMaskTests
    {
        public static IEnumerable TestData
        {
            get
            {
                var client = new Mock<IIrcClient>();
                client.Setup(x => x.ExtBanDelimiter).Returns("$");
                client.Setup(x => x.ExtBanTypes).Returns("a");
                
                var anonUser = new IrcUser(client.Object)
                {
                    Nickname = "a",
                    Username = "b",
                    Hostname = "c",
                    SkeletonStatus = IrcUserSkeletonStatus.Full
                };
                
                var accountUser = new IrcUser(client.Object)
                {
                    Nickname = "a",
                    Username = "b",
                    Hostname = "c",
                    Account = "d",
                    SkeletonStatus = IrcUserSkeletonStatus.Full
                };

                yield return new TestCaseData("*!*@*", anonUser).Returns(true);
                yield return new TestCaseData("a!*@*", anonUser).Returns(true);
                yield return new TestCaseData("*!b@*", anonUser).Returns(true);
                yield return new TestCaseData("*!*@c", anonUser).Returns(true);
                yield return new TestCaseData("d!*@c", anonUser).Returns(false);
                yield return new TestCaseData("a!?@c", anonUser).Returns(true);
                yield return new TestCaseData("a!??@c", anonUser).Returns(false);
                
                yield return new TestCaseData("*!*@*", accountUser).Returns(true);
                yield return new TestCaseData("a!*@*", accountUser).Returns(true);
                yield return new TestCaseData("*!b@*", accountUser).Returns(true);
                yield return new TestCaseData("*!*@c", accountUser).Returns(true);
                yield return new TestCaseData("d!*@c", accountUser).Returns(false);
                yield return new TestCaseData("a!?@c", accountUser).Returns(true);
                yield return new TestCaseData("a!??@c", accountUser).Returns(false);
                
                yield return new TestCaseData("$a:d", accountUser).Returns(true);
                yield return new TestCaseData("$a:e", accountUser).Returns(false);
                yield return new TestCaseData("$a:d", anonUser).Returns(false);
                
                // Need to verify these vs atheme.
                yield return new TestCaseData("$~a:d", accountUser).Returns(false);
                yield return new TestCaseData("$~a:e", accountUser).Returns(true);
                yield return new TestCaseData("$~a:d", anonUser).Returns(true);
                
                
                yield return new TestCaseData("$a", accountUser).Returns(true);
                yield return new TestCaseData("$a", anonUser).Returns(false);
                yield return new TestCaseData("$~a", accountUser).Returns(false);
                yield return new TestCaseData("$~a", anonUser).Returns(true);
            }
        }

        [Test, TestCaseSource(typeof(IrcUserMaskTests), "TestData")]
        public bool? MatchTests(string mask, IrcUser user)
        {
            var ircUserMask = new IrcUserMask(mask, user.Client);
            Assert.AreEqual(mask, ircUserMask.ToString());
            return ircUserMask.Matches(user);
        }

        [Test]
        public void TestEmptyExtBanPrefix()
        {
            var client = new Mock<IIrcClient>();
            client.Setup(x => x.ExtBanDelimiter).Returns(string.Empty);
            client.Setup(x => x.ExtBanTypes).Returns("a");

            var user = new IrcUser(client.Object)
            {
                Nickname = "a",
                Username = "a",
                Hostname = "a",
                Account = "foo",
                SkeletonStatus = IrcUserSkeletonStatus.Full
            };
            
            var mask = new IrcUserMask("a:foo", client.Object);

            var result = mask.Matches(user);
            Assert.True(result);
        }
    }
}
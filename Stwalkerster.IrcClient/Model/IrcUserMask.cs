namespace Stwalkerster.IrcClient.Model
{
    using System;
    using System.Text.RegularExpressions;

    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Model.Interfaces;

    public class IrcUserMask
    {
        private static readonly Regex NuhMask = new Regex(@"(?:^(?<nick>.+)\!(?<user>.+)@(?<host>.+)$)");
        
        private readonly bool isExtMask, inverted;
        private readonly string nick, user, host, type, parameter;
        private readonly Regex nickRegex, userRegex, hostRegex;
        
        public IrcUserMask(string mask, IIrcClient client)
        {
            var skipExtBans = false;
            if (string.IsNullOrEmpty(client.ExtBanTypes))
            {
                client.WaitOnRegistration();
                if (string.IsNullOrEmpty(client.ExtBanTypes))
                {
                    // ext bans aren't available at all here.
                    skipExtBans = true;
                    this.isExtMask = false;
                }
            }

            if (!skipExtBans)
            {
                var extMask = new Regex(
                    @"(?:^" + Regex.Escape(client.ExtBanDelimiter)
                            + @"(?<inverted>~?)(?<type>[a-z])(:(?<parameter>.+))?$)");

                // must match either n!u@h, or an extban as supported by the client. Extbans are a bit more restrictive, so check that first.
                var extMatch = extMask.Match(mask);
                if (extMatch.Success)
                {
                    this.type = extMatch.Groups["type"].Value;
                    this.inverted = extMatch.Groups["inverted"].Value == "~";
                    this.parameter = extMatch.Groups["parameter"].Success ? extMatch.Groups["parameter"].Value : null;

                    this.isExtMask = true;

                    return;
                }
            }

            var nuhMatch = NuhMask.Match(mask);
            if (nuhMatch.Success)
            {
                this.nick = nuhMatch.Groups["nick"].Value;
                this.user = nuhMatch.Groups["user"].Value;
                this.host = nuhMatch.Groups["host"].Value;
                
                this.nickRegex = new Regex(Regex.Escape(this.nick).Replace(@"\?", ".").Replace(@"\*", ".*"));
                this.userRegex = new Regex(Regex.Escape(this.user).Replace(@"\?", ".").Replace(@"\*", ".*"));
                this.hostRegex = new Regex(Regex.Escape(this.host).Replace(@"\?", ".").Replace(@"\*", ".*"));

                this.isExtMask = false;

                return;
            }

            throw new ArgumentOutOfRangeException("mask");
        }

        public bool? Matches(IUser userObj)
        {
            var ircUser = userObj as IrcUser;

            if (ircUser == null)
            {
                return false;
            }

            if (this.isExtMask)
            {
                switch (this.type)
                {
                    case "R": // inspircd services account
                    case "a": // freenode services account
                        return this.MatchAccountExtmask(ircUser);
                    default:
                        return null;
                }
            }

            return this.nickRegex.IsMatch(userObj.Nickname)
                   && this.userRegex.IsMatch(userObj.Username)
                   && this.hostRegex.IsMatch(userObj.Hostname);
        }

        private bool? MatchAccountExtmask(IrcUser ircuser)
        {
            if (ircuser.SkeletonStatus < IrcUserSkeletonStatus.Account)
            {
                return null;
            }

            if (this.parameter == null)
            {
                var loggedIn = ircuser.Account != null;

                return this.inverted ? !loggedIn : loggedIn;
            }

            if (this.parameter == ircuser.Account)
            {
                return !this.inverted;
            }

            return this.inverted;
        }

        public override string ToString()
        {
            if (this.isExtMask)
            {
                return string.Format("${0}{1}{2}", this.inverted ? "~" : string.Empty, this.type,
                    string.IsNullOrEmpty(this.parameter) ? "" : ":" + this.parameter);
            }

            return string.Format("{0}!{1}@{2}", this.nick, this.user, this.host);
        }
    }
}
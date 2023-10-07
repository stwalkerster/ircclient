namespace Stwalkerster.IrcClient.Model
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The channel user.
    /// </summary>
    public class IrcChannelUser
    {
        // map of mode char => prefix
        private readonly Dictionary<string, string> prefixFlags = new Dictionary<string, string>();
        
        public IrcChannelUser(IrcUser user, string channel)
        {
            this.User = user;
            this.Channel = channel;
        }

        public IrcUser User { get; }

        public string Channel { get; }

        public bool Operator
        {
            get => this.prefixFlags.ContainsKey("o");
            set
            {
                if (value && ! this.prefixFlags.ContainsKey("o"))
                {
                    this.prefixFlags.Add("o", "@");
                }
            }
        }

        public bool Voice
        {
            get => this.prefixFlags.ContainsKey("v");
            set
            {
                if (value && ! this.prefixFlags.ContainsKey("v"))
                {
                    this.prefixFlags.Add("v", "+");
                }
            }
        }

        public void SetPrefix(string mode, string prefix)
        {
            if (!this.prefixFlags.ContainsKey(mode))
            {
                this.prefixFlags.Add(mode, prefix);
            }
        }
        
        public void RemovePrefix(string mode)
        {
            if (this.prefixFlags.ContainsKey(mode))
            {
                this.prefixFlags.Remove(mode);
            }
        }
        
        public override string ToString()
        {
            return string.Format(
                "[{0} {1} {2}]",
                this.Channel,
                this.StatusPrefix,
                this.User);
        }

        public string StatusPrefix => this.prefixFlags.Values.Aggregate("", (s, c) => s + c);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != this.GetType())
            {
                return false;
            }

            return this.Equals((IrcChannelUser) obj);
        }
        
        public override int GetHashCode()
        {
            unchecked
            {
                return ((this.User != null ? this.User.GetHashCode() : 0) * 397)
                       ^ (this.Channel != null ? this.Channel.GetHashCode() : 0);
            }
        }
        
        protected bool Equals(IrcChannelUser other)
        {
            return Equals(this.User, other.User) && string.Equals(this.Channel, other.Channel);
        }
    }
}
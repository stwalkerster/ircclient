namespace Stwalkerster.IrcClient
{
    using System;
    using Stwalkerster.IrcClient.Interfaces;

    public class IrcConfiguration : IIrcConfiguration
    {
        public IrcConfiguration(
            string hostname,
            int port,
            bool authToServices,
            string nickname,
            string username,
            string realName,
            bool ssl,
            string clientName,
            string serverPassword = null,
            string servicesUsername = null,
            string servicesPassword = null,
            bool restartOnHeavyLag = true,
            bool reclaimNickFromService = true,
            int pingInterval = 15,
            int missedPingLimit = 3
            )
        {
            if (hostname == null)
            {
                throw new ArgumentNullException("hostname");
            }

            if (string.IsNullOrWhiteSpace(hostname))
            {
                throw new ArgumentOutOfRangeException("hostname");
            }

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException("port");
            }

            if (nickname == null)
            {
                throw new ArgumentNullException("nickname");
            }

            if (string.IsNullOrWhiteSpace(nickname))
            {
                throw new ArgumentOutOfRangeException("nickname");
            }
            
            if (realName == null)
            {
                realName = nickname;
            }

            if (username == null)
            {
                username = nickname;
            }

            this.AuthToServices = authToServices;
            this.Hostname = hostname;
            this.Nickname = nickname;
            this.Port = port;
            this.RealName = realName;
            this.Username = username;
            this.ServerPassword = serverPassword;
            this.Ssl = ssl;
            this.ClientName = clientName;
            this.RestartOnHeavyLag = restartOnHeavyLag;
            this.ReclaimNickFromServices = reclaimNickFromService;
            this.PingInterval = pingInterval;
            this.MissedPingLimit = missedPingLimit;

            this.ServicesUsername = servicesUsername;
            this.ServicesPassword = servicesPassword;
        }

        public bool AuthToServices { get; private set; }
        public string Hostname { get; private set; }
        public string Nickname { get; private set; }
        public int Port { get; private set; }
        public string RealName { get; private set; }
        public string Username { get; private set; }
        public string ServerPassword { get; private set; }

        public string ServicesUsername { get; private set; }
        public string ServicesPassword { get; private set; }
        public bool Ssl { get; private set; }
        public string ClientName { get; private set; }
        public bool RestartOnHeavyLag { get; private set; }
        public bool ReclaimNickFromServices { get; private set; }
        public int PingInterval { get; }
        public int MissedPingLimit { get; }
    }
}
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
            string servicesCertificate = null,
            bool restartOnHeavyLag = true,
            bool reclaimNickFromService = true,
            int pingInterval = 15,
            int missedPingLimit = 3,
            string connectModes = "+Q"
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
            this.ServicesCertificate = servicesCertificate;

            this.ConnectModes = connectModes;
        }

        public bool AuthToServices { get; }
        public string Hostname { get; }
        public string Nickname { get; }
        public int Port { get; }
        public string RealName { get; }
        public string Username { get; }
        public string ServerPassword { get; }

        public string ServicesUsername { get; }
        public string ServicesPassword { get; }
        public string ServicesCertificate { get; }
        public bool Ssl { get; }
        public string ClientName { get; }
        public bool RestartOnHeavyLag { get; }
        public bool ReclaimNickFromServices { get; }
        public int PingInterval { get; }
        public int MissedPingLimit { get; }
        public string ConnectModes { get; }
    }
}
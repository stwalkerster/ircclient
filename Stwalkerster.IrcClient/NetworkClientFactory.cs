namespace Stwalkerster.IrcClient
{
    using Microsoft.Extensions.Logging;
    using Stwalkerster.IrcClient.Interfaces;
    using Stwalkerster.IrcClient.Network;

    public class NetworkClientFactory
    {
        public static INetworkClient Create(IIrcConfiguration configuration, ILoggerFactory loggerFactory)
        {
            INetworkClient client;
            if (configuration.Ssl)
            {
                client = new SslNetworkClient(
                    configuration.Hostname,
                    configuration.Port,
                    loggerFactory.CreateLogger<SslNetworkClient>());
            }
            else
            {
                client = new NetworkClient(
                    configuration.Hostname,
                    configuration.Port,
                    loggerFactory.CreateLogger<NetworkClient>());
            }

            return client;
        }
    }
}
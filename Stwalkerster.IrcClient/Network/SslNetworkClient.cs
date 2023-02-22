namespace Stwalkerster.IrcClient.Network
{
    using System.IO;
    using System.Net.Security;
    using System.Security.Authentication;
    using System.Security.Cryptography.X509Certificates;
    using Microsoft.Extensions.Logging;

    public class SslNetworkClient : NetworkClient
    {
        private readonly string servicesCertificate;

        public SslNetworkClient(string hostname, int port, ILogger<SslNetworkClient> logger, string servicesCertificate)
            : base(hostname, port, logger)
        {
            this.servicesCertificate = servicesCertificate;
        }

        /// <inheritdoc />
        public override void Connect()
        {
            this.Connect(false);
            
            var sslStream = new SslStream(this.Client.GetStream());

            this.Logger?.LogInformation("Performing SSL Handshake...");

            var clientCerts = new X509CertificateCollection();
            
            if (!string.IsNullOrWhiteSpace(this.servicesCertificate))
            {
                var cert = new X509Certificate2(this.servicesCertificate);
                clientCerts.Add(cert);
            }

            sslStream.AuthenticateAsClient(this.Hostname, clientCerts, SslProtocols.Tls12, false);

            this.Reader = new StreamReader(sslStream);
            this.Writer = new StreamWriter(sslStream);
            
            this.StartThreads();
        }
    }
}
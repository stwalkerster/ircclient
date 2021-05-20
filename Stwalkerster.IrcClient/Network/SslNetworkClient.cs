namespace Stwalkerster.IrcClient.Network
{
    using System.IO;
    using System.Net.Security;
    using System.Security.Authentication;
    using System.Security.Cryptography.X509Certificates;
    using Castle.Core.Logging;

    /// <summary>
    /// The SSL network client.
    /// </summary>
    public class SslNetworkClient : NetworkClient
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="SslNetworkClient" /> class.
        /// </summary>
        /// <param name="hostname">
        /// The hostname.
        /// </param>
        /// <param name="port">
        /// The port.
        /// </param>
        /// <param name="logger">
        /// The logger.
        /// </param>
        public SslNetworkClient(string hostname, int port, ILogger logger)
            : base(hostname, port, logger)
        {
        }

        /// <inheritdoc />
        public override void Connect()
        {
            this.Connect(false);
            
            var sslStream = new SslStream(this.Client.GetStream());

            this.Logger.Info("Performing SSL Handshake...");

            sslStream.AuthenticateAsClient(this.Hostname, new X509CertificateCollection(), SslProtocols.Tls12, false);

            this.Reader = new StreamReader(sslStream);
            this.Writer = new StreamWriter(sslStream);
            
            this.StartThreads();
        }
    }
}
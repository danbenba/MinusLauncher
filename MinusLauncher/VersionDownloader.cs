﻿// Utilisation des bibliothèques nécessaires
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace MCLauncher
{

    // Une exception personnalisée pour signaler une identité de mise à jour incorrecte
    class BadUpdateIdentityException : ArgumentException
    {
        public BadUpdateIdentityException() : base("Mauvaise identité de mise à jour") { }
    }

    // Classe chargée de télécharger les versions de mise à jour
    class VersionDownloader
    {

        private HttpClient client = new HttpClient();
        private WUProtocol protocol = new WUProtocol();

        // Envoi de requêtes XML asynchrones
        private async Task<XDocument> PostXmlAsync(string url, XDocument data)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
            using (var stringWriter = new StringWriter())
            {
                using (XmlWriter xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true }))
                {
                    data.Save(xmlWriter);
                }
                request.Content = new StringContent(stringWriter.ToString(), Encoding.UTF8, "application/soap+xml");
            }
            using (var resp = await client.SendAsync(request))
            {
                string str = await resp.Content.ReadAsStringAsync();
                return XDocument.Parse(str);
            }
        }

        // Téléchargement du fichier avec suivi de la progression
        private async Task DownloadFile(string url, string to, DownloadProgress progress, CancellationToken cancellationToken)
        {
            using (var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                using (var inStream = await resp.Content.ReadAsStreamAsync())
                using (var outStream = new FileStream(to, FileMode.Create))
                {
                    long? totalSize = resp.Content.Headers.ContentLength;
                    progress(0, totalSize);
                    long transferred = 0;
                    byte[] buf = new byte[1024 * 1024];
                    while (true)
                    {
                        int n = await inStream.ReadAsync(buf, 0, buf.Length, cancellationToken);
                        if (n == 0)
                            break;
                        await outStream.WriteAsync(buf, 0, n, cancellationToken);
                        transferred += n;
                        progress(transferred, totalSize);
                    }
                }
            }
        }

        // Obtention de l'URL de téléchargement à partir de l'identité de la mise à jour et du numéro de révision
        private async Task<string> GetDownloadUrl(string updateIdentity, string revisionNumber)
        {
            XDocument result = await PostXmlAsync(protocol.GetDownloadUrl(),
                protocol.BuildDownloadRequest(updateIdentity, revisionNumber));
            Debug.WriteLine($"Réponse de GetDownloadUrl() pour l'identité de mise à jour {updateIdentity}, révision {revisionNumber}:\n{result.ToString()}");
            foreach (string s in protocol.ExtractDownloadResponseUrls(result))
            {
                if (s.StartsWith("http://tlu.dl.delivery.mp.microsoft.com/"))
                    return s;
            }
            return null;
        }

        // Activation de l'autorisation utilisateur
        public void EnableUserAuthorization()
        {
            protocol.SetMSAUserToken(WUTokenHelper.GetWUToken());
        }

        // Processus de téléchargement de la mise à jour
        public async Task Download(string updateIdentity, string revisionNumber, string destination, DownloadProgress progress, CancellationToken cancellationToken)
        {
            string link = await GetDownloadUrl(updateIdentity, revisionNumber);
            if (link == null)
                throw new BadUpdateIdentityException();
            Debug.WriteLine("Lien de téléchargement résolu : " + link);
            await DownloadFile(link, destination, progress, cancellationToken);
        }

        // Déclaration du délégué pour le suivi de la progression du téléchargement
        public delegate void DownloadProgress(long current, long? total);

    }
}

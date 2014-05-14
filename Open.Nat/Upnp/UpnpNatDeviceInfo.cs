using System;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;

namespace Open.Nat
{
    internal class UpnpNatDeviceInfo
    {
        public UpnpNatDeviceInfo(IPAddress localAddress, string locationDetails, string serviceType)
        {
            var locationUri = new Uri(locationDetails);

            NatUtility.Log("Found device at: {0}", locationUri.ToString());

            LocalAddress = localAddress;
            Ip = locationUri.Host;
            Port = locationUri.Port;
            ServiceDescriptionPart = locationUri.AbsolutePath;
            ServiceType = serviceType;
        }

        public string Ip { get; private set; }
        public int Port { get; private set; }

        public IPAddress LocalAddress { get; private set; }

        public string ServiceDescriptionPart { get; private set; }

        public string ServiceType { get; private set; }

        public string ServiceControlPart { get; private set; }

        public IPEndPoint HostEndPoint
        {
            get { return new IPEndPoint(IPAddress.Parse(Ip), Port); }
        }

        public Uri ServiceControlUri
        {
            get
            {
                var builder = new UriBuilder("http", Ip, Port, ServiceControlPart);
                return builder.Uri;
            }
        }

        public Uri ServiceDescriptionUri
        {
            get
            {
                var builder = new UriBuilder("http", Ip, Port, ServiceDescriptionPart);
                return builder.Uri;
            }
        }

        internal void UpdateInfo()
        {
            WebResponse response = null;
            try
            {
                var request = WebRequest.CreateHttp(ServiceDescriptionUri);
                request.Headers.Add("ACCEPT-LANGUAGE", "en");
                request.Method = "GET";

                response = request.GetResponse();

                var httpresponse = response as HttpWebResponse;

                if (httpresponse != null && httpresponse.StatusCode != HttpStatusCode.OK)
                {
                    NatUtility.Log("Couldn't get services list: {0}", httpresponse.StatusCode);
                    return; // FIXME: This the best thing to do??
                }

                var xmldoc = ReadXmlResponse(response);

                NatUtility.Log("{0}: Parsed services list", HostEndPoint);
                var ns = new XmlNamespaceManager(xmldoc.NameTable);
                ns.AddNamespace("ns", "urn:schemas-upnp-org:device-1-0");
                var nodes = xmldoc.SelectNodes("//*/ns:serviceList", ns);

                foreach (XmlNode node in nodes)
                {
                    //Go through each service there
                    foreach (XmlNode service in node.ChildNodes)
                    {
                        //If the service is a WANIPConnection, then we have what we want
                        var type = service.GetXmlElementText("serviceType");
                        NatUtility.Log("{0}: Found service: {1}", HostEndPoint, type);

                        if (!type.Equals(ServiceType, StringComparison.OrdinalIgnoreCase)) continue;

                        ServiceControlPart = service.GetXmlElementText("controlURL");
                        NatUtility.Log("{0}: Found upnp service at: {1}", HostEndPoint, ServiceControlPart);

                        if (Uri.IsWellFormedUriString(ServiceControlPart, UriKind.Absolute))
                        {
                            var u = new Uri(ServiceControlPart);
                            var old = HostEndPoint;
                            Ip = u.Host;
                            Port = u.Port;
                            NatUtility.Log("{0}: Absolute URI detected. Host address is now: {1}", old, HostEndPoint);
                            ServiceControlPart = ServiceControlPart.Substring(u.GetLeftPart(UriPartial.Authority).Length);
                            NatUtility.Log("{0}: New control url: {1}", HostEndPoint, ServiceControlUri);
                        }
                        NatUtility.Log("{0}: Handshake Complete", HostEndPoint);
                    }
                }
            }
            catch (WebException ex)
            {
                // Just drop the connection, FIXME: Should i retry?
                NatUtility.Log("{0}: Device denied the connection attempt: {1}", HostEndPoint, ex);
            }
            finally
            {
                if (response != null)
                    response.Close();
            }
        }

        private XmlDocument ReadXmlResponse(WebResponse response)
        {
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                var servicesXml = reader.ReadToEnd();
                var xmldoc = new XmlDocument();
                xmldoc.LoadXml(servicesXml);
                return xmldoc;
            }
        }


    }
}
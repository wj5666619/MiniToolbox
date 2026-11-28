using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text;
using System.Media;
using Microsoft.Win32;
using System.Reflection;
using System.Collections.Generic;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Accessibility;

namespace ShutdownTool
{
    static class UpnpCore
    {
        public class Device
        {
            public string Name = "";
            public string Location = "";
            public string ControlUrl = "";
            public string ServiceType = "";
            public string LocalIp = "";
            public string ExternalIp = "";
        }

        public static string LocalIp()
        {
            try
            {
                Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect("8.8.8.8", 53);
                IPEndPoint ep = (IPEndPoint)s.LocalEndPoint;
                s.Close();
                return ep.Address.ToString();
            }
            catch (Exception) { return "127.0.0.1"; }
        }

        static string HeaderOf(string txt, string name)
        {
            string[] lines = txt.Split('\n');
            foreach (string raw in lines)
            {
                string ln = raw.Trim();
                int i = ln.IndexOf(':');
                if (i < 0) continue;
                if (string.Equals(ln.Substring(0, i).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return ln.Substring(i + 1).Trim();
            }
            return "";
        }

        public static List<Device> Discover(int timeoutMs)
        {
            List<Device> list = new List<Device>();
            UdpClient udp = null;
            try
            {
                string msg = "M-SEARCH * HTTP/1.1\r\n" +
                    "HOST: 239.255.255.250:1900\r\n" +
                    "MAN: \"ssdp:discover\"\r\n" +
                    "MX: 2\r\n" +
                    "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";
                byte[] req = Encoding.ASCII.GetBytes(msg);
                udp = new UdpClient();
                udp.Client.ReceiveTimeout = 1000;
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                IPEndPoint mc = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                udp.Send(req, req.Length, mc);
                IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
                DateTime end = DateTime.Now.AddMilliseconds(timeoutMs);
                while (DateTime.Now < end)
                {
                    byte[] resp = null;
                    try { resp = udp.Receive(ref any); }
                    catch (SocketException) { continue; }
                    if (resp == null || resp.Length == 0) continue;
                    string txt = Encoding.ASCII.GetString(resp);
                    string st = HeaderOf(txt, "ST");
                    string usn = HeaderOf(txt, "USN");
                    bool wan = st.IndexOf("InternetGatewayDevice", StringComparison.OrdinalIgnoreCase) >= 0
                        || st.IndexOf("WANDevice", StringComparison.OrdinalIgnoreCase) >= 0
                        || st.IndexOf("WANConnectionDevice", StringComparison.OrdinalIgnoreCase) >= 0
                        || usn.IndexOf("InternetGatewayDevice", StringComparison.OrdinalIgnoreCase) >= 0
                        || usn.IndexOf("WANDevice", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!wan) continue;
                    string loc = HeaderOf(txt, "LOCATION");
                    if (loc.Length == 0) continue;
                    bool dup = false;
                    foreach (Device d in list) { if (d.Location == loc) { dup = true; break; } }
                    if (dup) continue;
                    Device dv = Describe(loc);
                    if (dv != null) list.Add(dv);
                }
            }
            catch (Exception dg105_1) { Diag.Swallow("UpnpCore.cs:105", dg105_1); }
            finally { try { if (udp != null) udp.Close(); } catch (Exception dg106_1) { Diag.Swallow("UpnpCore.cs:106", dg106_1); } }
            return list;
        }

        static System.Xml.XmlNode FindNode(System.Xml.XmlNode root, string local)
        {
            if (root == null) return null;
            if (string.Equals(root.LocalName, local, StringComparison.OrdinalIgnoreCase)) return root;
            foreach (System.Xml.XmlNode c in root.ChildNodes)
            {
                System.Xml.XmlNode r = FindNode(c, local);
                if (r != null) return r;
            }
            return null;
        }

        static System.Xml.XmlNode FindWanService(System.Xml.XmlNode root)
        {
            if (root == null) return null;
            if (string.Equals(root.LocalName, "service", StringComparison.OrdinalIgnoreCase))
            {
                System.Xml.XmlNode t = FindNode(root, "serviceType");
                if (t != null)
                {
                    string v = t.InnerText;
                    if (v.IndexOf("WANIPConnection", StringComparison.OrdinalIgnoreCase) >= 0
                        || v.IndexOf("WANPPPConnection", StringComparison.OrdinalIgnoreCase) >= 0) return root;
                }
            }
            foreach (System.Xml.XmlNode c in root.ChildNodes)
            {
                System.Xml.XmlNode r = FindWanService(c);
                if (r != null) return r;
            }
            return null;
        }

        static string HostOf(string url)
        {
            try { return new Uri(url).Host; }
            catch (Exception) { return ""; }
        }

        static string AbsUrl(string baseUrl, string rel)
        {
            try
            {
                if (rel.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return rel;
                Uri b = new Uri(baseUrl);
                return new Uri(b, rel).ToString();
            }
            catch (Exception) { return rel; }
        }

        static string HttpGet(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
                rq.Method = "GET";
                rq.Timeout = timeoutMs;
                rq.ReadWriteTimeout = timeoutMs;
                rq.Proxy = null;
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (Exception) { return ""; }
        }

        static Device Describe(string location)
        {
            string xml = HttpGet(location, 6000);
            if (xml.Length == 0) return null;
            return ParseDescription(xml, location);
        }

        public static Device ParseDescription(string xml, string location)
        {
            try
            {
                if (xml == null || xml.Length == 0) return null;
                System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                doc.XmlResolver = null;
                doc.LoadXml(xml);
                Device dv = new Device();
                dv.Location = location;
                dv.LocalIp = HostOf(location);
                System.Xml.XmlNode friendly = FindNode(doc.DocumentElement, "friendlyName");
                if (friendly != null) dv.Name = friendly.InnerText.Trim();
                System.Xml.XmlNode model = FindNode(doc.DocumentElement, "modelName");
                if (dv.Name.Length == 0 && model != null) dv.Name = model.InnerText.Trim();
                System.Xml.XmlNode svc = FindWanService(doc.DocumentElement);
                if (svc == null) return null;
                string ctrl = "";
                string stype = "";
                foreach (System.Xml.XmlNode n in svc.ChildNodes)
                {
                    if (n.NodeType != System.Xml.XmlNodeType.Element) continue;
                    if (string.Equals(n.LocalName, "controlURL", StringComparison.OrdinalIgnoreCase)) ctrl = n.InnerText.Trim();
                    if (string.Equals(n.LocalName, "serviceType", StringComparison.OrdinalIgnoreCase)) stype = n.InnerText.Trim();
                }
                if (ctrl.Length == 0) return null;
                dv.ControlUrl = AbsUrl(location, ctrl);
                dv.ServiceType = stype;
                if (dv.Name.Length == 0) dv.Name = "UPnP 路由器";
                return dv;
            }
            catch (Exception) { return null; }
        }

        public static string Soap(Device dv, string action, string body, int timeoutMs)
        {
            if (dv == null || dv.ControlUrl.Length == 0) return "ERR:no-device";
            string env = "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
                "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>" +
                "<u:" + action + " xmlns:u=\"" + dv.ServiceType + "\">" + body + "</u:" + action + "></s:Body></s:Envelope>";
            try
            {
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(dv.ControlUrl);
                rq.Method = "POST";
                rq.ContentType = "text/xml; charset=\"utf-8\"";
                rq.Headers.Add("SOAPACTION", "\"" + dv.ServiceType + "#" + action + "\"");
                rq.Timeout = timeoutMs;
                rq.ReadWriteTimeout = timeoutMs;
                rq.Proxy = null;
                byte[] data = Encoding.UTF8.GetBytes(env);
                rq.ContentLength = data.Length;
                using (Stream req = rq.GetRequestStream()) req.Write(data, 0, data.Length);
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (WebException wex)
            {
                try
                {
                    HttpWebResponse err = (HttpWebResponse)wex.Response;
                    if (err != null)
                        using (StreamReader sr = new StreamReader(err.GetResponseStream(), Encoding.UTF8))
                            return "ERR:" + sr.ReadToEnd();
                }
                catch (Exception dg249_1) { Diag.Swallow("UpnpCore.cs:249", dg249_1); }
                return "ERR:" + wex.Message;
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        public static string Field(string xml, string name)
        {
            if (xml == null || xml.Length == 0) return "";
            if (xml.StartsWith("ERR:")) return "";
            try
            {
                System.Xml.XmlDocument d = new System.Xml.XmlDocument();
                d.XmlResolver = null;
                d.LoadXml(xml);
                System.Xml.XmlNode n = FindNode(d.DocumentElement, name);
                return n == null ? "" : n.InnerText.Trim();
            }
            catch (Exception) { return ""; }
        }

        static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        public static string ExternalIp(Device dv)
        {
            string r = Soap(dv, "GetExternalIPAddress", "", 6000);
            return Field(r, "NewExternalIPAddress");
        }

        public static string GetMapping(Device dv, int port, string proto)
        {
            string body = "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + port +
                "</NewExternalPort><NewProtocol>" + proto + "</NewProtocol>";
            string r = Soap(dv, "GetSpecificPortMappingEntry", body, 6000);
            if (r.StartsWith("ERR:")) return "ERR";
            string ip = Field(r, "NewInternalClient");
            if (ip.Length == 0) return "none";
            string ipport = Field(r, "NewInternalPort");
            string desc = Field(r, "NewPortMappingDescription");
            return ip + (ipport.Length > 0 ? ":" + ipport : "") + (desc.Length > 0 ? " " + desc : "");
        }

        public static List<string[]> ListMappings(Device dv, int max)
        {
            List<string[]> rows = new List<string[]>();
            for (int i = 0; i < max; i++)
            {
                string r = Soap(dv, "GetGenericPortMappingEntry", "<NewPortMappingIndex>" + i + "</NewPortMappingIndex>", 6000);
                if (r.StartsWith("ERR:")) break;
                string ep = Field(r, "NewExternalPort");
                if (ep.Length == 0) break;
                string ip = Field(r, "NewInternalClient");
                string ipp = Field(r, "NewInternalPort");
                string desc = Field(r, "NewPortMappingDescription");
                string en = Field(r, "NewEnabled");
                rows.Add(new string[] { ep, Field(r, "NewProtocol"), ip + (ipp.Length > 0 ? ":" + ipp : ""), desc, en == "0" ? "关" : "开" });
            }
            return rows;
        }

        public static string AddMapping(Device dv, string desc, int port, string proto, string localIp)
        {
            string body = "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + port + "</NewExternalPort>" +
                "<NewProtocol>" + proto + "</NewProtocol><NewInternalPort>" + port + "</NewInternalPort>" +
                "<NewInternalClient>" + localIp + "</NewInternalClient><NewEnabled>1</NewEnabled>" +
                "<NewPortMappingDescription>" + Esc(desc) + "</NewPortMappingDescription>" +
                "<NewLeaseDuration>0</NewLeaseDuration>";
            string r = Soap(dv, "AddPortMapping", body, 8000);
            if (r.StartsWith("ERR:")) return r;
            return "ok";
        }

        public static string DeleteMapping(Device dv, int port, string proto)
        {
            string body = "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + port +
                "</NewExternalPort><NewProtocol>" + proto + "</NewProtocol>";
            string r = Soap(dv, "DeletePortMapping", body, 8000);
            if (r.StartsWith("ERR:")) return r;
            return "ok";
        }

        public static string SelfTestText()
        {
            StringBuilder sb = new StringBuilder();
            string lip = LocalIp();
            sb.AppendLine("local_ip=" + lip);
            List<Device> ds = Discover(7000);
            sb.AppendLine("devices=" + ds.Count);
            foreach (Device d in ds)
            {
                d.ExternalIp = ExternalIp(d);
                sb.AppendLine("  " + d.Name + " | control=" + d.ControlUrl + " | ext=" + (d.ExternalIp.Length == 0 ? "-" : d.ExternalIp) + " | dev=" + d.LocalIp);
            }
            if (ds.Count > 0)
            {
                sb.AppendLine("probe_25565_tcp=" + GetMapping(ds[0], 25565, "TCP"));
                List<string[]> rows = ListMappings(ds[0], 20);
                sb.AppendLine("mapping_rows=" + rows.Count);
                foreach (string[] r in rows)
                    sb.AppendLine("  " + r[0] + "/" + r[1] + " -> " + r[2] + " " + r[3] + " enabled=" + r[4]);
                sb.AppendLine("soap_ok=True");
            }
            else sb.AppendLine("soap_ok=False");
            string[] sample = new string[] {
                "<?xml version=\"1.0\"?><root xmlns=\"urn:schemas-upnp-org:device-1-0\">",
                "<device><deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>",
                "<friendlyName>Sample Router 812</friendlyName><manufacturer>Demo</manufacturer>",
                "<deviceList><device><deviceType>urn:schemas-upnp-org:device:WANDevice:1</deviceType>",
                "<deviceList><device><deviceType>urn:schemas-upnp-org:device:WANConnectionDevice:1</deviceType>",
                "<serviceList><service><serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>",
                "<serviceId>urn:upnp-org:serviceId:WANIPConn1</serviceId>",
                "<controlURL>/ctl/IPConn</controlURL><eventSubURL>/evt/IPConn</eventSubURL>",
                "</service></serviceList></device></deviceList></device></deviceList></device></root>" };
            Device sd = ParseDescription(string.Join("", sample), "http://192.168.1.1:5000/rootDesc.xml");
            if (sd == null) sb.AppendLine("parse_sample=FAIL");
            else sb.AppendLine("parse_sample=ok name=" + sd.Name + " control=" + sd.ControlUrl + " type=" + sd.ServiceType);
            string mappingBody = "<NewExternalPort>25565</NewExternalPort><NewProtocol>TCP</NewProtocol><NewInternalClient>192.168.1.20</NewInternalClient><NewInternalPort>25565</NewInternalPort><NewPortMappingDescription>minecraft</NewPortMappingDescription><NewEnabled>1</NewEnabled>";
            string wrapped = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetSpecificPortMappingEntryResponse xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" + mappingBody + "</u:GetSpecificPortMappingEntryResponse></s:Body></s:Envelope>";
            sb.AppendLine("parse_soap=client=" + Field(wrapped, "NewInternalClient") + " desc=" + Field(wrapped, "NewPortMappingDescription") + " enabled=" + Field(wrapped, "NewEnabled"));
            return sb.ToString();
        }
    }
}
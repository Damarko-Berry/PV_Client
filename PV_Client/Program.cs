using PVLib;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Serialization;


namespace PV_Client
{
    internal class Program
    {
        static ClientState state;
        static ChannelList LocalChannels = new ChannelList();
        static ChannelList OtherChannels = new ChannelList();
        static ChannelList ListOChans => LocalChannels + OtherChannels;
        static Guid UniqueID = Guid.NewGuid();
        static Process Mplayer = new Process();
        public static string[] localServers = [];
        async static Task Main(string[] args)
        {
            Console.WriteLine("Searching for home server");
            
            state = ClientState.Loading;
            if (File.Exists("lS"))
            {
                localServers = File.ReadAllLines("lS");
                for (int i = 0; i < localServers.Length; i++)
                {
                    await ParseLocalServer(localServers[i]);
                }
            }
            else
            {
                File.Create("lS");
            }
            Task.Run(() => ListenForSsdpRequests());
            Task.Run(() => SendSsdpAnnouncements());
            Start();
            
            TcpListener listener = new TcpListener(IPAddress.Any, 7894);
            listener.Start();
            while(state != ClientState.ShuttingDown)
            {
                await GetInput(await listener.AcceptTcpClientAsync());
            }
        }

        static async Task GetInput(TcpClient client)
        {
            var stream = client.GetStream();
            byte[] buffer = new byte[client.ReceiveBufferSize];

            var sb = new StringBuilder();

            int bytesRead;
            do
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            } while (bytesRead == buffer.Length);

            var Req = sb.ToString();
            Console.WriteLine(Req);
            
        }

        static async Task ParseLocalServer(string localServer)
        {
            try
            {
                using HttpClient Hclient = new HttpClient();
                var response = await Hclient.GetAsync(localServer);
                string description = await response.Content.ReadAsStringAsync();
                var sr = new XmlSerializer(typeof(ChannelList));
                TextReader reader = new StringReader(description);
                ChannelList channelList = (ChannelList)sr.Deserialize(reader);
                LocalChannels += channelList;
                AddTolS(localServer);

            }
            catch (Exception ex)
            {
                File.WriteAllText("lS", File.ReadAllText("lS").Replace(localServer, string.Empty).Trim());
                Console.WriteLine($"Error fetching description: {ex.Message}");
            }

        }

        private static void AddTolS(string localServer)
        {
            for (int i = 0; i < localServers.Length; i++)
            {
                if (localServers[i] == localServer) return;
            }
            var LSLSLSL = File.ReadAllText("lS");
            LSLSLSL += $"\n{localServer}";
            LSLSLSL = LSLSLSL.Trim();
            File.WriteAllText("lS",LSLSLSL);
        }

        static async Task SendSsdpAnnouncements()
        {
            string searchMessage = $@"M-SEARCH * HTTP/1.1
HOST: 239.255.255.250:1900
MAN: ""ssdp:discover""
MX: 3
ST: urn:PseudoVision:device:MediaServer:1";

            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            UdpClient client = new UdpClient();
            byte[] buffer = Encoding.UTF8.GetBytes(searchMessage);

            while (true)
            {
                Console.WriteLine("ssdp message sent");
                client.Send(buffer, buffer.Length, endPoint);
                await Task.Delay(1000 * 3); // Send every 3 seconds
            }
        }

        static async Task ListenForSsdpRequests()
        {
            UdpClient client = new UdpClient();
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 1900);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.ExclusiveAddressUse = false;
            client.Client.Bind(localEndPoint);
            client.JoinMulticastGroup(IPAddress.Parse("239.255.255.250"));

            while (true)
            {
                UdpReceiveResult result = await client.ReceiveAsync();
                string request = Encoding.UTF8.GetString(result.Buffer);
                if (request.Contains("NOTIFY"))
                {
                    if (request.Contains("urn:PseudoVision:schemas-upnp-org:MediaServer:1"))
                    {
                        string location = string.Empty;
                        var req = request.Split("\n");
                        for (int i = 0; i < req.Length; i++)
                        {
                            if (req[i].Contains("CHANNELS:"))
                            {
                                location = req[i].Split(" ")[1].Trim();
                                break;
                            }
                        }
                        await ParseLocalServer(location);

                    }
                    if (request.Contains("urn:PseudoVision:schemas-upnp-org:Controller:1"))
                    {
                        XmlSerializer xmlSerializer = new XmlSerializer(typeof(ChannelList));
                        StringWriter sw = new StringWriter();
                        xmlSerializer.Serialize(sw, ListOChans);
                        byte[] responseData = Encoding.UTF8.GetBytes(sw.ToString());
                        await client.SendAsync(responseData, responseData.Length, result.RemoteEndPoint);
                    }
                }
            }
        }

        static async void Play(string ChannelName)
        {
            if(state == ClientState.Watching)
            {
                Mplayer.Kill();
            }
            state = ClientState.Watching;
            string command = $"{ChannelName} -fs";
            var escapedArgs = command.Replace("\"", "\\\"");

            Mplayer = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/mplayer",
                    Arguments = $"{escapedArgs}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            Mplayer.Start();
            //Mplayer.WaitForExit();

        }
        public static async void Start()
        {
            while(ListOChans.length < 1)
            {
                state = ClientState.Searching;
                await Task.Delay(1000);
            }
            state = ClientState.Watching;
            Play(ListOChans[0].Link);
        }
    }
}

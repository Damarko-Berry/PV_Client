﻿using PVLib;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using static System.Net.Mime.MediaTypeNames;

namespace PV_Client
{
    internal class Program
    {
        static ClientState state;
        static ChannelList LocalChannels = new ChannelList();
        static ChannelList OtherChannels = new ChannelList();
        static ChannelList ListOChans => LocalChannels + OtherChannels;
        static int CurrentChan = 0;
        static int port = 7896;
        static Process VLC = null;
        static VLController controller = null;
        static string UUID = Guid.NewGuid().ToString();
        static int reqount;
        public static string[] localServers = [];
        async static Task Main(string[] args)
        {
            Console.WriteLine("Searching for home server");
            //var oports = GetPorts();
            //port= oports[new Random().Next(oports.Length)];
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
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(ListenForSsdpRequests);
            Task.Run(SendSsdpAnnouncements);
            Start();
            #pragma warning restore CS4014
            ListenForController();

            
            await CheckVLC();
        }

        static int[] GetPorts()
        {
            List<int> openPorts = new List<int>(); 
            for (int port = 1; port <= 65535; port++)
            { 
                if (IsPortOpen(port))
                { openPorts.Add(port);
                } 
            }
            return openPorts.ToArray();
        }
        static bool IsPortOpen(int port)
        {
            bool isOpen = false;
            try 
            { 
                TcpConnectionInformation[] tcpConnections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections(); 
                foreach (var tcpConnection in tcpConnections) 
                { 
                    if (tcpConnection.LocalEndPoint.Port == port)
                    { 
                        isOpen = true; 
                        break; 
                    } 
                } 
            }
            catch (Exception)
            { 
            } 
            return isOpen; 
        }
        static async Task CheckVLC(){
            while (VLC == null)
            {

            }
            await Task.Delay(10*1000);
            controller = new();
            while (!VLC.HasExited & state == ClientState.Watching){
                await Task.Delay(1000*15);
            }
            state = ClientState.ShuttingDown;
            await Task.Delay(5000);
        }
        static async void ListenForController()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            while(state != ClientState.ShuttingDown)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClient(client));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accepting client: {ex.Message}");
                }
            }
        }
        static async Task HandleClient(TcpClient client)
        {
            var stream = client.GetStream();
            try
            {
                byte[] buffer = new byte[client.ReceiveBufferSize];
                var sb = new StringBuilder();

                while (client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        // Client disconnected
                        break;
                    }

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                    if (sb.ToString().StartsWith("GET /description.xml"))
                    {
                        XmlSerializer xmlSerializer = new XmlSerializer(typeof(ChannelList));
                        StringWriter sw = new StringWriter();
                        xmlSerializer.Serialize(sw, ListOChans);
                        var s = sw.ToString();
                        byte[] ResponseBody = Encoding.UTF8.GetBytes(s);
                        StreamWriter writer = new StreamWriter(stream) { AutoFlush = true };
                        writer.WriteLine("HTTP/1.1 200 OK");
                        writer.WriteLine($"Content-Type: application/xml");
                        writer.WriteLine($"Content-Length: {ResponseBody.Length}");
                        writer.WriteLine();
                        writer.Flush();
                        await stream.WriteAsync(ResponseBody, 0, ResponseBody.Length);
                        sb.Clear(); // Clear the StringBuilder for the next request
                    }
                    else
                    {
                        var Req = sb.ToString().Trim();
                        Console.WriteLine(Req);
                        if (controller != null)
                        {
                            try
                            {
                                controller.GetCommand(EnumTranslator<VLCCommand>.fromString(Req));
                            }
                            catch
                            {
                                if (Req == "NextChan")
                                {
                                    ChangeChannel(1);
                                }
                                else if (Req == "PrevChan")
                                {
                                    ChangeChannel(-1);
                                }
                            }
                        }
                        sb.Clear(); // Clear the StringBuilder for the next request
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            finally
            {
                client.Close();
            }
        }
        
        static void ChangeChannel(int mag)
        {
            if(ListOChans.Channels.Count <2) return;
            CurrentChan += mag;
            if (CurrentChan < 0) CurrentChan = ListOChans.length - 1;
            if (CurrentChan >= ListOChans.length) CurrentChan = 0;
            controller.ChangeMedia(ListOChans[CurrentChan].Link);
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

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                ChannelList channelList = (ChannelList)sr.Deserialize(reader);
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8604 // Possible null reference argument.
                LocalChannels += channelList;
#pragma warning restore CS8604 // Possible null reference argument.
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
            localServer = localServer.Trim();
            for (int i = 0; i < localServers.Length; i++)
            {
                if (localServers[i].Contains(localServer)) return;
            }
            var LSLSLSL = File.ReadAllText("lS");
            LSLSLSL += $"\n{localServer}";
            LSLSLSL = LSLSLSL.Trim();
            File.WriteAllText("lS",LSLSLSL);
            localServers = File.ReadAllLines("lS");
        }

        static async Task SendSsdpAnnouncements()
        {
            string serverSearch = $@"M-SEARCH * HTTP/1.1
HOST: 239.255.255.250:1900
MAN: ""ssdp:discover""
MX: 3
ST: {SSDPTemplates.ServerSchema}";
            string ClientNotify =   "NOTIFY * HTTP/1.1\r\n" +
                                    "HOST: 239.255.255.250:1900\r\n" +
                                    "CACHE-CONTROL: max-age=1800\r\n" +
                                    $"LOCATION: http://{GetIPAddress()}:{port}/description.xml\r\n" +
                                    $"NT: {SSDPTemplates.ClientSchema}\r\n" +
                                    "NTS: ssdp:alive\r\n" +
                                    "SERVER: Custom/1.0 UPnP/1.0 DLNADOC/1.50\r\n" +
                                    $"USN: uuid:{UUID}::{SSDPTemplates.ClientSchema}\r\n" +
                                    "\r\n";
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            UdpClient client = new UdpClient();
            byte[] Sbuffer = Encoding.UTF8.GetBytes(serverSearch);
            byte[] Cbuffer = Encoding.UTF8.GetBytes(ClientNotify);
            

            while (state != ClientState.ShuttingDown)
            {
                //Console.WriteLine("ssdp message sent");
                client.Send(Sbuffer, Sbuffer.Length, endPoint);
                await Task.Delay(1000 * 3); // Send every 3 seconds
                client.Send(Cbuffer, Cbuffer.Length, endPoint);
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

            while (state != ClientState.ShuttingDown)
            {
                UdpReceiveResult result = await client.ReceiveAsync();
                string request = Encoding.UTF8.GetString(result.Buffer);
                if (request.Contains("NOTIFY") | request.Contains($"HTTP/1.1 200 OK"))
                {
                    if (request.Contains(SSDPTemplates.ServerSchema))
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
                        if(!localServers.Contains(location))
                        await ParseLocalServer(location);
                    }

                }
                else if(request.Contains("M-SEARCH") & request.Contains($"ST: {SSDPTemplates.ClientSchema}"))
                {
                        string response = $"HTTP/1.1 200 OK\r\n" +
                                              "CACHE-CONTROL: max-age=1800\r\n" +
                                              $"DATE: {DateTime.UtcNow.ToString("r")}\r\n" +
                                              "EXT:\r\n" +
                                              $"LOCATION: http://{GetIPAddress()}:{port}/description.xml\r\n" +
                                              "SERVER: Custom/1.0 UPnP/1.0 DLNADOC/1.50\r\n" +
                                              $"ST: {SSDPTemplates.ControllerSchema}\r\n" +
                                              $"USN: uuid:{UUID}::{SSDPTemplates.ClientSchema}\r\n" +
                                              "\r\n";
                        var data = Encoding.UTF8.GetBytes(response);
                        await client.SendAsync(data, data.Length, result.RemoteEndPoint);
                }
            }
        }

        static IPAddress GetIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork & !ip.ToString().Contains("127"))
                {
                    return ip;
                }
            }
            throw new Exception("Local IP Address Not Found!");
        }
        static void Play(string ChannelName)
        {
            if (state == ClientState.Watching)
            {
                try
                {
                    VLC.Kill();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
            state = ClientState.Watching;
            string command = $"-f -L {ChannelName} --extraintf rc --rc-host=localhost:12345";
            var escapedArgs = command.Replace("\"", "\\\"");

            VLC = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/vlc",
                    Arguments = $"{escapedArgs}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            VLC.Start();
            VLC.BeginOutputReadLine();
            VLC.BeginErrorReadLine();
        }
        public static async void Start()
        {
            while(ListOChans.length < 1)
            {
                state = ClientState.Searching;
                await Task.Delay(1000);
            }
            Play(ListOChans[0].Link);
        }
    }
}

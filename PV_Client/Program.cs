using FFmpeg.AutoGen;
using PVLib;
using SDL2;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using static System.Net.Mime.MediaTypeNames;

namespace PV_Client
{
    internal class Program
    {
        static ClientState state = ClientState.Loading;
        static ChannelList LocalChannels = new ChannelList();
        static ChannelList OtherChannels = new ChannelList();
        static ChannelList ListOChans => LocalChannels + OtherChannels;
        static int CurrentChan = 0;
        static string CurrentlyPLaying => ListOChans[CurrentChan].Link;
        static int port = 7896;
        static bool paused = false;
        static Process VLC = null;
        public static SDLPlayer Player = null;
        static string UUID = Guid.NewGuid().ToString();

        
        public static string[] localServers = new string[] {};
        async static Task Main(string[] args)
        {
            //var oports = GetPorts();
            //port= oports[new Random().Next(oports.Length)];
            if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) < 0)
            {
                Console.WriteLine("SDL could not initialize! Error: " + SDL.SDL_GetError());
                return;
            }

            // Query the primary display (index 0)
            if (SDL.SDL_GetCurrentDisplayMode(0, out SDL.SDL_DisplayMode mode) != 0)
            {
                Console.WriteLine("SDL_GetCurrentDisplayMode failed: " + SDL.SDL_GetError());
            }
            else
            {
                Console.WriteLine($"Screen size: {mode.w}x{mode.h}");
            }
#if DEBUG
            Player = new SDLPlayer( ScreenMode.Small, @"C:\Users\marko\Videos\Young Justice (2010)\Season 1\Young Justice - S01E01 - Independence Day (1080p x265 EDGE2020).mkv");
#else
            
            Player = new SDLPlayer(mode.w, mode.h, @"C:\Users\marko\Videos\Young Justice (2010)\Season 1\Young Justice - S01E01 - Independence Day (1080p x265 EDGE2020).mkv");
#endif
            Console.WriteLine("Searching for home server");
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
            Player.Play();
            Task.Run(ListenForSsdpRequests);
            Task.Run(SendSsdpAnnouncements);
            Start();
#pragma warning restore CS4014
            ListenForController();

            while (Player.Open)
            {
                Thread.Sleep(1000);
            }
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
                    HttpRequest request = new HttpRequest(sb.ToString());
                    sb.Clear(); // Clear the StringBuilder for the next request
                    if (request.Method == HTTPMethod.GET)
                    {
                        StringWriter sw = new StringWriter();
                        if (request.Path.Contains("description.xml"))
                        {
                            XmlSerializer xmlSerializer = new XmlSerializer(typeof(ChannelList));
                            xmlSerializer.Serialize(sw, ListOChans);
                        }
                        else if(request.Path.Contains("Schedule"))
                        {
                            sw.WriteLine("<html><body><h1>PV Client</h1><p>This is a simple HTTP server running in a C# application.</p></body></html>");
                        }
                        var s = sw.ToString();
                        byte[] ResponseBody = Encoding.UTF8.GetBytes(s);
                        StreamWriter writer = new StreamWriter(stream) { AutoFlush = true };
                        writer.WriteLine("HTTP/1.1 200 OK");
                        writer.WriteLine($"Content-Type: application/xml");
                        writer.WriteLine($"Content-Length: {ResponseBody.Length}");
                        writer.WriteLine();
                        writer.Flush();
                        await stream.WriteAsync(ResponseBody, 0, ResponseBody.Length);
                        
                    }
                    else if (request.Method == HTTPMethod.POST)
                    {
                        if(request.Path.Contains("PLay"))
                        {
                            paused = !paused;
                        }
                        else if(request.Path.Contains("ChangeChannel"))
                        {
                            if (request.Body.Contains("Up"))
                            {
                                ChangeChannel(1);
                            }
                            else if (request.Body.Contains("Down"))
                            {
                                ChangeChannel(-1);
                            }
                            Player.SetSource(ListOChans[CurrentChan].Link);
                        }

                        StreamWriter sw = new StreamWriter(stream) { AutoFlush = true };
                        sw.WriteLine("HTTP/1.1 200 OK");
                        sw.WriteLine($"Content-Type: text/plain");
                        sw.WriteLine($"Content-Length: 2");
                        sw.WriteLine();
                        sw.WriteLine("OK");
                        var s = sw.ToString();
                        byte[] ResponseBody = Encoding.UTF8.GetBytes(s);
                        sw.Flush();
                        await stream.WriteAsync(ResponseBody, 0, ResponseBody.Length);
                        
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
            Player.SetSource(CurrentlyPLaying);
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
        
        public static async void Start()
        {
            while(ListOChans.length < 1)
            {
                state = ClientState.Searching;
                await Task.Delay(1000);
            }
            Player.SetSource(ListOChans[0].Link);
        }

    }

    
}

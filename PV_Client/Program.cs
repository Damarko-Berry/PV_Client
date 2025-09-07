using FFmpeg.AutoGen;
using PVLib;
using SDL2;
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
        static ClientState state;
        static ChannelList LocalChannels = new ChannelList();
        static ChannelList OtherChannels = new ChannelList();
        static ChannelList ListOChans => LocalChannels + OtherChannels;
        static int CurrentChan = 0;
        static int port = 7896;
        static bool paused = false;
        static Process VLC = null;
        static VLController controller = null;
        static string UUID = Guid.NewGuid().ToString();
        
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
            RenderSLD();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(ListenForSsdpRequests);
            Task.Run(SendSsdpAnnouncements);
            Start();
            #pragma warning restore CS4014
            ListenForController();

            await CheckVLC();
        }
        static unsafe void RenderSLD()
        {
            string url = @"C:\Users\marko\Videos\2025-04-12 02-12-52.mkv";

            ffmpeg.RootPath = @"FFmpeg"; // where your ffmpeg .dlls are

            AVFormatContext* pFormatContext = ffmpeg.avformat_alloc_context();
            if (ffmpeg.avformat_open_input(&pFormatContext, url, null, null) != 0)
                throw new ApplicationException("Could not open file");

            ffmpeg.avformat_find_stream_info(pFormatContext, null);

            int videoStreamIndex = ffmpeg.av_find_best_stream(pFormatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (videoStreamIndex < 0) throw new ApplicationException("No video stream");

            AVStream* pStream = pFormatContext->streams[videoStreamIndex];
            AVCodecParameters* codecpar = pStream->codecpar;
            AVCodec* pCodec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
            AVCodecContext* pCodecCtx = ffmpeg.avcodec_alloc_context3(pCodec);
            ffmpeg.avcodec_parameters_to_context(pCodecCtx, codecpar);
            ffmpeg.avcodec_open2(pCodecCtx, pCodec, null);

            // --- FPS Extraction ---
            double fps = 25.0; // Default fallback
            if (pStream->avg_frame_rate.den != 0)
                fps = ffmpeg.av_q2d(pStream->avg_frame_rate);
            int frameDelayMs = (int)(1000.0 / fps);

            // --- SDL Init ---
            SDL2.SDL.SDL_Init(SDL2.SDL.SDL_INIT_VIDEO);
            IntPtr window = SDL2.SDL.SDL_CreateWindow("FFmpeg + SDL2",
                SDL2.SDL.SDL_WINDOWPOS_CENTERED, SDL2.SDL.SDL_WINDOWPOS_CENTERED,
                pCodecCtx->width, pCodecCtx->height, SDL2.SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN);
            IntPtr renderer = SDL2.SDL.SDL_CreateRenderer(window, -1, 0);
            IntPtr texture = SDL2.SDL.SDL_CreateTexture(renderer,
                SDL2.SDL.SDL_PIXELFORMAT_IYUV,
                (int)SDL2.SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING,
                pCodecCtx->width, pCodecCtx->height);

            AVPacket* packet = ffmpeg.av_packet_alloc();
            AVFrame* frame = ffmpeg.av_frame_alloc();

            SDL2.SDL.SDL_Event e;
            bool running = true;

            var stopwatch = new Stopwatch();

            while (running && ffmpeg.av_read_frame(pFormatContext, packet) >= 0)
            {
                if (packet->stream_index == videoStreamIndex)
                {
                    ffmpeg.avcodec_send_packet(pCodecCtx, packet);

                    while (ffmpeg.avcodec_receive_frame(pCodecCtx, frame) == 0)
                    {
                        stopwatch.Restart();

                        SDL2Native.SDL_UpdateYUVTexture(
                            texture,
                            IntPtr.Zero,
                            (IntPtr)frame->data[0], frame->linesize[0],
                            (IntPtr)frame->data[1], frame->linesize[1],
                            (IntPtr)frame->data[2], frame->linesize[2]
                        );

                        SDL.SDL_RenderClear(renderer);
                        SDL.SDL_RenderCopy(renderer, texture, IntPtr.Zero, IntPtr.Zero);
                        SDL.SDL_RenderPresent(renderer);

                        // Wait for the correct frame duration
                        int elapsed = (int)stopwatch.ElapsedMilliseconds;
                        int delay = frameDelayMs - elapsed;
                        if (delay > 0)
                            Thread.Sleep(delay);
                    }
                }

                ffmpeg.av_packet_unref(packet);

                while (SDL2.SDL.SDL_PollEvent(out e) == 1)
                {
                    if (e.type == SDL2.SDL.SDL_EventType.SDL_QUIT)
                        running = false;
                }
                if (paused)
                {
                    Thread.Sleep(10); // Idle a bit
                    continue;
                }


            }

            // Cleanup
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avcodec_free_context(&pCodecCtx);
            ffmpeg.avformat_close_input(&pFormatContext);

            SDL2.SDL.SDL_DestroyTexture(texture);
            SDL2.SDL.SDL_DestroyRenderer(renderer);
            SDL2.SDL.SDL_DestroyWindow(window);
            SDL2.SDL.SDL_Quit();

            void Seek(AVFormatContext* fmtCtx, AVCodecContext* codecCtx, int streamIndex, long offsetSeconds)
            {
                long timestamp = (long)(offsetSeconds * ffmpeg.AV_TIME_BASE);
                ffmpeg.av_seek_frame(fmtCtx, -1, timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD);

                ffmpeg.avcodec_flush_buffers(codecCtx);
            }
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

    public static class SDL2Native
    {
        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_UpdateYUVTexture(
            IntPtr texture,
            IntPtr rect,
            IntPtr yPlane, int yPitch,
            IntPtr uPlane, int uPitch,
            IntPtr vPlane, int vPitch
        );
    }
}

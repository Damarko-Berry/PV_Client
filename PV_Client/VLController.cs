using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace PV_Client
{
    internal class VLController
    {
        int Volume = 256;
        bool playing = true;
        bool mute = false;
        TcpClient tcpClient = new TcpClient();
        StreamWriter writer;
        public VLController()
        {
            try
            {

                tcpClient = new TcpClient(IPAddress.IPv6Loopback.ToString(), 12345);
                writer = new StreamWriter(tcpClient.GetStream());
                writer.AutoFlush = true;
                Volume = 256 / 2;
                writer.WriteLine($"volume {Volume}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.WriteLine("Failure while connecting to VLC");
            }
        }
        public void GetCommand(VLCCommand cmd)
        {
            try
            {
                switch (cmd)
                {
                    case VLCCommand.Play:
                        Play();
                        break;
                    case VLCCommand.VolumeDown:
                        Volume = Volume - 26;
                        if (Volume < 0)
                        {
                            Volume = 0;
                        }
                        else
                        {
                            writer.WriteLine($"volume {Volume}");
                        }
                        break;
                    case VLCCommand.VolumeUp:
                        Volume = Volume + 26;
                        if (Volume > 512)
                        {
                            Volume = 512;
                        }
                        else
                        {

                            writer.WriteLine($"volume {Volume}");
                        }
                        break;
                    case VLCCommand.Status:
                        writer.WriteLine("status");
                        break;
                    case VLCCommand.Quit:
                        writer.WriteLine("quit");
                        break;
                    case VLCCommand.Mute:
                        Mute();
                        break;
                }
            }
            catch
            {
                Console.WriteLine("OOOOOPPPPPS");
            }
        }
        public void ChangeMedia(string mediaUrl)
        {
            writer.WriteLine("stop");
            writer.WriteLine("clear");
            writer.WriteLine($"add {mediaUrl}");
            writer.WriteLine("play");
            playing = true;
        }
        void Play()
        {
            playing =! playing;
            if (playing)
            {
                writer.WriteLine("play");
            }
            else
            {
                writer.WriteLine("pause");
            }
        }
        void Mute()
        {
            mute = !mute;
            if (mute)
            {
                writer.WriteLine("volume 0");
            }
            else
            {
                writer.WriteLine($"volume {Volume}");
            }
        }
    }
}

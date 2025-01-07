using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace PV_Client
{
    internal class VLController
    {
        int Volume = 256;
        bool playing = true;

        TcpClient tcpClient = new TcpClient();
        StreamWriter writer;
        
        public VLController()
        {
            tcpClient = new TcpClient("localhost", 12345);
            writer = new StreamWriter(tcpClient.GetStream());
            writer.AutoFlush= true;
            writer.WriteLine($"volume {Volume}");
        }

        public void GetCommand(VLCCommand cmd)
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
                    Volume = Volume - 26;
                    if (Volume>512)
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
    }
}

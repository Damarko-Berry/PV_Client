using FFmpeg.AutoGen;
using SDL2;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static SDL2.SDL_ttf;

namespace PV_Client
{
    internal unsafe class SDLPlayer : IDisposable
    {
        IntPtr window, renderer, texture;
        int Width, Height;
        string Source;
        bool paused = false;
        bool running = false;
        AVFormatContext* fmtCtx;
        AVCodecContext* videoCodecCtx;
        public bool Open { get; private set; } = false;
        private uint audioDevice;
        private IntPtr font;

        public SDLPlayer(int w, int h, string url = @"https://www.w3schools.com/html/mov_bbb.mp4")
        {
            Width = w;
            Height = h;
            this.Source = url;

            SDL2.SDL.SDL_Init(SDL2.SDL.SDL_INIT_VIDEO | SDL2.SDL.SDL_INIT_AUDIO);

            window = SDL2.SDL.SDL_CreateWindow("FFmpeg + SDL2",
                SDL2.SDL.SDL_WINDOWPOS_CENTERED, SDL2.SDL.SDL_WINDOWPOS_CENTERED,
                Width, Height,
                SDL2.SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN| SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE);

            renderer = SDL2.SDL.SDL_CreateRenderer(window, -1, 0);
            int TFF = TTF_Init();
            // Initialize SDL_ttf
            if (TTF_Init() == -1)
                throw new ApplicationException("Could not initialize SDL_ttf");

            // Load font
            font = TTF_OpenFont("arial.ttf", 62); // Make sure arial.ttf is present in your working directory
            if (font == IntPtr.Zero)
                throw new ApplicationException("Failed to load font: ");


            Open = window != IntPtr.Zero && renderer != IntPtr.Zero;
        }
        
        public unsafe async Task Play()
        {
            ffmpeg.RootPath = @"FFmpeg";
        Top:
            // Open format context
            fmtCtx = ffmpeg.avformat_alloc_context();
            AVFormatContext* pFmtCtx = fmtCtx;
            if (ffmpeg.avformat_open_input(&pFmtCtx, Source, null, null) != 0)
                throw new ApplicationException("Could not open file");
            ffmpeg.avformat_find_stream_info(fmtCtx, null);

            // Find video stream
            int videoStreamIndex = ffmpeg.av_find_best_stream(fmtCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (videoStreamIndex < 0) throw new ApplicationException("No video stream");
            AVStream* pStream = fmtCtx->streams[videoStreamIndex];
            AVCodecParameters* codecpar = pStream->codecpar;
            AVCodec* pCodec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
            videoCodecCtx = ffmpeg.avcodec_alloc_context3(pCodec);
            ffmpeg.avcodec_parameters_to_context(videoCodecCtx, codecpar);
            ffmpeg.avcodec_open2(videoCodecCtx, pCodec, null);

            // Find audio stream
            int audioStreamIndex = ffmpeg.av_find_best_stream(fmtCtx, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            if (audioStreamIndex < 0) throw new ApplicationException("No audio stream");
            AVStream* aStream = fmtCtx->streams[audioStreamIndex];
            AVCodecParameters* aCodecpar = aStream->codecpar;
            AVCodec* aCodec = ffmpeg.avcodec_find_decoder(aCodecpar->codec_id);
            AVCodecContext* audioCodecCtx = ffmpeg.avcodec_alloc_context3(aCodec);
            ffmpeg.avcodec_parameters_to_context(audioCodecCtx, aCodecpar);
            ffmpeg.avcodec_open2(audioCodecCtx, aCodec, null);

            // Find subtitle stream
            int subtitleStreamIndex = ffmpeg.av_find_best_stream(fmtCtx, AVMediaType.AVMEDIA_TYPE_SUBTITLE, -1, -1, null, 0);
            AVCodecContext* subtitleCodecCtx = null;
            if (subtitleStreamIndex >= 0)
            {
                AVStream* sStream = fmtCtx->streams[subtitleStreamIndex];
                AVCodecParameters* sCodecpar = sStream->codecpar;
                AVCodec* sCodec = ffmpeg.avcodec_find_decoder(sCodecpar->codec_id);
                subtitleCodecCtx = ffmpeg.avcodec_alloc_context3(sCodec);
                ffmpeg.avcodec_parameters_to_context(subtitleCodecCtx, sCodecpar);
                ffmpeg.avcodec_open2(subtitleCodecCtx, sCodec, null);
            }

            // Create texture for video frames
            texture = SDL2.SDL.SDL_CreateTexture(renderer,
                SDL2.SDL.SDL_PIXELFORMAT_IYUV,
                (int)SDL2.SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING,
                videoCodecCtx->width, videoCodecCtx->height);

            // SDL Audio Setup
            SDL2.SDL.SDL_AudioSpec wantedSpec = new SDL2.SDL.SDL_AudioSpec
            {
                freq = audioCodecCtx->sample_rate,
                format = SDL2.SDL.AUDIO_S16SYS,
                channels = (byte)audioCodecCtx->ch_layout.nb_channels,
                samples = 4096,
                callback = null,
                userdata = IntPtr.Zero
            };
            audioDevice = SDL2.SDL.SDL_OpenAudioDevice(null, 0, ref wantedSpec, out _, 0);
            SDL2.SDL.SDL_PauseAudioDevice(audioDevice, 0);

            // SwrContext for audio conversion
            AVChannelLayout outLayout = new AVChannelLayout();
            ffmpeg.av_channel_layout_default(&outLayout, audioCodecCtx->ch_layout.nb_channels);

            SwrContext* swr = ffmpeg.swr_alloc();
            ffmpeg.swr_alloc_set_opts2(
                &swr,
                &outLayout,
                AVSampleFormat.AV_SAMPLE_FMT_S16,
                audioCodecCtx->sample_rate,
                &audioCodecCtx->ch_layout,
                (AVSampleFormat)audioCodecCtx->sample_fmt,
                audioCodecCtx->sample_rate,
                0, null);
            ffmpeg.swr_init(swr);

            // SwsContext for video conversion
            SwsContext* swsCtx = ffmpeg.sws_getContext(
                videoCodecCtx->width, videoCodecCtx->height, videoCodecCtx->pix_fmt,
                videoCodecCtx->width, videoCodecCtx->height, AVPixelFormat.AV_PIX_FMT_YUV420P,
                ffmpeg.SWS_BILINEAR, null, null, null);

            AVFrame* yuvFrame = ffmpeg.av_frame_alloc();
            yuvFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            yuvFrame->width = videoCodecCtx->width;
            yuvFrame->height = videoCodecCtx->height;
            ffmpeg.av_frame_get_buffer(yuvFrame, 32);

            AVPacket* packet = ffmpeg.av_packet_alloc();
            AVFrame* frame = ffmpeg.av_frame_alloc();
            AVFrame* audioFrame = ffmpeg.av_frame_alloc();

            SDL2.SDL.SDL_Event e;
            running = true;
            Open = true;

            SDL2.SDL.SDL_Rect destRect = new SDL2.SDL.SDL_Rect
            {
                x = 0,
                y = 0,
                w = Width,
                h = Height
            };

            var playbackStart = DateTime.UtcNow;
            double firstFrameTime = -1;
            string currentSubtitle = "";
            double subtitleStart = 0, subtitleEnd = 0;

            var url = Source;
            while (running && ffmpeg.av_read_frame(fmtCtx, packet) >= 0 && url == Source)
            {
                if (packet->stream_index == videoStreamIndex)
                {
                    ffmpeg.avcodec_send_packet(videoCodecCtx, packet);
                    while (ffmpeg.avcodec_receive_frame(videoCodecCtx, frame) == 0)
                    {
                        double frameTime = frame->pts * ffmpeg.av_q2d(pStream->time_base);
                        if (firstFrameTime < 0)
                            firstFrameTime = frameTime;

                        double elapsed = (DateTime.UtcNow - playbackStart).TotalSeconds;
                        double target = frameTime - firstFrameTime;
                        double wait = target - elapsed;
                        if (wait > 0)
                            Thread.Sleep((int)(wait * 1000));

                        ffmpeg.sws_scale(
                            swsCtx,
                            frame->data, frame->linesize, 0, videoCodecCtx->height,
                            yuvFrame->data, yuvFrame->linesize);

                        SDL2Native.SDL_UpdateYUVTexture(
                            texture,
                            IntPtr.Zero,
                            (IntPtr)yuvFrame->data[0], yuvFrame->linesize[0],
                            (IntPtr)yuvFrame->data[1], yuvFrame->linesize[1],
                            (IntPtr)yuvFrame->data[2], yuvFrame->linesize[2]
                        );

                        SDL.SDL_RenderClear(renderer);
                        SDL.SDL_RenderCopy(renderer, texture, IntPtr.Zero, ref destRect);
                        IntPtr textSurface = TTF_RenderUTF8_Blended(font, "Test Subtitle", new SDL2.SDL.SDL_Color { r = 255, g = 255, b = 255, a = 255 });
                        // Render subtitle if active
                        if (!string.IsNullOrEmpty(currentSubtitle) && frameTime >= subtitleStart && frameTime <= subtitleEnd)
                        {
                            Console.WriteLine("Subtitle: " + currentSubtitle);
                            SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 128);
                            SDL2.SDL.SDL_Rect subRect = new SDL2.SDL.SDL_Rect
                            {
                                x = 0,
                                y = Height - 60,
                                w = Width,
                                h = 60
                            };
                            SDL.SDL_RenderFillRect(renderer, ref subRect);

                            SDL2.SDL.SDL_Color white = new SDL2.SDL.SDL_Color { r = 255, g = 255, b = 255, a = 255 };
                            textSurface = TTF_RenderUTF8_Blended(font, currentSubtitle, white);
                            if (textSurface == IntPtr.Zero)
                            {
                                Console.WriteLine("TTF_RenderUTF8_Blended failed: " + SDL2.SDL.SDL_GetError());
                            }
                            else
                            {
                                IntPtr textTexture = SDL2.SDL.SDL_CreateTextureFromSurface(renderer, textSurface);
                                if (textTexture == IntPtr.Zero)
                                {
                                    Console.WriteLine("SDL_CreateTextureFromSurface failed: " + SDL2.SDL.SDL_GetError());
                                }
                                else
                                {
                                    SDL2.SDL.SDL_Rect textRect = new SDL2.SDL.SDL_Rect
                                    {
                                        x = 10,
                                        y = Height - 50,
                                        w = Width - 20,
                                        h = 40
                                    };
                                    SDL2.SDL.SDL_RenderCopy(renderer, textTexture, IntPtr.Zero, ref textRect);
                                    SDL2.SDL.SDL_DestroyTexture(textTexture);
                                }
                                SDL2.SDL.SDL_FreeSurface(textSurface);
                            }
                        }
                        
                        SDL.SDL_RenderPresent(renderer);
                    }
                }
                else if (packet->stream_index == audioStreamIndex)
                {
                    ffmpeg.avcodec_send_packet(audioCodecCtx, packet);
                    while (ffmpeg.avcodec_receive_frame(audioCodecCtx, audioFrame) == 0)
                    {
                        int outSamples = (int)ffmpeg.av_rescale_rnd(
                            ffmpeg.swr_get_delay(swr, audioCodecCtx->sample_rate) + audioFrame->nb_samples,
                            audioCodecCtx->sample_rate, audioCodecCtx->sample_rate, AVRounding.AV_ROUND_UP);

                        int bufferSize = ffmpeg.av_samples_get_buffer_size(
                            null, (byte)audioCodecCtx->ch_layout.nb_channels, outSamples, AVSampleFormat.AV_SAMPLE_FMT_S16, 1);

                        byte[] outBuffer = new byte[bufferSize];
                        fixed (byte* outBufPtr = outBuffer)
                        {
                            byte** outPtrs = stackalloc byte*[1];
                            outPtrs[0] = outBufPtr;
                            ffmpeg.swr_convert(swr, outPtrs, outSamples, audioFrame->extended_data, audioFrame->nb_samples);
                        }

                        GCHandle handle = GCHandle.Alloc(outBuffer, GCHandleType.Pinned);
                        int result = SDL2.SDL.SDL_QueueAudio(audioDevice, handle.AddrOfPinnedObject(), (uint)outBuffer.Length);
                        if (result < 0)
                            Console.WriteLine("SDL_QueueAudio error: " + SDL2.SDL.SDL_GetError());
                        handle.Free();
                    }
                }
                else if (subtitleStreamIndex >= 0 && packet->stream_index == subtitleStreamIndex)
                {
                    if (subtitleCodecCtx == null || packet == null)
                    {
                        ffmpeg.av_packet_unref(packet);
                        continue;
                    }

                    AVSubtitle subtitle = new AVSubtitle();
                    int gotSubtitle = 0;
                    int ret = ffmpeg.avcodec_decode_subtitle2(subtitleCodecCtx, &subtitle, &gotSubtitle, packet);
                    if (ret >= 0 && gotSubtitle != 0)
                    {
                        for (int i = 0; i < subtitle.num_rects; i++)
                        {
                            AVSubtitleRect* rect = subtitle.rects[i];
                            if (rect->type == 0 && rect->text != null) // SUBTITLE_TEXT
                            {
                                string subText = Marshal.PtrToStringAnsi((IntPtr)rect->text);
                                currentSubtitle = subText;
                                subtitleStart = subtitle.pts * ffmpeg.av_q2d(fmtCtx->streams[subtitleStreamIndex]->time_base);
                                subtitleEnd = subtitleStart + subtitle.end_display_time / 1000.0;
                            }
                            else if (rect->type ==  AVSubtitleType.SUBTITLE_BITMAP) // SUBTITLE_BITMAP
                            {
                                
                                int w = rect->w;
                                int h = rect->h;

                                // Create SDL surface from FFmpeg bitmap data
                                IntPtr surface = SDL2.SDL.SDL_CreateRGBSurfaceWithFormatFrom(
                                    (IntPtr)rect->data[0], w, h, 32, rect->linesize[0], SDL2.SDL.SDL_PIXELFORMAT_ABGR8888
);

                                if (surface != IntPtr.Zero)
                                {
                                    IntPtr texture = SDL2.SDL.SDL_CreateTextureFromSurface(renderer, surface);
                                    SDL2.SDL.SDL_FreeSurface(surface);

                                    if (texture != IntPtr.Zero)
                                    {
                                        SDL2.SDL.SDL_Rect dstRect = new SDL2.SDL.SDL_Rect
                                        {
                                            x = rect->x,
                                            y = rect->y,
                                            w = w,
                                            h = h
                                        };
                                        SDL2.SDL.SDL_RenderCopy(renderer, texture, IntPtr.Zero, ref dstRect);
                                        SDL2.SDL.SDL_DestroyTexture(texture);
                                    }
                                }
                            }

                            Console.WriteLine($"Subtitle rect type: {rect->type}, text: {Marshal.PtrToStringAnsi((IntPtr)rect->text)}");
                        }
                        ffmpeg.avsubtitle_free(&subtitle);
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
                    Thread.Sleep(10);
                    continue;
                }
            }

            // Cleanup
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_frame_free(&yuvFrame);
            ffmpeg.av_frame_free(&audioFrame);
            ffmpeg.av_packet_free(&packet);
            AVCodecContext* videoCtx = videoCodecCtx;
            AVCodecContext* audioCtx = audioCodecCtx;
            AVFormatContext* fmt = fmtCtx;
            ffmpeg.avcodec_free_context(&videoCtx);
            ffmpeg.avcodec_free_context(&audioCtx);
            ffmpeg.avformat_close_input(&fmt);
            ffmpeg.swr_free(&swr);
            ffmpeg.sws_freeContext(swsCtx);
            if (subtitleCodecCtx != null)
                ffmpeg.avcodec_free_context(&subtitleCodecCtx);

            SDL2.SDL.SDL_CloseAudioDevice(audioDevice);
            SDL2.SDL.SDL_DestroyTexture(texture);
            if(Source != string.Empty)
            {
                goto Top;
            }
            Open = false;

        }

        public void Pause() => paused = true;
        public void Resume() => paused = false;
        public void Stop() => running = false;

        public void SetSource(string url)
        {
            if (url != Source)
            {
                Source = url;
            }
        }

        public void Dispose()
        {
            SDL2.SDL.SDL_DestroyTexture(texture);
            SDL2.SDL.SDL_DestroyRenderer(renderer);
            SDL2.SDL.SDL_DestroyWindow(window);
            SDL2.SDL.SDL_Quit();
            TTF_CloseFont(font);
            TTF_Quit();
        }
    }

    public static class SDL2Native
    {
#if LINUX
        [DllImport("libSDL2.so", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_UpdateYUVTexture(
            IntPtr texture,
            IntPtr rect,
            IntPtr yPlane, int yPitch,
            IntPtr uPlane, int uPitch,
            IntPtr vPlane, int vPitch
        );
#else

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_UpdateYUVTexture(
            IntPtr texture,
            IntPtr rect,
            IntPtr yPlane, int yPitch,
            IntPtr uPlane, int uPitch,
            IntPtr vPlane, int vPitch
        );
#endif
    }
}

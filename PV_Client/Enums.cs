namespace PV_Client
{
    enum ClientState
    {
        Loading,
        Watching,
        Searching,
        ShuttingDown
    }

    enum VLCCommand
    {
        Play,
        VolumeDown,
        VolumeUp,
        Status,
        Mute,
        Quit,
    }

    public enum HTTPMethod
    {
        GET,
        POST,
        PUT,
        DELETE,
        HEAD,
        OPTIONS,
        PATCH,
        TRACE,
        CONNECT
    }

    public enum ScreenMode
    {
        Fullscreen,
        Small
    }

    public static class Enums<T>
    {
        public static T ParseEnum(string value)
        {
            return (T)Enum.Parse(typeof(T), value, true);
        }
    }
}
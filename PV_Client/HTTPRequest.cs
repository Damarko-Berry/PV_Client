using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PV_Client
{
    public class HttpRequest
    {
        public HTTPMethod Method { get; private set; }
        public string Path { get; private set; }
        public string HttpVersion { get; private set; }
        public Dictionary<string, string> Headers { get; private set; }
        public string Body { get; private set; }

        public HttpRequest(string rawRequest)
        {
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Parse(rawRequest);
        }

        private void Parse(string rawRequest)
        {
            // Split head and body
            var requestParts = rawRequest.Split(new[] { "\r\n\r\n" }, 2, StringSplitOptions.None);
            string head = requestParts[0];
            if (requestParts.Length > 1)
                Body = requestParts[1];
            else
                Body = string.Empty;

            // Split request line + headers
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);

            // Parse request line
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length != 3)
                throw new FormatException("Invalid HTTP request line");

            Method = Enums<HTTPMethod>.ParseEnum(requestLine[0]);
            Path = requestLine[1];
            HttpVersion = requestLine[2];

            // Parse headers
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                int colonIndex = lines[i].IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = lines[i].Substring(0, colonIndex).Trim();
                    string value = lines[i].Substring(colonIndex + 1).Trim();
                    Headers[key] = value;
                }
            }
        }

        public override string ToString()
        {
            return $"{Method} {Path} {HttpVersion} " +
                   $"(Headers: {Headers.Count}, Body length: {Body.Length})";
        }
    }
}

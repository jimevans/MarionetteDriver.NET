// -----------------------------------------------------------------------
// <copyright file="MarionetteConnection.cs" company="">
// TODO: Update copyright text.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Globalization;
using Newtonsoft.Json;
using OpenQA.Selenium.Remote;

namespace Marionette
{
    /// <summary>
    /// TODO: Update summary.
    /// </summary>
    public class MarionetteConnection
    {
        private Socket socket;

        public MarionetteConnection()
        {
            this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(IPAddress.Parse("127.0.0.1"), 2828);
        }

        public string ReceiveResponse()
        {
            byte[] buffer = new byte[1024];
            int bytesReceived = socket.Receive(buffer, 1024, SocketFlags.None);
            StringBuilder builder = new StringBuilder(Encoding.UTF8.GetString(buffer, 0, bytesReceived));
            while (bytesReceived >= 1024)
            {
                buffer = new byte[1024];
                bytesReceived = socket.Receive(buffer, 1024, SocketFlags.None);
                builder.Append(Encoding.UTF8.GetString(buffer, 0, bytesReceived));
            }

            string[] responseParts = builder.ToString().Split(new char[] { ':' }, 2);
            int length = int.Parse(responseParts[0], CultureInfo.InvariantCulture);
            return responseParts[1].Substring(0, length);
        }

        public void SendCommand(string command)
        {
            string formattedCommand = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", command.Length, command);
            this.socket.Send(Encoding.UTF8.GetBytes(formattedCommand));
        }

        public void Dispose()
        {
            this.socket.Close();
        }

        public Dictionary<string, object> DeserializeResponse(string rawResponse)
        {
            Dictionary<string, object> deserializedResponse = JsonConvert.DeserializeObject(rawResponse, typeof(IDictionary<string, object>), new ResponseValueJsonConverter()) as Dictionary<string, object>;
            return deserializedResponse;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using OpenQA.Selenium.Remote;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Globalization;
using Newtonsoft.Json;

namespace OpenQA.Selenium.Firefox
{
    class MarionetteCommandExecutor : ICommandExecutor
    {
        private FirefoxBinary binary;
        private FirefoxProfile profile;

        private List<IPEndPoint> addresses = new List<IPEndPoint>();
        private string host = "localhost";
        private Socket socket;
        private string marionetteId = string.Empty;
        private Dictionary<string, string> commandNameMap = new Dictionary<string, string>();

        public MarionetteCommandExecutor(FirefoxBinary binary, FirefoxProfile profile)
        {
            this.binary = binary;
            this.profile = profile;
            this.PopulateCommandNameMap();
        }

        public Response Execute(Command commandToExecute)
        {
            if (commandToExecute.Name == DriverCommand.NewSession)
            {
                // Marionette sends back an initial acknowledgement response upon first
                // connect. We need to read that response before we can proceed. The
                // Right Thing(TM) to do is do this in the initial handshake, but I've
                // not gotten round to that yet.
                string initialResponse = this.ReceiveResponse();

                // This initializes the "actor" for future communication with this instance.
                string getIdCommand = JsonConvert.SerializeObject(this.ConvertCommand("getMarionetteID", null, null));
                this.SendCommand(getIdCommand);
                string getMarionetteIdRawResponse = this.ReceiveResponse();

                Dictionary<string, object> getMarionetteIdResponse = DeserializeResponse(getMarionetteIdRawResponse);
                this.marionetteId = getMarionetteIdResponse["id"].ToString();
            }

            string command = this.SerializeCommand(commandToExecute);
            this.SendCommand(command);
            string rawResponse = this.ReceiveResponse();
            Dictionary<string, object> responseDictionary = DeserializeResponse(rawResponse);
            Response response = null;
            if (commandToExecute.Name == DriverCommand.NewSession)
            {
                // If we're starting a new session, we need to return the response
                // with that session.
                // ***************************************************************
                // Marionette Compliance Issue: The response should return the 
                // newly created session ID in the "sessionId" member of the
                // returned JSON object.
                // ***************************************************************
                response = new Response(new SessionId(responseDictionary["value"].ToString()));
                response.Value = new Dictionary<string, object>();
            }
            else
            {
                if (responseDictionary.ContainsKey("error"))
                {
                    // ***************************************************************
                    // Marionette Compliance Issue: Error responses should, at a
                    // minimum, put the status property at the root of the object.
                    // In other words:
                    // {
                    //   status: 7,
                    //   value:
                    //   {
                    //     message: "Did not find element with id=foo",
                    //     stackTrace: <stack trace goes here>
                    //   }
                    // }
                    // ***************************************************************
                    response = new Response();
                    Dictionary<string, object> errorDictionary = responseDictionary["error"] as Dictionary<string, object>;
                    if (errorDictionary != null)
                    {
                        int status = Convert.ToInt32(errorDictionary["status"]);
                        response.Status = (WebDriverResult)status;
                        errorDictionary.Remove("status");
                        response.Value = errorDictionary;
                    }
                }
                else
                {
                    // ***************************************************************
                    // Marionette Compliance Issue: Responses from findElement and
                    // findElements are returned with raw element IDs as the value.
                    // This should be a JSON object with the following structure:
                    //
                    //   { "ELEMENT": "<element ID goes here>" }
                    //
                    // This is to allow the client bindings to distinguish between
                    // a raw string and an element reference returned from the
                    // executeScript command.
                    // ***************************************************************
                    response = Response.FromJson(rawResponse);
                    if (commandToExecute.Name == DriverCommand.FindElement || commandToExecute.Name == DriverCommand.FindChildElement)
                    {
                        if (response.Status == WebDriverResult.Success)
                        {
                            string elementId = response.Value.ToString();
                            Dictionary<string, object> wrapped = new Dictionary<string, object>();
                            wrapped["ELEMENT"] = elementId;
                            response.Value = wrapped;
                        }
                    }

                    if (commandToExecute.Name == DriverCommand.FindElements || commandToExecute.Name == DriverCommand.FindChildElements)
                    {
                        if (response.Status == WebDriverResult.Success)
                        {
                            List<object> wrappedElements = new List<object>();
                            object[] idArray = response.Value as object[];
                            foreach (object id in idArray)
                            {
                                Dictionary<string, object> wrappedElement = new Dictionary<string, object>();
                                wrappedElement["ELEMENT"] = id.ToString();
                                wrappedElements.Add(wrappedElement);
                            }

                            response.Value = wrappedElements.ToArray();
                        }
                    }
                }
            }

            return response;
        }

        public void Start()
        {
            // Launches Firefox twice, once to clean the profile, and
            // a second time to actually work with.
            this.profile.WriteToDisk();
            this.binary.Clean(this.profile);
            this.binary.StartProfile(this.profile, new string[] { "-foreground" });
            this.SetAddress(2828);
            this.ConnectToBrowser(45000);
        }

        public void Quit()
        {
            this.socket.Close();
            this.socket = null;

            // The .Quit() call here actually kills the process. I worry
            // about graceful shutdowns when we resort to this.
            this.binary.Quit();
        }

        private void PopulateCommandNameMap()
        {
            // Marionette's command names don't exactly coincide with what
            // the spec says they ought to be. Create a map to get the name
            // Marionette expects.
            // ***************************************************************
            // Marionette Compliance Issue: The commands should standardize
            // on the names used by the W3C standard.
            // ***************************************************************
            this.commandNameMap[DriverCommand.Get] = "goUrl";
            this.commandNameMap[DriverCommand.ImplicitlyWait] = "setSearchTimeout";
            this.commandNameMap[DriverCommand.SetAsyncScriptTimeout] = "setScriptTimeout";
            this.commandNameMap[DriverCommand.GetCurrentWindowHandle] = "getWindow";
            this.commandNameMap[DriverCommand.GetWindowHandles] = "getWindows";
            this.commandNameMap[DriverCommand.Close] = "closeWindow";
            this.commandNameMap[DriverCommand.GetCurrentUrl] = "getUrl";
            this.commandNameMap[DriverCommand.FindChildElement] = "findElement";
            this.commandNameMap[DriverCommand.FindChildElements] = "findElements";
            this.commandNameMap[DriverCommand.GetElementLocation] = "getElementPosition";
            this.commandNameMap[DriverCommand.Quit] = "deleteSession";
        }

        private string SerializeCommand(Command commandToExecute)
        {
            string commandName = commandToExecute.Name;
            if (this.commandNameMap.ContainsKey(commandToExecute.Name))
            {
                commandName = commandNameMap[commandToExecute.Name];
            }

            // This is pretty stupid. The names of the commands in the .NET bindings
            // are static readonly, but you still can't use them in a switch statement
            // because they're not const. This will change.
            // We also have to rename the parameters for some of the commands, since
            // Marionette doesn't use the same parameter names that other RemoteWebDriver
            // clients expect.
            // ***************************************************************
            // Marionette Compliance Issue: The commands should standardize
            // on the parameter names used by the W3C standard.
            // ***************************************************************
            switch (commandToExecute.Name)
            {
                case "newSession":
                    commandToExecute.Parameters.Remove("desiredCapabilities");
                    break;

                case "get":
                    RenameParameter(commandToExecute, "url", "value");
                    break;

                case "implicitlyWait":
                    RenameParameter(commandToExecute, "ms", "value");
                    break;

                case "setAsyncScriptTimeout":
                    RenameParameter(commandToExecute, "ms", "value");
                    break;

                case "executeScript":
                case "executeAsyncScript":
                    RenameParameter(commandToExecute, "script", "value");
                    break;

                case "switchToWindow":
                    RenameParameter(commandToExecute, "name", "value");
                    break;

                case "switchToFrame":
                    Dictionary<string, object> param = commandToExecute.Parameters["id"] as Dictionary<string, object>;
                    if (param != null)
                    {
                        commandToExecute.Parameters["element"] = param["ELEMENT"];
                    }
                    else
                    {
                        RenameParameter(commandToExecute, "id", "frame");
                    }

                    break;

                case "findChildElement":
                case "findChildElements":
                case "clickElement":
                case "clearElement":
                case "getElementAttribute":
                case "getElementText":
                case "sendKeysToElement":
                case "isElementSelected":
                case "isElementEnabled":
                case "isElementDisplayed":
                case "getElementSize":
                case "getElementLocation":
                case "getElementTagName":
                    RenameParameter(commandToExecute, "id", "element");
                    break;
            }

            // Convert the Command object into a flat dictionary, because Marionette
            // doesn't have its command's parameters as an object, but as properties
            // of the flat JSON object sent over the wire.
            // ***************************************************************
            // Marionette Compliance Issue: The properties should be sent
            // as an object, not flattened as properties within the JSON.
            // ***************************************************************
            Dictionary<string, object> commandDictionary = ConvertCommand(commandName, commandToExecute.SessionId, commandToExecute.Parameters);
            string serializedCommand = JsonConvert.SerializeObject(commandDictionary);
            return serializedCommand;
        }

        private Dictionary<string, object> ConvertCommand(string commandName, SessionId sessionId, Dictionary<string, object> parameters)
        {
            Dictionary<string, object> commandDictionary = new Dictionary<string, object>();
            commandDictionary["to"] = "root";
            if (!string.IsNullOrEmpty(this.marionetteId))
            {
                commandDictionary["to"] = this.marionetteId;
            }

            commandDictionary["type"] = commandName;
            if (sessionId != null)
            {
                commandDictionary["session"] = sessionId.ToString();
            }

            if (parameters != null)
            {
                foreach (string paramName in parameters.Keys)
                {
                    commandDictionary[paramName] = parameters[paramName];
                }
            }
            return commandDictionary;
        }

        private static void RenameParameter(Command commandToExecute, string originalParameterName, string newParameterName)
        {
            commandToExecute.Parameters[newParameterName] = commandToExecute.Parameters[originalParameterName];
            commandToExecute.Parameters.Remove(originalParameterName);
        }

        private static Dictionary<string, object> DeserializeResponse(string rawResponse)
        {
            Dictionary<string, object> deserializedResponse = JsonConvert.DeserializeObject(rawResponse, typeof(IDictionary<string, object>), new ResponseValueJsonConverter()) as Dictionary<string, object>;
            return deserializedResponse;
        }

        private string ReceiveResponse()
        {
            // Receive the response in 1KB chunks.
            byte[] buffer = new byte[1024];
            int bytesReceived = this.socket.Receive(buffer, 1024, SocketFlags.None);
            StringBuilder builder = new StringBuilder(Encoding.UTF8.GetString(buffer, 0, bytesReceived));
            while (bytesReceived >= 1024)
            {
                buffer = new byte[1024];
                bytesReceived = this.socket.Receive(buffer, 1024, SocketFlags.None);
                builder.Append(Encoding.UTF8.GetString(buffer, 0, bytesReceived));
            }

            // Split the length off of the response, and return the raw JSON.
            string[] responseParts = builder.ToString().Split(new char[] { ':' }, 2);
            int length = int.Parse(responseParts[0], CultureInfo.InvariantCulture);
            return responseParts[1].Substring(0, length);
        }

        private void SendCommand(string command)
        {
            // Send the command in the proper format, prefacing it with the length.
            string formattedCommand = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", command.Length, command);
            this.socket.Send(Encoding.UTF8.GetBytes(formattedCommand));
        }

        private static List<IPEndPoint> ObtainLoopbackAddresses(int port)
        {
            List<IPEndPoint> endpoints = new List<IPEndPoint>();

            // Obtain a reference to all network interfaces in the machine
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in adapters)
            {
                IPInterfaceProperties properties = adapter.GetIPProperties();
                foreach (IPAddressInformation uniCast in properties.UnicastAddresses)
                {
                    if (IPAddress.IsLoopback(uniCast.Address))
                    {
                        endpoints.Add(new IPEndPoint(uniCast.Address, port));
                    }
                }
            }

            return endpoints;
        }

        private bool IsSocketConnected
        {
            get { return this.socket != null && this.socket.Connected; }
        }

        private void SetAddress(int port)
        {
            if (string.Compare("localhost", this.host, StringComparison.OrdinalIgnoreCase) == 0)
            {
                this.addresses = ObtainLoopbackAddresses(port);
            }
            else
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(this.host);

                // Use the first IPv4 address that we find
                IPAddress endPointAddress = IPAddress.Parse("127.0.0.1");
                foreach (IPAddress ip in hostEntry.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        endPointAddress = ip;
                        break;
                    }
                }

                IPEndPoint hostEndPoint = new IPEndPoint(endPointAddress, port);
                this.addresses.Add(hostEndPoint);
            }
        }

        private void ConnectToBrowser(long timeToWaitInMilliSeconds)
        {
            // Attempt to connect to the browser extension on a Socket.
            // A successful connection means the browser is running and
            // the extension has been properly initialized.
            DateTime waitUntil = DateTime.Now.AddMilliseconds(timeToWaitInMilliSeconds);
            while (!this.IsSocketConnected && waitUntil > DateTime.Now)
            {
                foreach (IPEndPoint addr in this.addresses)
                {
                    try
                    {
                        this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        this.socket.Connect(addr);
                        break;
                    }
                    catch (SocketException)
                    {
                        System.Threading.Thread.Sleep(250);
                    }
                }
            }

            // If the socket was either not created or not connected successfully,
            // throw an exception.
            if (!this.IsSocketConnected)
            {
                if (this.socket == null || this.socket.RemoteEndPoint == null)
                {
                    throw new WebDriverException(string.Format(CultureInfo.InvariantCulture, "Failed to start up socket within {0}", timeToWaitInMilliSeconds));
                }
                else
                {
                    IPEndPoint endPoint = (IPEndPoint)this.socket.RemoteEndPoint;
                    string formattedError = string.Format(CultureInfo.InvariantCulture, "Unable to connect to host {0} on port {1} after {2} ms", endPoint.Address.ToString(), endPoint.Port.ToString(CultureInfo.InvariantCulture), timeToWaitInMilliSeconds);
                    throw new WebDriverException(formattedError);
                }
            }
        }
    }
}

using LogicModule.Nodes.Helpers;
using LogicModule.ObjectModel;
using LogicModule.ObjectModel.TypeSystem;
using System;
using System.Net;
using System.Net.Security;
using System.Collections.Specialized;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Linq;
using System.Web;
using System.Diagnostics;
using Newtonsoft.Json;

namespace teclab_at.logic.collection {
    public class TelegramMsg : LogicNodeBase {
        public TelegramMsg(INodeContext context) : base(context) {
            // check
            context.ThrowIfNull("context");

            // Initialize the input ports
            ITypeService typeService = context.GetService<ITypeService>();
            this.Trigger = typeService.CreateBool(PortTypes.Bool, "Trigger");
            this.ChatId = typeService.CreateString(PortTypes.String, "Chat ID");
            this.BotToken = typeService.CreateString(PortTypes.String, "Bot Token");
            this.Message = typeService.CreateString(PortTypes.String, "Message", "Message from X1");
            this.MessageNl = typeService.CreateString(PortTypes.String, "Message (Newline)", "");
            this.SendRetries = typeService.CreateInt(PortTypes.Integer, "Nr. Retries", 10);
            this.DoLog = typeService.CreateBool(PortTypes.Bool, "Enable Logging", false);
            // Internals
            this.LogicError = typeService.CreateBool(PortTypes.Bool, "Logic Error");
        }

        // Class internals
        private Mutex mutex = new Mutex();
        public static String logFile = "/var/log/teclab.at.telegrammsg.log";
        public static String webUrl = "https://api.telegram.org/bot{0}/sendmessage";

        [Input(DisplayOrder = 1, IsInput = true, IsRequired = true)]
        public BoolValueObject Trigger { get; private set; }

        [Parameter(DisplayOrder = 2, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject ChatId { get; private set; }

        [Parameter(DisplayOrder = 3, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject BotToken { get; private set; }

        [Input(DisplayOrder = 4, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject Message { get; private set; }

        [Input(DisplayOrder = 5, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MessageNl { get; private set; }

        [Input(DisplayOrder = 6, InitOrder = 1, IsDefaultShown = false)]
        public IntValueObject SendRetries { get; private set; }

        [Input(DisplayOrder = 5, IsInput = true, IsRequired = false)]
        public BoolValueObject DoLog { get; private set; }

        [Output(DisplayOrder = 20)]
        public BoolValueObject LogicError { get; private set; }

        private string ToKeyValuePairs(NameValueCollection nvc) {
            var array = (from key in nvc.AllKeys from value in nvc.GetValues(key) select string.Format("{0}={1}", key, value)).ToArray();
            return string.Join("&", array);
        }

        public void Log(String message, Boolean force = false) {
            if ((Environment.OSVersion.Platform != PlatformID.Unix) || (!this.DoLog.Value && !force)) return;
            this.mutex.WaitOne();
            File.AppendAllText(TelegramMsg.logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + ": " + message + Environment.NewLine);
            this.mutex.ReleaseMutex();
        }

        public void SignalLogicError(Boolean state = true) {
            this.mutex.WaitOne();
            if (!this.LogicError.HasValue || (this.LogicError.Value != state)) { this.LogicError.Value = state; }
            this.mutex.ReleaseMutex();
        }

        public override void Startup() {
            // call base
            base.Startup();
            ServicePointManager.ServerCertificateValidationCallback =
                delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) { return true; };
            if (Environment.OSVersion.Platform == PlatformID.Unix) {
                try { File.Delete(logFile); } catch (Exception) { }
                this.Log("Telegram Message logic page started");
            }
        }

        public override void Execute() {
            // React only on Trigger input
            if (!this.Trigger.Value || !this.Trigger.WasSet) return;
            if (this.Message.Value == String.Empty || this.BotToken.Value == String.Empty || this.ChatId.Value == String.Empty) return;

            // Schedule as async task ...
            this.SignalLogicError(false);
            if (Environment.OSVersion.Platform == PlatformID.Unix) {
                Task task = new Task(() => SendMessage_curl(this, this.Message.Value, this.MessageNl.Value, this.SendRetries.Value));
                task.Start();
            } else {
                Task task = new Task(() => SendMessage_net(this, this.Message.Value, this.MessageNl.Value, this.SendRetries.Value));
                task.Start();
            }
        }

        private class MsgResponse {
            public Boolean ok { get; set; } = false;
            public Int16 error_code { get; set; } = 0;
            public String description { get; set; } = String.Empty;
        }

        static void SendMessage_curl(TelegramMsg parent, String message, String messageNl, int sendRetries) {
            // Add a second line of message if given
            if (messageNl != String.Empty) {message += Environment.NewLine + messageNl;}

            int tryNr = 1;
            while (sendRetries >= 0) {
                try {
                    parent.Log("SendMessage (try " + tryNr.ToString() + ") ...");
                    parent.SendMessage_curlworker(message);
                    parent.Log("SendMessage (try " + tryNr.ToString() + ") success");
                    break;
                } catch (Exception ex) {
                    parent.SignalLogicError();
                    parent.Log("SendMessage (try " + tryNr.ToString() + ") failed: " + ex.ToString(), true);
                }
                // Sleep for 50 seconds. Note that the curl timeout is set to 10 seconds which gives us a one minute cycle
                Thread.Sleep(10000);
                sendRetries -= 1;
                tryNr += 1;
            }
        }

        void SendMessage_curlworker(String message) {
            // Create the payload
            var payload = new NameValueCollection();
            payload.Add("chat_id", this.ChatId.Value);
            payload.Add("text", HttpUtility.UrlEncode(message, Encoding.UTF8));

            // Build the complete URL
            String msgUrl = String.Format(TelegramMsg.webUrl, this.BotToken.Value) + "?" + this.ToKeyValuePairs(payload);
            this.Log(msgUrl);

            // create and define the process
            Process curlCmd = new Process();
            curlCmd.StartInfo.FileName = "/usr/bin/curl";
            curlCmd.StartInfo.Arguments = string.Format("-m 10 \"{0}\" ", msgUrl);
            curlCmd.StartInfo.RedirectStandardOutput = true;
            curlCmd.StartInfo.RedirectStandardError = true;
            curlCmd.StartInfo.CreateNoWindow = true;
            curlCmd.StartInfo.UseShellExecute = false;

            // Execute and wait until it exits
            curlCmd.Start();
            if (!curlCmd.WaitForExit(15000)) {
                curlCmd.Kill();
                throw new System.Exception("Process did not exit in time");
            } else if (curlCmd.ExitCode != 0) {
                String data = curlCmd.StandardError.ReadToEnd();
                throw new System.Exception("Process error: " + data);
            } else {
                String data = curlCmd.StandardOutput.ReadToEnd();
                if (data == String.Empty) {throw new System.Exception("Process returned empty result");}
                this.Log(data);
                // Parse the JSON string into the response class
                MsgResponse response = JsonConvert.DeserializeObject<MsgResponse>(data);
                // Check
                if (response.ok == false) {
                    throw new System.Exception(data);
                }
            }
        }

        static void SendMessage_net(TelegramMsg parent, String message, String messageNl, int sendRetries) {
            // Add a second line of message if given
            if (messageNl != String.Empty) {message += Environment.NewLine + messageNl;}

            // Create the payload
            var payload = new NameValueCollection();
            payload.Add("chat_id", parent.ChatId.Value);
            payload.Add("text", HttpUtility.UrlEncode(message, Encoding.UTF8));

            // Build the complete URL
            String msgUrl = String.Format(TelegramMsg.webUrl, parent.BotToken.Value) + "?" + parent.ToKeyValuePairs(payload);
            parent.Log(msgUrl);

            // Create the HTTP request using the GET method and the required headers
            HttpWebRequest httpRequest = (HttpWebRequest)HttpWebRequest.Create(msgUrl);
            httpRequest.Method = "GET";
            httpRequest.AuthenticationLevel = AuthenticationLevel.None;

            Boolean sendSuccess = false;
            int tryNr = 1;
            HttpWebResponse httpResponse = null;
            while (sendRetries > 1) {
                try {
                    parent.Log("SendMessage (try " + tryNr.ToString() + ") ...");
                    httpResponse = (HttpWebResponse)httpRequest.GetResponse();
                    sendSuccess = true;
                    break;
                } catch (WebException ex) {
                    parent.Log("SendMessage (try " + tryNr.ToString() + ") failed: " + ex.ToString(), true);
                }
                Thread.Sleep(60000);
                sendRetries -= 1;
                tryNr += 1;
            }

            // Prepare for the response
            try {
                if (!sendSuccess) {
                    parent.Log("SendMessage (try " + tryNr.ToString() + ") ...");
                    ServicePointManager.ServerCertificateValidationCallback =
                        delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) { return true; };
                    parent.Log("A");
                    httpResponse = (HttpWebResponse)httpRequest.GetResponse();
                    parent.Log("B");
                }
                Stream streamIn = httpResponse.GetResponseStream();
                var streamReader = new StreamReader(streamIn, Encoding.UTF8);
                String data = streamReader.ReadToEnd();
                parent.Log(data);
                // Parse the JSON string into the response class
                MsgResponse response = JsonConvert.DeserializeObject<MsgResponse>(data);
                // Check
                if (response.ok == false) {
                    parent.SignalLogicError();
                    parent.Log("SendMessage (try " + tryNr.ToString() + ") failed", true);
                } else {
                    parent.Log("SendMessage (try " + tryNr.ToString() + ") success");
                }
                // Cleanup
                streamReader.Close();
                streamIn.Close();
                httpResponse.Close();
            } catch (WebException ex) {
                parent.SignalLogicError();
                parent.Log("SendMessage (try " + tryNr.ToString() + ") failed: " + ex.ToString(), true);
                try {
                    httpResponse = (HttpWebResponse)ex.Response;
                    Stream streamIn = httpResponse.GetResponseStream();
                    var streamReader = new StreamReader(streamIn, Encoding.UTF8);
                    String data = streamReader.ReadToEnd();
                    parent.Log(data);
                    // Cleanup
                    streamReader.Close();
                    streamIn.Close();
                    httpResponse.Close();
                } catch (Exception exd) {
                    parent.Log("SendMessage (try " + tryNr.ToString() + ") failed fatal: " + exd.ToString(), true);
                }
            }
        }
    }
}

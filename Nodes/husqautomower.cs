using LogicModule.Nodes.Helpers;
using LogicModule.ObjectModel;
using LogicModule.ObjectModel.TypeSystem;
using System;
using System.Web;
using System.Net;
using System.Net.Security;
using System.Collections.Specialized;
using System.Security.Cryptography.X509Certificates;
//using System.Collections.Generic;
//using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using System.Linq;
//using Newtonsoft.Json.Linq;
//using System.Runtime.CompilerServices;
//using System.ComponentModel;
//using System.Text.Json;

namespace teclab_at.logic {
    public class HusqAutomower : LogicNodeBase {
        public HusqAutomower(INodeContext context) : base(context) {
            // check
            context.ThrowIfNull("context");

            // Because of multiple parallel access to resources we need mutex
            this.mutex = new Mutex();

            // Initialize the input ports
            ITypeService typeService = context.GetService<ITypeService>();
            this.Trigger = typeService.CreateBool(PortTypes.Bool, "Trigger");
            this.AppId = typeService.CreateString(PortTypes.String, "App ID");
            this.AuthUser = typeService.CreateString(PortTypes.String, "Connect User");
            this.AuthPassword = typeService.CreateString(PortTypes.String, "Connect Password");
            this.DoLog = typeService.CreateBool(PortTypes.Bool, "Enable Logging", false);
            // Initialize the output ports
            this.ErrorMessage = typeService.CreateString(PortTypes.String, "Status Message");
            this.ErrorSignal = typeService.CreateBool(PortTypes.Bool, "Error Status");
        }

        // Class internals
        private Mutex mutex = new Mutex();
        private AuthCache authCache = null;
        private const String logFile = "/var/log/teclab.at.husqautomower.log";

        [Input(DisplayOrder = 1, IsInput = true, IsRequired = true)]
        public BoolValueObject Trigger { get; private set; }

        [Parameter(DisplayOrder = 9, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject AppId { get; private set; }

        [Parameter(DisplayOrder = 9, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject AuthUser { get; private set; }

        [Parameter(DisplayOrder = 10, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject AuthPassword { get; private set; }

        [Input(DisplayOrder = 12, IsInput = true, IsRequired = false)]
        public BoolValueObject DoLog { get; private set; }

        [Output(DisplayOrder = 1)]
        public StringValueObject ErrorMessage { get; private set; }
        
        [Output(DisplayOrder = 2)]
        public BoolValueObject ErrorSignal { get; private set; }

        public void Log(String message) {
            if ((Environment.OSVersion.Platform != PlatformID.Unix) || !this.DoLog.Value) return;
            mutex.WaitOne();
            File.AppendAllText(logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + ": " + message + Environment.NewLine);
            mutex.ReleaseMutex();
        }

        public override void Startup() {
            // call base
            base.Startup();
            ServicePointManager.ServerCertificateValidationCallback =
                delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) { return true; };
            if ((Environment.OSVersion.Platform == PlatformID.Unix) && this.DoLog.Value) {
                try {File.Delete(logFile);} catch (Exception) {}
                this.Log("Husqvarner Automower logic page started");
            }
        }

        public override void Execute() {
            // React only on Trigger input
            if (!this.Trigger.Value || !this.Trigger.WasSet) return;

            // Schedule as async task ...
            try {
                Task task = new Task(() => Authorize(this));
                task.Start();
            } catch (Exception ex) {
                this.Log("Authorisation failed: " + ex.Message);
                this.ReportError(ex.Message);
            } finally {
            }
        }

        public void ReportError(String message) {
            mutex.WaitOne();
            this.ErrorMessage.Value = message;
            this.ErrorSignal.Value = true;
            mutex.ReleaseMutex();
        }
        
        public void ReportSuccess() {
            mutex.WaitOne();
            this.ErrorMessage.Value = "";
            this.ErrorSignal.Value = false;
            mutex.ReleaseMutex();
        }

        private class AuthCache {
            public String access_token { get; set; } = null;
            public String refresh_token { get; set; } = null;
            public String provider { get; set; }
            public String user_id { get; set; }
            public String token_type { get; set; }
            public String scope { get; set; }
            public int expires_in { get; set; } = 0;
        }

        public void StoreAuth(String jsonStr) {
            mutex.WaitOne();
            if (this.authCache != null) {
                this.authCache.refresh_token = null;
                this.authCache.access_token = null;
            }
            if (jsonStr != null) {
                this.Log(jsonStr);
                this.authCache = JsonConvert.DeserializeObject<AuthCache>(jsonStr);
            }
            mutex.ReleaseMutex();
        }

        private string ToKeyValuePairs(NameValueCollection nvc) {
            var array = (from key in nvc.AllKeys from value in nvc.GetValues(key) select string.Format("{0}={1}", key, value)).ToArray();
            return string.Join("&", array);
        }

        private Boolean RequestData(String url, NameValueCollection payload) {
            // Initialize the web-request
            HttpWebRequest httpRequest = (HttpWebRequest)WebRequest.Create(url);
            httpRequest.Method = "POST";
            httpRequest.ContentType = "application/x-www-form-urlencoded; charset=utf-8";

            // Create the http output stream and write our payload
            var streamOut = new StreamWriter(httpRequest.GetRequestStream(), Encoding.UTF8);
            streamOut.Write(this.ToKeyValuePairs(payload));
            streamOut.Close();
            streamOut.Dispose();

            // Prepare the response
            try {
                HttpWebResponse httpResponse = (HttpWebResponse)httpRequest.GetResponse();
                Stream streamIn = httpResponse.GetResponseStream();
                var streamReader = new StreamReader(streamIn, Encoding.UTF8);
                String data = streamReader.ReadToEnd();
                this.Log(data);
                this.StoreAuth(data);
                // Cleanup
                streamReader.Close();
                streamReader.Dispose();
                streamIn.Close();
                streamIn.Dispose();
                httpResponse.Close();
                return true;
            } catch (WebException ex) {
                HttpWebResponse httpResponse = (HttpWebResponse)ex.Response;
                Stream streamIn = httpResponse.GetResponseStream();
                var streamReader = new StreamReader(streamIn, Encoding.UTF8);
                String data = streamReader.ReadToEnd();
                this.Log("Authentication failure: " + data);
                this.StoreAuth(null);
                // Cleanup
                streamReader.Close();
                streamReader.Dispose();
                streamIn.Close();
                streamIn.Dispose();
                httpResponse.Close();
                return false;
            }
        }

        // Note that the 'async' keyword ist not working on the X1 - the Task is simply not scheduled - unknown why ...
        static Boolean Authorize(HusqAutomower parent) {
            // host 99.86.243.3 || host 99.86.243.107 || host 99.86.243.65
            const String authUrl = "https://api.authentication.husqvarnagroup.dev/v1/oauth2/token";
            var payload = new NameValueCollection();

            /* The first authorisation is the password grant.
             * If this fails, there must be something wrong in the app ID, user or password.
             * Nothing else we can do ... */
            if ((parent.authCache == null) || (parent.authCache.access_token == null) || (parent.authCache.refresh_token == null)) {
                payload.Add("grant_type", "password");
                payload.Add("client_id", parent.AppId.Value);
                payload.Add("username", parent.AuthUser.Value);
                payload.Add("password", parent.AuthPassword.Value);
                if (parent.RequestData(authUrl, payload)) { return true; } else { return false; }
            }

            /* The second authorisation trys through the refresh token.
             * Though the refresh token might be invalid or timed out.
             * In that case we need to use password authentication again. */
            if (parent.authCache.refresh_token != null) {
                payload.Add("grant_type", "refresh_token");
                payload.Add("client_id", parent.AppId.Value);
                payload.Add("refresh_token", parent.authCache.refresh_token);
                if (parent.RequestData(authUrl, payload)) { return true; }
            }

            /* There was a refresh token but it got invalid for whatever reason.
             * Last resort is to try full password authentication.
             * If this fails, nothing else we can do ... */
            payload.Add("grant_type", "password");
            payload.Add("client_id", parent.AppId.Value);
            payload.Add("username", parent.AuthUser.Value);
            payload.Add("password", parent.AuthPassword.Value);
            if (parent.RequestData(authUrl, payload)) { return true; } else { return false; }
        }

        /*static void Authorize_FW45(HusqAutomower parent, Dictionary<string, string> credentials) {
            const String authUrl = "https://api.authentication.husqvarnagroup.dev/v1/oauth2/token";
            parent.Log("A");

            var authRequest = new Dictionary<string, string>(credentials);
            authRequest.Add("grant_type", "password");

            // Create http client and content
            var reqContent = new FormUrlEncodedContent(authRequest);
            HttpClientHandler handler = new HttpClientHandler();
            var client = new HttpClient(handler);
            parent.Log("B");
            // Send content and read response (which is JSON)
            Task<HttpResponseMessage> httpTask = client.PostAsync(authUrl, reqContent);
            parent.Log("httpTask status: " + httpTask.Status.ToString());
            httpTask.Wait(10000);
            parent.Log("httpTask status: " + httpTask.Status.ToString());
            //HttpResponseMessage response = client.PostAsync(authUrl, reqContent);
            HttpResponseMessage response = httpTask.Result;
            parent.Log("response status: " + response.StatusCode.ToString());

            Task<String> contentTask = response.Content.ReadAsStringAsync();
            parent.Log("contentTask status: " + contentTask.Status.ToString());
            contentTask.Wait(10000);
            parent.Log("contentTask status: " + contentTask.Status.ToString());
            //parent.Store(response.Content.ReadAsStringAsync(), true);
            parent.Store(contentTask.Result, true);

            // kept for reference
            // var jsonObj = JObject.Parse(jsonString);
            // parent.ReportError(jsonObj["access_token"].Value<string>());
        }*/
    }
}

using LogicModule.Nodes.Helpers;
using LogicModule.ObjectModel;
using LogicModule.ObjectModel.TypeSystem;
using System;
using System.Net;
using System.Net.Security;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace teclab_at.logic.collection {
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
            this.MowerName = typeService.CreateString(PortTypes.String, "Mower Name");
            this.DoLog = typeService.CreateBool(PortTypes.Bool, "Enable Logging", false);
            // Initialize the output ports
            this.BatteryCapacity = typeService.CreateByte(PortTypes.Byte, "Battery Capacity");
            this.ActivityMowing = typeService.CreateBool(PortTypes.Bool, "Activity Mowing");
            this.ActivityGoingHome = typeService.CreateBool(PortTypes.Bool, "Activity Going Home");
            this.ActivityCharging = typeService.CreateBool(PortTypes.Bool, "Activity Charging");
            this.ActivityLeavingCS = typeService.CreateBool(PortTypes.Bool, "Activity Leaving CS");
            this.ActivityParkingCS = typeService.CreateBool(PortTypes.Bool, "Activity Parking CS");
            this.ActivityStopped = typeService.CreateBool(PortTypes.Bool, "Activity Stopped");
            this.StateOperational = typeService.CreateBool(PortTypes.Bool, "State Operational");
            this.StatePaused = typeService.CreateBool(PortTypes.Bool, "State Paused");
            this.StateRestricted = typeService.CreateBool(PortTypes.Bool, "State Restricted");
            this.StateError = typeService.CreateBool(PortTypes.Bool, "State Error");
            // Internals
            this.LogicError = typeService.CreateBool(PortTypes.Bool, "Logic Error");
        }

        // Class internals
        private Task statusTask = null;
        private Mutex mutex = new Mutex();
        private AuthCache authCache = null;
        private String mowerId = "";
        public static String logFile = "/var/log/teclab.at.husqautomower.log";
        public static String authUrl = "https://api.authentication.husqvarnagroup.dev/v1/oauth2/token"; // host 99.86.243.3 || host 99.86.243.107 || host 99.86.243.65
        public static String mowersUrl = "https://api.amc.husqvarna.dev/v1/mowers";

        [Input(DisplayOrder = 1, IsInput = true, IsRequired = true)]
        public BoolValueObject Trigger { get; private set; }

        [Parameter(DisplayOrder = 2, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject AppId { get; private set; }

        [Parameter(DisplayOrder = 3, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject AuthUser { get; private set; }

        [Parameter(DisplayOrder = 4, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject AuthPassword { get; private set; }

        [Parameter(DisplayOrder = 5, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject MowerName { get; private set; }

        [Input(DisplayOrder = 6, IsInput = true, IsRequired = false)]
        public BoolValueObject DoLog { get; private set; }

        [Output(DisplayOrder = 1, IsRequired = false)]
        public ByteValueObject BatteryCapacity { get; private set; }

        [Output(DisplayOrder = 2, IsRequired = false)]
        public BoolValueObject ActivityMowing { get; private set; }

        [Output(DisplayOrder = 3)]
        public BoolValueObject ActivityGoingHome { get; private set; }

        [Output(DisplayOrder = 4)]
        public BoolValueObject ActivityCharging { get; private set; }

        [Output(DisplayOrder = 5)]
        public BoolValueObject ActivityLeavingCS { get; private set; }

        [Output(DisplayOrder = 6)]
        public BoolValueObject ActivityParkingCS { get; private set; }

        [Output(DisplayOrder = 7)]
        public BoolValueObject ActivityStopped { get; private set; }

        [Output(DisplayOrder = 8)]
        public BoolValueObject StateOperational { get; private set; }

        [Output(DisplayOrder = 9)]
        public BoolValueObject StatePaused { get; private set; }

        [Output(DisplayOrder = 10)]
        public BoolValueObject StateRestricted { get; private set; }

        [Output(DisplayOrder = 11)]
        public BoolValueObject StateError { get; private set; }

        [Output(DisplayOrder = 20)]
        public BoolValueObject LogicError { get; private set; }

        public void Log(String message, Boolean force = false) {
            if ((Environment.OSVersion.Platform != PlatformID.Unix) || (!this.DoLog.Value && !force)) return;
            mutex.WaitOne();
            File.AppendAllText(HusqAutomower.logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + ": " + message + Environment.NewLine);
            mutex.ReleaseMutex();
        }

        public void SignalLogicError(Boolean state = true) {
            mutex.WaitOne();
            if (this.LogicError.Value != state) { this.LogicError.Value = state; }
            mutex.ReleaseMutex();
        }

        public override void Startup() {
            // call base
            base.Startup();
            ServicePointManager.ServerCertificateValidationCallback =
                delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) { return true; };
            if (Environment.OSVersion.Platform == PlatformID.Unix) {
                try {File.Delete(logFile);} catch (Exception) {}
                this.Log("Husqvarner Automower logic page started");
            }
            this.ActivityMowing.Value = false;
            this.ActivityGoingHome.Value = false;
            this.ActivityCharging.Value = false;
            this.ActivityLeavingCS.Value = false;
            this.ActivityParkingCS.Value = false;
            this.ActivityStopped.Value = false;
            this.StateOperational.Value = false;
            this.StatePaused.Value = false;
            this.StateRestricted.Value = false;
            this.StateError.Value = false;
            this.LogicError.Value = false;
        }

        public override void Execute() {
            // React only on Trigger input
            if (!this.Trigger.Value || !this.Trigger.WasSet) return;

            // Schedule as async task ...
            if ((statusTask == null) || (statusTask.Status != TaskStatus.Running)) {
                this.SignalLogicError(false);
                statusTask = Task.Run(() => UpdateStatus(this));
            }            
        }

        private class AuthCache {
            public String access_token { get; set; } = null;
            public String refresh_token { get; set; } = null;
            public String provider { get; set; }
            public String user_id { get; set; }
            public String token_type { get; set; }
            public String scope { get; set; }
            public int expires_in { get; set; } = 0;
            public DateTime time { get; set; }
        }

        static void UpdateStatus(HusqAutomower parent) {
            // It might be the first call or other reasons for not having an access token
            // Anyhow, start to authorize
            if (!parent.AuthorizeCheck()) {
                parent.Log("Failed authorization", true);
                parent.SignalLogicError();
                return;
            }

            // Sanity check
            // Check availability of the access token
            parent.mutex.WaitOne();
            if ((parent.authCache.access_token == null) || (parent.authCache.access_token == "")) {
                parent.mutex.ReleaseMutex();
                parent.Log("Invalid access token", true);
                parent.SignalLogicError();
                return;
            }

            // Create the request message that lists us all mowers with their ID
            var reqMessage = new HttpRequestMessage(HttpMethod.Get, HusqAutomower.mowersUrl);
            reqMessage.Headers.Add("authorization-provider", parent.authCache.provider);
            reqMessage.Headers.Add("x-api-key", parent.AppId.Value);
            reqMessage.Headers.Add("authorization", parent.authCache.token_type + " " + parent.authCache.access_token);
            parent.mutex.ReleaseMutex();

            // Trace log
            parent.Log(HusqAutomower.mowersUrl + ": " + reqMessage.ToString());

            // Create the http request message and process it asynchronously
            HttpClientHandler handler = new HttpClientHandler();
            var client = new HttpClient(handler);
            Task<HttpResponseMessage> reqTask = client.SendAsync(reqMessage);
            reqTask.Wait(10000);
            Task<String> respTask = reqTask.Result.Content.ReadAsStringAsync();
            respTask.Wait(10000);

            // Check response status
            if (reqTask.Result.StatusCode != HttpStatusCode.OK) {
                parent.Log(respTask.Result, true);
                parent.SignalLogicError();
                return;
            }

            var jss = new JavaScriptSerializer();
            var mowersData = jss.Deserialize<Dictionary<string, dynamic>>(respTask.Result);
            foreach (var data in mowersData["data"]) {
                if (data["attributes"]["system"]["name"] == parent.MowerName.Value) {
                    parent.mutex.WaitOne();
                    parent.mowerId = data["id"];
                    parent.mutex.ReleaseMutex();
                    parent.BatteryCapacity.Value = (Byte)data["attributes"]["battery"]["batteryPercent"];
                    switch (data["attributes"]["mower"]["activity"]) {
                        case "MOWING":
                            if (!parent.ActivityMowing.Value) { parent.ActivityMowing.Value = true; }
                            if (parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = false; }
                            if (parent.ActivityCharging.Value) { parent.ActivityCharging.Value = false; }
                            if (parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = false; }
                            if (parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = false; }
                            if (parent.ActivityStopped.Value) { parent.ActivityStopped.Value = false; }
                            break;
                        case "GOING_HOME":
                            if (parent.ActivityMowing.Value) { parent.ActivityMowing.Value = false; }
                            if (!parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = true; }
                            if (parent.ActivityCharging.Value) { parent.ActivityCharging.Value = false; }
                            if (parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = false; }
                            if (parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = false; }
                            if (parent.ActivityStopped.Value) { parent.ActivityStopped.Value = false; }
                            break;
                        case "CHARGING":
                            if (parent.ActivityMowing.Value) { parent.ActivityMowing.Value = false; }
                            if (parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = false; }
                            if (!parent.ActivityCharging.Value) { parent.ActivityCharging.Value = true; }
                            if (parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = false; }
                            if (parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = false; }
                            if (parent.ActivityStopped.Value) { parent.ActivityStopped.Value = false; }
                            break;
                        case "LEAVING":
                            if (parent.ActivityMowing.Value) { parent.ActivityMowing.Value = false; }
                            if (parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = false; }
                            if (parent.ActivityCharging.Value) { parent.ActivityCharging.Value = false; }
                            if (!parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = true; }
                            if (parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = false; }
                            if (parent.ActivityStopped.Value) { parent.ActivityStopped.Value = false; }
                            break;
                        case "PARKED_IN_CS":
                            if (parent.ActivityMowing.Value) { parent.ActivityMowing.Value = false; }
                            if (parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = false; }
                            if (parent.ActivityCharging.Value) { parent.ActivityCharging.Value = false; }
                            if (parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = false; }
                            if (!parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = true; }
                            if (parent.ActivityStopped.Value) { parent.ActivityStopped.Value = false; }
                            break;
                        case "STOPPED_IN_GARDEN":
                            if (parent.ActivityMowing.Value) { parent.ActivityMowing.Value = false; }
                            if (parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = false; }
                            if (parent.ActivityCharging.Value) { parent.ActivityCharging.Value = false; }
                            if (parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = false; }
                            if (parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = false; }
                            if (!parent.ActivityStopped.Value) { parent.ActivityStopped.Value = true; }
                            break;
                        default:
                            if (parent.ActivityMowing.Value) { parent.ActivityMowing.Value = false; }
                            if (parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = false; }
                            if (parent.ActivityCharging.Value) { parent.ActivityCharging.Value = false; }
                            if (parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = false; }
                            if (parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = false; }
                            if (parent.ActivityStopped.Value) { parent.ActivityStopped.Value = false; }
                            break;
                    }
                    switch (data["attributes"]["mower"]["state"]) {
                        case "IN_OPERATION":
                            if (!parent.StateOperational.Value) { parent.StateOperational.Value = true; }
                            if (parent.StatePaused.Value) { parent.StatePaused.Value = false; }
                            if (parent.StateRestricted.Value) { parent.StateRestricted.Value = false; }
                            if (parent.StateError.Value) { parent.StateError.Value = false; }
                            break;
                        case "PAUSED":
                            if (parent.StateOperational.Value) { parent.StateOperational.Value = false; }
                            if (!parent.StatePaused.Value) { parent.StatePaused.Value = true; }
                            if (parent.StateRestricted.Value) { parent.StateRestricted.Value = false; }
                            if (parent.StateError.Value) { parent.StateError.Value = false; }
                            break;
                        case "RESTRICTED":
                            if (parent.StateOperational.Value) { parent.StateOperational.Value = false; }
                            if (parent.StatePaused.Value) { parent.StatePaused.Value = false; }
                            if (!parent.StateRestricted.Value) { parent.StateRestricted.Value = true; }
                            if (parent.StateError.Value) { parent.StateError.Value = false; }
                            break;
                        case "ERROR":
                        case "FATAL_ERROR":
                        case "ERROR_AT_POWER_UP":
                            if (parent.StateOperational.Value) { parent.StateOperational.Value = false; }
                            if (parent.StatePaused.Value) { parent.StatePaused.Value = false; }
                            if (parent.StateRestricted.Value) { parent.StateRestricted.Value = false; }
                            if (!parent.StateError.Value) { parent.StateError.Value = true; }
                            break;
                        default:
                            if (parent.StateOperational.Value) { parent.StateOperational.Value = false; }
                            if (parent.StatePaused.Value) { parent.StatePaused.Value = false; }
                            if (parent.StateRestricted.Value) { parent.StateRestricted.Value = false; }
                            if (parent.StateError.Value) { parent.StateError.Value = false; }
                            break;
                    }
                }
                break;
            }
        }

        private Boolean AuthorizeRequest(String url, Dictionary<String, String> payload) {
            this.Log(url + ": " + payload.ToString());

            // Create an URL encoded http content message with the provided payload
            var reqContent = new FormUrlEncodedContent(payload);
            HttpClientHandler handler = new HttpClientHandler();
            var client = new HttpClient(handler);

            // Send content and wait for result
            Task<HttpResponseMessage> reqTask = client.PostAsync(url, reqContent);
            reqTask.Wait(10000);
            Task<String> respTask = reqTask.Result.Content.ReadAsStringAsync();
            respTask.Wait(10000);

            // Check return status and store the authentication details
            if (reqTask.Result.StatusCode == HttpStatusCode.OK) {
                JavaScriptSerializer jss = new JavaScriptSerializer();
                this.authCache = jss.Deserialize<AuthCache>(respTask.Result);
                this.Log(respTask.Result);
                this.authCache.time = DateTime.UtcNow;
                return true;
            } else {
                if (this.authCache != null) {
                    this.authCache.refresh_token = null;
                    this.authCache.access_token = null;
                }
                this.Log(respTask.Result);
                return false;
            }
        }

        private Boolean AuthorizeCheck() {
            mutex.WaitOne();

            // Re-authorize if authorization is about to expire
            // We give it a 20 seconds margin
            if ((this.authCache != null) && (this.authCache.access_token != null)) {
                if (((DateTime.UtcNow.Ticks - this.authCache.time.Ticks) / TimeSpan.TicksPerSecond) < (this.authCache.expires_in - 20)) {
                    mutex.ReleaseMutex();
                    return true;
                }
            }

            // Create the request payload as url encoded string
            var payload = new Dictionary<String, String>();
            payload.Add("client_id", this.AppId.Value);

            /* The first authorisation is the password grant.
             * If this fails, there must be something wrong in the app ID, user or password.
             * Nothing else we can do ... */
            if ((this.authCache == null) || (this.authCache.access_token == null) || (this.authCache.refresh_token == null)) {
                this.Log("Performing password authorisation");
                payload.Add("grant_type", "password");
                payload.Add("username", this.AuthUser.Value);
                payload.Add("password", this.AuthPassword.Value);
                if (this.AuthorizeRequest(HusqAutomower.authUrl, payload)) { 
                    mutex.ReleaseMutex();
                    return true;
                } else {
                    mutex.ReleaseMutex();
                    return false;
                }
            }

            /* The second authorisation trys through the refresh token.
             * Though the refresh token might be invalid or timed out.
             * In that case we need to use password authentication again. */
            if (this.authCache.refresh_token != null) {
                this.Log("Performing refresh-token authorisation");
                payload.Add("grant_type", "refresh_token");
                payload.Add("refresh_token", this.authCache.refresh_token);
                if (this.AuthorizeRequest(HusqAutomower.authUrl, payload)) {
                    mutex.ReleaseMutex();
                    return true;
                }
            }

            /* There was a refresh token but it got invalid for whatever reason.
             * Last resort is to try full password authentication.
             * If this fails, nothing else we can do ... */
            this.Log("Performing password authorisation");
            payload.Add("grant_type", "password");
            payload.Add("username", this.AuthUser.Value);
            payload.Add("password", this.AuthPassword.Value);
            if (this.AuthorizeRequest(HusqAutomower.authUrl, payload)) { 
                mutex.ReleaseMutex();
                return true;
            } else {
                mutex.ReleaseMutex();
                return false;
            }
        }
    }
}

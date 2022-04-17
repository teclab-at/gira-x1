using LogicModule.Nodes.Helpers;
using LogicModule.ObjectModel;
using LogicModule.ObjectModel.TypeSystem;
using System;
using System.Net;
using System.Net.Security;
using System.Collections.Specialized;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Linq;
using System.Diagnostics;
using Newtonsoft.Json;

namespace teclab_at.logic.collection {
    public class HusqAutomower : LogicNodeBase {
        public HusqAutomower(INodeContext context) : base(context) {
            // check
            context.ThrowIfNull("context");

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
        private AuthCache authCache = new AuthCache();
        private MowersCache mowersCache = new MowersCache();
        private String mowerId = String.Empty;
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

        private string ToKeyValuePairs(NameValueCollection nvc) {
            var array = (from key in nvc.AllKeys from value in nvc.GetValues(key) select string.Format("{0}={1}", key, value)).ToArray();
            return string.Join("&", array);
        }

        public void Log(String message, Boolean force = false) {
            if ((Environment.OSVersion.Platform != PlatformID.Unix) || (!this.DoLog.Value && !force)) return;
            File.AppendAllText(HusqAutomower.logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + ": " + message + Environment.NewLine);
        }

        public void SignalLogicError(Boolean state = true) {
            if (!this.LogicError.HasValue || (this.LogicError.Value != state)) { this.LogicError.Value = state; }
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
        }

        public override void Execute() {
            // React only on Trigger input
            if (!this.Trigger.Value || !this.Trigger.WasSet) return;

            // Schedule as async task ...
            if ((this.statusTask == null) || ((this.statusTask.Status != TaskStatus.Running) && (this.statusTask.Status != TaskStatus.WaitingToRun))) {
                if (Environment.OSVersion.Platform == PlatformID.Unix) {
                    this.statusTask = new Task(() => QueryStatus_curl(this));
                } else {
                    this.statusTask = new Task(() => QueryStatus_net(this));
                }
                this.statusTask.Start();
            }
        }

        private class AuthCache {
            public String error { get; set; } = String.Empty;
            public String access_token { get; set; } = String.Empty;
            public String refresh_token { get; set; } = String.Empty;
            public String provider { get; set; } = String.Empty;
            public String user_id { get; set; } = String.Empty;
            public String token_type { get; set; } = String.Empty;
            public String scope { get; set; } = String.Empty;
            public int expires_in { get; set; } = 0;
            public DateTime time { get; set; } = DateTime.MinValue;
        }

        public class MowerData {
            public class AttributesData {
                public class SystemData {
                    public String name { get; set; } = String.Empty;
                    public String model { get; set; } = String.Empty;
                    public String serialNumber { get; set; } = String.Empty;
                }
                public class BatteryData {
                    public int batteryPercent { get; set; } = 0;
                }
                public class MowerState {
                    public String mode { get; set; } = String.Empty;
                    public String activity { get; set; } = String.Empty;
                    public String state { get; set; } = String.Empty;
                    public int errorCode { get; set; } = 0;
                    public long errorCodeTimestamp { get; set; } = 0;
                }
                public class MetaData {
                    public Boolean connected { get; set; } = false;
                    public long statusTimestamp { get; set; } = 0;
                }
                public SystemData system { get; set; }
                public BatteryData battery { get; set; }
                public MowerState mower { get; set; }
                public MetaData metadata { get; set; }
            }
            public String type { get; set; } = String.Empty;
            public String id { get; set; }
            public AttributesData attributes { get; set; }
        }

        private class MowerError {
            public String id { get; set; } = String.Empty;
        }

        private class MowersCache {
            public MowerError[] errors { get; set; }
            public MowerData[] data { get; set; }
            public DateTime time { get; set; } = DateTime.MinValue;
        }

        static void QueryStatus_curl(HusqAutomower parent) {
            // It might be the first call or other reasons for not having an access token
            // Anyhow, start to authorize
            if (!parent.AuthorizeCheck()) return;
            // Sanity check
            // Check availability of the access token
            if (parent.authCache.access_token == String.Empty) {
                parent.Log("Internal error: access token cannot be null or empty", true);
                parent.SignalLogicError();
                return;
            }

            // create and define the process
            Process curlCmd = new Process();
            curlCmd.StartInfo.FileName = "/usr/bin/curl";
            //curlCmd.StartInfo.FileName = "D:/Programs/msys64/usr/bin/curl.exe";
            curlCmd.StartInfo.Arguments = string.Format("-m 10 -X GET -H \"Authorization-Provider: husqvarna\" -H \"Content - Type: application / vnd.api + json\" -H \"Authorization: Bearer {0}\" -H \"X-Api-Key: {1}\" {2}", parent.authCache.access_token, parent.AppId.Value, HusqAutomower.mowersUrl);
            curlCmd.StartInfo.RedirectStandardOutput = true;
            curlCmd.StartInfo.RedirectStandardError = true;
            curlCmd.StartInfo.CreateNoWindow = true;
            curlCmd.StartInfo.UseShellExecute = false;

            // Execute and wait until it exits
            curlCmd.Start();
            if (!curlCmd.WaitForExit(15000)) {
                curlCmd.Kill();
                parent.Log("Curl process did not exit in time", true);
                parent.SignalLogicError();
            } else if (curlCmd.ExitCode != 0) {
                String data = curlCmd.StandardError.ReadToEnd();
                parent.Log("Curl process error: " + data, true);
                parent.SignalLogicError();
            } else {
                // Read string response
                String data = curlCmd.StandardOutput.ReadToEnd();
                if (data == String.Empty) {
                    parent.Log("Curl process returned empty result", true);
                    parent.SignalLogicError();
                }
                parent.Log(data);
                // Parse the JSON string into the authentication cache
                parent.mowersCache.errors = null;
                parent.mowersCache = JsonConvert.DeserializeObject<MowersCache>(data);
                // Check
                if (parent.mowersCache.errors != null) {
                    parent.Log("Response error: " + data, true);
                    parent.SignalLogicError();
                } else {
                    // Interpret the stored cache status
                    UpdateStatus(parent);
                }
            }

         }

        static void QueryStatus_net(HusqAutomower parent) {
            // It might be the first call or other reasons for not having an access token
            // Anyhow, start to authorize
            if (!parent.AuthorizeCheck()) return;
            // Sanity check
            // Check availability of the access token
            if (parent.authCache.access_token == String.Empty) {
                parent.Log("Internal error: access token cannot be null or empty", true);
                parent.SignalLogicError();
                return;
            }

            // Create the HTTP request using the GET method and the required headers
            HttpWebRequest httpRequest = (HttpWebRequest)HttpWebRequest.Create(HusqAutomower.mowersUrl);
            httpRequest.Method = "GET";
            httpRequest.ContentType = "application/vnd.api+json";
            httpRequest.Headers.Add("authorization-provider", parent.authCache.provider);
            httpRequest.Headers.Add("x-api-key", parent.AppId.Value);
            httpRequest.Headers.Add("authorization", parent.authCache.token_type + " " + parent.authCache.access_token);
            // Prepare for the response
            try {
                HttpWebResponse httpResponse = (HttpWebResponse)httpRequest.GetResponse();
                Stream streamIn = httpResponse.GetResponseStream();
                var streamReader = new StreamReader(streamIn, Encoding.UTF8);
                String data = streamReader.ReadToEnd();
                parent.Log(data);
                // Parse the JSON string into the authentication cache
                parent.mowersCache = JsonConvert.DeserializeObject<MowersCache>(data);
                // Cleanup
                streamReader.Close();
                streamIn.Close();
                httpResponse.Close();
            } catch (WebException ex) {
                parent.Log(ex.ToString(), true);
                parent.SignalLogicError();
                // get more details on the error condition
                HttpWebResponse httpResponse = (HttpWebResponse)ex.Response;
                Stream streamIn = httpResponse.GetResponseStream();
                var streamReader = new StreamReader(streamIn, Encoding.UTF8);
                String data = streamReader.ReadToEnd();
                parent.Log(data, true);
                // Cleanup
                streamReader.Close();
                streamIn.Close();
                httpResponse.Close();
            } finally {
                // Interpret the stored cache status
                UpdateStatus(parent);
            }
        }

        // Interpret the stored cache status and set output states of the node
        static void UpdateStatus(HusqAutomower parent) {
            // Loop through all mowers and find the one we are looking for
            // Then write the outputs and store the mowers ID for commanding
            bool foundMower = false;
            for (int i = 0; i < parent.mowersCache.data.Length; i++) {
                MowerData data = parent.mowersCache.data[i];
                if (data.attributes.system.name != parent.MowerName.Value) continue;
                foundMower = true;
                parent.mowerId = data.id;
                if (!parent.BatteryCapacity.HasValue || (parent.BatteryCapacity.Value != (Byte)data.attributes.battery.batteryPercent)) { parent.BatteryCapacity.Value = (Byte)data.attributes.battery.batteryPercent; }
                switch (data.attributes.mower.activity) {
                    case "MOWING":
                        if (!parent.ActivityMowing.HasValue || !parent.ActivityMowing.Value) { parent.ActivityMowing.Value = true; }
                        if (!parent.ActivityGoingHome.HasValue || parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = false; }
                        if (!parent.ActivityCharging.HasValue || parent.ActivityCharging.Value) { parent.ActivityCharging.Value = false; }
                        if (!parent.ActivityLeavingCS.HasValue || parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = false; }
                        if (!parent.ActivityParkingCS.HasValue || parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = false; }
                        if (!parent.ActivityStopped.HasValue || parent.ActivityStopped.Value) { parent.ActivityStopped.Value = false; }
                        break;
                    case "GOING_HOME":
                        if (!parent.ActivityMowing.HasValue || parent.ActivityMowing.Value) { parent.ActivityMowing.Value = false; }
                        if (!parent.ActivityGoingHome.HasValue || !parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = true; }
                        if (!parent.ActivityCharging.HasValue || parent.ActivityCharging.Value) { parent.ActivityCharging.Value = false; }
                        if (!parent.ActivityLeavingCS.HasValue || parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = false; }
                        if (!parent.ActivityParkingCS.HasValue || parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = false; }
                        if (!parent.ActivityStopped.HasValue || parent.ActivityStopped.Value) { parent.ActivityStopped.Value = false; }
                        break;
                    case "CHARGING":
                        if (!parent.ActivityMowing.HasValue || parent.ActivityMowing.Value) { parent.ActivityMowing.Value = false; }
                        if (!parent.ActivityGoingHome.HasValue || parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = false; }
                        if (!parent.ActivityCharging.HasValue || !parent.ActivityCharging.Value) { parent.ActivityCharging.Value = true; }
                        if (!parent.ActivityLeavingCS.HasValue || parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = false; }
                        if (!parent.ActivityParkingCS.HasValue || parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = false; }
                        if (!parent.ActivityStopped.HasValue || parent.ActivityStopped.Value) { parent.ActivityStopped.Value = false; }
                        break;
                    case "LEAVING":
                        if (!parent.ActivityMowing.HasValue || parent.ActivityMowing.Value) { parent.ActivityMowing.Value = false; }
                        if (!parent.ActivityGoingHome.HasValue || parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = false; }
                        if (!parent.ActivityCharging.HasValue || parent.ActivityCharging.Value) { parent.ActivityCharging.Value = false; }
                        if (!parent.ActivityLeavingCS.HasValue || !parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = true; }
                        if (!parent.ActivityParkingCS.HasValue || parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = false; }
                        if (!parent.ActivityStopped.HasValue || parent.ActivityStopped.Value) { parent.ActivityStopped.Value = false; }
                        break;
                    case "PARKED_IN_CS":
                        if (!parent.ActivityMowing.HasValue || parent.ActivityMowing.Value) { parent.ActivityMowing.Value = false; }
                        if (!parent.ActivityGoingHome.HasValue || parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = false; }
                        if (!parent.ActivityCharging.HasValue || parent.ActivityCharging.Value) { parent.ActivityCharging.Value = false; }
                        if (!parent.ActivityLeavingCS.HasValue || parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = false; }
                        if (!parent.ActivityParkingCS.HasValue || !parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = true; }
                        if (!parent.ActivityStopped.HasValue || parent.ActivityStopped.Value) { parent.ActivityStopped.Value = false; }
                        break;
                    case "NOT_APPLICABLE":
                    case "STOPPED_IN_GARDEN":
                        if (!parent.ActivityMowing.HasValue || parent.ActivityMowing.Value) { parent.ActivityMowing.Value = false; }
                        if (!parent.ActivityGoingHome.HasValue || parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = false; }
                        if (!parent.ActivityCharging.HasValue || parent.ActivityCharging.Value) { parent.ActivityCharging.Value = false; }
                        if (!parent.ActivityLeavingCS.HasValue || parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = false; }
                        if (!parent.ActivityParkingCS.HasValue || parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = false; }
                        if (!parent.ActivityStopped.HasValue || !parent.ActivityStopped.Value) { parent.ActivityStopped.Value = true; }
                        break;
                    default:
                        if (!parent.ActivityMowing.HasValue || parent.ActivityMowing.Value) { parent.ActivityMowing.Value = false; }
                        if (!parent.ActivityGoingHome.HasValue || parent.ActivityGoingHome.Value) { parent.ActivityGoingHome.Value = false; }
                        if (!parent.ActivityCharging.HasValue || parent.ActivityCharging.Value) { parent.ActivityCharging.Value = false; }
                        if (!parent.ActivityLeavingCS.HasValue || parent.ActivityLeavingCS.Value) { parent.ActivityLeavingCS.Value = false; }
                        if (!parent.ActivityParkingCS.HasValue || parent.ActivityParkingCS.Value) { parent.ActivityParkingCS.Value = false; }
                        if (!parent.ActivityStopped.HasValue || parent.ActivityStopped.Value) { parent.ActivityStopped.Value = false; }
                        break;
                }
                switch (data.attributes.mower.state) {
                    case "IN_OPERATION":
                        if (!parent.StateOperational.HasValue || !parent.StateOperational.Value) { parent.StateOperational.Value = true; }
                        if (!parent.StatePaused.HasValue || parent.StatePaused.Value) { parent.StatePaused.Value = false; }
                        if (!parent.StateRestricted.HasValue || parent.StateRestricted.Value) { parent.StateRestricted.Value = false; }
                        if (!parent.StateError.HasValue || parent.StateError.Value) { parent.StateError.Value = false; }
                        break;
                    case "PAUSED":
                        if (!parent.StateOperational.HasValue || parent.StateOperational.Value) { parent.StateOperational.Value = false; }
                        if (!parent.StatePaused.HasValue || !parent.StatePaused.Value) { parent.StatePaused.Value = true; }
                        if (!parent.StateRestricted.HasValue || parent.StateRestricted.Value) { parent.StateRestricted.Value = false; }
                        if (!parent.StateError.HasValue || parent.StateError.Value) { parent.StateError.Value = false; }
                        break;
                    case "RESTRICTED":
                        if (!parent.StateOperational.HasValue || parent.StateOperational.Value) { parent.StateOperational.Value = false; }
                        if (!parent.StatePaused.HasValue || parent.StatePaused.Value) { parent.StatePaused.Value = false; }
                        if (!parent.StateRestricted.HasValue || !parent.StateRestricted.Value) { parent.StateRestricted.Value = true; }
                        if (!parent.StateError.HasValue || parent.StateError.Value) { parent.StateError.Value = false; }
                        break;
                    case "ERROR":
                    case "FATAL_ERROR":
                    case "ERROR_AT_POWER_UP":
                        if (!parent.StateOperational.HasValue || parent.StateOperational.Value) { parent.StateOperational.Value = false; }
                        if (!parent.StatePaused.HasValue || parent.StatePaused.Value) { parent.StatePaused.Value = false; }
                        if (!parent.StateRestricted.HasValue || parent.StateRestricted.Value) { parent.StateRestricted.Value = false; }
                        if (!parent.StateError.HasValue || !parent.StateError.Value) { parent.StateError.Value = true; }
                        break;
                    default:
                        if (!parent.StateOperational.HasValue || parent.StateOperational.Value) { parent.StateOperational.Value = false; }
                        if (!parent.StatePaused.HasValue || parent.StatePaused.Value) { parent.StatePaused.Value = false; }
                        if (!parent.StateRestricted.HasValue || parent.StateRestricted.Value) { parent.StateRestricted.Value = false; }
                        if (!parent.StateError.HasValue || parent.StateError.Value) { parent.StateError.Value = false; }
                        break;
                }
                foundMower = true;
                parent.SignalLogicError(false);
                break;
            }
            if (!foundMower) {
                parent.Log("Mower name not found", true);
                parent.SignalLogicError();
            }
        }

        private Boolean AuthorizeRequest_curl(NameValueCollection payload) {
            // create and define the process
            Process curlCmd = new Process();
            curlCmd.StartInfo.FileName = "/usr/bin/curl";
            //curlCmd.StartInfo.FileName = "D:/Programs/msys64/usr/bin/curl.exe";
            curlCmd.StartInfo.Arguments = string.Format("-m 10 -X POST -d \"{0}\" {1}", this.ToKeyValuePairs(payload), HusqAutomower.authUrl);
            curlCmd.StartInfo.RedirectStandardOutput = true;
            curlCmd.StartInfo.RedirectStandardError = true;
            curlCmd.StartInfo.CreateNoWindow = true;
            curlCmd.StartInfo.UseShellExecute = false;

            // Execute and wait until it exits
            curlCmd.Start();
            if (!curlCmd.WaitForExit(15000)) {
                curlCmd.Kill();
                this.Log("Curl process did not exit in time", true);
            } else if (curlCmd.ExitCode != 0) {
                String data = curlCmd.StandardError.ReadToEnd();
                this.Log("Curl process error: " + data, true);
            } else {
                // Read string response
                String data = curlCmd.StandardOutput.ReadToEnd();
                if (data == String.Empty) {
                    this.Log("Curl process returned empty result", true);
                } else {
                    this.Log(data);
                    // Parse the JSON string into the authentication cache
                    this.authCache.error = String.Empty;
                    this.authCache = JsonConvert.DeserializeObject<AuthCache>(data);
                    this.authCache.time = DateTime.UtcNow;
                    // Check
                    if (authCache.error == String.Empty) { return true; }
                    this.Log("Response error: " + data, true);
                }
            }
            return false;
        }

        private Boolean AuthorizeRequest_net(NameValueCollection payload) {
            // Create the HTTP data from our payload and then log
            String httpData = this.ToKeyValuePairs(payload);
            this.Log(HusqAutomower.authUrl + "?" + httpData);

            // Create the HTTP request using the POST method and the required content
            HttpWebRequest httpRequest = (HttpWebRequest)HttpWebRequest.Create(HusqAutomower.authUrl);
            httpRequest.Method = "POST";
            httpRequest.ContentType = "application/x-www-form-urlencoded; charset=utf-8";

            // Now write our request into the world and ...
            // Prepare for the response
            try {
                // Request
                var streamOut = new StreamWriter(httpRequest.GetRequestStream(), Encoding.UTF8);
                streamOut.Write(httpData);
                streamOut.Close();
                // Response
                HttpWebResponse httpResponse = (HttpWebResponse)httpRequest.GetResponse();
                Stream streamIn = httpResponse.GetResponseStream();
                var streamReader = new StreamReader(streamIn, Encoding.UTF8);
                String data = streamReader.ReadToEnd();
                this.Log(data);
                // Parse the JSON string into the authentication cache
                this.authCache = JsonConvert.DeserializeObject<AuthCache>(data);
                this.authCache.time = DateTime.UtcNow;
                // Cleanup
                streamReader.Close();
                streamIn.Close();
                httpResponse.Close();
                return true;
            } catch (WebException ex) {
                this.Log(ex.ToString(), true);
                this.SignalLogicError();
                // get more details on the error condition
                HttpWebResponse httpResponse = (HttpWebResponse)ex.Response;
                Stream streamIn = httpResponse.GetResponseStream();
                var streamReader = new StreamReader(streamIn, Encoding.UTF8);
                String data = streamReader.ReadToEnd();
                this.Log(data, true);
                // Clear the authentication cache
                this.authCache.refresh_token = String.Empty;
                this.authCache.access_token = String.Empty;
                this.authCache.time = DateTime.MinValue;
                // Cleanup
                streamReader.Close();
                streamIn.Close();
                httpResponse.Close();
                return false;
            }
        }

        private Boolean AuthorizeCheck() {
            // Re-authorize if authorization is about to expire
            // We give it a 20 seconds margin
            if (((DateTime.UtcNow.Ticks - this.authCache.time.Ticks) / TimeSpan.TicksPerSecond) < (this.authCache.expires_in - 20)) {
                return true;
            }

            // Create the request payload as url encoded string
            var payload = new NameValueCollection();
            payload.Add("client_id", this.AppId.Value);

            /* The first authorisation is the password grant.
             * If this fails, there must be something wrong in the app ID, user or password.
             * Nothing else we can do ... */
            if ((this.authCache.access_token == String.Empty) || (this.authCache.refresh_token == String.Empty)) {
                this.Log("Performing password authorisation ...");
                payload.Add("grant_type", "password");
                payload.Add("username", this.AuthUser.Value);
                payload.Add("password", this.AuthPassword.Value);
                if (Environment.OSVersion.Platform == PlatformID.Unix) {
                    if (this.AuthorizeRequest_curl(payload)) {
                        this.Log("... success");
                        return true;
                    }
                } else {
                    if (this.AuthorizeRequest_net(payload)) {
                        this.Log("... success");
                        return true;
                    }
                }   
                this.Log("... failed");
                return false;
            }

            /* The second authorisation trys through the refresh token.
             * Though the refresh token might be invalid or timed out.
             * In that case we need to use password authentication again. */
            if (this.authCache.refresh_token != String.Empty) {
                this.Log("Performing refresh-token authorisation ...");
                payload.Add("grant_type", "refresh_token");
                payload.Add("refresh_token", this.authCache.refresh_token);
                if (Environment.OSVersion.Platform == PlatformID.Unix) {
                    if (this.AuthorizeRequest_curl(payload)) {
                        this.Log("... success");
                        return true;
                    }
                } else {
                    if (this.AuthorizeRequest_net(payload)) {
                        this.Log("... success");
                        return true;
                    }
                }
                this.Log("... failed");
            }

            /* There was a refresh token but it got invalid for whatever reason.
             * Last resort is to try full password authentication.
             * If this fails, nothing else we can do ... */
            this.Log("Performing password authorisation ...");
            payload.Add("grant_type", "password");
            payload.Add("username", this.AuthUser.Value);
            payload.Add("password", this.AuthPassword.Value);
            if (Environment.OSVersion.Platform == PlatformID.Unix) {
                if (this.AuthorizeRequest_curl(payload)) {
                    this.Log("... success");
                    return true;
                }
            } else {
                if (AuthorizeRequest_net(payload)) {
                    this.Log("... success");
                    return true;
                }
            }
            this.Log("... failed");
            return false;
        }
    }
}

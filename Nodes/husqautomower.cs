using LogicModule.Nodes.Helpers;
using LogicModule.ObjectModel;
using LogicModule.ObjectModel.TypeSystem;
using System;
using System.Web;
using System.Net;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text;
//using System.Text.Json.Serialization;
//using Newtonsoft.Json;
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
            this.StatusMessage = typeService.CreateString(PortTypes.String, "Status Message");
            this.ErrorStatus = typeService.CreateBool(PortTypes.Bool, "Error Status");
        }

        // Class internals
        private Mutex mutex;

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
        public StringValueObject StatusMessage { get; private set; }
        
        [Output(DisplayOrder = 2)]
        public BoolValueObject ErrorStatus { get; private set; }

        public void Log(String message) {
            if ((Environment.OSVersion.Platform != PlatformID.Unix) || !this.DoLog.Value) return;
            mutex.WaitOne();
            File.AppendAllText("/var/log/teclab.at.sendmail.log", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + ": " + message + Environment.NewLine);
            mutex.ReleaseMutex();
        }

        public override void Startup() {
            // call base
            base.Startup();
            if ((Environment.OSVersion.Platform == PlatformID.Unix) && this.DoLog.Value) {
                try {File.Delete("/var/log/teclab.at.husqautomower.log");} catch (Exception) {}
                this.Log("Husqvarner Automower logic page started");
            }
        }

        public override void Execute() {
            // React only on Trigger input
            if (!this.Trigger.Value || !this.Trigger.WasSet) return;

            const string authUrl = "https://api.authentication.husqvarnagroup.dev/v1/oauth2/token";

            var dict = new Dictionary<string, string>();
            dict.Add("grant_type", "password");
            dict.Add("client_id", AppId.Value);
            dict.Add("username", AuthUser.Value);
            dict.Add("password", AuthPassword.Value);

            //byte[] baMsec = Encoding.UTF8.GetBytes();

            var reqContent = new FormUrlEncodedContent(dict);
            HttpClientHandler handler = new HttpClientHandler();
            var client = new HttpClient(handler);
            // todo check status
            Task<HttpResponseMessage> httpTask = client.PostAsync(authUrl, reqContent);
            httpTask.Wait(10000);

            Task<String> contentTask = httpTask.Result.Content.ReadAsStringAsync();
            contentTask.Wait(10000);



            // Schedule as async task ...
            /*try {
                Task task = Task.Run(() => SendMessage(this, client, message, this.SendRetries.Value));
            } catch (Exception ex) {
                this.Log("SendMessage failed: " + message.Subject + " -> " + ex.Message);
                this.MailError(ex.Message);
            } finally {
                client.Dispose();
                message.Dispose();
            }*/
        }

        public void MailError(String message) {
            mutex.WaitOne();
            this.StatusMessage.Value = message;
            this.ErrorStatus.Value = true;
            mutex.ReleaseMutex();
        }
        
        public void MailSuccess() {
            mutex.WaitOne();
            this.StatusMessage.Value = "";
            this.ErrorStatus.Value = false;
            mutex.ReleaseMutex();
        }

        /*static void SendMessage(SendMail parent, SmtpClient client, MailMessage message, int sendRetries) {
        }*/
    }
}

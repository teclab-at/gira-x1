using LogicModule.Nodes.Helpers;
using LogicModule.ObjectModel;
using LogicModule.ObjectModel.TypeSystem;
using System;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.IO;

namespace teclab_at.logic {
    public class SendMail : LogicNodeBase {
        public SendMail(INodeContext context) : base(context) {
            // check
            context.ThrowIfNull("context");

            // Because of multiple parallel access to resources we need mutex
            this.mutex = new Mutex();

            // Initialize the input ports
            ITypeService typeService = context.GetService<ITypeService>();
            this.SendTrigger = typeService.CreateBool(PortTypes.Bool, "Trigger");
            this.MailTo = typeService.CreateString(PortTypes.String, "To");
            this.MailFrom = typeService.CreateString(PortTypes.String, "From");
            this.MailFromAlias = typeService.CreateString(PortTypes.String, "From Alias");
            this.MailSubject = typeService.CreateString(PortTypes.String, "Subject", "Mail from X1");
            this.MailBody = typeService.CreateString(PortTypes.String, "Message Body", "Message from X1");
            this.SmtpHost = typeService.CreateString(PortTypes.String, "SMTP Server");
            this.SmtpPort = typeService.CreateInt(PortTypes.Integer, "SMTP Port");
            this.SmtpUser = typeService.CreateString(PortTypes.String, "SMTP User");
            this.SmtpPassword = typeService.CreateString(PortTypes.String, "SMTP Password");
            this.SendRetries = typeService.CreateInt(PortTypes.Integer, "Nr. Retries", 10);
            this.DoLog = typeService.CreateBool(PortTypes.Bool, "Enable Logging", false);
            // Initialize the output ports
            this.ErrorMessage = typeService.CreateString(PortTypes.String, "Error Message");
            this.ErrorStatus = typeService.CreateBool(PortTypes.Bool, "Error Status");
        }

        // Class internals
        private Mutex mutex;

        // Logic block definition
        [Input(DisplayOrder = 1, IsInput = true, IsRequired = true)]
        public BoolValueObject SendTrigger { get; private set; }

        [Parameter(DisplayOrder = 2, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailTo { get; private set; }

        [Parameter(DisplayOrder = 3, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailFrom { get; private set; }

        [Parameter(DisplayOrder = 3, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailFromAlias { get; private set; }

        [Input(DisplayOrder = 4, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailSubject { get; private set; }

        [Input(DisplayOrder = 5, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailBody { get; private set; }

        [Parameter(DisplayOrder = 6, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject SmtpHost { get; private set; }

        [Parameter(DisplayOrder = 7, InitOrder = 1, IsDefaultShown = false)]
        public IntValueObject SmtpPort { get; private set; }

        [Parameter(DisplayOrder = 9, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject SmtpUser { get; private set; }

        [Parameter(DisplayOrder = 10, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject SmtpPassword { get; private set; }

        [Input(DisplayOrder = 11, InitOrder = 1, IsDefaultShown = false)]
        public IntValueObject SendRetries { get; private set; }

        [Input(DisplayOrder = 12, IsInput = true, IsRequired = false)]
        public BoolValueObject DoLog { get; private set; }

        [Output(DisplayOrder = 1)]
        public StringValueObject ErrorMessage { get; private set; }
        
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
            // this is needed to accept all SSL certificates (honestly I have no clue ...)
            ServicePointManager.ServerCertificateValidationCallback =
                delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) {return true;};
            if ((Environment.OSVersion.Platform == PlatformID.Unix) && this.DoLog.Value) {
                try {File.Delete("/var/log/teclab.at.sendmail.log");} catch (Exception) {}
                this.Log("SendMail logic page started");
            }
        }

        public override void Execute() {
            // React only on Trigger input
            if (!this.SendTrigger.Value || !this.SendTrigger.WasSet) return;
            this.Log("Execute: " + this.MailSubject.Value);

            // Configure a new smtp client instance which handles the connection details
            SmtpClient client = new SmtpClient(this.SmtpHost.Value, this.SmtpPort.Value);
            client.Credentials = new NetworkCredential(this.SmtpUser.Value, this.SmtpPassword.Value);
            client.EnableSsl = true;
            client.Timeout = 10;

            // Create a message object with subject, body, etc...
            MailAddress mailFrom = new MailAddress(this.MailFrom.Value, this.MailFromAlias.Value, System.Text.Encoding.UTF8);
            MailAddress mailTo = new MailAddress(this.MailTo.Value, this.MailTo.Value, System.Text.Encoding.UTF8);
            MailMessage message = new MailMessage(mailFrom, mailTo);
            message.Body = new String(this.MailBody.Value.ToCharArray());
            message.BodyEncoding = System.Text.Encoding.UTF8;
            message.Subject = new String(this.MailSubject.Value.ToCharArray());
            message.SubjectEncoding = System.Text.Encoding.UTF8;

            // Schedule as async task ...
            try {
                Task task = Task.Run(() => SendMessage(this, client, message, this.SendRetries.Value));
            } catch (Exception ex) {
                this.Log("SendMessage failed: " + message.Subject + " -> " + ex.Message);
                this.MailError(ex.Message);
            } finally {
                client.Dispose();
                message.Dispose();
            }
        }

        public void MailError(String message) {
            mutex.WaitOne();
            this.ErrorMessage.Value = message;
            this.ErrorStatus.Value = true;
            mutex.ReleaseMutex();
        }
        
        public void MailSuccess() {
            mutex.WaitOne();
            this.ErrorMessage.Value = "";
            this.ErrorStatus.Value = false;
            mutex.ReleaseMutex();
        }

        static void SendMessage(SendMail parent, SmtpClient client, MailMessage message, int sendRetries) {
            // Send until success
            Boolean sendSuccess = false;
            int tryNr = 1;
            while (sendRetries > 1) {
                try {
                    parent.Log("SendMessage (try " + tryNr.ToString() + "): " + message.Subject);
                    client.Send(message);
                    sendSuccess = true;
                    break;
                } catch (Exception ex) {
                    parent.Log("SendMessage (try " + tryNr.ToString() + ") failed: " + message.Subject + " -> " + ex.Message);
                    parent.MailError(sendRetries.ToString() + ": " + ex.Message);
                    // Sleep for 50 seconds. Note that the client.Send timeout is set to 10 seconds which gives us a one minute cycle
                    Thread.Sleep(50000);
                }
                sendRetries -= 1;
                tryNr += 1;
            }

            // Cleanup if we succeeded on first retries
            if (sendSuccess) {
                client.Dispose();
                message.Dispose();
                parent.Log("SendMessage (try " + tryNr.ToString() + ") succeess");
                parent.MailSuccess();
                return;
            }

            // Final try
            try {
                parent.Log("SendMessage (try " + tryNr.ToString() + "): " + message.Subject);
                client.Send(message);
                sendSuccess = true;
                parent.MailSuccess();
            } catch (Exception ex) {
                parent.Log("SendMessage (try " + tryNr.ToString() + ") failed: " + message.Subject + " -> " + ex.Message);
                parent.MailError(ex.Message);
            } finally {
                client.Dispose();
                message.Dispose();
            }

            // Check if sending succeeded
            if (sendSuccess) {
                parent.Log("SendMessage (try " + tryNr.ToString() + ") succeess");
                parent.MailSuccess();
            }
        }
    }
}

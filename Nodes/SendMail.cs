using LogicModule.Nodes.Helpers;
using LogicModule.ObjectModel;
using LogicModule.ObjectModel.TypeSystem;
using System;
using System.Net;
using System.Net.Mail;
//using System.Net.Mime;
//using System.Net.Mime;
//using MailKit.Security;
//using MailKit.Net.Smtp;
//using MimeKit;
using System.Threading;
using System.Threading.Tasks;

namespace teclab_at.logic {
    public class SendMail : LogicNodeBase {
        /*static class EncryptionTypes {
            public const string NONE = "None";
            public const string AUTO = "Auto";
            public const string SSL = "SSL";
            public const string STARTTLS = "STARTTLS";
            public static string[] Values = new[] {NONE, AUTO, SSL, STARTTLS};
        }*/

        public SendMail(INodeContext context) : base(context) {
            // check
            context.ThrowIfNull("context");

            // Initialize the input ports
            ITypeService typeService = context.GetService<ITypeService>();
            this.SendTrigger = typeService.CreateBool(PortTypes.Bool, "Trigger");
            this.MailTo = typeService.CreateString(PortTypes.String, "To");
            this.MailFrom = typeService.CreateString(PortTypes.String, "From");
            this.MailSubject = typeService.CreateString(PortTypes.String, "Subject");
            this.MailBody = typeService.CreateString(PortTypes.String, "Message Body");
            this.SmtpHost = typeService.CreateString(PortTypes.String, "SMTP Server");
            this.SmtpPort = typeService.CreateInt(PortTypes.Integer, "SMTP Port");
            //this.SmtpEncryption = typeService.CreateEnum("TecLabMailEncryption", "Encryption", EncryptionTypes.Values, EncryptionTypes.AUTO);
            this.SmtpUser = typeService.CreateString(PortTypes.String, "SMTP User");
            this.SmtpPassword = typeService.CreateString(PortTypes.String, "SMTP Password");
            this.SendRetries = typeService.CreateInt(PortTypes.Integer, "Retries", 10);
            // Initialize the output ports
            this.ErrorMessage = typeService.CreateString(PortTypes.String, "Error Message");
            this.ErrorStatus = typeService.CreateBool(PortTypes.Bool, "Error Status");
        }

        [Input(DisplayOrder = 1, IsInput = true, IsRequired = true)]
        public BoolValueObject SendTrigger { get; private set; }

        [Parameter(DisplayOrder = 2, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailTo { get; private set; }

        [Parameter(DisplayOrder = 3, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailFrom { get; private set; }

        [Input(DisplayOrder = 4, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailSubject { get; private set; }

        [Input(DisplayOrder = 5, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailBody { get; private set; }

        [Parameter(DisplayOrder = 6, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject SmtpHost { get; private set; }

        [Parameter(DisplayOrder = 7, InitOrder = 1, IsDefaultShown = false)]
        public IntValueObject SmtpPort { get; private set; }

        /*[Parameter(DisplayOrder = 8, InitOrder = 1, IsDefaultShown = false)]
        public EnumValueObject SmtpEncryption { get; private set; }*/

        [Parameter(DisplayOrder = 9, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject SmtpUser { get; private set; }

        [Parameter(DisplayOrder = 10, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject SmtpPassword { get; private set; }

        [Input(DisplayOrder = 11, InitOrder = 1, IsDefaultShown = false)]
        public IntValueObject SendRetries { get; private set; }

        [Output(DisplayOrder = 1)]
        public StringValueObject ErrorMessage { get; private set; }
        
        [Output(DisplayOrder = 2)]
        public BoolValueObject ErrorStatus { get; private set; }
        
        public override void Execute() {
            if (!this.SendTrigger.HasValue || !this.MailTo.HasValue || !this.MailFrom.HasValue || !this.SmtpHost.HasValue || !this.SmtpPort.HasValue /*|| !this.SmtpEncryption.HasValue*/
                || !this.SmtpUser.HasValue || !this.SmtpPassword.HasValue || !this.SendRetries.HasValue) {
                return;
            }
            if (!this.SendTrigger.Value || !this.SendTrigger.WasSet) return;

            SmtpClient client = new SmtpClient(this.SmtpHost.Value, this.SmtpPort);
            client.UseDefaultCredentials = false;
            client.Credentials = new System.Net.NetworkCredential(this.SmtpUser.Value, this.SmtpPassword);
            client.EnableSsl = true;

            MailAddress mailFrom = new MailAddress(this.MailFrom.Value, "X1", System.Text.Encoding.UTF8);
            MailAddress mailTo = new MailAddress(this.MailTo.Value, this.MailTo.Value, System.Text.Encoding.UTF8);

            MailMessage message = new MailMessage(mailFrom, mailTo);
            message.Body = this.MailBody.Value;
            message.BodyEncoding = System.Text.Encoding.UTF8;
            message.Subject = this.MailSubject.Value;
            message.SubjectEncoding = System.Text.Encoding.UTF8;

            // Set the method that is called back when the send operation ends.
            /*client.SendCompleted += new
            SendCompletedEventHandler(SendCompletedCallback);*/
            // The userState can be any object that allows your callback
            // method to identify this send operation.
            // For this example, the userToken is a string constant.
            //string userState = "test message1";
            //client.Send(message);

            // Create Mime Message
            /*MimeMessage message = new MimeMessage();
            message.From.Add(new MailboxAddress("X1", this.MailFrom.Value));
            message.To.Add(new MailboxAddress(this.MailTo.Value, this.MailTo.Value));
            if (this.MailSubject.HasValue) {
                message.Subject = this.MailSubject.Value;
            } else {
                message.Subject = "";
            }
            if (this.MailBody.HasValue) {
                message.Body = new TextPart("plain") { Text = this.MailBody.Value };
            } else {
                message.Body = new TextPart("plain") { Text = "" };
            }

            // Configure the security
            SecureSocketOptions socketOptions;
            switch (this.SmtpEncryption.Value) {
                case EncryptionTypes.SSL:
                    socketOptions = SecureSocketOptions.SslOnConnect;
                    break;
                case EncryptionTypes.STARTTLS:
                    socketOptions = SecureSocketOptions.StartTls;
                    break;
                case EncryptionTypes.NONE:
                    socketOptions = SecureSocketOptions.None;
                    break;
                default:
                    socketOptions = SecureSocketOptions.Auto;
                    break;
            }*/

            // Schedule as async task ...
            try {
                // MailKit Version
                //Task task = Task.Run(() => SendMessageAsync(this, message, socketOptions, this.SmtpHost.Value, this.SmtpPort.Value, this.SmtpUser.Value, this.SmtpPassword.Value, this.SendRetries.Value));
                // .NET SmtpClient Version
                Task task = Task.Run(() => SendMessageAsync(this, client, message, this.SendRetries.Value));
            } catch (Exception ex) {
                this.MailError(ex.Message);
            }
        }

        public void MailError(String message) {
            this.ErrorMessage.Value = message;
            this.ErrorStatus.Value = true;
        }
        
        public void MailSuccess() {
            this.ErrorMessage.Value = "";
            this.ErrorStatus.Value = false;
        }

        static void SendMessageAsync(SendMail parent, SmtpClient client, MailMessage message, int sendRetries) {
            // Send until success
            Boolean sendSuccess = false;
            while (sendRetries > 1) {
                try {
                    client.Send(message);
                    sendSuccess = true;
                    break;
                } catch (Exception) {
                    // Sleep for one minute
                    Thread.Sleep(60000);
                }
                sendRetries -= 1;
            }

            // Cleanup if we succeeded on first tries
            if (sendSuccess) {
                client.Dispose();
                message.Dispose();
                parent.MailSuccess();
                return;
            }

            // Final try
            try {
                client.Send(message);
                parent.MailSuccess();
            } catch (Exception ex) {
                parent.MailError(ex.Message);
            }

            // Dispose
            client.Dispose();
            message.Dispose();
        }

        /*static void SendMessageAsync(SendMail parent, MimeMessage message, SecureSocketOptions socketOptions,
                String smtpHost, int smtpPort, String smtpUser, string smtpPassword, int sendRetries) {
            // Connect
            var client = new SmtpClient();
            try {
                client.Connect(smtpHost, smtpPort, socketOptions);
            } catch (SmtpCommandException ex) {
                parent.MailError("Error trying to connect: " + ex.Message);
                return;
            } catch (SmtpProtocolException ex) {
                parent.MailError("Protocol error while trying to connect: " + ex.Message);
                return;
            }

            // Authenticate
            if (client.Capabilities.HasFlag(SmtpCapabilities.Authentication)) {
                try {
                    client.Authenticate(smtpUser, smtpPassword);
                } catch (AuthenticationException ex) {
                    client.Disconnect(true);
                    parent.MailError("Invalid user name or password: " + ex.Message);
                    return;
                } catch (SmtpCommandException ex) {
                    client.Disconnect(true);
                    parent.MailError("Error trying to authenticate: " + ex.Message);
                    return;
                } catch (SmtpProtocolException ex) {
                    client.Disconnect(true);
                    parent.MailError("Protocol error while trying to authenticate: " + ex.Message);
                    return;
                }
            }

            // Send until success
            while (sendRetries > 1) {
                try {
                    client.Send(message);
                    client.Disconnect(true);
                    parent.MailSuccess();
                    return;
                } catch (SmtpCommandException) {
                    // Sleep for one minute
                    Thread.Sleep(60000);
                }
                sendRetries -= 1;
            }

            // Send
            try {
                client.Send(message);
            } catch (SmtpCommandException ex) {
                client.Disconnect(true);
                parent.MailError("Error sending message: " + ex.Message);
                return;
            } catch (SmtpProtocolException ex) {
                client.Disconnect(true);
                parent.MailError("Protocol error while sending message: " + ex.Message);
                return;
            }

            // Disconnect
            try {
                client.Disconnect(true);
            } catch (Exception) {
                // Shall not generate any error
            }

            // All good
            parent.MailSuccess();
        }*/

        /// <summary>
        /// By default this function gets the translation for the node's in- and output from the <see cref="LogicNodeBase.ResourceManager"/>.
        /// A resource file with translation is required for this to work.
        /// </summary>
        /// <param name="language">The requested language, for example "en" or "de".</param>
        /// <param name="key">The key to translate.</param>
        /// <returns>The translation of <paramref name="key"/> in the requested language, or <paramref name="key"/> if the translation is missing.</returns>
        /*public override string Localize(String language, String key) {
            return base.Localize(language, key);
        }*/
    }
}

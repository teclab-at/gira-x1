using LogicModule.Nodes.Helpers;
using LogicModule.ObjectModel;
using LogicModule.ObjectModel.TypeSystem;
using System;
using MailKit.Security;
using MailKit.Net.Smtp;
using MimeKit;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace teclab_at.logic {

    static class EncryptionTypes {
        public const string NONE = "None";
        public const string AUTO = "Auto";
        public const string SSL = "SSL";
        public const string STARTTLS = "STARTTLS";
        public static string[] Values = new[] {NONE, AUTO, SSL, STARTTLS};
    }

    public class SendMail : LogicNodeBase {
        [Input(DisplayOrder = 1, IsInput = true, IsRequired = true)]
        public BoolValueObject SendTrigger { get; private set; }

        [Parameter(DisplayOrder = 2, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailTo { get; private set; }

        [Parameter(DisplayOrder = 3, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailFrom { get; private set; }

        [Parameter(DisplayOrder = 4, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject SmtpHost { get; private set; }

        [Parameter(DisplayOrder = 5, InitOrder = 1, IsDefaultShown = false)]
        public IntValueObject SmtpPort { get; private set; }

        [Parameter(DisplayOrder = 6, InitOrder = 1, IsDefaultShown = false)]
        public EnumValueObject SmtpEncryption { get; private set; }

        [Parameter(DisplayOrder = 7, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject SmtpUser { get; private set; }

        [Parameter(DisplayOrder = 8, InitOrder = 1, IsDefaultShown = false, IsRequired = false)]
        public StringValueObject SmtpPassword { get; private set; }

        [Input(DisplayOrder = 9, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailSubject { get; private set; }
        
        [Input(DisplayOrder = 10, InitOrder = 1, IsDefaultShown = false)]
        public StringValueObject MailBody { get; private set; }

        [Input(DisplayOrder = 11, InitOrder = 1, IsDefaultShown = false)]
        public IntValueObject SendRetries { get; private set; }

        [Output(DisplayOrder = 1)]
        public StringValueObject ErrorMessage { get; private set; }
        
        [Output(DisplayOrder = 2)]
        public BoolValueObject ErrorStatus { get; private set; }
        
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
            this.SmtpEncryption = typeService.CreateEnum("TecLabMailEncryption", "Encryption", EncryptionTypes.Values, EncryptionTypes.AUTO);
            this.SmtpUser = typeService.CreateString(PortTypes.String, "SMTP User");
            this.SmtpPassword = typeService.CreateString(PortTypes.String, "SMTP Password");
            this.SendRetries = typeService.CreateInt(PortTypes.Integer, "Retries", 10);
            // Initialize the output ports
            this.ErrorMessage = typeService.CreateString(PortTypes.String, "Error Message");
            this.ErrorStatus = typeService.CreateBool(PortTypes.Bool, "Error Status");
}

        public override void Startup() {
            // Nothing to do
        }

        public override void Execute() {
            if (!SendTrigger.HasValue || !MailTo.HasValue || !MailFrom.HasValue || !SmtpHost.HasValue || !SmtpPort.HasValue || !SmtpEncryption.HasValue
                || !SmtpUser.HasValue || !SmtpPassword.HasValue || !SendRetries.HasValue) {
                return;
            }
            if (!SendTrigger.Value || !SendTrigger.WasSet) return;

            // TODO: schedule as async task ...
            try {
                Task task = Task.Run(() => SendMessageAsync(this, MailFrom, MailTo, MailSubject, MailBody, SmtpEncryption, SmtpHost, SmtpPort, SmtpUser, SmtpPassword, SendRetries));
            } catch (Exception ex) {
                this.ErrorMessage.Value = ex.ToString();
                this.ErrorStatus.Value = true;
            }
        }

        public void MessageEvent(object sender, EventArgs e) {
            SmtpClient client = (SmtpClient)sender;
            client.Disconnect(true);
        }

        public void MailError(string Message) {
            ErrorMessage.Value = Message;
            ErrorStatus.Value = true;
        }
        
        public void MailSuccess() {
            ErrorMessage.Value = "";
            ErrorStatus.Value = false;
        }

          static void SendMessageAsync(SendMail Parent, StringValueObject MailFrom, StringValueObject MailTo, StringValueObject MailSubject, StringValueObject MailBody, EnumValueObject SmtpEncryption,
                StringValueObject SmtpHost, IntValueObject SmtpPort, StringValueObject SmtpUser, StringValueObject SmtpPassword, IntValueObject SendRetries) {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("X1", MailFrom.Value));
            message.To.Add(new MailboxAddress(MailTo.Value, MailTo.Value));
            if (MailSubject.HasValue) {
                message.Subject = MailSubject.Value;
            } else {
                message.Subject = "";
            }

            if (MailBody.HasValue) {
                message.Body = new TextPart("plain") { Text = MailBody.Value };
            } else {
                message.Body = new TextPart("plain") { Text = "" };
            }

            var client = new SmtpClient();
            SecureSocketOptions socketOptions;
            switch (SmtpEncryption.Value) {
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
            }

            // Connect
            try {
                client.Connect(SmtpHost.Value, SmtpPort.Value, socketOptions);
            } catch (SmtpCommandException ex) {
                Parent.MailError("Error trying to connect: " + ex.Message);
                return;
            } catch (SmtpProtocolException ex) {
                Parent.MailError("Protocol error while trying to connect: " + ex.Message);
                return;
            }

            // Authenticate
            if (client.Capabilities.HasFlag(SmtpCapabilities.Authentication)) {
                try {
                    client.Authenticate(SmtpUser.Value, SmtpPassword.Value);
                } catch (AuthenticationException ex) {
                    client.Disconnect(true);
                    Parent.MailError("Invalid user name or password: " + ex.Message);
                    return;
                } catch (SmtpCommandException ex) {
                    client.Disconnect(true);
                    Parent.MailError("Error trying to authenticate: " + ex.Message);
                    return;
                } catch (SmtpProtocolException ex) {
                    client.Disconnect(true);
                    Parent.MailError("Protocol error while trying to authenticate: " + ex.Message);
                    return;
                }
            }

            // Send until success
            while(SendRetries.Value > 1) {
                try {
                    client.Send(message);
                    client.Disconnect(true);
                    Parent.MailSuccess();
                    return;
                } catch (SmtpCommandException) {
                    // Sleep for one minute
                    Thread.Sleep(60000);
                }
                SendRetries.Value -= 1;
            }

            // Send
            try {
                client.Send(message);
            } catch (SmtpCommandException ex) {
                client.Disconnect(true);
                Parent.MailError("Error sending message: " + ex.Message);
                return;
                // Keep specific SMTP error codes for reference
                /*
                switch (ex.ErrorCode) {
                    case SmtpErrorCode.RecipientNotAccepted:
                        Console.WriteLine("\tRecipient not accepted: {0}", ex.Mailbox);
                        break;
                    case SmtpErrorCode.SenderNotAccepted:
                        Console.WriteLine("\tSender not accepted: {0}", ex.Mailbox);
                        break;
                    case SmtpErrorCode.MessageNotAccepted:
                        Console.WriteLine("\tMessage not accepted.");
                        break;
                }*/
            } catch (SmtpProtocolException ex) {
                client.Disconnect(true);
                Parent.MailError("Protocol error while sending message: " + ex.Message);
                return;
            }

            // Disconnect
            try {
                client.Disconnect(true);
            } catch (Exception) {
                // Shall not generate any error
            }

            // All good
            Parent.MailSuccess();
        }

        public override ValidationResult Validate(string language) {
            return base.Validate(language);
        }

        /// <summary>
        /// By default this function gets the translation for the node's in- and output from the <see cref="LogicNodeBase.ResourceManager"/>.
        /// A resource file with translation is required for this to work.
        /// </summary>
        /// <param name="language">The requested language, for example "en" or "de".</param>
        /// <param name="key">The key to translate.</param>
        /// <returns>The translation of <paramref name="key"/> in the requested language, or <paramref name="key"/> if the translation is missing.</returns>
        public override string Localize(string language, string key) {
            return base.Localize(language, key);
        }
    }
}

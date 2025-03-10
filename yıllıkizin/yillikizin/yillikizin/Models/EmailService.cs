﻿using System;
using System.IO;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

public class EmailService : IDisposable
{
    private const string SmtpServer = "smtp.turkticaret.net";
    private const int SmtpPort = 465;  // SSL portu
    private const string Email = "destek@teknolojitek.com.tr";
    private const string Password = "Sucorap123."; // Parolanızı buraya yazın.

    // Asenkron e-posta göndermek için SMTP kullanma
    public async Task SendEmailAsync(string recipientEmail, string subject, string body)
    {
        try
        {
            var message = new MimeMessage();

            // Gönderen adres
            message.From.Add(new MailboxAddress("Teknoloji Tek | RAPOR", Email));
            // Alıcı adres
            message.To.Add(new MailboxAddress("", recipientEmail));
            // Konu
            message.Subject = subject;

            // Gövde
            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = body
            };
            message.Body = bodyBuilder.ToMessageBody();

            using (var smtp = new SmtpClient())
            {
                await smtp.ConnectAsync(SmtpServer, SmtpPort, SecureSocketOptions.SslOnConnect);
                await smtp.AuthenticateAsync(Email, Password);
                await smtp.SendAsync(message);
                await smtp.DisconnectAsync(true);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"E-posta gönderimi başarısız oldu: {ex.Message}");
        }
    }

    // Asenkron PDF eklentisiyle e-posta göndermek için metot
    public async Task SendEmailWithAttachmentAsync(string recipientEmail, string subject, string body, Stream attachmentStream, string attachmentName)
    {
        try
        {
            using (var message = new MimeMessage())
            {
                message.From.Add(new MailboxAddress("Teknoloji Tek | RAPOR", Email));
                message.To.Add(new MailboxAddress("", recipientEmail.Trim()));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder
                {
                    HtmlBody = $@"
                        <html>
                            <body>
                                <h2>Teknoloji Tek Rapor</h2>
                                <p>{body}</p>
                                <p>Saygılarımızla,<br>Teknoloji Tek</p>
                            </body>
                        </html>"
                };

                // PDF dosyasını kopyala
                using (var ms = new MemoryStream())
                {
                    await attachmentStream.CopyToAsync(ms);
                    ms.Position = 0;
                    bodyBuilder.Attachments.Add(attachmentName, ms);
                }

                message.Body = bodyBuilder.ToMessageBody();

                using (var smtp = new SmtpClient())
                {
                    smtp.ServerCertificateValidationCallback = (s, c, h, e) => true;
                    await smtp.ConnectAsync(SmtpServer, SmtpPort, SecureSocketOptions.SslOnConnect);
                    await smtp.AuthenticateAsync(Email, Password);
                    await smtp.SendAsync(message);
                    await smtp.DisconnectAsync(true);
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"E-posta gönderimi başarısız oldu: {ex.Message}", ex);
        }
    }
    public void Dispose()
    {
        // Dispose işlemleri burada yapılabilir. 
        // Ancak, MailKit.SmtpClient sınıfı, "using" bloğu ile otomatik olarak dispose edilir.
        // Dolayısıyla burada ek bir işlem gerekmeyebilir.
    }
}
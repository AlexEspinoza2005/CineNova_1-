using SendGrid;
using SendGrid.Helpers.Mail;

namespace MovieApi.Services
{
    public interface ISendGridEmailService
    {
        Task<bool> SendEmailAsync(string toEmail, string subject, string plainTextContent, string htmlContent);
    }

    public class SendGridEmailService : ISendGridEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SendGridEmailService> _logger;

        public SendGridEmailService(IConfiguration configuration, ILogger<SendGridEmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string plainTextContent, string htmlContent)
        {
            try
            {
                var apiKey = _configuration["SendGridSettings:ApiKey"];
                var fromEmail = _configuration["SendGridSettings:FromEmail"];
                var fromName = _configuration["SendGridSettings:FromName"];

                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogError("SendGrid ApiKey is missing in configuration.");
                    return false;
                }

                var client = new SendGridClient(apiKey);
                var from = new EmailAddress(fromEmail, fromName);
                var to = new EmailAddress(toEmail);
                var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
                
                var response = await client.SendEmailAsync(msg);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Email sent successfully to {toEmail}");
                    return true;
                }
                else
                {
                    var body = await response.Body.ReadAsStringAsync();
                    _logger.LogError($"Failed to send email to {toEmail}. Status Code: {response.StatusCode}, Body: {body}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception occurred while sending email to {toEmail}");
                return false;
            }
        }
    }
}

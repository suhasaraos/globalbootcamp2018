using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;
using SendGrid.Helpers.Mail;

namespace VSSample
{
    public static class ValidateAndCopyFiles
    {
        const string SearchText = "Contoso"; //Simple Validation Rule

        [FunctionName("ValidateAndCopyFiles")]
        public static async Task<bool> Run(
            [OrchestrationTrigger] DurableOrchestrationContext context,
            TraceWriter log)
        {
            log.Info("ValidateAndCopyFiles Orchestrator called");

            string fileName = context.GetInput<string>();
            if (string.IsNullOrEmpty(fileName))
            {
                throw new ArgumentNullException(
                    nameof(fileName),
                    "Filename is missing.");
            }

            bool isfileValid = await context.CallActivityAsync<bool>("ValidateFile", fileName);
            if (isfileValid)
            {
                await context.CallActivityAsync<bool>("CopyFile", fileName);
            }
            else
            {
                string emailMessage = $"{fileName} doesn't meet the quality guidelines.Instance Id = {context.InstanceId}";
                int authCode = await context.CallActivityAsync<int>("SendEmail", emailMessage);

                using (var timeoutCts = new CancellationTokenSource())
                {
                    // The user has 2 minutes to approve for an exception
                    DateTime expiration = context.CurrentUtcDateTime.AddMinutes(2);
                    Task timeoutTask = context.CreateTimer(expiration, timeoutCts.Token);

                    bool approved = false;

                    // Wait for an external event "Approval"
                    Task<int> approvalResponseTask =
                        context.WaitForExternalEvent<int>("Approval");

                    Task winner = await Task.WhenAny(approvalResponseTask, timeoutTask);
                    if (winner == approvalResponseTask)
                    {
                        // We got back a response! Compare it to the auth code to verify authenticity.
                        if (approvalResponseTask.Result == authCode)
                        {
                            approved = true;
                            await context.CallActivityAsync<bool>("CopyFile", fileName);
                        }
                    }
                    else
                    {
                        // Timeout expired, send email that file was not approved
                        string timeoutMessage = $"{fileName} not approved in time since and " +
                            $"it doesn't meet the quality guidelines.";
                        await context.CallActivityAsync<int>("SendEmail", timeoutMessage);
                    }

                    if (!timeoutTask.IsCompleted)
                    {
                        // All pending timers must be complete or canceled before the function exits.
                        timeoutCts.Cancel();
                    }

                    return approved;
                }

            }

            return true;
        }

        [FunctionName("ValidateFile")]
        public static async Task<bool> ValidateFile(
           [ActivityTrigger] string filePath,           
           TraceWriter log)
        {          
            string fileText = string.Empty;

            using (var reader = File.OpenText(filePath))
            {
                fileText = await reader.ReadToEndAsync();                
            }

            log.Info($"Validating '{filePath}'");

            return fileText.Contains(SearchText);
        }

        [FunctionName("CopyFile")]
        public static async Task<bool> CopyFiletoBlob(
           [ActivityTrigger] string filePath,
           Binder binder,
           TraceWriter log)
        {
            // strip the drive letter prefix and convert to forward slashes
            string blobPath = filePath
                .Substring(Path.GetPathRoot(filePath).Length)
                .Replace('\\', '/');

            log.Info($"Copying '{filePath}' to blob location");
            
            string outputLocation = $"validatedfiles/{blobPath}";

            using (Stream source = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (Stream destination = await binder.BindAsync<CloudBlobStream>(
                new BlobAttribute(outputLocation, FileAccess.Write)))
            {
                await source.CopyToAsync(destination);
            }

            return true;
        }        

        [FunctionName("SendEmail")]
        public static int SendEmail(
            [ActivityTrigger] string emailmessage,
            [SendGrid(ApiKey = "SendGridAttributeApiKey")] out SendGridMessage message,
            TraceWriter log)
        {
            int authCode = getAuthCode();
            if (!emailmessage.Contains("approved in time")) //Little hack since two diff emails are sent out
                emailmessage = emailmessage + $"Use the code {authCode} to provide an exception.";

            message = new SendGridMessage();
            message.AddTo("suhasa.rao@microsoft.com");
            message.AddContent("text/html", emailmessage);
            message.SetFrom(new EmailAddress("suhasaraos@gmail.com"));
            message.SetSubject("File doesn't conform to standard");
                   
            //Send an email approval on an exception
            log.Info($"Sending email with message {emailmessage}");

            return authCode;
        }

        private static int getAuthCode()
        {
            // Get a random number generator with a random seed (not time-based)
            var rand = new Random(Guid.NewGuid().GetHashCode());
            return rand.Next(10000);
        }
    }
}
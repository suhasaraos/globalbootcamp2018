using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;

namespace FunctionApp4
{
    public static class ValidateSiteContent
    {
        [FunctionName("ValidateSiteContent")]
        public static async Task<bool> Run(
            [OrchestrationTrigger] DurableOrchestrationContext context, 
            TraceWriter log)
        {
            log.Info("ValidateSiteContent Orchestrator called");
            
            string rootDirectory = context.GetInput<string>()?.Trim();
            if (string.IsNullOrEmpty(rootDirectory))
            {
                throw new ArgumentNullException(
                    nameof(rootDirectory),
                    "A directory path input is required.");
            }

            string[] files = await context.CallActivityAsync<string[]>(
                "GetFileList",
                rootDirectory);

            var tasks = new Task[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                tasks[i] = context.CallSubOrchestratorAsync("ValidateAndCopyFiles", files[i]);
            }

            await Task.WhenAll(tasks);            
            return true;
        }

        [FunctionName("GetFileList")]
        public static string[] GetFileList(
            [ActivityTrigger] string rootDirectory,
            TraceWriter log)
        {
            log.Info($"Searching for files under '{rootDirectory}'...");
            string[] files = Directory.GetFiles(rootDirectory, "*", SearchOption.AllDirectories);
            log.Info($"Found {files.Length} file(s) under {rootDirectory}.");

            return files;
        }       
    }
}

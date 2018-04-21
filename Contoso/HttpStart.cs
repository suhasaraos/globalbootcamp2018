using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;

namespace FunctionApp4
{
    public static class HttpStart
    {
        [FunctionName("HttpStart")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orchestrators/{functionName}")]HttpRequestMessage req, 
            [OrchestrationClient] DurableOrchestrationClient client,
            string functionName,
            TraceWriter log)
        {
                       
            log.Info("C# HTTP trigger function processed a request.");
                        
            // Get request body
            string data = await req.Content.ReadAsStringAsync();

            var clientId = await client.StartNewAsync(functionName, data);

            log.Info($"Started orchestration '{clientId}'");

            return client.CreateCheckStatusResponse(req, clientId);
            
        }
    }
}

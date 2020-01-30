using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace VideoClip
{
    public static class clippingFunc
    {
        private static Settings settings;

        [FunctionName("clippingFunc")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log, ExecutionContext context)
        {
            try
            {
                GetSettings(context, log);

                log.LogInformation("C# HTTP trigger function processed a request.");
            
                var psi = new ProcessStartInfo();
                //Set Executable location
                psi.FileName = settings.ffmpegPath;
                log.LogInformation(psi.FileName);

                //Replace the following with preferred output file name
                string filename = string.Concat(System.Guid.NewGuid().ToString(),".mp4");
                
                //Set output file name
                string outfile = Path.Combine(settings.outputPath, filename);
                log.LogInformation(outfile);

                //Set Frames
                int inFrame = 100;
                int outFrame = 600;

                //Set Arguments including the outfile path
                psi.Arguments = $"-i \"https://aka.ms/justworkbro\" -ss {inFrame.ToString()} -frames {(outFrame-inFrame).ToString()} {outfile}";

                psi.UseShellExecute = false;

                if (settings.verboseFFMPEGLogging)
                {
                    psi.RedirectStandardError = true;
                    psi.RedirectStandardOutput = true;
                }

                log.LogInformation($"Args: {psi.Arguments}");
                log.LogInformation("Start exe");
                var process = Process.Start(psi);

                if (settings.verboseFFMPEGLogging)
                {
                    log.LogInformation(process.StandardError.ReadToEnd());
                    log.LogInformation(process.StandardOutput.ReadToEnd());
                }

                process.WaitForExit();
                log.LogInformation("Completed ffmpeg write");

                SaveVideoToBlobContainer(filename, outfile, log);
                
                return (ActionResult)new OkObjectResult("Complete");
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
                return (ActionResult)new BadRequestResult();
            }
        }

        static async void SaveVideoToBlobContainer(string fileName, string filePath, ILogger log)
        {
            try
            {
                BlobServiceClient blobServiceClient = new BlobServiceClient(settings.outputStorageAccountConnStr);
                BlobContainerClient blobContainerClient = blobServiceClient.GetBlobContainerClient(settings.blobContainerName);

                BlobClient blobClient = blobContainerClient.GetBlobClient(fileName);

                FileStream uploadFileStream = File.OpenRead(filePath);
                await blobClient.UploadAsync(uploadFileStream);
                uploadFileStream.Close();
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
            }

        }

        static void GetSettings(ExecutionContext context, ILogger log)
        {
            try
            {

                var config = new ConfigurationBuilder()
                    .SetBasePath(context.FunctionAppDirectory)
                    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();

                settings = new Settings();

                //Check for user specified ffmpeg path or use default
                //Needs to execute from the home\site\wwwroot folder in Azure Functions
                if (String.IsNullOrEmpty(config["ffmpegPath"]))
                {
                    settings.ffmpegPath = Path.Combine(@"D:\home\site\wwwroot", "ffmpeg.exe");
                }
                else
                {
                    settings.ffmpegPath = config["ffmpegPath"];
                }

                //Check for user specified output path or use default
                //Must write to the Azure Functions temp directory. GetTempPath will get that for you
                if (String.IsNullOrEmpty(config["outputPath"]))
                {
                    settings.outputPath = Path.GetTempPath();
                }
                else
                {
                    settings.outputPath = config["outputPath"];
                }

                //Assume verbose logging is off unless set to true in config
                settings.verboseFFMPEGLogging = false;
                if (!String.IsNullOrEmpty(config["verboseFFMPEGLogging"]))
                {
                    if (config["verboseFFMPEGLogging"] == "true")
                    {
                        settings.verboseFFMPEGLogging = true;
                    }
                }

                //Get connection string for output storage account
                settings.outputStorageAccountConnStr = config["outputStorageAccountConnStr"];

                settings.blobContainerName = "outputvideos";
                if (!String.IsNullOrEmpty(config["blobContainerName"]))
                {
                        settings.blobContainerName = config["blobContainerName"];
                }
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
            }
        }

    }

    class Settings
    {
        public string ffmpegPath { get; set; }
        public string outputPath { get; set; }
        public bool verboseFFMPEGLogging { get; set; }
        public string outputStorageAccountConnStr { get; set; }
        public string blobContainerName { get; set; }

    }
}

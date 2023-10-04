// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

// Learn how to locally debug an Event Grid-triggered function:
//    https://aka.ms/AA30pjh

// Use for local testing:
//   https://{ID}.ngrok.io/runtime/webhooks/EventGrid?functionName=Thumbnail

using Azure.Storage.Blobs;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace ImageFunctions
{
    public static class Thumbnail
    {        
        private static readonly string BLOB_STORAGE_CONNECTION_STRING = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        private static string GetBlobNameFromUrl(string bloblUrl)
        {
            var uri = new Uri(bloblUrl);
            var blobClient = new BlobClient(uri);
            return blobClient.Name;
        }

        private static IImageEncoder GetEncoder(string extension)
        {
            IImageEncoder encoder = null;

            extension = extension.Replace(".", "");

            var isSupported = Regex.IsMatch(extension, "gif|png|jpe?g", RegexOptions.IgnoreCase);

            if (isSupported)
            {
                switch (extension.ToLower())
                {
                    case "png":
                        encoder = new PngEncoder();
                        break;
                    case "jpg":
                        encoder = new JpegEncoder();
                        break;
                    case "jpeg":
                        encoder = new JpegEncoder();
                        break;
                    case "gif":
                        encoder = new GifEncoder();
                        break;
                    default:
                        break;
                }
            }

            return encoder;
        }

        [FunctionName("Thumbnail")]
        public static async Task Run(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [Blob("{data.url}", FileAccess.Read)] Stream input,
            ILogger log)
        {
            try
            {
                if (input != null)
                {
                    var createdEvent = ((JObject)eventGridEvent.Data).ToObject<StorageBlobCreatedEventData>();
                    var extension = Path.GetExtension(createdEvent.Url);
                    var encoder = GetEncoder(extension);

                    if (encoder != null)
                    {
                        var thumbnailWidth = Convert.ToInt32(Environment.GetEnvironmentVariable("THUMBNAIL_WIDTH"));

                        var thumbContainerName = Environment.GetEnvironmentVariable("THUMBNAIL_CONTAINER_NAME");
                        var blobServiceClient = new BlobServiceClient(BLOB_STORAGE_CONNECTION_STRING);
                        var blobContainerClient = blobServiceClient.GetBlobContainerClient(thumbContainerName);
                        var blobName = GetBlobNameFromUrl(createdEvent.Url);
                        Uri u = new Uri(createdEvent.Url.ToString());
                        string suri = u.AbsolutePath;
                        log.LogInformation($"blobName : {blobName}");
                        log.LogInformation($"AbsolutePath : {suri}");

                        string filename = blobName.Split('/')[blobName.Split('/').Length - 1];
                        string sPath = suri.Replace(filename, "w" + thumbnailWidth + "/" + filename).Replace("/" + u.AbsoluteUri.Split('/')[3] + "/", "");
                        log.LogInformation($"sPath : {sPath}");
                        log.LogInformation($"THUMBNAIL_WIDTH : {thumbnailWidth}");

                        BlobClient bc = blobContainerClient.GetBlobClient(sPath);

                        //Image<Rgba32> image2;
                        using (MemoryStream output= new MemoryStream())
                        {
                            using (Image image = Image.Load(input))
                            {  
                                //image2 = image.Clone();
                                try
                                {
                                    image.Mutate(x => x.AutoOrient());

                                    if (image.Width > thumbnailWidth)
                                    {
                                        var divisor = (decimal)image.Width / (decimal)thumbnailWidth;
                                        var height = Convert.ToInt32(Math.Round((decimal)(image.Height / divisor)));

                                        image.Mutate(x => x.AutoOrient().Resize(thumbnailWidth, height));
                                        image.Save(output, encoder);
                                        output.Position = 0;
                                        await bc.UploadAsync(output, true);
                                    }
                                    else //가로 크기가 썸내일보다 작으면 원본 업로드
                                    {
                                        image.Save(output, encoder);
                                        output.Position = 0;
                                        await bc.UploadAsync(output, true);
                                    }
                                }
                                catch(DivideByZeroException zex)
                                {
                                    log.LogInformation($"DivideByZeroException for: {zex}");
                                    
                                    image.Save(output, encoder);
                                    output.Position = 0;

                                    await bc.UploadAsync(output, true);
                                }
                            }
                        }
                    }
                    else
                    {
                        log.LogInformation($"No encoder support for: {createdEvent.Url}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
                throw;
            }
        }

        public static async Task saveImage(Stream input, int width, BlobClient bc, IImageEncoder encoder)
        {
            using (MemoryStream output = new MemoryStream())
            using (Image image = Image.Load(input))
            {
                var divisor = image.Width / width;
                var height = Convert.ToInt32(Math.Round((decimal)(image.Height / divisor)));

                image.Mutate(x => x.Resize(width, height));
                image.Save(output, encoder);
                output.Position = 0;

                //await blobContainerClient.UploadBlobAsync(blobName, output);
                await bc.UploadAsync(output, true);
            }

        }
    }
}

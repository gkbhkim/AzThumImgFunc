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
using System.Text;
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
                        var thumbnailWidth1 = Convert.ToInt32(Environment.GetEnvironmentVariable("THUMBNAIL_WIDTH1"));
                        var thumbnailWidth2 = Convert.ToInt32(Environment.GetEnvironmentVariable("THUMBNAIL_WIDTH2"));

                        var thumbContainerName = Environment.GetEnvironmentVariable("THUMBNAIL_CONTAINER_NAME");
                        var blobServiceClient = new BlobServiceClient(BLOB_STORAGE_CONNECTION_STRING);
                        var blobContainerClient = blobServiceClient.GetBlobContainerClient(thumbContainerName);
                        var blobName = GetBlobNameFromUrl(createdEvent.Url);
                        Uri u = new Uri(createdEvent.Url.ToString());
                        string suri = u.AbsolutePath;
                        log.LogInformation($"blobName : {blobName}");
                        log.LogInformation($"AbsolutePath : {suri}");

                        string filename = blobName.Split('/')[blobName.Split('/').Length - 1];
                        string sPath1 = suri.Replace(filename, "w" + thumbnailWidth1 + "/" + filename).Replace("/" + u.AbsoluteUri.Split('/')[3] + "/", "");
                        string sPath2 = suri.Replace(filename, "w" + thumbnailWidth2 + "/" + filename).Replace("/" + u.AbsoluteUri.Split('/')[3] + "/", "");

                        log.LogInformation($"sPath1 : {sPath1}");
                        log.LogInformation($"sPath2 : {sPath2}");
                        log.LogInformation($"THUMBNAIL_WIDTH1 : {thumbnailWidth1}");
                        log.LogInformation($"THUMBNAIL_WIDTH2 : {thumbnailWidth2}");

                        BlobClient bc1 = blobContainerClient.GetBlobClient(sPath1);
                        BlobClient bc2 = blobContainerClient.GetBlobClient(sPath2);

                        Image<Rgba32> image2;
                        using (MemoryStream output1 = new MemoryStream())
                        {
                            using (Image<Rgba32> image1 = Image.Load(input))
                            {
                                image2 = image1.Clone();
                                var divisor1 = image1.Width / thumbnailWidth1;
                                var height1 = Convert.ToInt32(Math.Round((decimal)(image1.Height / divisor1)));

                                image1.Mutate(x => x.Resize(thumbnailWidth1, height1));
                                image1.Save(output1, encoder);
                                output1.Position = 0;

                                //await blobContainerClient.UploadBlobAsync(blobName, output);
                                await bc1.UploadAsync(output1, true);
                            }
                        }

                        if (image2 != null)
                        {
                            using (MemoryStream output2 = new MemoryStream())
                            {
                                var divisor = image2.Width / thumbnailWidth2;
                                var height = Convert.ToInt32(Math.Round((decimal)(image2.Height / divisor)));

                                image2.Mutate(x => x.Resize(thumbnailWidth2, height));
                                image2.Save(output2, encoder);
                                output2.Position = 0;

                                //await blobContainerClient.UploadBlobAsync(blobName, output);
                                await bc2.UploadAsync(output2, true);
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
            using (Image<Rgba32> image = Image.Load(input))
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

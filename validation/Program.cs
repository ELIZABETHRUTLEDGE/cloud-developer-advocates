using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace AdvocateValidation
{
    class Program
    {
        readonly static HttpClient _client = new();

        readonly static string _advocatesPath =
#if DEBUG
            Path.Combine("../../../../", "advocates");
#else
            Path.Combine("../", "advocates");
#endif

        readonly static IDeserializer _yamlDeserializer = new DeserializerBuilder().Build();

        static async Task Main()
        {
            const string gitHub = "GitHub";

            var advocateList = new List<CloudAdvocateYamlModel>();

            var advocateFiles = Directory.GetFiles(_advocatesPath);

            await foreach (var (filePath, advocate) in GetAdvocateYmlFiles(advocateFiles).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(advocate?.Metadata.Alias))
                    throw new Exception($"Missing Microsoft Alias: {filePath}");

                if (string.IsNullOrWhiteSpace(advocate.Metadata.Team))
                    throw new Exception($"Missing Team: {filePath}");

                var gitHubUri = advocate.Connect.FirstOrDefault(x => x.Title.Equals(gitHub, StringComparison.OrdinalIgnoreCase))?.Url;

                EnsureValidUri(filePath, gitHubUri, gitHub);

                EnsureValidImage(filePath, advocate.Image);

                advocateList.Add(advocate);
            }

            var duplicateAliasList = advocateList.GroupBy(x => x.Metadata.Alias).Where(g => g.Count() > 1).Select(x => x.Key);
            foreach (var duplicateAlias in duplicateAliasList)
            {
                throw new Exception($"Duplicate Alias Found; ms.author: {duplicateAlias}");
            }

            Console.WriteLine("Validation Completed Successfully");
        }

        static async IAsyncEnumerable<(string filePath, CloudAdvocateYamlModel advocate)> GetAdvocateYmlFiles(IEnumerable<string> files)
        {
            var ymlFiles = files.Where(x => x.EndsWith(".yml", StringComparison.OrdinalIgnoreCase));

            foreach (var filePath in ymlFiles)
            {
                var text = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

                if (text.StartsWith("### YamlMime:Profile") && !text.StartsWith("### YamlMime:ProfileList"))
                {
                    Console.WriteLine($"Parsing {filePath}");
                    yield return (filePath, ParseAdvocateFromYaml(text));
                }
            }
        }

        static CloudAdvocateYamlModel ParseAdvocateFromYaml(in string fileText)
        {
            var stringReaderFile = new StringReader(fileText);

            return _yamlDeserializer.Deserialize<CloudAdvocateYamlModel>(stringReaderFile);
        }

        static async Task EnsureValidUri(string filePath, Uri? uri, string uriName)
        {
            if (uri is null)
                throw new Exception($"Missing {uriName} Url: {filePath}");

            if (!uri.IsWellFormedOriginalString())
                throw new Exception($"Invalid {uriName} Url: {filePath}");

            var response = await _client.GetAsync(uri).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode)
                throw new Exception($"Invalid {uriName} Url: {filePath}");
        }

        static void EnsureValidImage(in string filePath, in Image? cloudAdvocateImage)
        {
            if (cloudAdvocateImage is null)
                throw new Exception($"Image Source Missing: {filePath}");

            if (string.IsNullOrWhiteSpace(cloudAdvocateImage.Src))
                throw new Exception($"Image Source Missing: {filePath}");

            var filePathRelativeToValidation = Path.Combine(_advocatesPath, cloudAdvocateImage.Src);

            var fileStream = new FileStream(filePathRelativeToValidation, FileMode.Open);
            var binaryReader = new BinaryReader(fileStream, Encoding.UTF8);

            var imageSize = ImageService.GetDimensions(binaryReader);

            if (imageSize.Height <= 0)
                throw new Exception($"Invalid Image Height (must be greater than 0): {filePath}");

            if (imageSize.Width <= 0)
                throw new Exception($"Invalid Image Width (must be greater than 0): {filePath}");

            if (imageSize.Height != imageSize.Width)
                throw new Exception($"Invalid Image (Height and Width must be equal): {filePath}");

            if (cloudAdvocateImage.Alt is null)
                throw new Exception($"Image Alt Text Missing: {filePath}");
        }
    }
}
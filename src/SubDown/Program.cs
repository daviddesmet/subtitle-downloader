using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

using Colorful;
using Console = Colorful.Console;
//using ShellProgressBar;

namespace SubDown
{
    class Program
    {
#if DEBUG
        private const string API_URL = "http://sandbox.thesubdb.com/?action=";
#else
        // Docs: http://thesubdb.com/api/
        private const string API_URL = "http://api.thesubdb.com/?action=";
#endif

        private enum UrlActions
        {
            Languages,
            Search,
            Download,
            Upload
        }

        private static async Task Main(string[] args)
        {
            System.Console.BackgroundColor = ConsoleColor.Black;
            System.Console.ForegroundColor = ConsoleColor.Gray;

            Console.WriteAscii("SUB DOWNLOADER", Color.Orange);
            Console.WriteLine("A no hassle subtitle downloader for SubDB", Color.Orange);
            PrintLineBreak();

            var filePath = string.Empty;
            var processedByArg = false;

            if (args.Length > 0)
            {
                filePath = args[0];

                if (File.Exists(filePath))
                {
                    processedByArg = true;

                    PrintStatus("Got a movie file...");
                    PrintValue("MOVIE", filePath);

                    await ProcessMovieFile(filePath);

                    PrintLineBreak();
                    PrintExitMessage();
                }
                else
                {
                    PrintError("The received argument doesn't appear to be a file path, skipping...");
                }
            }

            if (processedByArg)
                return;

            Console.WriteLine("Do you know you can drag & drop a file on top of the exe to download a subtitle?", Color.Yellow);
            PrintLineBreak();

            var busy = true;
            while (busy)
            {
                var requiredMessage = "The '{0}' is required, program cannot continue...";

                filePath = GetInput("Type the movie's file path");
                if (!File.Exists(filePath))
                {
                    PrintError(requiredMessage, "file path");
                    break;
                }

                await ProcessMovieFile(filePath);

                PrintLineBreak();
                var another = GetInput("Do you want to download another subtitle? (yes / no)").ToLower();
                switch (another)
                {
                    case "y":
                    case "yes":
                        PrintStatus("Alright! Let's do one more...");
                        break;
                    case "n":
                    case "no":
                        PrintStatus("Sure! Let's call it a day...");
                        break;
                    default:
                        // ¯\_(ツ)_/¯
                        PrintStatus($"Me no entender... what do you mean by '{another}'?");
                        PrintStatus("I'll asume you just wanted to say no ;)");
                        break;
                }

                PrintLineBreak();
                busy = another == "y" || another == "yes";
            }

            PrintExitMessage();
        }

        /// <summary>
        /// Processes the movie file.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        private static async Task ProcessMovieFile(string filePath)
        {
            PrintLineBreak();
            var filmHash = CalculateFilmHash(filePath);

            PrintLineBreak();
            var subtitles = await GetAvailableSubtitles(filmHash);
            if (subtitles.Length == 0)
                return;

            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // TODO: SheelProgressBar overwrites the download messages printed to the console
            // https://github.com/Mpdreamz/shellprogressbar/issues/54
            //PrintLineBreak();
            //using var pbar = new ProgressBar(subtitles.Length, "downloading subtitles", new ProgressBarOptions
            //{
            //    ForegroundColor = ConsoleColor.Yellow,
            //    ForegroundColorDone = ConsoleColor.DarkGreen,
            //    BackgroundColor = ConsoleColor.DarkGray,
            //    BackgroundCharacter = '\u2593',
            //    CollapseWhenFinished = false
            //});
            foreach (var sub in subtitles)
            {
                PrintLineBreak();
                await DownloadSubtitle(Path.Combine(Path.GetDirectoryName(filePath), fileName), filmHash, sub);
                //pbar.Tick();
            }

            PrintLineBreak();
            PrintStatus("Download completed!");
        }

        /// <summary>
        /// Calculates the hash code which is composed by taking the first and last 64kb of the video file,
        /// putting all together and generating a MD5 of the resulting data (128kb).
        /// </summary>
        private static string CalculateFilmHash(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            PrintStatus("Reading movie file...");

            var readSize = 64 * 1024;
            var buffer = new byte[readSize * 2];

            using (var reader = new BinaryReader(new FileStream(filePath, FileMode.Open)))
            {
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
                reader.Read(buffer, 0, readSize);
                reader.BaseStream.Seek(-readSize, SeekOrigin.End);
                reader.Read(buffer, readSize, readSize);
            }

            return ComputeMD5HexHash(buffer);
        }

        /// <summary>
        /// Computes a MD5 hash and returns an hexadecimal representation.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns>System.String.</returns>
        private static string ComputeMD5HexHash(byte[] bytes)
        {
            if (bytes is null || bytes.Length == 0)
                throw new ArgumentNullException(nameof(bytes));

            PrintStatus("Calculating film hash...");

            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(bytes);
            var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();

            PrintValue("FILM HASH", hex);
            return hex;
        }

        /// <summary>
        /// Gets the available subtitles.
        /// </summary>
        /// <param name="filmHash">The film hash.</param>
        /// <returns>System.String[].</returns>
        private static async Task<string[]> GetAvailableSubtitles(string filmHash)
        {
            PrintStatus("Fetching subtitles...");

            var response = await GetRequest(CreateRequestUrl(UrlActions.Search, filmHash));
            if (response is null || !response.IsSuccessStatusCode)
            {
                PrintError("No subtitles found :(");
                return new string[0];
            }

            var subtitles = await response.Content.ReadAsStringAsync();

            PrintValue("AVAILABLE SUBTITLES", subtitles);
            return subtitles.Split(',');
        }

        /// <summary>
        /// Downloads the movie subtitle.
        /// </summary>
        /// <param name="filePath">The movie file path.</param>
        /// <param name="filmHash">The movie film hash.</param>
        /// <param name="language">The language.</param>
        /// <returns><c>true</c> if downloaded, <c>false</c> otherwise.</returns>
        private static async Task<bool> DownloadSubtitle(string filePath, string filmHash, string language)
        {
            PrintStatus($"Downloading '{language}' subtitle...");

            var response = await GetRequest(CreateRequestUrl(UrlActions.Download, filmHash, $"&language={language}"));
            if (response is null)
                return false;

            if (response.IsSuccessStatusCode)
            {
                //var availLangs = response.Content.Headers.ContentLanguage;
                var fileName = response.Content.Headers.ContentDisposition.FileName;

                var path = $"{filePath}-{language}{Path.GetExtension(fileName)}";
                var content = await response.Content.ReadAsStringAsync();
                
                try
                {
                    File.WriteAllText(path, content);

                    PrintValue("SUBTITLE PATH", path);
                    return true;
                }
                catch (Exception ex)
                {
                    PrintError(ex.Message);
                }
            }

            return false;
        }

        /// <summary>
        /// Append the action and hash generated to the API URL and return the request URL
        /// </summary>
        private static string CreateRequestUrl(UrlActions action, string filmHash, string param = "")
            => $"{API_URL}{action.ToString().ToLowerInvariant()}&hash={filmHash}{param}";

        /// <summary>
        /// Creates an HTTP request and returns the response.
        /// </summary>
        /// <param name="uri">The request URI.</param>
        /// <returns>HttpResponseMessage.</returns>
        private static async Task<HttpResponseMessage> GetRequest(string uri)
        {
            try
            {
                using var client = new HttpClient();
                const string protocolName = "SubDB/1.0";

                var message = new HttpRequestMessage(HttpMethod.Get, uri);
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"{protocolName} (SubtitleDownloader/1.0; https://github.com/daviddesmet/subtitle-downloader.git)");
                var response = await client.SendAsync(message);

                return response;
            }
            catch (Exception ex)
            {
                PrintError(ex.Message);
                return null;
            }
        }

        private static void PrintLineBreak() => Console.WriteLine();

        private static string GetInput(string question)
        {
            Console.Write($"{question}: ", Color.OrangeRed);
            return Console.ReadLine();
        }

        private static void PrintStatus(string message) => Console.WriteLine(message, Color.DeepSkyBlue);

        private static void PrintValue(string title, string value)
            => Console.WriteLineFormatted("{0}: {1}", Color.SeaGreen, new Formatter[] { new Formatter(title, Color.SpringGreen), new Formatter(value, Color.WhiteSmoke) });

        private static void PrintError(string message, params string[] args)
        {
            if (args.Length > 0)
                Console.WriteLine(string.Format(message, args), Color.Salmon);
            else
                Console.WriteLine(message, Color.Salmon);

            PrintLineBreak();
        }

        private static void PrintExitMessage()
        {
            Console.Write("Press any key to exit the app...");
            Console.ReadKey();
        }
    }
}

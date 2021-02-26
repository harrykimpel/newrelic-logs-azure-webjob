using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;

namespace newrelic_logs_webjob
{
    class Program
    {
        private static LogMonitor _logMonitor;
        private static bool _switch = true;
        static void Main(string[] args)
        {
            _logMonitor = new LogMonitor();
            while (_switch)
            {
                try
                {
                    _logMonitor.Exec();
                }
                catch (Exception ex)
                {
                    Console.Out.WriteLine(ex.Message);
                    Console.Out.WriteLine("Error. Stopping Monitor...");
                    _switch = false;
                }
            }
        }
    }

    class LogMonitor
    {
        private bool _initizated;
        private readonly string _logFile;
        private readonly string _logHistoryFile;
        private static int _lineNumber;
        private static DateTime _dateCreatedLog;
        private NewRelicHelper _newRelicHelper;

        public LogMonitor()
        {
            _logFile = GetLogFile();
            _logHistoryFile = GetHistoryFile();
            _lineNumber = 0;
        }

        public void Exec()
        {
            if (_newRelicHelper == null)
                _newRelicHelper = new NewRelicHelper(GetNewRelicRegion(), GetNewRelicLicenseKey());

            if (!_initizated)
                InitLogFile();

            // You can use ReadAndSend() method if wanna send one line each time
            ReadLinesAndSend();

            var delay = Convert.ToInt32(GetDelay()) * 1000;
            Thread.Sleep(delay);
        }

        private void InitLogFile()
        {
            Console.Out.WriteLine("Initializing Log reader");

            if (!LogHistoryFileExists())
                WriteHistory();
            else
            {
                SetDateCreatedLogFromHistory();
                if (_dateCreatedLog != ReadDateCreatedLogOfFile())
                    ResetHistory();
            }

            SetLineNumberFromHistory();
            _initizated = true;

            Console.Out.WriteLine("Log reader initialized");
        }

        private async void ReadAndSend()
        {
            var line = ReadLogLine();
            if (line == null) return;

            if (!await _newRelicHelper.SendMessage(line))
                return;

            _lineNumber++;
            WriteHistory();
        }

        private async void ReadLinesAndSend()
        {
            var lines = ReadLogLines();
            if (!lines.Any()) return;

            if (!await _newRelicHelper.SendMessages(lines))
                return;

            _lineNumber += lines.Count;
            WriteHistory();
        }

        private void ResetHistory(int lineNumber = 0)
        {
            _lineNumber = lineNumber;
            _dateCreatedLog = ReadDateCreatedLogOfFile();
            WriteHistory();

            Console.Out.WriteLine($"Log reader reseted with lineNumber = {lineNumber}");
        }

        #region IOMethods

        private void WriteHistory()
        {
            using (var sw = new StreamWriter(_logHistoryFile, false))
            {
                sw.WriteLine(_lineNumber);
                sw.WriteLine(_dateCreatedLog);
            }
        }

        private void SetLineNumberFromHistory()
        {
            var line = ReadLines(_logHistoryFile).ElementAt(0);
            int.TryParse(line, out _lineNumber);
        }

        private void SetDateCreatedLogFromHistory()
        {
            try
            {
                var line = ReadLines(_logHistoryFile).ElementAt(1);
                DateTime.TryParse(line, out _dateCreatedLog);
            }
            catch (ArgumentOutOfRangeException) // Catch exception for sites doesn't have reset feature
            {
                SetLineNumberFromHistory();
                ResetHistory(_lineNumber);
            }
        }

        private bool LogHistoryFileExists()
        {
            return File.Exists(_logHistoryFile);
        }

        private DateTime ReadDateCreatedLogOfFile()
        {
            return File.GetCreationTime(_logHistoryFile);
        }

        private string ReadLogLine()
        {
            var line = ReadLines(_logFile).Skip(_lineNumber).FirstOrDefault();
            return line;
        }

        private List<string> ReadLogLines()
        {
            var line = ReadLines(_logFile).Skip(_lineNumber).ToList();
            return line;
        }

        private static IEnumerable<string> ReadLines(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0x1000, FileOptions.SequentialScan))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    yield return line;
                }
            }
        }

        #endregion

        #region Getting Environment Variables
        private static string GetHistoryFile()
        {
            var rootExtensionPath = GetEnvironmentVariable("HOME");
            return Path.Combine(rootExtensionPath, @"SiteExtensions", "LogForwarder_history.txt");
        }

        private static string GetLogFile()
        {
            var rootPath = GetEnvironmentVariable("HOME");
            var logFilePath = GetEnvironmentVariable("LOG_FILE_PATH");
            return GetStartOnHome() == "1"
                ? Path.Combine(rootPath, logFilePath)
                : Path.Combine(rootPath, @"site\wwwroot", logFilePath);
        }

        private static string GetIndexName()
        {
            return GetEnvironmentVariable("INDEX_NAME");
        }

        private static string GetNewRelicRegion()
        {
            return GetEnvironmentVariable("NEWRELIC_REGION");
        }

        private static string GetNewRelicLicenseKey()
        {
            return GetEnvironmentVariable("NEWRELIC_LICENSEKEY");
        }

        private static string GetDelay()
        {
            return GetEnvironmentVariable("DELAY");
        }

        private static string GetStartOnHome()
        {
            return GetEnvironmentVariable("START_ON_HOME");
        }

        private static string GetEnvironmentVariable(string keyName)
        {
            var rootPath = Environment.GetEnvironmentVariable(keyName);

            if (rootPath == null)
                throw new NullReferenceException($"{keyName} variable environment doesn't exist");

            return rootPath;
        }
        #endregion
    }

    public class NewRelicHelper
    {
        public NewRelicHelper(string region, string licenseKey)
        {
            Region = region;
            LicenseKey = licenseKey;
        }

        public static string Region { get; set; }
        public static string LicenseKey { get; set; }
        static readonly HttpClient client = new HttpClient();

        public static long ConvertToUnixTime(DateTime datetime)
        {
            DateTime sTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            return (long)(datetime - sTime).TotalSeconds;
        }

        public async Task<bool> SendMessage(string message)
        {
            try
            {
                StringBuilder TheListBuilder = new StringBuilder();

                string dtS = message.Substring(0, 19);
                DateTime dt = DateTime.Now;
                if (DateTime.TryParse(dtS, out dt))
                {
                    //dt.AddHours(1);
                    long unix = ConvertToUnixTime(dt);
                    TheListBuilder.Append("{");
                    TheListBuilder.Append("  \"timestamp\": " + unix + ", ");
                    TheListBuilder.Append("  \"message\": \"" + message + "\" ");
                    TheListBuilder.Append("}");
                }

                TheListBuilder.Append("]");

                var json = TheListBuilder.ToString();

                return await PostLogPayload(json);
            }
            catch (Exception ex)
            {
                Console.Out.WriteLine(ex.Message);
                Console.Out.WriteLine(ex.InnerException?.Message);
                throw;
            }
        }

        public async Task<bool> SendMessages(List<string> messages)
        {
            try
            {
                StringBuilder TheListBuilder = new StringBuilder();

                TheListBuilder.Append("[");
                int TheCounter = 0;

                foreach (string s in messages)
                {
                    string dtS = s.Substring(0, 19);
                    DateTime dt = DateTime.Now;
                    if (DateTime.TryParse(dtS, out dt))
                    {
                        TheCounter++;
                        //dt.AddHours(1);
                        long unix = ConvertToUnixTime(dt);
                        TheListBuilder.Append("{");
                        TheListBuilder.Append("  \"timestamp\": " + unix + ", ");
                        TheListBuilder.Append("  \"message\": \"" + s + "\" ");
                        TheListBuilder.Append("}");
                    }

                    if (TheCounter != messages.Count())
                    {
                        TheListBuilder.Append(",");
                    }
                }
                TheListBuilder.Append("]");

                var json = TheListBuilder.ToString();

                return await PostLogPayload(json);
            }
            catch (Exception ex)
            {
                Console.Out.WriteLine(ex.Message);
                Console.Out.WriteLine(ex.InnerException?.Message);
                throw;
            }
        }

        private async Task<bool> PostLogPayload(string payload)
        {
            try
            {
                //Console.Out.WriteLine(payload);
                HttpContent content = new StringContent(payload, Encoding.UTF8, "application/json");
                content.Headers.Add("X-License-Key", LicenseKey);
                string NewRelicLogEndpoint = "https://log-api.newrelic.com/log/v1";
                if (Region.Equals("EU"))
                {
                    NewRelicLogEndpoint = "https://log-api.eu.newrelic.com/log/v1";
                }
                HttpResponseMessage response = await client.PostAsync(NewRelicLogEndpoint, content);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                //Console.Out.WriteLine(responseBody);
                return true;
            }
            catch (Exception ex)
            {
                Console.Out.WriteLine(ex.Message);
                Console.Out.WriteLine(ex.InnerException?.Message);
                throw;
            }
        }
    }
}

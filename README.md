# New Relic Logs Azure Webjob

This work is inspired by https://github.com/cignium/LogForwarder. I used this as a basis and modified in order to adopt to New Relic Logs solution.

The idea is to run this as a Azure App Service WebJob or Extension. It sends logs from a file in your Azure App Service to New Relic.

## Gettting Started

1. Build and publish this code: dotnet publish -c Release
2. ZIP the entire publish folder
3. Got to "WebJobs" option and click on "Add".
4. Enter a name and select the ZIP file to upload
5. Once installed, go to App Configuration and configure the followings variables:
   - LOG_FILE_PATH = Path of Log File, relative to website's root, e.g. "LogFiles\http\RawLogs\18e609-202102261024.log"
   - START_ON_HOME = Set "1" to start LOG_FILE_PATH on Home (Optional, default 0)
   - INDEX_NAME = Index where you can write
   - DELAY = Monitor Delay in seconds (Optional, default 5)
   - NEWRELIC_REGION = US or EU
   - NEWRELIC_LICENSEKEY = the New Relic License key of your account

You need to enable "Always On" option in App Settings for keep alive the WebJob.

The WebJob should start automatically and log information should be visible in your New Relic account within a couple of minutes.

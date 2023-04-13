using Amazon;
using Amazon.CostExplorer;
using Amazon.CostExplorer.Model;
using Amazon.Lambda.Core;
using System.Globalization;
using System.Text.Json;
using System.Text;
using Sentry;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CostTracker
{
    public class Program
    {
        public void Execute(ILambdaContext context)
        {
            using (SentrySdk.Init(cfg =>
            {
                cfg.Dsn = Environment.GetEnvironmentVariable("SENTRY_DSN");
                cfg.EnableTracing = true;
                cfg.TracesSampleRate = 1.0;
            }))
            {
                var transaction = SentrySdk.StartTransaction("invocation", "execute");

                (string usage, string forecast) = Calculate(transaction);

                Publish($"Current AWS usage is {usage} and projected to be {forecast} by the end of the month.", transaction);

                transaction.Finish();
            }
        }

        private (string usage, string forecast) Calculate(ITransaction? transaction = null)
        {
            var result = ("", "");

            var exchange_currency = "AUD";

            var exchange_rate = 1.49;

            var span = transaction?.StartChild("calculate");

            using (var api = new AmazonCostExplorerClient())
            {
                var usage = api.GetCostAndUsageAsync(new GetCostAndUsageRequest
                {
                    Granularity = Granularity.MONTHLY,
                    Metrics = new List<string> { "NetAmortizedCost" },
                    TimePeriod = new DateInterval
                    {
                        Start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("yyyy-MM-dd"),
                        End = DateTime.Today.ToString("yyyy-MM-dd")
                    }
                }).GetAwaiter().GetResult();

                var forecast = api.GetCostForecastAsync(new GetCostForecastRequest
                {
                    Granularity = Granularity.MONTHLY,
                    Metric = new Metric("NET_AMORTIZED_COST"),
                    TimePeriod = new DateInterval
                    {
                        Start = DateTime.Today.ToString("yyyy-MM-dd"),
                        End = new DateTime(DateTime.Today.Year, DateTime.Today.Month + 1, 1).Subtract(TimeSpan.FromDays(1)).ToString("yyyy-MM-dd")
                    }
                }).GetAwaiter().GetResult();

                result = ($"{(double.Parse(usage.ResultsByTime.Last().Total.Last().Value.Amount) * exchange_rate).ToString("C", CultureInfo.CreateSpecificCulture("en-US"))} {exchange_currency}", 
                          $"{(double.Parse(forecast.Total.Amount) * exchange_rate).ToString("C", CultureInfo.CreateSpecificCulture("en-US"))} {exchange_currency}");
            }

            span?.Finish();

            return result;
        }

        private void Publish(string message, ITransaction? transaction = null)
        {
            var span = transaction?.StartChild("publish");

            var user = Environment.GetEnvironmentVariable("PUSHOVER_USER");

            var token = Environment.GetEnvironmentVariable("PUSHOVER_TOKEN");

            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(token))
            {
                using (var api = new HttpClient())
                {
                    var body = new Dictionary<string, string>
                    {
                        ["token"] = token,
                        ["user"] = user,
                        ["message"] = message,
                        ["title"] = "AWS Billing",
                        ["url"] = "https://console.aws.amazon.com/cost-management/home"
                    };

                    var response = api.PostAsync("https://api.pushover.net/1/messages.json", new FormUrlEncodedContent(body)).GetAwaiter().GetResult();

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new ApplicationException($"Push notification failed. Error: {response.StatusCode}");
                    }
                }
            }

            span?.Finish();
        }
    }
}
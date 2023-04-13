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

                (double usage, double forecast) = Calculate(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID"), Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY"));

                var used = $"{usage.ToString("C", CultureInfo.CreateSpecificCulture("en-US"))}";

                var forecasted = $"{forecast.ToString("C", CultureInfo.CreateSpecificCulture("en-US"))}";

                Publish($"AWS usage is {used} USD and projected to be {forecasted} USD by the end of the month.");

                transaction.Finish();
            }
        }

        private (double usage, double forecast) Calculate(string? username, string? password, ITransaction? transaction = null)
        {
            var result = (0.0, 0.0);

            var span = transaction?.StartChild("calculate");

            var region = RegionEndpoint.GetBySystemName(Environment.GetEnvironmentVariable("AWS_REGION"));

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                var api = new AmazonCostExplorerClient(username, password, region);

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

                result = (double.Parse(usage.ResultsByTime.Last().Total.Last().Value.Amount), double.Parse(forecast.Total.Amount));
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
                using (var api = new HttpClient { BaseAddress = new Uri("https://api.pushover.net/1") })
                {
                    var body = new StringContent(JsonSerializer.Serialize(new
                    {
                        token, user, message,
                        title = "AWS Usage & Forecast",
                        url = "https://console.aws.amazon.com/cost-management/home"
                    }), Encoding.UTF8, "application/json");

                    api.PostAsync("/messages.json", body);
                }
            }

            span?.Finish();
        }
    }
}
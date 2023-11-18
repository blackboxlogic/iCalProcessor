using System.Net;
using Ical.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

// #r "System.Web.HttpUtility.dll"; System.Web.HttpUtility.UrlEncode("")
// http://localhost:7134/api/GetOne?url=https%3a%2f%2fcongresssquarepark.org%2fevents%2f%3fical%3d1&town=Portland&source=iCalAsCSV&location=Congress Square Park&format=html
// http://localhost:7134/api/GetOne?url=https%3a%2f%2fcongresssquarepark.org%2fevents%2f%3fical%3d1&town=Portland&source=iCalAsCSV&location=Congress Square Park&format=csv

namespace iCalProcessor
{
	public class iCal
	{
		private enum ResponseFormat { csv, html }

		private readonly ILogger _logger;

		public iCal(ILoggerFactory loggerFactory)
		{
			_logger = loggerFactory.CreateLogger<iCal>();
		}

		// parameters: url, format, town, location?
		[Function("GetOne")]
		public async Task<HttpResponseData> GetOne([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
		{
			try
			{
				// TODO get query params into function params
				var parameters = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
				var format = Enum.Parse<ResponseFormat>(parameters["format"] ?? "csv");
				var fetched = await Fetch(parameters["url"] ?? throw new ArgumentNullException("URL parameter is required"));
				var calendar = Calendar.Load(fetched);

				foreach (var e in calendar.Events)
				{
					e.Location = parameters["location"] ?? e.Location ?? "unknown location";

					if (parameters["town"] != null)
					{
						e.Location += ", " + parameters["town"];
					}
				}

				var formatted = format == ResponseFormat.csv
					? FormatICalCSV(calendar, parameters["town"])
					: FormatICalHTML(calendar);

				var response = req.CreateResponse(HttpStatusCode.OK);
				response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
				await response.WriteStringAsync(formatted);

				return response;
			}
			catch (Exception e)
			{
				var response = req.CreateResponse(HttpStatusCode.InternalServerError);
				await response.WriteStringAsync(e.ToString());
				return response;
			}
		}

		[Function("GetMany")]
		public async Task<HttpResponseData> GetMany([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
		{
			try
			{
				var parameters = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
				var format = Enum.Parse<ResponseFormat>(parameters["format"] ?? "csv");

				var formatters = new[] { CongressSquarePark(format),
					ScarboroughLandTrust(format),
					DiscoverDowntownWestbrook(format),
					BikeMaine(format),
					FreeportLibrary(format)};
				await Task.WhenAll(formatters);
				var formatted = string.Join(Environment.NewLine, formatters.Select(task => task.Result));

				var response = req.CreateResponse(HttpStatusCode.OK);
				response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
				await response.WriteStringAsync(formatted);

				return response;
			}
			catch (Exception e)
			{
				var response = req.CreateResponse(HttpStatusCode.InternalServerError);
				await response.WriteStringAsync(e.ToString());
				return response;
			}
		}

		// Usually doesn't have any location
		private async Task<string> CongressSquarePark(ResponseFormat format)
		{
			var fetched = await Fetch("https://congresssquarepark.org/events/?ical=1");
			var calendar = Calendar.Load(fetched);

			foreach (var e in calendar.Events)
			{
				e.Location = "Congress Square Park, Portland";
			}

			var formatted = format == ResponseFormat.csv
				? FormatICalCSV(calendar, "Portland")
				: FormatICalHTML(calendar);

			return formatted;
		}

		// Sometimes has "ME" or null for location
		private async Task<string> ScarboroughLandTrust(ResponseFormat format)
		{
			var fetched = await Fetch("https://scarboroughlandtrust.org/events/?ical=1");
			var calendar = Calendar.Load(fetched);

			foreach (var e in calendar.Events)
			{
				if (e.Location == "ME" || e.Location == null)
				{
					e.Location = "Scarborough";
				}
				else
				{
					e.Location = e.Location.Split(',').First() + ", Scarborough";
				}
			}

			var formatted = format == ResponseFormat.csv
				? FormatICalCSV(calendar, "Scarborough")
				: FormatICalHTML(calendar);

			return formatted;
		}

		// Has full addresses for location
		private async Task<string> DiscoverDowntownWestbrook(ResponseFormat format)
		{
			var fetched = await Fetch("https://www.downtownwestbrook.com/calendars/list/?ical=1");
			var calendar = Calendar.Load(fetched);

			foreach (var e in calendar.Events)
			{
				if (e.Location == null)
				{
					e.Location = "Westbrook";
				}
				else
				{
					e.Location = e.Location.Split(',').First() + ", Westbrook";
				}
			}

			var formatted = format == ResponseFormat.csv
				? FormatICalCSV(calendar, "Westbrook")
				: FormatICalHTML(calendar);

			return formatted;
		}

		private async Task<string> BikeMaine(ResponseFormat format)
		{
			var fetched = await Fetch("https://www.bikemaine.org/events/month/?ical=1");
			var calendar = Calendar.Load(fetched);

			var formatted = format == ResponseFormat.csv
				? FormatICalCSV(calendar)
				: FormatICalHTML(calendar);

			return formatted;
		}

		// Remove the "FCL Closed" events. Location is usually what room it's in
		private async Task<string> FreeportLibrary(ResponseFormat format)
		{
			var fetched = await Fetch("https://freeportmaine.libcal.com/ical_subscribe.php?src=p&cid=12960");
			var calendar = Calendar.Load(fetched);

			foreach (var closed in calendar.Events.Where(e => e.Summary == "FCL Closed").ToArray())
			{
				calendar.Events.Remove(closed);
			}

			foreach (var e in calendar.Events)
			{
				e.Location = "Library, Freeport";
			}

			var formatted = format == ResponseFormat.csv
				? FormatICalCSV(calendar, "Freeport")
				: FormatICalHTML(calendar);

			return formatted;
		}

		private static async Task<string> Fetch(string url)
		{
			using (HttpClient client = new HttpClient())
			using (HttpResponseMessage response = await client.GetAsync(url))
			using (HttpContent content = response.Content)
			{
				return await content.ReadAsStringAsync();
			}
		}

		public static string FormatICalHTML(Calendar calendar)
		{
			// title=\"{/*EscapeTextForHTML(ElideText(e.Description, 100))}\"
			var pretty = string.Join(Environment.NewLine,
				calendar.Events.Select(e => $"{e.Start.Date:yyyy-MM-dd}, <a href=\"{e.Url}\">{EscapeTextForHTML(e.Summary)}<\\a>, {FormatTimeSpan(e.Start.Value, e.End.Value)}, {EscapeTextForHTML(e.Location)}"));

			return pretty;
		}

		//1) date
		//2) event name
		//3) start and end times
		//4) location
		//5) town
		//6) link
		public static string FormatICalCSV(Calendar calendar, string? town = null)
		{
			var csv = string.Join(Environment.NewLine,
				calendar.Events.Select(e =>
				string.Join(',',
					e.Start.Date.ToString("MM-dd-yyyy"),
					EscapeTextForCSV(e.Summary),
					FormatTimeSpan(e.Start.Value, e.End.Value),
					EscapeTextForCSV(e.Location),
					EscapeTextForCSV(town ?? "unknown town"),
					EscapeTextForCSV(e.Url.ToString()))));

			return csv;
		}

		private static string EscapeTextForCSV(string text)
		{
			return '"' + text.Replace("\"", "\"\"") + '"';
		}

		private static string? EscapeTextForHTML(string? text)
		{
			return System.Web.HttpUtility.HtmlEncode(text);
		}

		private static string? ElideText(string? text, int length)
		{
			if (text == null) return null;
			if (text.Length <= length) return text;

			return text?.Substring(0, Math.Min(text.Length, length)) + '…';
		}

		// TODO put day name if multiple days?
		private static string FormatTimeSpan(DateTime start, DateTime end)
		{
			string result = start.Minute == 0
				? start.ToString("%h") // % is ignored, but throws an exception otherwise
				: start.ToString("h:mm");

			result += end == default || end.ToString("tt") != start.ToString("tt")
				? start.ToString(" tt")
				: "";

			result += end == default
				? ""
				: end.Minute == 0
					? end.ToString("-h tt")
					: end.ToString("-h:mm tt");

			// special case for "all-day"
			// 12 AM-11:59 PM
			if (result == "12 AM-11:59 PM") result = "all day";
			if (result == "12-12 AM") result = "all day";

			return result;
		}
	}
}

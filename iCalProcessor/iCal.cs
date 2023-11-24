using System.Net;
using System.Text.Json;
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
		private enum ResponseFormat { csv, html, json }

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
				var parameters = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
				var format = Enum.Parse<ResponseFormat>(parameters["format"] ?? "csv");
				var fetched = await Fetch(parameters["url"] ?? throw new ArgumentNullException("URL parameter is required"));
				var calendar = Calendar.Load(fetched);

				var events = calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location, Town = parameters["town"] }).ToArray();
				var formatted = FormatICal(events, format);

				var response = req.CreateResponse(HttpStatusCode.OK);

				if (format == ResponseFormat.csv)
					response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
				if (format == ResponseFormat.json)
					response.Headers.Add("Content-Type", "application/json; charset=utf-8");
				if (format == ResponseFormat.html)
					response.Headers.Add("Content-Type", "text/html; charset=utf-8");

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

				var fetchers = new[] { CongressSquarePark(),
					ScarboroughLandTrust(),
					DiscoverDowntownWestbrook(),
					BikeMaine(),
					FreeportLibrary()};
				await Task.WhenAll(fetchers);
				var events = fetchers.SelectMany(task => task.Result).ToArray();
				var formatted = FormatICal(events, format);

				var response = req.CreateResponse(HttpStatusCode.OK);

				if(format == ResponseFormat.csv)
					response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
				if (format == ResponseFormat.json)
					response.Headers.Add("Content-Type", "application/json; charset=utf-8");
				if (format == ResponseFormat.html)
					response.Headers.Add("Content-Type", "text/html; charset=utf-8");
					
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
		private async Task<Event[]> CongressSquarePark()
		{
			var fetched = await Fetch("https://congresssquarepark.org/events/?ical=1");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

			foreach (var e in calendar.Events)
			{
				e.Location = "Congress Square Park, Portland";
			}

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location, Town = "Portland" }).ToArray();
		}

		// Sometimes has "ME" or null for location
		private async Task<Event[]> ScarboroughLandTrust()
		{
			var fetched = await Fetch("https://scarboroughlandtrust.org/events/?ical=1");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

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

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location, Town = "Scarborough" }).ToArray();
		}

		// Has full addresses for location
		private async Task<Event[]> DiscoverDowntownWestbrook()
		{
			var fetched = await Fetch("https://www.downtownwestbrook.com/calendars/list/?ical=1");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

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

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location, Town = "Westbrook" }).ToArray();
		}

		private async Task<Event[]> BikeMaine()
		{
			var fetched = await Fetch("https://www.bikemaine.org/events/month/?ical=1");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location}).ToArray(); ;
		}

		// Remove the "FCL Closed" events. Location is usually what room it's in
		private async Task<Event[]> FreeportLibrary()
		{
			var fetched = await Fetch("https://freeportmaine.libcal.com/ical_subscribe.php?src=p&cid=12960");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

			foreach (var closed in calendar.Events.Where(e => e.Summary == "FCL Closed").ToArray())
			{
				calendar.Events.Remove(closed);
			}

			foreach (var e in calendar.Events)
			{
				e.Location = "Library, Freeport";
			}

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location, Town = "Freeport" }).ToArray();
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

		private static string FormatICal(Event[] events, ResponseFormat format)
		{
			return format == ResponseFormat.csv
				? FormatICalCSV(events)
				: format == ResponseFormat.json
					? FormatICalJSON(events)
					: FormatICalHTML(events);
		}

		public static string FormatICalJSON(Event[] events)
		{
			return JsonSerializer.Serialize(events);
		}

		public static string FormatICalHTML(Event[] events)
		{
			//StringBuilder result = new StringBuilder();

			// title=\"{/*EscapeTextForHTML(ElideText(e.Description, 100))}\"
			var pretty = string.Join(Environment.NewLine,
				events.Select(e => $"{e.Start.Date:yyyy-MM-dd}, <a href=\"{e.Url}\">{EscapeTextForHTML(e.Summary)}<\\a>, {FormatTimeSpan(e.Start, e.End)}, {EscapeTextForHTML(e.Location)}"));

			return pretty + Environment.NewLine;
		}

		//1) date
		//2) event name
		//3) start and end times
		//4) location
		//5) town
		//6) link
		public static string FormatICalCSV(Event[] events)
		{
			var csv = string.Join(Environment.NewLine,
				events.Select(e =>
				string.Join(',',
					e.Start.Date.ToString("MM-dd-yyyy"),
					EscapeTextForCSV(e.Summary),
					FormatTimeSpan(e.Start, e.End),
					EscapeTextForCSV(e.Location),
					EscapeTextForCSV(e.Town ?? "unknown town"),
					EscapeTextForCSV(e.Url.ToString()))));

			return csv + Environment.NewLine;
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

		public class Event
		{
			public DateTime Start { get; set; }
			public DateTime End { get; set; }
			public string Summary { get; set; }
			//public string? Description { get; set; }
			public string Location { get; set; } // like address
			public string? Town { get; set; }
			public Uri Url { get; set; }
			public string TimeSpanPretty => FormatTimeSpan(Start, End);
		}
	}
}

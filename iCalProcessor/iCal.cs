using System.Net;
using System.Net.Http.Headers;
using System.Text;
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

		[Function("CorsProxy")]
		public async Task<HttpResponseData> CorsProxy([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
		{
			var parameters = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
			var url = new Uri(parameters["url"]);
			//if (url.Host != "calendar.google.com")
			//{
				//throw new ArgumentException("invalid url parameter");
			//}

			var fetched = await FetchWithHeaders(url.ToString());
			var response = req.CreateResponse(HttpStatusCode.OK);
			await response.WriteStringAsync(fetched.content);

			response.Headers.Clear();

			foreach (var header in fetched.headers)
			{
				if(!header.Key.StartsWith("Cross-Origin") && header.Key != "Transfer-Encoding")
					response.Headers.Add(header.Key, header.Value);
			}

			response.Headers.Remove("Access-Control-Allow-Origin");
			response.Headers.Add("Access-Control-Allow-Origin", "*");

			return response;
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
				var format = Enum.Parse<ResponseFormat>(parameters["format"] ?? "html");

				var fetchers = new[] { CongressSquarePark(),
					ScarboroughLandTrust(),
					DiscoverDowntownWestbrook(),
					BikeMaine(),
					FreeportLibrary(),
					UniversitySouthernMaine(),
					SEDCO(),
					WoodfordsCorner(),
					VisitPortland(),
					MaineMaritimeMuseum(),
					GravesLibrary(),
					DEFFA()};
				await Task.WhenAll(fetchers);
				var events = fetchers.SelectMany(task => task.Result).ToArray();
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

		//https://www.googleapis.com/calendar/v3/calendars/x3d96ac1509248ba165d75df7e03c0e57a2afb2be15b0af5ae856e88ff90fe9e0b8/events
		//https://stackoverflow.com/questions/21539375/get-json-from-a-public-google-calendar
		//https://bridgtonlibrary.org/calendar/

		private async Task<Event[]> DEFFA()
		{
			var fetched = await Fetch("https://deffa.org/?post_type=tribe_events&ical=1&eventDisplay=list");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location, Town = "Kennebunkport" }).ToArray();
		}

		private async Task<Event[]> GravesLibrary()
		{
			var fetched = await Fetch("https://graveslibrary.org/?post_type=tribe_events&ical=1&eventDisplay=list");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

			foreach (var e in calendar.Events)
			{
				e.Location = "Graves Library, Kennebunkport";
			}

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location, Town = "Kennebunkport" }).ToArray();
		}

		private async Task<Event[]> MaineMaritimeMuseum()
		{
			var fetched = await Fetch("https://www.mainemaritimemuseum.org/?post_type=tribe_events&ical=1&eventDisplay=list");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

			foreach (var e in calendar.Events)
			{
				e.Location = "Maine Maritime Museum, Bath";
			}

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location, Town = "Bath" }).ToArray();
		}

		// has none?
		private async Task<Event[]> VisitPortland()
		{
			var fetched = await Fetch("https://www.visitportland.com/visit/things-to-do/event-calendar/?ical=1");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location, Town = "Portland" }).ToArray();
		}

		//https://woodfordscorner.org/community-calendar
		private async Task<Event[]> WoodfordsCorner()
		{
			var fetched = await Fetch("https://tockify.com/api/feeds/ics/fwc.community");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location, Town = "Portland" }).ToArray();
		}

		//https://sedcomaine.com/scarborough-community-calendar/
		private async Task<Event[]> SEDCO()
		{
			var fetched = await Fetch("https://tockify.com/api/feeds/ics/scarboroughcc");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location, Town = "Scarborough" }).ToArray();
		}

		private async Task<Event[]> UniversitySouthernMaine()
		{
			var fetched = await Fetch("https://usm.maine.edu/calendar-of-events?ical=1");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

			foreach (var e in calendar.Events)
			{
				e.Location = "USM, Portland";
			}

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location, Town = "Portland" }).ToArray();
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

			return calendar.Events.Select(e => new Event() { Start = e.Start.Value, End = e.End.Value, Summary = e.Summary, Url = e.Url, Location = e.Location }).ToArray();
		}

		// Remove the "FCL Closed" events. Location is usually what room it's in
		private async Task<Event[]> FreeportLibrary()
		{
			var fetched = await Fetch("https://freeportmaine.libcal.com/ical_subscribe.php?src=p&cid=12960");
			var calendar = Calendar.Load(fetched);

			if (calendar == null) return new Event[0];

			// Spelling is hard
			foreach (var closed in calendar.Events.Where(e => e.Summary == "FCL Closed"
				|| e.Summary.Contains("CANCEL")).ToArray())
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

		private static async Task<(string content, HttpResponseHeaders headers)> FetchWithHeaders(string url)
		{
			using (HttpClient client = new HttpClient())
			using (HttpResponseMessage response = await client.GetAsync(url))
			using (HttpContent content = response.Content)
			{
				return (await content.ReadAsStringAsync(), response.Headers);
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
			StringBuilder result = new StringBuilder();
			result.AppendLine("<html><head></head><body>");

			result.AppendLine(@" <label for=""townSelect"">Filter by town:</label>

<select name=""townSelect"" id=""townSelect"">
  <option value=""All"" selected>All</option>
  <option value=""Portland"">Portland</option>
  <option value=""Freeport"">Freeport</option>
  <option value=""Scarborough"">Scarborough</option>
  <option value=""Westbrook"">Westbrook</option>
</select> ");

			result.AppendLine("<div id=\"eventList\">");

			var byDay = events.Where(e => e.Start > DateTime.Now.Date && e.Start < DateTime.Now.Date.AddDays(7)).OrderBy(e => e.Start).GroupBy(e => e.Start.Date);

			foreach (var day in byDay)
			{
				result.AppendLine($"<h4>{day.Key.ToString("dddd, MMMM dd")}</h1>");

				result.AppendLine(string.Join(Environment.NewLine,
					day.Select(e => $"<div data-town=\"{e.Town}\"><a href=\"{e.Url}\">{EscapeTextForHTML(ElideText(e.Summary, 55))}</a>, {FormatTimeSpan(e.Start, e.End)}, {EscapeTextForHTML(e.Location)}</div>")));

			}

			result.AppendLine("</div>");
			// https://www.w3schools.com/howto/howto_js_filter_lists.asp
			result.AppendLine(@"
<script>

function Filter() {
  var input, filter, ul, li, town, i, txtValue;
  input = document.getElementById('townSelect');
  filter = input.value.toUpperCase();

  ul = document.getElementById(""eventList"");
  li = ul.getElementsByTagName('div');

  // Loop through all list items, and hide those who don't match the search query
  for (i = 0; i < li.length; i++) {
    town = li[i].getAttribute(""data-town"");
    if (town.toUpperCase() == filter.toUpperCase() || filter == ""ALL"") {
      li[i].style.display = """";
    } else {
      li[i].style.display = ""none"";
    }
  }
}

var mySelect = document.getElementById(""townSelect"");
mySelect.addEventListener(""change"", Filter);
Filter();

</script>
");

			result.AppendLine("</body>");

			return result.ToString();
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
			public string Price { get; set; }
			public string AgeRange { get; set; }
		}
	}
}

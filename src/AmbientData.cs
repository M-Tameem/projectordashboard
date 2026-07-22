using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ProjectorDash
{
    public sealed class AmbientLocation
    {
        public string Name;
        public string Region;
        public string Country;
        public double Latitude;
        public double Longitude;

        public string DisplayName
        {
            get
            {
                string place = Name ?? "";
                if (!string.IsNullOrEmpty(Region) &&
                    !string.Equals(place, Region, StringComparison.OrdinalIgnoreCase))
                    place += ", " + Region;
                if (place.Length == 0) place = Country ?? "Saved location";
                return place;
            }
        }
    }

    public sealed class WeatherReading
    {
        public double Temperature;
        public double FeelsLike;
        public double High;
        public double Low;
        public double WindSpeed;
        public int WeatherCode;
        public bool Fahrenheit;
        public DateTime NextSunrise;
        public DateTime UpdatedUtc;

        public string Unit { get { return Fahrenheit ? "°F" : "°C"; } }
        public string WindUnit { get { return Fahrenheit ? "mph" : "km/h"; } }
        public string Condition { get { return AmbientService.WeatherDescription(WeatherCode); } }
    }

    public sealed class PlaneReading
    {
        public string Identifier;
        public string Label;
        public string Registration;
        public string AircraftType;
        public double DistanceKm;
        public double BearingDegrees;
        public int AltitudeFeet;
        public double TrackDegrees;
        public double SpeedKnots;
        public double PositionAgeSeconds;
        public bool HasTrack;
    }

    public sealed class IssReading
    {
        public bool Available;
        public double ElevationDegrees;
        public double BearingDegrees;
        public double DistanceKm;
        public bool AboveHorizon { get { return Available && ElevationDegrees > 0.0; } }
    }

    public sealed class PlanetReading
    {
        public string Name;
        public double AltitudeDegrees;
        public double BearingDegrees;
    }

    public sealed class StarReading
    {
        public string Name;
        public double AltitudeDegrees;
        public double BearingDegrees;
        public double Magnitude;
    }

    public sealed class SkyReading
    {
        public readonly List<PlaneReading> Planes = new List<PlaneReading>();
        public readonly List<PlanetReading> Planets = new List<PlanetReading>();
        public readonly List<StarReading> Stars = new List<StarReading>();
        public IssReading Iss = new IssReading();
        public DateTime UpdatedUtc;
        public bool AircraftFeedAvailable;
        public bool IssFeedAvailable;
        public string AircraftFeedName = "";
        public string AircraftError = "";
        public double ObserverLatitude;
        public double ObserverLongitude;
    }

    /// <summary>
    /// Tiny, dependency-free ambient data client. All remote services are
    /// public, need no key/account, and are called off the UI thread. Planet
    /// positions are calculated locally from JPL's approximate orbital model.
    /// </summary>
    public static class AmbientService
    {
        private const double EarthRadiusKm = 6371.0;
        private const double Deg = Math.PI / 180.0;
        private static readonly object FallbackSync = new object();
        private static DateTime _fallbackFetchedUtc = DateTime.MinValue;
        private static List<PlaneReading> _fallbackPlanes;

        public static async Task<AmbientLocation> FindLocationAsync(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
                throw new InvalidOperationException("Enter a city, town, or postal code.");

            string url = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                Uri.EscapeDataString(search.Trim()) + "&count=1&language=en&format=json";
            Dictionary<string, object> root = AsObject(await DownloadAsync(url));
            object rawResults;
            if (!root.TryGetValue("results", out rawResults))
                throw new InvalidOperationException("No matching location was found.");
            object[] results = rawResults as object[];
            if (results == null || results.Length == 0)
                throw new InvalidOperationException("No matching location was found.");

            Dictionary<string, object> item = AsObject(results[0]);
            AmbientLocation result = new AmbientLocation();
            result.Name = StringValue(item, "name");
            result.Region = StringValue(item, "admin1");
            result.Country = StringValue(item, "country");
            result.Latitude = NumberValue(item, "latitude");
            result.Longitude = NumberValue(item, "longitude");
            return result;
        }

        public static async Task<WeatherReading> GetWeatherAsync(
            double latitude, double longitude, bool fahrenheit)
        {
            string lat = latitude.ToString("0.#####", CultureInfo.InvariantCulture);
            string lon = longitude.ToString("0.#####", CultureInfo.InvariantCulture);
            string unit = fahrenheit ? "fahrenheit" : "celsius";
            string wind = fahrenheit ? "mph" : "kmh";
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat +
                "&longitude=" + lon +
                "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m" +
                "&daily=temperature_2m_max,temperature_2m_min,sunrise" +
                "&forecast_days=2&timezone=auto&temperature_unit=" + unit +
                "&wind_speed_unit=" + wind;

            Dictionary<string, object> root = AsObject(await DownloadAsync(url));
            Dictionary<string, object> current = AsObject(root["current"]);
            Dictionary<string, object> daily = AsObject(root["daily"]);

            WeatherReading result = new WeatherReading();
            result.Temperature = NumberValue(current, "temperature_2m");
            result.FeelsLike = NumberValue(current, "apparent_temperature");
            result.WindSpeed = NumberValue(current, "wind_speed_10m");
            result.WeatherCode = (int)Math.Round(NumberValue(current, "weather_code"));
            result.High = FirstNumber(daily, "temperature_2m_max");
            result.Low = FirstNumber(daily, "temperature_2m_min");
            result.NextSunrise = FirstFutureDateTime(daily, "sunrise",
                ParseDateTime(StringValue(current, "time")));
            result.Fahrenheit = fahrenheit;
            result.UpdatedUtc = DateTime.UtcNow;
            return result;
        }

        public static async Task<SkyReading> GetSkyAsync(double latitude, double longitude)
        {
            SkyReading result = new SkyReading();
            result.ObserverLatitude = latitude;
            result.ObserverLongitude = longitude;
            result.Planets.AddRange(CalculatePlanets(DateTime.UtcNow, latitude, longitude));
            result.Stars.AddRange(CalculateStars(DateTime.UtcNow, latitude, longitude));

            Task<PlaneFeedResult> planes = GetPlanesAsync(latitude, longitude);
            Task<IssReading> iss = GetIssAsync(latitude, longitude);
            try
            {
                PlaneFeedResult feed = await planes;
                result.Planes.AddRange(feed.Planes);
                result.AircraftFeedAvailable = true;
                result.AircraftFeedName = feed.Name;
            }
            catch (Exception ex)
            {
                result.AircraftFeedAvailable = false;
                result.AircraftError = DescribeNetworkError(ex);
            }
            try
            {
                result.Iss = await iss;
                result.IssFeedAvailable = result.Iss.Available;
            }
            catch
            {
                result.Iss = new IssReading();
                result.IssFeedAvailable = false;
            }
            result.UpdatedUtc = DateTime.UtcNow;
            return result;
        }

        private sealed class PlaneFeedResult
        {
            public string Name;
            public List<PlaneReading> Planes;
        }

        private static async Task<PlaneFeedResult> GetPlanesAsync(
            double latitude, double longitude)
        {
            string lat = latitude.ToString("0.#####", CultureInfo.InvariantCulture);
            string lon = longitude.ToString("0.#####", CultureInfo.InvariantCulture);
            string suffix = "/v2/point/" + lat + "/" + lon + "/25";
            Exception primaryError;
            try
            {
                PlaneFeedResult primary = new PlaneFeedResult();
                primary.Name = "adsb.lol";
                primary.Planes = await GetPlanesFromAsync(
                    "https://api.adsb.lol" + suffix, latitude, longitude);
                return primary;
            }
            catch (Exception ex) { primaryError = ex; }

            List<PlaneReading> cached = GetCachedFallbackPlanes();
            if (cached != null)
            {
                return new PlaneFeedResult
                {
                    Name = "airplanes.live fallback · quota-safe 3 min sync",
                    Planes = cached
                };
            }

            try
            {
                PlaneFeedResult fallback = new PlaneFeedResult();
                fallback.Name = "airplanes.live fallback · quota-safe 3 min sync";
                fallback.Planes = await GetPlanesFromAsync(
                    "https://api.airplanes.live" + suffix, latitude, longitude);
                StoreFallbackPlanes(fallback.Planes);
                return fallback;
            }
            catch (Exception fallbackError)
            {
                throw new InvalidOperationException("adsb.lol " +
                    DescribeNetworkError(primaryError) + "; airplanes.live " +
                    DescribeNetworkError(fallbackError), fallbackError);
            }
        }

        private static List<PlaneReading> GetCachedFallbackPlanes()
        {
            lock (FallbackSync)
            {
                if (_fallbackPlanes == null) return null;
                double age = (DateTime.UtcNow - _fallbackFetchedUtc).TotalSeconds;
                if (age < 0.0 || age >= 180.0) return null;
                return ClonePlanes(_fallbackPlanes, age);
            }
        }

        private static void StoreFallbackPlanes(List<PlaneReading> planes)
        {
            lock (FallbackSync)
            {
                _fallbackFetchedUtc = DateTime.UtcNow;
                _fallbackPlanes = ClonePlanes(planes, 0.0);
            }
        }

        private static List<PlaneReading> ClonePlanes(List<PlaneReading> source,
            double additionalAgeSeconds)
        {
            List<PlaneReading> result = new List<PlaneReading>();
            foreach (PlaneReading value in source)
            {
                result.Add(new PlaneReading
                {
                    Identifier = value.Identifier,
                    Label = value.Label,
                    Registration = value.Registration,
                    AircraftType = value.AircraftType,
                    DistanceKm = value.DistanceKm,
                    BearingDegrees = value.BearingDegrees,
                    AltitudeFeet = value.AltitudeFeet,
                    TrackDegrees = value.TrackDegrees,
                    SpeedKnots = value.SpeedKnots,
                    PositionAgeSeconds = value.PositionAgeSeconds +
                        additionalAgeSeconds,
                    HasTrack = value.HasTrack
                });
            }
            return result;
        }

        private static async Task<List<PlaneReading>> GetPlanesFromAsync(
            string url, double latitude, double longitude)
        {
            Dictionary<string, object> root = AsObject(await DownloadAsync(url));
            object raw;
            List<PlaneReading> result = new List<PlaneReading>();
            if (!root.TryGetValue("ac", out raw)) return result;
            object[] aircraft = raw as object[];
            if (aircraft == null) return result;

            foreach (object entry in aircraft)
            {
                Dictionary<string, object> item = entry as Dictionary<string, object>;
                if (item == null || !item.ContainsKey("lat") || !item.ContainsKey("lon")) continue;
                double planeLat = NumberValue(item, "lat");
                double planeLon = NumberValue(item, "lon");
                double distance = HaversineKm(latitude, longitude, planeLat, planeLon);
                if (distance > 40.0) continue;

                object altitudeRaw;
                if (!item.TryGetValue("alt_baro", out altitudeRaw) || altitudeRaw == null) continue;
                if (string.Equals(Convert.ToString(altitudeRaw, CultureInfo.InvariantCulture),
                    "ground", StringComparison.OrdinalIgnoreCase)) continue;
                double altitude;
                if (!TryNumber(altitudeRaw, out altitude)) continue;

                string label = StringValue(item, "flight").Trim();
                if (label.Length == 0) label = StringValue(item, "r").Trim();
                if (label.Length == 0) label = StringValue(item, "t").Trim();
                if (label.Length == 0) label = "Aircraft";

                PlaneReading plane = new PlaneReading();
                plane.Identifier = StringValue(item, "hex").TrimStart('~').Trim();
                plane.Label = label;
                plane.Registration = StringValue(item, "r").Trim();
                plane.AircraftType = StringValue(item, "t").Trim();
                plane.DistanceKm = distance;
                plane.BearingDegrees = InitialBearing(latitude, longitude, planeLat, planeLon);
                plane.AltitudeFeet = (int)Math.Round(altitude);
                double track;
                if (item.ContainsKey("track") && TryNumber(item["track"], out track))
                {
                    plane.TrackDegrees = track;
                    plane.HasTrack = true;
                }
                double speed;
                if (item.ContainsKey("gs") && TryNumber(item["gs"], out speed))
                    plane.SpeedKnots = speed;
                double age;
                if (item.ContainsKey("seen_pos") && TryNumber(item["seen_pos"], out age))
                    plane.PositionAgeSeconds = age;
                result.Add(plane);
            }
            result.Sort(delegate(PlaneReading a, PlaneReading b)
            {
                return a.DistanceKm.CompareTo(b.DistanceKm);
            });
            if (result.Count > 12) result.RemoveRange(12, result.Count - 12);
            return result;
        }

        private static string DescribeNetworkError(Exception error)
        {
            if (error == null) return "failed";
            InvalidOperationException invalid = error as InvalidOperationException;
            if (invalid != null && invalid.InnerException != null &&
                invalid.Message.StartsWith("adsb.lol", StringComparison.OrdinalIgnoreCase))
                return invalid.Message;
            WebException web = error as WebException;
            if (web != null)
            {
                HttpWebResponse response = web.Response as HttpWebResponse;
                if (response != null) return "HTTP " + ((int)response.StatusCode).ToString();
                if (web.Status == WebExceptionStatus.TrustFailure)
                    return "TLS/certificate error";
                if (web.Status == WebExceptionStatus.Timeout) return "timed out";
                if (web.Status == WebExceptionStatus.NameResolutionFailure)
                    return "DNS failed";
                if (web.Status == WebExceptionStatus.ConnectFailure)
                    return "connection failed";
                return web.Status.ToString();
            }
            return error.Message;
        }

        private static async Task<IssReading> GetIssAsync(
            double latitude, double longitude)
        {
            Dictionary<string, object> root = AsObject(await DownloadAsync(
                "https://api.wheretheiss.at/v1/satellites/25544"));
            double issLat = NumberValue(root, "latitude");
            double issLon = NumberValue(root, "longitude");
            double issAltitude = NumberValue(root, "altitude");

            double obsLat = latitude * Deg;
            double obsLon = longitude * Deg;
            double satLat = issLat * Deg;
            double satLon = issLon * Deg;
            double satRadius = EarthRadiusKm + issAltitude;

            double ox = EarthRadiusKm * Math.Cos(obsLat) * Math.Cos(obsLon);
            double oy = EarthRadiusKm * Math.Cos(obsLat) * Math.Sin(obsLon);
            double oz = EarthRadiusKm * Math.Sin(obsLat);
            double sx = satRadius * Math.Cos(satLat) * Math.Cos(satLon);
            double sy = satRadius * Math.Cos(satLat) * Math.Sin(satLon);
            double sz = satRadius * Math.Sin(satLat);
            double dx = sx - ox;
            double dy = sy - oy;
            double dz = sz - oz;

            double east = -Math.Sin(obsLon) * dx + Math.Cos(obsLon) * dy;
            double north = -Math.Sin(obsLat) * Math.Cos(obsLon) * dx -
                Math.Sin(obsLat) * Math.Sin(obsLon) * dy + Math.Cos(obsLat) * dz;
            double up = Math.Cos(obsLat) * Math.Cos(obsLon) * dx +
                Math.Cos(obsLat) * Math.Sin(obsLon) * dy + Math.Sin(obsLat) * dz;

            IssReading result = new IssReading();
            result.Available = true;
            result.DistanceKm = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            result.ElevationDegrees = Math.Atan2(up, Math.Sqrt(east * east + north * north)) / Deg;
            result.BearingDegrees = NormalizeDegrees(Math.Atan2(east, north) / Deg);
            return result;
        }

        public static string WeatherDescription(int code)
        {
            if (code == 0) return "Clear";
            if (code == 1) return "Mostly clear";
            if (code == 2) return "Partly cloudy";
            if (code == 3) return "Overcast";
            if (code == 45 || code == 48) return "Fog";
            if (code >= 51 && code <= 57) return "Drizzle";
            if (code >= 61 && code <= 67) return "Rain";
            if (code >= 71 && code <= 77) return "Snow";
            if (code >= 80 && code <= 82) return "Rain showers";
            if (code >= 85 && code <= 86) return "Snow showers";
            if (code >= 95) return "Thunderstorm";
            return "Mixed conditions";
        }

        public static string Cardinal(double degrees)
        {
            string[] names = new string[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int index = (int)Math.Floor((NormalizeDegrees(degrees) + 22.5) / 45.0) % 8;
            return names[index];
        }

        public static string RelativeDirection(double bearing, int facingDegrees)
        {
            double delta = NormalizeSignedDegrees(bearing - facingDegrees);
            double a = Math.Abs(delta);
            if (a <= 22.5) return "ahead";
            // This describes the upward-looking ceiling view. Compass
            // bearings increase clockwise on a ground map, but toward the
            // viewer's left when the same sky is seen from underneath.
            if (a <= 67.5) return delta > 0 ? "ahead-left" : "ahead-right";
            if (a <= 112.5) return delta > 0 ? "left" : "right";
            if (a <= 157.5) return delta > 0 ? "behind-left" : "behind-right";
            return "behind";
        }

        public static string Direction(double bearing, int facingDegrees)
        {
            return Cardinal(bearing) + " · " + RelativeDirection(bearing, facingDegrees);
        }

        // JPL approximate Keplerian elements and rates, valid 1800-2050.
        private sealed class Orbit
        {
            public string Name;
            public double[] V;
            public Orbit(string name, params double[] values) { Name = name; V = values; }
        }

        private static readonly Orbit[] Orbits = new Orbit[]
        {
            new Orbit("Mercury", .38709927,.00000037,.20563593,.00001906,7.00497902,-.00594749,252.25032350,149472.67411175,77.45779628,.16047689,48.33076593,-.12534081),
            new Orbit("Venus", .72333566,.00000390,.00677672,-.00004107,3.39467605,-.00078890,181.97909950,58517.81538729,131.60246718,.00268329,76.67984255,-.27769418),
            new Orbit("Earth", 1.00000261,.00000562,.01671123,-.00004392,-.00001531,-.01294668,100.46457166,35999.37244981,102.93768193,.32327364,0.0,0.0),
            new Orbit("Mars", 1.52371034,.00001847,.09339410,.00007882,1.84969142,-.00813131,-4.55343205,19140.30268499,-23.94362959,.44441088,49.55953891,-.29257343),
            new Orbit("Jupiter", 5.20288700,-.00011607,.04838624,-.00013253,1.30439695,-.00183714,34.39644051,3034.74612775,14.72847983,.21252668,100.47390909,.20469106),
            new Orbit("Saturn", 9.53667594,-.00125060,.05386179,-.00050991,2.48599187,.00193609,49.95424423,1222.49362201,92.59887831,-.41897216,113.66242448,-.28867794),
            new Orbit("Uranus", 19.18916464,-.00196176,.04725744,-.00004397,.77263783,-.00242939,313.23810451,428.48202785,170.95427630,.40805281,74.01692503,.04240589),
            new Orbit("Neptune", 30.06992276,.00026291,.00859048,.00005105,1.77004347,.00035372,-55.12002969,218.45945325,44.96476227,-.32241464,131.78422574,-.00508664)
        };

        private sealed class BrightStar
        {
            public string Name;
            public double RightAscensionHours;
            public double DeclinationDegrees;
            public double Magnitude;
            public BrightStar(string name, double ra, double dec, double magnitude)
            {
                Name = name;
                RightAscensionHours = ra;
                DeclinationDegrees = dec;
                Magnitude = magnitude;
            }
        }

        // A compact J2000 catalog of the brightest and most recognizable stars.
        // Keeping this embedded makes the star layer instant, offline, and exact
        // enough for a ceiling orientation view without another web service.
        private static readonly BrightStar[] BrightStars = new BrightStar[]
        {
            new BrightStar("Sirius", 6.752477, -16.716116, -1.46),
            new BrightStar("Canopus", 6.399200, -52.695700, -0.74),
            new BrightStar("Arcturus", 14.261030, 19.182410, -0.05),
            new BrightStar("Vega", 18.615649, 38.783690, 0.03),
            new BrightStar("Capella", 5.278155, 45.997990, 0.08),
            new BrightStar("Rigel", 5.242298, -8.201640, 0.13),
            new BrightStar("Procyon", 7.655033, 5.224990, 0.34),
            new BrightStar("Achernar", 1.628570, -57.236750, 0.46),
            new BrightStar("Betelgeuse", 5.919529, 7.407060, 0.50),
            new BrightStar("Hadar", 14.063700, -60.373000, 0.61),
            new BrightStar("Altair", 19.846389, 8.868300, 0.76),
            new BrightStar("Acrux", 12.443300, -63.099000, 0.77),
            new BrightStar("Aldebaran", 4.598677, 16.509300, 0.85),
            new BrightStar("Antares", 16.490129, -26.432000, 0.96),
            new BrightStar("Spica", 13.419883, -11.161300, 0.97),
            new BrightStar("Pollux", 7.755263, 28.026200, 1.14),
            new BrightStar("Fomalhaut", 22.960848, -29.622200, 1.16),
            new BrightStar("Deneb", 20.690532, 45.280300, 1.25),
            new BrightStar("Regulus", 10.139530, 11.967200, 1.35),
            new BrightStar("Castor", 7.576670, 31.888300, 1.58),
            new BrightStar("Bellatrix", 5.418850, 6.349700, 1.64),
            new BrightStar("Elnath", 5.438200, 28.607500, 1.65),
            new BrightStar("Alnilam", 5.603560, -1.201900, 1.69),
            new BrightStar("Dubhe", 11.062100, 61.750800, 1.79),
            new BrightStar("Polaris", 2.530300, 89.264100, 1.98)
        };

        public static List<PlanetReading> CalculatePlanets(
            DateTime utc, double latitude, double longitude)
        {
            double jd = JulianDate(utc);
            double t = (jd - 2451545.0) / 36525.0;
            double[] earth = Heliocentric(Orbits[2], t);
            List<PlanetReading> result = new List<PlanetReading>();
            foreach (Orbit orbit in Orbits)
            {
                if (orbit.Name == "Earth") continue;
                double[] planet = Heliocentric(orbit, t);
                double x = planet[0] - earth[0];
                double y = planet[1] - earth[1];
                double z = planet[2] - earth[2];

                double obliquity = 23.439291111 * Deg;
                double xeq = x;
                double yeq = y * Math.Cos(obliquity) - z * Math.Sin(obliquity);
                double zeq = y * Math.Sin(obliquity) + z * Math.Cos(obliquity);
                double ra = Math.Atan2(yeq, xeq);
                double dec = Math.Atan2(zeq, Math.Sqrt(xeq * xeq + yeq * yeq));
                PrecessFromJ2000(ref ra, ref dec, t);

                double gmst = NormalizeDegrees(280.46061837 +
                    360.98564736629 * (jd - 2451545.0) + .000387933 * t * t -
                    t * t * t / 38710000.0);
                double hourAngle = NormalizeSignedDegrees(gmst + longitude - ra / Deg) * Deg;
                double lat = latitude * Deg;
                double altitude = Math.Asin(Math.Sin(lat) * Math.Sin(dec) +
                    Math.Cos(lat) * Math.Cos(dec) * Math.Cos(hourAngle));
                double azimuth = Math.Atan2(Math.Sin(hourAngle),
                    Math.Cos(hourAngle) * Math.Sin(lat) - Math.Tan(dec) * Math.Cos(lat)) / Deg + 180.0;

                if (altitude / Deg > 0.0)
                {
                    PlanetReading reading = new PlanetReading();
                    reading.Name = orbit.Name;
                    reading.AltitudeDegrees = altitude / Deg;
                    reading.BearingDegrees = NormalizeDegrees(azimuth);
                    result.Add(reading);
                }
            }
            result.Sort(delegate(PlanetReading a, PlanetReading b)
            {
                return b.AltitudeDegrees.CompareTo(a.AltitudeDegrees);
            });
            return result;
        }

        public static List<StarReading> CalculateStars(
            DateTime utc, double latitude, double longitude)
        {
            double jd = JulianDate(utc);
            double t = (jd - 2451545.0) / 36525.0;
            double gmst = NormalizeDegrees(280.46061837 +
                360.98564736629 * (jd - 2451545.0) + .000387933 * t * t -
                t * t * t / 38710000.0);
            double lat = latitude * Deg;
            List<StarReading> result = new List<StarReading>();

            foreach (BrightStar star in BrightStars)
            {
                double ra = star.RightAscensionHours * 15.0 * Deg;
                double dec = star.DeclinationDegrees * Deg;
                PrecessFromJ2000(ref ra, ref dec, t);
                double hourAngle = NormalizeSignedDegrees(
                    gmst + longitude - ra / Deg) * Deg;
                double altitude = Math.Asin(Math.Sin(lat) * Math.Sin(dec) +
                    Math.Cos(lat) * Math.Cos(dec) * Math.Cos(hourAngle));
                if (altitude / Deg <= 1.0) continue;
                double azimuth = Math.Atan2(Math.Sin(hourAngle),
                    Math.Cos(hourAngle) * Math.Sin(lat) -
                    Math.Tan(dec) * Math.Cos(lat)) / Deg + 180.0;
                result.Add(new StarReading
                {
                    Name = star.Name,
                    Magnitude = star.Magnitude,
                    AltitudeDegrees = altitude / Deg,
                    BearingDegrees = NormalizeDegrees(azimuth)
                });
            }
            result.Sort(delegate(StarReading a, StarReading b)
            {
                return a.Magnitude.CompareTo(b.Magnitude);
            });
            return result;
        }

        private static double[] Heliocentric(Orbit orbit, double t)
        {
            double[] v = orbit.V;
            double a = v[0] + v[1] * t;
            double e = v[2] + v[3] * t;
            double inc = (v[4] + v[5] * t) * Deg;
            double meanLongitude = v[6] + v[7] * t;
            double peri = v[8] + v[9] * t;
            double node = (v[10] + v[11] * t) * Deg;
            double meanAnomaly = NormalizeDegrees(meanLongitude - peri) * Deg;
            double eccentricAnomaly = meanAnomaly;
            for (int i = 0; i < 10; i++)
            {
                double delta = (eccentricAnomaly - e * Math.Sin(eccentricAnomaly) -
                    meanAnomaly) / (1.0 - e * Math.Cos(eccentricAnomaly));
                eccentricAnomaly -= delta;
                if (Math.Abs(delta) < 1e-10) break;
            }
            double xp = a * (Math.Cos(eccentricAnomaly) - e);
            double yp = a * Math.Sqrt(1.0 - e * e) * Math.Sin(eccentricAnomaly);
            double argPeri = peri * Deg - node;
            double cosO = Math.Cos(node), sinO = Math.Sin(node);
            double cosW = Math.Cos(argPeri), sinW = Math.Sin(argPeri);
            double cosI = Math.Cos(inc), sinI = Math.Sin(inc);
            return new double[]
            {
                (cosW * cosO - sinW * sinO * cosI) * xp +
                    (-sinW * cosO - cosW * sinO * cosI) * yp,
                (cosW * sinO + sinW * cosO * cosI) * xp +
                    (-sinW * sinO + cosW * cosO * cosI) * yp,
                (sinW * sinI) * xp + (cosW * sinI) * yp
            };
        }

        private static void PrecessFromJ2000(ref double ra, ref double dec, double t)
        {
            double zeta = (2306.2181 * t + .30188 * t * t + .017998 * t * t * t) / 3600.0 * Deg;
            double z = (2306.2181 * t + 1.09468 * t * t + .018203 * t * t * t) / 3600.0 * Deg;
            double theta = (2004.3109 * t - .42665 * t * t - .041833 * t * t * t) / 3600.0 * Deg;
            double a = Math.Cos(dec) * Math.Sin(ra + zeta);
            double b = Math.Cos(theta) * Math.Cos(dec) * Math.Cos(ra + zeta) -
                Math.Sin(theta) * Math.Sin(dec);
            double c = Math.Sin(theta) * Math.Cos(dec) * Math.Cos(ra + zeta) +
                Math.Cos(theta) * Math.Sin(dec);
            ra = Math.Atan2(a, b) + z;
            dec = Math.Asin(c);
        }

        private static double JulianDate(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return 2440587.5 + (utc - new DateTime(1970, 1, 1, 0, 0, 0,
                DateTimeKind.Utc)).TotalDays;
        }

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = (lat2 - lat1) * Deg;
            double dLon = (lon2 - lon1) * Deg;
            double a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                Math.Cos(lat1 * Deg) * Math.Cos(lat2 * Deg) *
                Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);
            return EarthRadiusKm * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        }

        private static double InitialBearing(double lat1, double lon1, double lat2, double lon2)
        {
            double p1 = lat1 * Deg, p2 = lat2 * Deg, dLon = (lon2 - lon1) * Deg;
            double y = Math.Sin(dLon) * Math.Cos(p2);
            double x = Math.Cos(p1) * Math.Sin(p2) -
                Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dLon);
            return NormalizeDegrees(Math.Atan2(y, x) / Deg);
        }

        private static double NormalizeDegrees(double value)
        {
            value %= 360.0;
            if (value < 0.0) value += 360.0;
            return value;
        }

        private static double NormalizeSignedDegrees(double value)
        {
            value = NormalizeDegrees(value);
            return value > 180.0 ? value - 360.0 : value;
        }

        private static async Task<string> DownloadAsync(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2 on .NET 4.5
            using (WebClient client = new TimeoutWebClient())
            {
                client.Encoding = System.Text.Encoding.UTF8;
                client.Headers[HttpRequestHeader.UserAgent] =
                    "ProjectorDashboard/" + SelfUpdater.CurrentVersion;
                return await client.DownloadStringTaskAsync(new Uri(url)).ConfigureAwait(false);
            }
        }

        private sealed class TimeoutWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                request.Timeout = 8000;
                HttpWebRequest http = request as HttpWebRequest;
                if (http != null) http.ReadWriteTimeout = 8000;
                return request;
            }
        }

        private static Dictionary<string, object> AsObject(object value)
        {
            Dictionary<string, object> result = value as Dictionary<string, object>;
            if (result == null) throw new InvalidOperationException("The data service returned an unexpected response.");
            return result;
        }

        private static Dictionary<string, object> AsObject(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 1024 * 1024;
            return AsObject(serializer.DeserializeObject(json));
        }

        private static string StringValue(Dictionary<string, object> item, string key)
        {
            object value;
            return item.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }

        private static double NumberValue(Dictionary<string, object> item, string key)
        {
            object value;
            double result;
            if (!item.TryGetValue(key, out value) || !TryNumber(value, out result))
                throw new InvalidOperationException("The data service omitted " + key + ".");
            return result;
        }

        private static double FirstNumber(Dictionary<string, object> item, string key)
        {
            object value;
            if (!item.TryGetValue(key, out value))
                throw new InvalidOperationException("The data service omitted " + key + ".");
            object[] array = value as object[];
            double result;
            if (array == null || array.Length == 0 || !TryNumber(array[0], out result))
                throw new InvalidOperationException("The data service omitted " + key + ".");
            return result;
        }

        private static DateTime ParseDateTime(string value)
        {
            DateTime parsed;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out parsed) ? parsed : DateTime.Now;
        }

        private static DateTime FirstFutureDateTime(Dictionary<string, object> item,
            string key, DateTime after)
        {
            object value;
            if (!item.TryGetValue(key, out value)) return DateTime.MinValue;
            object[] array = value as object[];
            if (array == null) return DateTime.MinValue;
            DateTime last = DateTime.MinValue;
            foreach (object raw in array)
            {
                DateTime parsed;
                if (!DateTime.TryParse(Convert.ToString(raw,
                    CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out parsed)) continue;
                last = parsed;
                if (parsed >= after) return parsed;
            }
            return last;
        }

        private static bool TryNumber(object value, out double result)
        {
            if (value is double) { result = (double)value; return true; }
            if (value is decimal) { result = (double)(decimal)value; return true; }
            if (value is int) { result = (int)value; return true; }
            if (value is long) { result = (long)value; return true; }
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }
}

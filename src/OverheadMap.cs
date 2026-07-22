using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProjectorDash
{
    /// <summary>
    /// An upward-looking ceiling view. Celestial objects use literal sky
    /// elevation (zenith at center, horizon at the edge). Aircraft use the
    /// selected ground-range scale instead, otherwise ordinary cruising
    /// traffic beyond roughly 40 km collapses into the outer few pixels.
    /// Bearings remain literal and aircraft move between feed samples using
    /// their reported ADS-B track and groundspeed.
    /// </summary>
    public sealed class OverheadMap : FrameworkElement
    {
        private const double AircraftRangeInset = 0.90;

        private double MinimumAircraftElevation
        {
            get { return _sky != null && _sky.AircraftRadiusKm > 40 ? 0.5 : 5.0; }
        }
        private sealed class TrackSample
        {
            public DateTime AtUtc;
            public double EastKm;
            public double NorthKm;
        }

        private sealed class AircraftTrack
        {
            public string Id;
            public string Label;
            public string Registration;
            public string AircraftType;
            public int AltitudeFeet;
            public double TrackDegrees;
            public double SpeedKnots;
            public bool HasTrack;
            public DateTime AnchorUtc;
            public double AnchorEastKm;
            public double AnchorNorthKm;
            public DateTime LastReceivedUtc;
            public readonly List<TrackSample> Samples = new List<TrackSample>();
        }

        private sealed class SkyTrailSample
        {
            public DateTime AtUtc;
            public double BearingDegrees;
            public double ElevationDegrees;
        }

        private SkyReading _sky;
        private int _facingDegrees;
        private int _aircraftRadiusKm = 40;
        private DateTime _celestialUpdatedUtc = DateTime.MinValue;
        private List<PlanetReading> _livePlanets = new List<PlanetReading>();
        private List<PlanetReading> _futurePlanets = new List<PlanetReading>();
        private List<StarReading> _liveStars = new List<StarReading>();
        private readonly Dictionary<string, AircraftTrack> _tracks =
            new Dictionary<string, AircraftTrack>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SkyTrailSample> _issTrail = new List<SkyTrailSample>();
        private readonly DispatcherTimer _motionTimer;
        private readonly Typeface _ui = new Typeface(new FontFamily(Ui.FontUi),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private readonly Typeface _strong = new Typeface(new FontFamily(Ui.FontUi),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public OverheadMap()
        {
            SnapsToDevicePixels = true;
            IsHitTestVisible = false;
            _motionTimer = new DispatcherTimer(DispatcherPriority.Render);
            _motionTimer.Interval = TimeSpan.FromMilliseconds(125);
            _motionTimer.Tick += delegate { if (IsVisible) InvalidateVisual(); };
            IsVisibleChanged += delegate
            {
                if (IsVisible) _motionTimer.Start();
                else _motionTimer.Stop();
            };
        }

        public void SetData(SkyReading sky, int facingDegrees)
        {
            _sky = sky;
            _facingDegrees = Normalize(facingDegrees);
            if (sky != null)
            {
                if (!sky.ExternalFeedsEnabled)
                {
                    _tracks.Clear();
                    _issTrail.Clear();
                }
                if (sky.AircraftRadiusKm != _aircraftRadiusKm)
                {
                    _tracks.Clear();
                    _aircraftRadiusKm = sky.AircraftRadiusKm;
                }
                if (sky.ExternalFeedsEnabled)
                {
                    RecordAircraft(sky);
                    RecordIss(sky);
                }
                _livePlanets = new List<PlanetReading>(sky.Planets);
                _futurePlanets = AmbientService.CalculatePlanets(
                    (sky.UpdatedUtc == DateTime.MinValue ? DateTime.UtcNow :
                    sky.UpdatedUtc).AddMinutes(10), sky.ObserverLatitude,
                    sky.ObserverLongitude);
                _liveStars = new List<StarReading>(sky.Stars);
                _celestialUpdatedUtc = sky.UpdatedUtc;
            }
            InvalidateVisual();
        }

        private void RecordAircraft(SkyReading sky)
        {
            DateTime received = sky.UpdatedUtc == DateTime.MinValue
                ? DateTime.UtcNow : sky.UpdatedUtc;
            HashSet<string> observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int unnamed = 0;
            foreach (PlaneReading plane in sky.Planes)
            {
                // ADS-B fallback samples can be cached for quota safety. Past
                // this age, continued dead-reckoning looks convincing but is
                // no longer trustworthy, so accuracy wins over persistence.
                if (plane.PositionAgeSeconds > 75.0) continue;
                string id = plane.Identifier;
                if (string.IsNullOrWhiteSpace(id))
                    id = (plane.Label ?? "Aircraft") + "-" + (++unnamed).ToString();
                observed.Add(id);

                double angle = plane.BearingDegrees * Math.PI / 180.0;
                double east = plane.DistanceKm * Math.Sin(angle);
                double north = plane.DistanceKm * Math.Cos(angle);
                DateTime sampleAt = received.AddSeconds(-Math.Max(0.0,
                    Math.Min(plane.PositionAgeSeconds, 60.0)));

                AircraftTrack track;
                bool existing = _tracks.TryGetValue(id, out track);
                if (!existing)
                {
                    track = new AircraftTrack { Id = id };
                    _tracks.Add(id, track);
                }

                bool acceptPosition = true;
                double acceptedCourse = Normalize(plane.TrackDegrees);
                if (existing && track.AnchorUtc != DateTime.MinValue)
                {
                    double deltaSeconds = (sampleAt - track.AnchorUtc).TotalSeconds;
                    if (deltaSeconds < -2.0)
                    {
                        acceptPosition = false;
                    }
                    else
                    {
                        double previousSpeed = track.HasTrack
                            ? track.SpeedKnots * 1.852 / 3600.0 : 0.0;
                        double previousCourse = track.TrackDegrees * Math.PI / 180.0;
                        double predictedEast = track.AnchorEastKm + previousSpeed *
                            Math.Sin(previousCourse) * Math.Max(0.0, deltaSeconds);
                        double predictedNorth = track.AnchorNorthKm + previousSpeed *
                            Math.Cos(previousCourse) * Math.Max(0.0, deltaSeconds);
                        double errorKm = Distance(predictedEast, predictedNorth,
                            east, north);
                        double allowanceKm = 2.5 + previousSpeed *
                            Math.Max(0.0, deltaSeconds) * 0.35;
                        if (errorKm > allowanceKm) acceptPosition = false;

                        double movedKm = Distance(track.AnchorEastKm,
                            track.AnchorNorthKm, east, north);
                        if (acceptPosition && plane.HasTrack && movedKm > 0.15)
                        {
                            double observedCourse = Normalize(Math.Atan2(
                                east - track.AnchorEastKm,
                                north - track.AnchorNorthKm) * 180.0 / Math.PI);
                            if (Math.Abs(SignedAngle(acceptedCourse -
                                observedCourse)) > 75.0)
                                acceptedCourse = observedCourse;
                        }
                        else if (plane.HasTrack && track.HasTrack &&
                            Math.Abs(SignedAngle(acceptedCourse -
                                track.TrackDegrees)) > 100.0)
                        {
                            acceptedCourse = track.TrackDegrees;
                        }
                    }
                }
                track.Label = plane.Label;
                track.Registration = plane.Registration;
                track.AircraftType = plane.AircraftType;
                track.AltitudeFeet = plane.AltitudeFeet;
                track.TrackDegrees = acceptedCourse;
                track.SpeedKnots = Math.Max(0.0, plane.SpeedKnots);
                track.HasTrack = plane.HasTrack;
                track.LastReceivedUtc = received;

                if (!acceptPosition) continue;
                track.AnchorUtc = sampleAt;
                track.AnchorEastKm = east;
                track.AnchorNorthKm = north;

                bool add = track.Samples.Count == 0;
                if (!add)
                {
                    TrackSample last = track.Samples[track.Samples.Count - 1];
                    double moved = Distance(last.EastKm, last.NorthKm, east, north);
                    add = moved > 0.025 || (sampleAt - last.AtUtc).TotalSeconds > 20.0;
                }
                if (add)
                {
                    track.Samples.Add(new TrackSample
                    {
                        AtUtc = sampleAt,
                        EastKm = east,
                        NorthKm = north
                    });
                    while (track.Samples.Count > 10) track.Samples.RemoveAt(0);
                }
            }

            List<string> expired = new List<string>();
            foreach (KeyValuePair<string, AircraftTrack> pair in _tracks)
            {
                if (!observed.Contains(pair.Key) &&
                    (received - pair.Value.LastReceivedUtc).TotalSeconds > 90.0)
                    expired.Add(pair.Key);
            }
            foreach (string id in expired) _tracks.Remove(id);
        }

        private void RecordIss(SkyReading sky)
        {
            if (sky.Iss == null || !sky.Iss.AboveHorizon) return;
            DateTime at = sky.UpdatedUtc == DateTime.MinValue
                ? DateTime.UtcNow : sky.UpdatedUtc;
            bool add = _issTrail.Count == 0;
            if (!add)
            {
                SkyTrailSample last = _issTrail[_issTrail.Count - 1];
                add = Math.Abs(last.BearingDegrees - sky.Iss.BearingDegrees) > 0.02 ||
                    Math.Abs(last.ElevationDegrees - sky.Iss.ElevationDegrees) > 0.02;
            }
            if (add)
            {
                _issTrail.Add(new SkyTrailSample
                {
                    AtUtc = at,
                    BearingDegrees = sky.Iss.BearingDegrees,
                    ElevationDegrees = sky.Iss.ElevationDegrees
                });
                while (_issTrail.Count > 12) _issTrail.RemoveAt(0);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 160 || height < 160) return;

            dc.DrawRectangle(Ui.SkyBg, null, new Rect(0, 0, width, height));
            Point center = new Point(width / 2.0, height / 2.0);
            double radiusX = width / 2.0 - 20.0;
            double radiusY = height / 2.0 - 20.0;
            List<Rect> labels = new List<Rect>();

            DrawElevationContours(dc, center, radiusX, radiusY, labels);
            DrawAircraftRangeScale(dc, center, radiusX, labels);
            DrawOrientation(dc, width, height, labels);
            DrawZenith(dc, center);
            labels.Add(new Rect(center.X - 62, center.Y - 16, 124, 64));

            if (_sky == null)
            {
                DrawText(dc, "CONNECTING TO LIVE SKY", 14, Ui.TextDim,
                    new Point(center.X, center.Y + 22), true, true);
                return;
            }

            DateTime now = DateTime.UtcNow;
            UpdateCelestial(now);
            DrawIssPassStatus(dc, now, center, labels);
            if (_sky.AirportMarkersEnabled)
                DrawAirportMarkers(dc, center, radiusX, radiusY, labels,
                    width, height);

            foreach (StarReading star in _liveStars)
            {
                Point point = SkyPoint(center, radiusX, radiusY,
                    star.BearingDegrees, star.AltitudeDegrees);
                Brush color = Ui.Star;
                double size = Math.Max(1.2, 2.8 - Math.Max(0.0, star.Magnitude) * 0.55);
                dc.DrawEllipse(color, null, point, size, size);
                if (!AircraftNear(point, now, center, radiusX, radiusY))
                    DrawPlacedLabel(dc, star.Name, 9, color,
                        new Point(point.X + 5, point.Y - 6), labels,
                        width, height, false);
            }

            foreach (PlanetReading planet in _livePlanets)
            {
                Point point = SkyPoint(center, radiusX, radiusY,
                    planet.BearingDegrees, planet.AltitudeDegrees);
                Brush color = Ui.Planet;
                DrawPlanetArrow(dc, planet, point, center, radiusX, radiusY,
                    color);
                DrawPlacedLabel(dc, planet.Name, 11, color,
                    new Point(point.X + 7, point.Y - 7), labels, width, height, false);
            }

            if (_sky.Iss != null && _sky.Iss.AboveHorizon)
            {
                DrawIss(dc, now, center, radiusX, radiusY,
                    labels, width, height);
            }

            List<PlaneReading> labelPriority = new List<PlaneReading>(_sky.Planes);
            labelPriority.RemoveAll(delegate(PlaneReading plane)
            {
                return ViewingElevation(plane) < MinimumAircraftElevation;
            });
            labelPriority.Sort(delegate(PlaneReading a, PlaneReading b)
            {
                return a.DistanceKm.CompareTo(b.DistanceKm);
            });
            HashSet<string> labeled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int labelLimit = Math.Min(6, labelPriority.Count);
            for (int i = 0; i < labelLimit; i++)
                if (!string.IsNullOrWhiteSpace(labelPriority[i].Identifier))
                    labeled.Add(labelPriority[i].Identifier);

            foreach (KeyValuePair<string, AircraftTrack> pair in _tracks)
            {
                string id = pair.Key;
                AircraftTrack track = pair.Value;
                if ((now - track.LastReceivedUtc).TotalSeconds > 90.0) continue;
                DrawAircraftTrack(dc, track, now, center, radiusX, radiusY,
                    labels, width, height, labeled.Contains(id));
            }
        }

        private void DrawIssPassStatus(DrawingContext dc, DateTime now,
            Point center, List<Rect> labels)
        {
            if (_sky.Iss == null || !_sky.Iss.PassPredictionAvailable) return;
            string text;
            bool passActive = _sky.Iss.NextSetUtc != DateTime.MinValue &&
                now >= _sky.Iss.NextRiseUtc && now < _sky.Iss.NextSetUtc;
            if (passActive)
            {
                text = "ISS OVERHEAD  /  TLE  /  SETS IN " + ShortCountdown(
                    _sky.Iss.NextSetUtc - now);
            }
            else
            {
                text = "NEXT ISS PASS  /  TLE EST.  /  " + ShortCountdown(
                    _sky.Iss.NextRiseUtc - now) + "  /  PEAK " +
                    Math.Round(_sky.Iss.PassPeakElevationDegrees).ToString("0") +
                    "\u00B0";
            }
            FormattedText formatted = Format(text, 10, Ui.Sunrise, true);
            Point point = new Point(center.X - formatted.Width / 2.0, 25.0);
            dc.DrawText(formatted, point);
            labels.Add(new Rect(point.X - 4, point.Y - 2,
                formatted.Width + 8, formatted.Height + 4));
        }

        private void DrawAirportMarkers(DrawingContext dc, Point center,
            double radiusX, double radiusY, List<Rect> labels,
            double width, double height)
        {
            foreach (AirportReading airport in _sky.Airports)
            {
                Point inner = BearingPoint(center, radiusX, radiusY,
                    airport.BearingDegrees, 0.91);
                Point marker = BearingPoint(center, radiusX, radiusY,
                    airport.BearingDegrees, 0.975);
                Pen pen = new Pen(Ui.Airport, 1.2);
                dc.DrawLine(pen, inner, marker);
                dc.DrawEllipse(Ui.SkyBg, pen, marker, 4.0, 4.0);

                double dx = center.X - marker.X;
                double dy = center.Y - marker.Y;
                double length = Math.Max(1.0, Math.Sqrt(dx * dx + dy * dy));
                Point desired = new Point(marker.X + dx / length * 14.0 + 5.0,
                    marker.Y + dy / length * 14.0 - 7.0);
                string label = airport.Code + "  /  " +
                    Math.Round(airport.DistanceKm).ToString("0") + " KM";
                DrawPlacedLabel(dc, label, 9, Ui.Airport, desired, labels,
                    width, height, true);
            }
        }

        private static string ShortCountdown(TimeSpan remaining)
        {
            if (remaining.TotalSeconds < 0.0) remaining = TimeSpan.Zero;
            if (remaining.TotalHours >= 1.0)
                return ((int)remaining.TotalHours).ToString() + "H " +
                    remaining.Minutes.ToString("00") + "M";
            if (remaining.TotalMinutes >= 1.0)
                return Math.Max(0, remaining.Minutes).ToString() + "M";
            return Math.Max(0, remaining.Seconds).ToString() + "S";
        }

        private void UpdateCelestial(DateTime now)
        {
            if ((now - _celestialUpdatedUtc).TotalSeconds < 5.0) return;
            _livePlanets = AmbientService.CalculatePlanets(now,
                _sky.ObserverLatitude, _sky.ObserverLongitude);
            _futurePlanets = AmbientService.CalculatePlanets(now.AddMinutes(10),
                _sky.ObserverLatitude, _sky.ObserverLongitude);
            _liveStars = AmbientService.CalculateStars(now,
                _sky.ObserverLatitude, _sky.ObserverLongitude);
            _celestialUpdatedUtc = now;
        }

        private void DrawPlanetArrow(DrawingContext dc, PlanetReading planet,
            Point point, Point center, double radiusX, double radiusY, Brush color)
        {
            PlanetReading future = null;
            foreach (PlanetReading candidate in _futurePlanets)
            {
                if (string.Equals(candidate.Name, planet.Name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    future = candidate;
                    break;
                }
            }
            if (future == null)
            {
                dc.DrawEllipse(color, null, point, 2.5, 2.5);
                return;
            }

            Point futurePoint = SkyPoint(center, radiusX, radiusY,
                future.BearingDegrees, future.AltitudeDegrees);
            double dx = futurePoint.X - point.X;
            double dy = futurePoint.Y - point.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.0001)
            {
                dc.DrawEllipse(color, null, point, 2.5, 2.5);
                return;
            }
            dx /= length;
            dy /= length;
            Point tail = new Point(point.X - dx * 5.0, point.Y - dy * 5.0);
            Point tip = new Point(point.X + dx * 7.0, point.Y + dy * 7.0);
            dc.DrawLine(new Pen(color, 1.7), tail, tip);
            StreamGeometry arrow = new StreamGeometry();
            using (StreamGeometryContext geometry = arrow.Open())
            {
                geometry.BeginFigure(tip, true, true);
                geometry.LineTo(new Point(tip.X - dx * 5.0 - dy * 3.0,
                    tip.Y - dy * 5.0 + dx * 3.0), true, false);
                geometry.LineTo(new Point(tip.X - dx * 5.0 + dy * 3.0,
                    tip.Y - dy * 5.0 - dx * 3.0), true, false);
            }
            arrow.Freeze();
            dc.DrawGeometry(color, null, arrow);
        }

        private void DrawIss(DrawingContext dc, DateTime now, Point center,
            double radiusX, double radiusY, List<Rect> labels,
            double width, double height)
        {
            Brush color = Ui.Sunrise;
            List<Point> points = new List<Point>();
            foreach (SkyTrailSample sample in _issTrail)
                points.Add(SkyPoint(center, radiusX, radiusY,
                    sample.BearingDegrees, sample.ElevationDegrees));
            if (points.Count == 0)
                points.Add(SkyPoint(center, radiusX, radiusY,
                    _sky.Iss.BearingDegrees, _sky.Iss.ElevationDegrees));

            Point current = points[points.Count - 1];
            if (points.Count > 1)
            {
                SkyTrailSample previousSample = _issTrail[_issTrail.Count - 2];
                SkyTrailSample lastSample = _issTrail[_issTrail.Count - 1];
                double seconds = Math.Max(1.0,
                    (lastSample.AtUtc - previousSample.AtUtc).TotalSeconds);
                double elapsed = Math.Max(0.0, Math.Min(20.0,
                    (now - lastSample.AtUtc).TotalSeconds));
                Point previous = points[points.Count - 2];
                current = new Point(current.X + (current.X - previous.X) / seconds * elapsed,
                    current.Y + (current.Y - previous.Y) / seconds * elapsed);
                points.Add(current);

                StreamGeometry path = new StreamGeometry();
                using (StreamGeometryContext geometry = path.Open())
                {
                    geometry.BeginFigure(points[0], false, false);
                    for (int i = 1; i < points.Count; i++)
                        geometry.LineTo(points[i], true, false);
                }
                path.Freeze();
                dc.DrawGeometry(null, new Pen(Ui.IssLine, 1.4), path);
            }
            dc.DrawEllipse(null, new Pen(color, 1.5), current, 5.5, 5.5);
            dc.DrawEllipse(color, null, current, 1.8, 1.8);
            string label = "ISS  /  " +
                AmbientService.Cardinal(_sky.Iss.BearingDegrees) + " " +
                Math.Round(_sky.Iss.BearingDegrees).ToString("0") + "\u00B0  /  EL " +
                Math.Round(_sky.Iss.ElevationDegrees).ToString("0") +
                "\u00B0  /  " + Math.Round(_sky.Iss.DistanceKm).ToString("0") + " km";
            double labelWidth = Format(label, 12, color, true).Width;
            Point labelPoint = current.X > width * 0.58
                ? new Point(current.X - labelWidth - 16, current.Y - 9)
                : new Point(current.X + 10, current.Y - 9);
            DrawPlacedLabel(dc, label, 12, color, labelPoint, labels,
                width, height, true);
        }

        private void DrawOrientation(DrawingContext dc, double width, double height,
            List<Rect> labels)
        {
            Pen corner = new Pen(Ui.ReferenceDim, 1.2);
            double length = 18.0;
            dc.DrawLine(corner, new Point(16, 16), new Point(16 + length, 16));
            dc.DrawLine(corner, new Point(16, 16), new Point(16, 16 + length));
            dc.DrawLine(corner, new Point(width - 16, 16),
                new Point(width - 16 - length, 16));
            dc.DrawLine(corner, new Point(width - 16, 16),
                new Point(width - 16, 16 + length));
            dc.DrawLine(corner, new Point(16, height - 16),
                new Point(16 + length, height - 16));
            dc.DrawLine(corner, new Point(16, height - 16),
                new Point(16, height - 16 - length));
            dc.DrawLine(corner, new Point(width - 16, height - 16),
                new Point(width - 16 - length, height - 16));
            dc.DrawLine(corner, new Point(width - 16, height - 16),
                new Point(width - 16, height - 16 - length));

            // This is the sky seen from underneath, not a ground map seen
            // from above, so bearings increase toward the left side.
            DrawCornerBearing(dc, new Point(24, 22), _facingDegrees + 45, false, labels);
            DrawCornerBearing(dc, new Point(width - 24, 22), _facingDegrees - 45, true, labels);
            DrawCornerBearing(dc, new Point(width - 24, height - 40),
                _facingDegrees - 135, true, labels);
            DrawCornerBearing(dc, new Point(24, height - 40),
                _facingDegrees + 135, false, labels);

        }

        private void DrawElevationContours(DrawingContext dc, Point center,
            double radiusX, double radiusY, List<Rect> labels)
        {
            int[] elevations = new int[] { 25, 50, 75 };
            foreach (int elevation in elevations)
            {
                double scale = Math.Cos(elevation * Math.PI / 180.0);
                Rect contour = new Rect(center.X - radiusX * scale,
                    center.Y - radiusY * scale,
                    radiusX * scale * 2.0, radiusY * scale * 2.0);
                Pen pen = new Pen(Ui.ReferenceDim, 1.0);
                pen.DashStyle = new DashStyle(new double[] { 1, 9 }, 0);
                dc.DrawRectangle(null, pen, contour);
                string text = "EL " + elevation.ToString() + "\u00B0";
                FormattedText formatted = Format(text, 9, Ui.Reference, false);
                Point position = new Point(center.X + 7, contour.Top + 3);
                dc.DrawText(formatted, position);
                labels.Add(new Rect(position.X - 3, position.Y - 2,
                    formatted.Width + 6, formatted.Height + 4));
            }
        }

        private void DrawAircraftRangeScale(DrawingContext dc, Point center,
            double radiusX, List<Rect> labels)
        {
            int rangeKm = Math.Max(20, _aircraftRadiusKm);
            double endX = center.X + radiusX * AircraftRangeInset;
            double startX = center.X + 68.0;
            if (endX - startX < 135.0) return;

            Pen pen = new Pen(Ui.Reference, 1.3);
            pen.DashStyle = new DashStyle(new double[] { 2, 6 }, 0);
            dc.DrawLine(pen, new Point(startX, center.Y),
                new Point(endX, center.Y));

            FormattedText heading = Format("AIRCRAFT DISTANCE", 11,
                Ui.Reference, true);
            Point headingPoint = new Point(startX, center.Y - heading.Height - 3.0);
            dc.DrawText(heading, headingPoint);
            labels.Add(new Rect(headingPoint.X - 2, headingPoint.Y - 1,
                heading.Width + 4, heading.Height + 2));

            double[] fractions = new double[] { 0.25, 0.50, 0.75, 1.0 };
            Pen tickPen = new Pen(Ui.Reference, 1.2);
            foreach (double fraction in fractions)
            {
                double x = center.X + radiusX * AircraftRangeInset * fraction;
                dc.DrawLine(tickPen,
                    new Point(x, center.Y - 6), new Point(x, center.Y + 6));
                string text = Math.Round(rangeKm * fraction).ToString("0") + " KM";
                if (fraction >= 0.999) text += " LIMIT";
                FormattedText formatted = Format(text, 10, Ui.Reference, false);
                Point position = new Point(x - formatted.Width / 2.0, center.Y + 8.0);
                dc.DrawText(formatted, position);
                labels.Add(new Rect(position.X - 2, position.Y - 1,
                    formatted.Width + 4, formatted.Height + 2));
            }
        }

        private void DrawCornerBearing(DrawingContext dc, Point point,
            double bearing, bool right, List<Rect> labels)
        {
            int value = Normalize(bearing);
            string text = AmbientService.Cardinal(value) + "  " + value.ToString() +
                "\u00B0  /  HORIZON";
            FormattedText formatted = Format(text, 11, Ui.Reference, true);
            double x = right ? point.X - formatted.Width : point.X;
            dc.DrawText(formatted, new Point(x, point.Y));
            labels.Add(new Rect(x - 3, point.Y - 2,
                formatted.Width + 6, formatted.Height + 4));
        }

        private void DrawZenith(DrawingContext dc, Point center)
        {
            Brush dim = Ui.Reference;
            Pen pen = new Pen(dim, 1.3);
            dc.DrawEllipse(null, pen, center, 5, 5);
            dc.DrawLine(pen, new Point(center.X - 10, center.Y),
                new Point(center.X - 5, center.Y));
            dc.DrawLine(pen, new Point(center.X + 5, center.Y),
                new Point(center.X + 10, center.Y));
            dc.DrawLine(pen, new Point(center.X, center.Y - 10),
                new Point(center.X, center.Y - 5));
            dc.DrawLine(pen, new Point(center.X, center.Y + 5),
                new Point(center.X, center.Y + 10));
            DrawText(dc, "ZENITH  /  90\u00B0", 11, dim,
                new Point(center.X, center.Y + 17), true, true);
            double pulse = 2.5 + (Math.Sin(DateTime.UtcNow.TimeOfDay.TotalSeconds * 3.0) + 1.0);
            dc.DrawEllipse(Ui.Accent, null,
                new Point(center.X - 49, center.Y + 42), pulse, pulse);
            DrawText(dc, "LIVE  " + DateTime.Now.ToString("HH:mm:ss"), 12,
                Ui.Reference, new Point(center.X - 39, center.Y + 33), false, true);
        }

        private void DrawAircraftTrack(DrawingContext dc, AircraftTrack track,
            DateTime now, Point center, double radiusX, double radiusY,
            List<Rect> labels, double width, double height, bool showLabel)
        {
            double elapsed = Math.Max(0.0, Math.Min(75.0,
                (now - track.AnchorUtc).TotalSeconds));
            double speedKmSecond = track.HasTrack
                ? track.SpeedKnots * 1.852 / 3600.0 : 0.0;
            double course = track.TrackDegrees * Math.PI / 180.0;
            double velocityEast = speedKmSecond * Math.Sin(course);
            double velocityNorth = speedKmSecond * Math.Cos(course);
            double east = track.AnchorEastKm + velocityEast * elapsed;
            double north = track.AnchorNorthKm + velocityNorth * elapsed;
            double distanceKm = Math.Sqrt(east * east + north * north);
            double elevation = AmbientService.AircraftElevationDegrees(distanceKm,
                track.AltitudeFeet);
            if (elevation < MinimumAircraftElevation) return;

            List<Point> observed = new List<Point>();
            foreach (TrackSample sample in track.Samples)
                observed.Add(AircraftPoint(center, radiusX, radiusY,
                    sample.EastKm, sample.NorthKm));
            Point current = AircraftPoint(center, radiusX, radiusY, east, north);
            if (observed.Count == 0 || Distance(observed[observed.Count - 1].X,
                observed[observed.Count - 1].Y, current.X, current.Y) > 1.0)
                observed.Add(current);

            Pen historyPen = new Pen(Ui.AircraftHistory, 1.8);
            if (observed.Count > 1)
            {
                StreamGeometry history = new StreamGeometry();
                using (StreamGeometryContext geometry = history.Open())
                {
                    geometry.BeginFigure(observed[0], false, false);
                    for (int i = 1; i < observed.Count; i++)
                        geometry.LineTo(observed[i], true, false);
                }
                history.Freeze();
                dc.DrawGeometry(null, historyPen, history);
                foreach (Point sample in observed)
                    dc.DrawEllipse(Ui.AircraftHistory, null, sample, 1.5, 1.5);
            }

            Point directionPoint = current;
            if (track.HasTrack && track.SpeedKnots > 20.0)
            {
                directionPoint = AircraftPoint(center, radiusX, radiusY,
                    east + velocityEast * 10.0,
                    north + velocityNorth * 10.0);
                if (showLabel) DrawCourseLine(dc, center, radiusX, radiusY,
                    current, directionPoint);
            }

            double screenRotation = track.HasTrack
                ? Math.Atan2(directionPoint.X - current.X,
                    current.Y - directionPoint.Y) * 180.0 / Math.PI
                : 0.0;
            DrawAircraft(dc, current, screenRotation);

            if (!showLabel) return;
            string primaryLabel = string.IsNullOrWhiteSpace(track.Label)
                ? "AIRCRAFT" : track.Label.Trim();
            string label = primaryLabel;
            if (!string.IsNullOrWhiteSpace(track.AircraftType))
                label += "  /  " + track.AircraftType.Trim();
            if (!string.IsNullOrWhiteSpace(track.Registration) &&
                !string.Equals(track.Registration.Trim(), primaryLabel,
                    StringComparison.OrdinalIgnoreCase))
                label += "  /  " + track.Registration.Trim();
            string detail = track.HasTrack
                ? AmbientService.Cardinal(track.TrackDegrees) + " " +
                    Math.Round(track.TrackDegrees).ToString("0") + "\u00B0  /  " +
                    Math.Round(track.SpeedKnots).ToString("0") + " kt  /  "
                : "COURSE UNREPORTED  /  ";
            detail += Math.Round(track.AltitudeFeet / 1000.0).ToString("0") +
                "k ft  /  " + Math.Round(distanceKm).ToString("0") +
                " km  /  EL " + Math.Round(elevation).ToString("0") + "\u00B0";
            DrawAircraftLabel(dc, label, detail, new Point(current.X + 15,
                current.Y - 17), labels, width, height);
        }

        private void DrawAircraft(DrawingContext dc, Point point, double rotation)
        {
            StreamGeometry plane = new StreamGeometry();
            using (StreamGeometryContext geometry = plane.Open())
            {
                geometry.BeginFigure(new Point(0, -12), true, true);
                geometry.LineTo(new Point(2, -2), true, false);
                geometry.LineTo(new Point(10, 3), true, false);
                geometry.LineTo(new Point(10, 6), true, false);
                geometry.LineTo(new Point(2, 4), true, false);
                geometry.LineTo(new Point(2, 10), true, false);
                geometry.LineTo(new Point(5, 12), true, false);
                geometry.LineTo(new Point(0, 11), true, false);
                geometry.LineTo(new Point(-5, 12), true, false);
                geometry.LineTo(new Point(-2, 10), true, false);
                geometry.LineTo(new Point(-2, 4), true, false);
                geometry.LineTo(new Point(-10, 6), true, false);
                geometry.LineTo(new Point(-10, 3), true, false);
                geometry.LineTo(new Point(-2, -2), true, false);
            }
            plane.Freeze();
            dc.PushTransform(new TranslateTransform(point.X, point.Y));
            dc.PushTransform(new RotateTransform(rotation));
            dc.DrawGeometry(Ui.Accent, new Pen(Ui.AircraftOutline, 0.8), plane);
            dc.Pop();
            dc.Pop();
        }

        private void DrawCourseLine(DrawingContext dc, Point center,
            double radiusX, double radiusY, Point current, Point directionPoint)
        {
            double rawX = directionPoint.X - current.X;
            double rawY = directionPoint.Y - current.Y;
            if (Math.Abs(rawX) + Math.Abs(rawY) < 0.01) return;
            double left = center.X - radiusX;
            double right = center.X + radiusX;
            double top = center.Y - radiusY;
            double bottom = center.Y + radiusY;
            List<double> hits = new List<double>();
            AddVerticalHit(hits, left, top, bottom, current, rawX, rawY);
            AddVerticalHit(hits, right, top, bottom, current, rawX, rawY);
            AddHorizontalHit(hits, top, left, right, current, rawX, rawY);
            AddHorizontalHit(hits, bottom, left, right, current, rawX, rawY);
            if (hits.Count == 0) return;
            hits.Sort();
            double endParameter = double.MaxValue;
            foreach (double hit in hits)
                if (hit > 0.0 && hit < endParameter) endParameter = hit;
            if (endParameter == double.MaxValue) return;
            double directionLength = Math.Sqrt(rawX * rawX + rawY * rawY);
            double startParameter = 16.0 / Math.Max(0.001, directionLength);
            if (endParameter <= startParameter) return;
            Point start = new Point(current.X + rawX * startParameter,
                current.Y + rawY * startParameter);
            Point end = new Point(current.X + rawX * endParameter,
                current.Y + rawY * endParameter);
            Pen coursePen = new Pen(Ui.AircraftCourse, 1.1);
            coursePen.DashStyle = new DashStyle(new double[] { 6, 8 }, 0);
            dc.DrawLine(coursePen, start, end);
        }

        private static void AddVerticalHit(List<double> hits, double x,
            double top, double bottom, Point current, double rawX, double rawY)
        {
            if (Math.Abs(rawX) < 0.000001) return;
            double t = (x - current.X) / rawX;
            double y = current.Y + rawY * t;
            if (y >= top - 0.1 && y <= bottom + 0.1) hits.Add(t);
        }

        private static void AddHorizontalHit(List<double> hits, double y,
            double left, double right, Point current, double rawX, double rawY)
        {
            if (Math.Abs(rawY) < 0.000001) return;
            double t = (y - current.Y) / rawY;
            double x = current.X + rawX * t;
            if (x >= left - 0.1 && x <= right + 0.1) hits.Add(t);
        }

        private void DrawAircraftLabel(DrawingContext dc, string title, string detail,
            Point desired, List<Rect> occupied, double width, double height)
        {
            FormattedText first = Format(title, 12, Ui.Text, true);
            FormattedText second = Format(detail, 10, Ui.TextDim, false);
            double boxWidth = Math.Max(first.Width, second.Width) + 12;
            double boxHeight = first.Height + second.Height + 8;
            Rect rect = PlaceRect(new Rect(desired.X, desired.Y, boxWidth, boxHeight),
                occupied, width, height);
            dc.DrawRectangle(Ui.LabelBg, new Pen(Ui.LabelLine, 1), rect);
            dc.DrawText(first, new Point(rect.X + 6, rect.Y + 3));
            dc.DrawText(second, new Point(rect.X + 6, rect.Y + 3 + first.Height));
            occupied.Add(rect);
        }

        private void DrawPlacedLabel(DrawingContext dc, string text, double size,
            Brush color, Point desired, List<Rect> occupied, double width,
            double height, bool strong)
        {
            FormattedText formatted = Format(text, size, color, strong);
            Rect rect = PlaceRect(new Rect(desired.X, desired.Y,
                formatted.Width + 4, formatted.Height + 2), occupied, width, height);
            dc.DrawText(formatted, new Point(rect.X + 2, rect.Y + 1));
            occupied.Add(rect);
        }

        private Rect PlaceRect(Rect desired, List<Rect> occupied,
            double width, double height)
        {
            Rect chosen = desired;
            for (int row = 0; row < 12; row++)
            {
                double vertical = row == 0 ? 0.0 :
                    ((row % 2 == 1 ? 1.0 : -1.0) * ((row + 1) / 2) *
                    (desired.Height + 7.0));
                for (int side = 0; side < 2; side++)
                {
                    double horizontal = side == 0 ? 0.0 : -desired.Width - 24.0;
                    Rect candidate = new Rect(desired.X + horizontal,
                        desired.Y + vertical, desired.Width, desired.Height);
                    candidate.X = Math.Max(18,
                        Math.Min(width - candidate.Width - 18, candidate.X));
                    candidate.Y = Math.Max(44,
                        Math.Min(height - candidate.Height - 44, candidate.Y));
                    bool clear = true;
                    foreach (Rect other in occupied)
                    {
                        Rect padded = other;
                        padded.Inflate(5, 4);
                        if (padded.IntersectsWith(candidate)) { clear = false; break; }
                    }
                    chosen = candidate;
                    if (clear) return candidate;
                }
            }
            return chosen;
        }

        private static double ViewingElevation(PlaneReading plane)
        {
            return AmbientService.AircraftElevationDegrees(plane.DistanceKm,
                plane.AltitudeFeet);
        }

        private Point AircraftPoint(Point center, double radiusX, double radiusY,
            double eastKm, double northKm)
        {
            double distanceKm = Math.Sqrt(eastKm * eastKm + northKm * northKm);
            double bearing = Math.Atan2(eastKm, northKm) * 180.0 / Math.PI;
            if (bearing < 0) bearing += 360.0;
            double rangeKm = Math.Max(20.0, _aircraftRadiusKm);
            return BearingPoint(center, radiusX, radiusY, bearing,
                distanceKm / rangeKm * AircraftRangeInset);
        }

        private Point SkyPoint(Point center, double radiusX, double radiusY,
            double bearing, double elevation)
        {
            double altitude = Math.Max(0.0, Math.Min(90.0, elevation));
            // Orthographic all-sky projection: zenith is the center and the
            // horizon is the edge. At 50 degrees elevation an object therefore
            // sits cos(50 degrees), or 64%, of the way out—not near the zenith.
            double distance = Math.Cos(altitude * Math.PI / 180.0);
            return BearingPoint(center, radiusX, radiusY, bearing, distance);
        }

        private Point BearingPoint(Point center, double radiusX, double radiusY,
            double bearing, double distance)
        {
            double angle = (bearing - _facingDegrees) * Math.PI / 180.0;
            // Looking up mirrors east/west relative to a conventional map:
            // with north at the top, east belongs on the viewer's left.
            double dx = -Math.Sin(angle);
            double dy = -Math.Cos(angle);
            double scaleX = Math.Abs(dx) < 0.000001
                ? double.MaxValue : radiusX / Math.Abs(dx);
            double scaleY = Math.Abs(dy) < 0.000001
                ? double.MaxValue : radiusY / Math.Abs(dy);
            double horizon = Math.Min(scaleX, scaleY);
            return new Point(center.X + dx * horizon * distance,
                center.Y + dy * horizon * distance);
        }

        private bool AircraftNear(Point point, DateTime now, Point center,
            double radiusX, double radiusY)
        {
            foreach (KeyValuePair<string, AircraftTrack> pair in _tracks)
            {
                AircraftTrack track = pair.Value;
                if ((now - track.LastReceivedUtc).TotalSeconds > 90.0) continue;
                double elapsed = Math.Max(0.0, Math.Min(75.0,
                    (now - track.AnchorUtc).TotalSeconds));
                double speed = track.HasTrack
                    ? track.SpeedKnots * 1.852 / 3600.0 : 0.0;
                double course = track.TrackDegrees * Math.PI / 180.0;
                double east = track.AnchorEastKm + speed * Math.Sin(course) * elapsed;
                double north = track.AnchorNorthKm + speed * Math.Cos(course) * elapsed;
                Point aircraft = AircraftPoint(center, radiusX, radiusY, east, north);
                if (Distance(point.X, point.Y, aircraft.X, aircraft.Y) < 32.0)
                    return true;
            }
            return false;
        }

        private static double Distance(double x1, double y1, double x2, double y2)
        {
            double x = x2 - x1;
            double y = y2 - y1;
            return Math.Sqrt(x * x + y * y);
        }

        private static int Normalize(double degrees)
        {
            int value = (int)Math.Round(degrees) % 360;
            if (value < 0) value += 360;
            return value;
        }

        private static double SignedAngle(double degrees)
        {
            degrees %= 360.0;
            if (degrees > 180.0) degrees -= 360.0;
            if (degrees < -180.0) degrees += 360.0;
            return degrees;
        }

        private FormattedText Format(string text, double size, Brush color, bool strong)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, strong ? _strong : _ui, size, color);
        }

        private void DrawText(DrawingContext dc, string text, double size,
            Brush color, Point point, bool centered, bool strong)
        {
            DrawText(dc, text, size, color, point, centered, strong, false);
        }

        private void DrawText(DrawingContext dc, string text, double size,
            Brush color, Point point, bool centered, bool strong, bool alignRight)
        {
            FormattedText formatted = Format(text, size, color, strong);
            double x = point.X;
            double y = point.Y;
            if (centered)
            {
                x -= formatted.Width / 2.0;
                y -= formatted.Height / 2.0;
            }
            else if (alignRight) x -= formatted.Width;
            dc.DrawText(formatted, new Point(x, y));
        }
    }
}

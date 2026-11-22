using System;
using UnityEngine;

namespace LocalSkyLighting {
    public static class SolarDirection {
        // Low-precision solar ephemeris. Result is (east, north, up).
        public static Vector3 FromLocation(double latitude, double longitude, DateTimeOffset time) {
            double days = (time - new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero)).TotalDays;
            double anomaly = Radians(357.529 + 0.98560028 * days);
            double ecliptic = Radians(280.459 + 0.98564736 * days + 1.915 * Math.Sin(anomaly) + 0.020 * Math.Sin(2 * anomaly));
            double obliquity = Radians(23.439 - 0.00000036 * days);
            double rightAscension = Math.Atan2(Math.Cos(obliquity) * Math.Sin(ecliptic), Math.Cos(ecliptic));
            double declination = Math.Asin(Math.Sin(obliquity) * Math.Sin(ecliptic));
            double hourAngle = Radians(280.46061837 + 360.98564736629 * days + longitude) - rightAscension;
            double lat = Radians(latitude);
            double meridian = Math.Cos(declination) * Math.Cos(hourAngle);
            return new Vector3(
                (float)(-Math.Cos(declination) * Math.Sin(hourAngle)),
                (float)(Math.Cos(lat) * Math.Sin(declination) - Math.Sin(lat) * meridian),
                (float)(Math.Sin(lat) * Math.Sin(declination) + Math.Cos(lat) * meridian));
        }

        private static double Radians(double degrees) => (degrees % 360) * Math.PI / 180;
    }
}

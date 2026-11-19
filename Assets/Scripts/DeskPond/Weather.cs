using System;

namespace DeskPond
{
    public enum WeatherKind
    {
        Clear,
        Rain,
        Fog,
        Snow
    }

    /// <summary>
    /// The pond's weather. It is worked out from the clock alone, in two-hour blocks, so it needs
    /// no saved state, no network, and is the same after a restart. Snow only falls in winter.
    /// -weather=rain|fog|snow|clear on the command line pins it for testing.
    /// </summary>
    public static class Weather
    {
        static readonly WeatherKind? Override = ReadOverride();

        static WeatherKind? ReadOverride()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (a.StartsWith("-weather=") && Enum.TryParse<WeatherKind>(a.Substring(9), true, out var w))
                    return w;
            return null;
        }

        /// <summary>Set by snapshot mode to show a particular sky.</summary>
        public static WeatherKind? Forced;

        public static WeatherKind Now => At(DateTime.Now);

        public static WeatherKind At(DateTime local)
        {
            if (Forced.HasValue)
                return Forced.Value;
            if (Override.HasValue)
                return Override.Value;

            var block = local.Year * 5000 + local.DayOfYear * 12 + local.Hour / 2;
            var roll = Hash(block) % 100;
            if (Season(local) == 3)
                return roll < 52 ? WeatherKind.Clear : roll < 64 ? WeatherKind.Rain : roll < 76 ? WeatherKind.Fog : WeatherKind.Snow;
            return roll < 62 ? WeatherKind.Clear : roll < 86 ? WeatherKind.Rain : WeatherKind.Fog;
        }

        /// <summary>0 spring, 1 summer, 2 autumn, 3 winter, by the calendar month.</summary>
        public static int Season(DateTime local)
        {
            var m = local.Month;
            return m >= 3 && m <= 5 ? 0 : m >= 6 && m <= 8 ? 1 : m >= 9 && m <= 11 ? 2 : 3;
        }

        public static string SeasonName(int season)
        {
            switch (season)
            {
                case 0: return "spring";
                case 1: return "summer";
                case 2: return "autumn";
                default: return "winter";
            }
        }

        /// <summary>What the cat says when the sky changes.</summary>
        public static string Announce(WeatherKind w)
        {
            switch (w)
            {
                case WeatherKind.Rain: return "Rain! Loach weather.";
                case WeatherKind.Fog: return "Fog rolling in... spooky.";
                case WeatherKind.Snow: return "Snow! Icefish are about.";
                default: return "Sky's clearing up.";
            }
        }

        static int Hash(int n)
        {
            unchecked
            {
                var h = (uint)n * 2654435761u;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (int)(h & 0x7fffffff);
            }
        }
    }
}

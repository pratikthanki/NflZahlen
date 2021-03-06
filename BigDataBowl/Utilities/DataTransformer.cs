using System;
using System.Collections.Generic;
using System.Linq;
using BigDataBowl.DataModels;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;

namespace BigDataBowl.Utilities
{
    public class DataTransformer
    {
        private static ILogger _logger;
        private const string RelativePath = @"../../../../Data/train.csv";
        private static readonly string DataPath = CsvReader.GetAbsolutePath(RelativePath);

        public DataTransformer(ILogger logger)
        {
            _logger = logger;
        }

        public IEnumerable<RushingRaw> ReadAndPreprocess()
        {
            var rawCsv = ReadTracking();

            return PreProcess(rawCsv);
        }

        private IList<RushingRaw> ReadTracking()
        {
            _logger.LogInformation($"Starting {nameof(ReadTracking)}");

            var rushingPlays = new List<RushingRaw>();
            const string GameClockFormat = "HH:mm:ss";
            const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            const string BirthDateFormat = "MM/dd/yyyy";

            _logger.LogInformation($"Reading from: {DataPath}");

            using var parser = new TextFieldParser(DataPath) {HasFieldsEnclosedInQuotes = true};
            parser.SetDelimiters(",");

            // Skip the header row
            if (!parser.EndOfData)
                parser.ReadLine();

            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields();
                var play = new RushingRaw
                {
                    GameId = StringParser.ToLong(fields[0]),
                    PlayId = StringParser.ToLong(fields[1]),
                    Team = fields[2],
                    X = StringParser.ToFloat(fields[3]),
                    Y = StringParser.ToFloat(fields[4]),
                    S = StringParser.ToFloat(fields[5]),
                    A = StringParser.ToFloat(fields[6]),
                    Dis = StringParser.ToFloat(fields[7]),
                    Orientation = StringParser.ToFloat(fields[8]),
                    Dir = StringParser.ToFloat(fields[9]),
                    NflId = StringParser.ToLong(fields[10]),
                    DisplayName = fields[11],
                    JerseyNumber = StringParser.ToInt(fields[12]),
                    Season = StringParser.ToInt(fields[13]),
                    YardLine = StringParser.ToInt(fields[14]),
                    Quarter = StringParser.ToInt(fields[15]),
                    GameClock = StringParser.ToDateTime(fields[16], GameClockFormat),
                    PossessionTeam = fields[17],
                    Down = StringParser.ToInt(fields[18]),
                    Distance = StringParser.ToInt(fields[19]),
                    FieldPosition = fields[20],
                    HomeScoreBeforePlay = StringParser.ToInt(fields[21]),
                    VisitorScoreBeforePlay = StringParser.ToInt(fields[22]),
                    NflIdRusher = StringParser.ToLong(fields[23]),
                    OffenseFormation = fields[24],
                    OffensePersonnel = fields[25],
                    DefendersInTheBox = StringParser.ToInt(fields[26]),
                    DefensePersonnel = fields[27],
                    PlayDirection = fields[28],
                    TimeHandoff = StringParser.ToDateTime(fields[29], TimeFormat),
                    TimeSnap = StringParser.ToDateTime(fields[30], TimeFormat),
                    Yards = StringParser.ToInt(fields[31]),
                    PlayerHeight = fields[32],
                    PlayerWeight = StringParser.ToInt(fields[33]),
                    PlayerBirthDate = StringParser.ToDateTime(fields[34], BirthDateFormat),
                    PlayerCollegeName = fields[35],
                    Position = fields[36],
                    HomeTeamAbbr = fields[37],
                    VisitorTeamAbbr = fields[38],
                    Week = StringParser.ToInt(fields[39]),
                    Stadium = fields[40],
                    Location = fields[41],
                    StadiumType = fields[42],
                    Turf = fields[43],
                    GameWeather = fields[44],
                    Temperature = StringParser.ToInt(fields[45]),
                    Humidity = StringParser.ToInt(fields[46]),
                    WindSpeed = fields[47],
                    WindDirection = fields[48]
                };

                play.IsLeftDirection = play.PlayDirection == "left";
                play.PlayDirectionEncoded = play.IsLeftDirection ? 1 : 0;
                play.IsBallCarrier = play.NflId == play.NflIdRusher;

                play.MinutesRemainingInQuarter = MinutesRemaining(play.GameClock);
                play.TimeDelta = (int) play.TimeHandoff.Subtract(play.TimeSnap).TotalSeconds;

                play.StandardisedOrientation = play.IsLeftDirection ? (180 + play.Orientation) % 360 : play.Orientation;
                play.StandardisedOrientationX = (float) Math.Cos(play.Orientation * 2 * Math.PI / 360);
                play.StandardisedOrientationY = (float) Math.Sin(play.Orientation * 2 * Math.PI / 360);

                play.StandardisedDir = play.IsLeftDirection ? (180 + play.Dir) % 360 : play.Dir;
                play.StandardisedDirX = (float) Math.Cos(play.Orientation * 2 * Math.PI / 360);
                play.StandardisedDirY = (float) Math.Sin(play.Orientation * 2 * Math.PI / 360);

                play.StandardisedX = play.IsLeftDirection ? 120 - play.X : play.X;
                play.StandardisedY = (float) (play.IsLeftDirection ? 160 / 3.0 - play.Y : play.Y);

                play.StandardisedSpeedX =
                    (float) (play.S * Math.Cos(90 - play.StandardisedDir * Math.PI / 180) + play.StandardisedX);
                play.StandardisedSpeedY =
                    (float) (play.S * Math.Sin(90 - play.StandardisedDir * Math.PI / 180) + play.StandardisedY);


                rushingPlays.Add(play);
                ReportProgress(rushingPlays.Count, 10_000);

                if (rushingPlays.Count == 1_100)
                    return rushingPlays;
            }

            _logger.LogInformation($"Total rows: {rushingPlays.Count}");
            
            _logger.LogInformation($"Ending {nameof(PreProcess)}");

            return rushingPlays;
        }

        private IList<RushingRaw> PreProcess(IList<RushingRaw> rushingPlays)
        {
            _logger.LogInformation($"Starting {nameof(PreProcess)}");

            var teamMap = BuildTeamMap(rushingPlays);
            var rushers = rushingPlays
                .Where(x => x.IsBallCarrier)
                .GroupBy(x => x.PlayId)
                .ToDictionary(
                    x => x.Key,
                    x => x.First());

            var count = 0;

            foreach (var play in rushingPlays)
            {
                var rusher = rushers[play.PlayId];

                // Ensure team names are consistent across all names
                play.PossessionTeam = teamMap[play.PossessionTeam];
                play.HomeTeamAbbr = teamMap[play.HomeTeamAbbr];
                play.VisitorTeamAbbr = teamMap[play.VisitorTeamAbbr];
                play.FieldPosition = play.FieldPosition == "" ? "50" : teamMap[play.FieldPosition];
                play.TeamOnOffense = play.PossessionTeam == play.HomeTeamAbbr ? "home" : "away";
                play.IsOnOffense = play.Team == play.TeamOnOffense;

                play.IsOffenseLeading = play.TeamOnOffense == "home"
                    ? play.HomeScoreBeforePlay > play.VisitorScoreBeforePlay
                    : play.HomeScoreBeforePlay < play.VisitorScoreBeforePlay;

                play.YardsFromOwnGoal = play.FieldPosition == play.PossessionTeam
                    ? play.YardLine == 50 ? 50 : play.YardLine
                    : 50 + (50 - play.YardLine);

                play.StandardisedYardLine =
                    play.PossessionTeam != play.FieldPosition ? 100 - play.YardLine : play.YardLine;

                play.RelativeX = play.StandardisedX - rusher.StandardisedX;
                play.RelativeY = play.StandardisedY - rusher.StandardisedY;
                play.RelativeSpeedX = play.StandardisedSpeedX - rusher.StandardisedSpeedX;
                play.RelativeSpeedY = play.StandardisedSpeedY - rusher.StandardisedSpeedY;

                play.DistanceToBall =
                    (float) Math.Pow(
                        Math.Pow(play.StandardisedX - rusher.StandardisedX, 2) +
                        Math.Pow(play.StandardisedY - rusher.StandardisedY, 2), 0.5);

                count++;
                ReportProgress(count, 50_000);
            }
            
            _logger.LogInformation($"Total rows: {count}");

            _logger.LogInformation($"Ending {nameof(PreProcess)}");
            return rushingPlays;
        }

        private static void ReportProgress(int count, int threshold)
        {
            if (count % threshold == 0)
                _logger.LogInformation($"Rows preprocessed: {count}");
        }

        private static Dictionary<string, string> BuildTeamMap(IList<RushingRaw> rushingPlays)
        {
            var homeTeams = rushingPlays
                .GroupBy(x => x.HomeTeamAbbr)
                .Select(x => x.Key)
                .ToList();

            var possessionTeams = rushingPlays
                .GroupBy(x => x.PossessionTeam)
                .Select(x => x.Key)
                .ToList();

            var teamMap = homeTeams
                .Zip(possessionTeams, (k, v) => new {k, v})
                .Where(x => x.k != x.v)
                .ToDictionary(x => x.k, x => x.v);

            foreach (var team in possessionTeams)
                teamMap[team] = team;

            return teamMap;
        }

        private static float MinutesRemaining(DateTime gameClock)
        {
            // 15 minute quarters
            var start = new TimeSpan(0, 0, 0);
            var clock = new TimeSpan(0, gameClock.Hour, gameClock.Minute);

            return (float) ((int) clock.Subtract(start).TotalSeconds / 60.0);
        }
    }
}
namespace Ai_Petrov_Mikhail
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal class Program
    {
         static void Main()
        {
            var logic = new Logic();
            logic.GameLoop();
        }
    }

    public class Logic
    {
        public void GameLoop()
        {
            GameData data = null;
            while (!CommandsHandling.WasExitCommand)
            {
                Command input = Communicator.ReceiveCommand();
                data = CommandsHandling.Process(input, data);

                Vector2 output = TargetCalculating.FindShotTarget(data);
                Communicator.SendTarget(output);
            }
        }

        static class CommandsHandling
        {
            public static bool WasExitCommand;

            public static GameData Process(Command command, GameData data)
            {
                switch (command.Name)
                {
                    case "Init":
                        return StartNewGame(command);
                    case "Wound":
                        return WoundShip(command, data);
                    case "Kill":
                        return KillShip(command, data);
                    case "Miss":
                        return NoteMiss(command, data);
                    case "Exit":
                        WasExitCommand = true;
                        break;
                }
                return data;
            }

            static GameData StartNewGame(Command command)
            {
                var mapWidth = command.DimensionData.X;
                var mapHeight = command.DimensionData.Y;

                return new GameData(mapWidth, mapHeight, command.Ships);
            }

            static GameData WoundShip(Command command, GameData data)
            {
                var map = data.Map;
                var coords = command.DimensionData;

                if (!data.HaveWoundedShip)
                    CheckShipOrientationPossibility(data, map, coords);

                map.MarkAsWound(coords);
                map.MarkAsChecked(map[coords].DiagonalsNearby);

                data.HaveWoundedShip = true;
                return data;
            }

            static GameData KillShip(Command command, GameData data)
            {
                var map = data.Map;

                var coords = command.DimensionData;
                var shipCells = map.GetAllShipCells(coords);
                var neighborShipCells = map.GetNearby(shipCells);

                map.MarkAsChecked(neighborShipCells);
                data.DeleteShip(shipCells.Count());
                data.HaveWoundedShip = false;

                return data;
            }

            static GameData NoteMiss(Command command, GameData data)
            {
                var map = data.Map;
                var coords = command.DimensionData;

                map.MarkAsChecked(coords);

                if (data.HaveWoundedShip)
                {
                    var woundsNear = map[coords].AllNearby
                        .Where(p => map[p].IsWounded);
                    CheckShipOrientationPossibility(data, map, woundsNear.First());
                }

                return data;
            }

            private static void CheckShipOrientationPossibility(GameData data, GameData.Board map, Vector2 coords)
            {
                if (map.GetHorizontalDistances(coords).Sum() + 1 < data.MinShipSize)
                    map.MarkAsChecked(map[coords].HorizontalNearby);
                if (map.GetVerticalDistances(coords).Sum() + 1 < data.MinShipSize)
                    map.MarkAsChecked(map[coords].VerticalNearby);
            }
        }

        static class TargetCalculating
        {
            public static Vector2 FindShotTarget(GameData data)
            {
                var map = data.Map;

                if (data.HaveWoundedShip)
                    return FindWoundedTarget(map);

                var target = FindExploringTarget(data);

                return Rating.IsWeakCheck(target, data)
                    ? FindMaxSliceSpaceTarget(map)
                    : target;
            }

            static Vector2 FindWoundedTarget(GameData.Board map)
            {
                return map.AllCoords
                    .Where(p => map[p].IsWounded)
                    .SelectMany(p => map[p].LinesNearby)
                    .First(p => !map[p].IsKnown);
            }

            static Vector2 FindExploringTarget(GameData data)
            {
                var map = data.Map;

                return map.AllCoords
                    .Where(p => !map[p].IsKnown)
                    .OrderByDescending(p => Rating.RateTarget(p, data))
                    .ThenByDescending(p => map.GetLinesDistances(p).Sum())
                    .First();
            }

            static Vector2 FindMaxSliceSpaceTarget(GameData.Board map)
            {
                return map.AllCoords
                    .Where(p => !map[p].IsKnown)
                    .OrderByDescending(p => map.GetLinesDistances(p).Sum())
                    .First();
            }

            static class Rating
            {
                const int BiggestShipHitScore = 3;
                const int SmallShipHitScore = 1;

                public static int RateTarget(Vector2 point, GameData data)
                {
                    var maxCheckLength = data.MaxShipSize - 1;
                    var secondCheckLength = data.SecondMaxShipSize - 1;

                    var horizontalDistances = data.Map.GetHorizontalDistances(point);
                    var verticalDistances = data.Map.GetVerticalDistances(point);

                    return RateDimention(horizontalDistances, maxCheckLength, secondCheckLength)
                           + RateDimention(verticalDistances, maxCheckLength, secondCheckLength);
                }

                public static bool IsWeakCheck(Vector2 target, GameData data)
                {
                    return data.MaxShipSize != data.MinShipSize
                         && RateTarget(target, data) <= BiggestShipHitScore;
                }

                static int RateDimention(int[] distances, int maxCheckLength, int secondCheckLength)
                {
                    if (IsRedundancyCheck(distances, maxCheckLength))
                        return RateShipHit(distances, maxCheckLength, BiggestShipHitScore)
                            + RateShipHit(distances, secondCheckLength, SmallShipHitScore);

                    return GetEffectivelyChecksAmount(distances, maxCheckLength);
                }

                private static int RateShipHit(int[] dimensionDistances, int checkLength, int hitScore)
                {
                    if (checkLength <= 0)
                        return 0;

                    return HaveShipHitChance(dimensionDistances, checkLength) ? hitScore : 0;
                }

                private static int GetEffectivelyChecksAmount(int[] distances, int checkedSize)
                {
                    return distances
                        .Select(d => (d == checkedSize) ? BiggestShipHitScore : 0)
                        .Sum();
                }

                static bool HaveShipHitChance(int[] lineDistances, int checkLength)
                {
                    return lineDistances.Sum() >= checkLength
                           && lineDistances.All(distance => (distance <= checkLength));
                }

                static bool IsRedundancyCheck(int[] dimensionDistances, int checkLength)
                {
                    return dimensionDistances.Sum() + 1 <= 2 * checkLength;
                }
            }
        }
    }

    public class GameData
    {
        readonly List<int> aliveShipsLengths;
        public readonly Board Map;
        public bool HaveWoundedShip;

        public GameData(int mapWidth, int mapHeight, List<int> shipsLengths)
        {
            Map = new Board(mapWidth, mapHeight);
            aliveShipsLengths = shipsLengths;

            UpdateSizes();
        }

        public int MaxShipSize { get; set; }
        public int SecondMaxShipSize { get; set; }
        public int MinShipSize { get; set; }

        void UpdateSizes()
        {
            var sizes = aliveShipsLengths
                .Distinct()
                .OrderByDescending(l => l);

            MaxShipSize = sizes.First();
            SecondMaxShipSize = (sizes.Count() > 1) ? sizes.Skip(1).First() : 0;
            MinShipSize = sizes.Last();
        }

        public void DeleteShip(int shipSize)
        {
            aliveShipsLengths.Remove(shipSize);
            UpdateSizes();
        }

        public class Board
        {
            public readonly IEnumerable<Vector2> AllCoords;
            public readonly int Height;
            public readonly int Width;
            readonly Cell[,] cells;

            public Board(int width, int height)
            {
                Width = width;
                Height = height;
                cells = new Cell[width, height];

                AllCoords = Enumerable.Range(0, Width)
                    .SelectMany(x => Enumerable.Range(0, Height)
                        .Select(y => new Vector2(x, y)));

                InitCells(width, height);
            }

            public Cell this[Vector2 point]
            {
                get { return cells[point.X, point.Y]; }
            }

            void InitCells(int width, int height)
            {
                for (var x = 0; x < width; x++)
                    for (var y = 0; y < height; y++)
                        cells[x, y] = InitCell(new Vector2(x, y));

                DistancesCalculator.Update(AllCoords, this);
            }

            Cell InitCell(Vector2 coords)
            {
                var cell = new Cell
                {
                    DirectionDistances = new Dictionary<Vector2, int>(),
                    HorizontalNearby = GetNearbyByShifts(coords, Shifts.Horizontal),
                    VerticalNearby = GetNearbyByShifts(coords, Shifts.Vertical),
                    DiagonalsNearby = GetNearbyByShifts(coords, Shifts.Diagonals)
                };

                cell.LinesNearby = cell.VerticalNearby
                    .Union(cell.HorizontalNearby)
                    .ToList();

                cell.AllNearby = cell.LinesNearby
                    .Union(cell.DiagonalsNearby)
                    .ToList();

                return cell;
            }

            bool IsInBounds(Vector2 point)
            {
                return point.X >= 0
                       && point.X < Width
                       && point.Y >= 0
                       && point.Y < Height;
            }

            public IEnumerable<Vector2> GetNearby(IEnumerable<Vector2> points)
            {
                return points
                    .SelectMany(p => this[p].AllNearby)
                    .Distinct();
            }

            public List<Vector2> GetNearbyByShifts(Vector2 point, IEnumerable<Vector2> shifts)
            {
                return shifts
                    .Select(point.Add)
                    .Where(IsInBounds)
                    .ToList();
            }

            public List<Vector2> GetAllShipCells(Vector2 shipCoords)
            {
                var collected = new List<Vector2>();
                var newFounded = new List<Vector2> { shipCoords };

                while (newFounded.Any())
                {
                    var nextCoords = newFounded.First();
                    newFounded.Remove(nextCoords);
                    collected.Add(nextCoords);

                    var shipCellsNear = FindShipPartsNear(nextCoords);
                    newFounded = newFounded
                        .Union(shipCellsNear.Except(collected))
                        .ToList();
                }

                return collected;
            }

            IEnumerable<Vector2> FindShipPartsNear(Vector2 point)
            {
                return this[point].AllNearby
                    .Where(p => this[p].IsWounded)
                    .ToList();
            }

            void Mark(IEnumerable<Vector2> points, Cell.States state)
            {
                foreach (var point in points.Where(IsInBounds))
                    cells[point.X, point.Y].State = state;

                DistancesCalculator.Update(AllCoords, this);
            }

            void Mark(Vector2 point, Cell.States state)
            {
                cells[point.X, point.Y].State = state;
                var subjectPoints = Enumerable.Range(0, Width)
                    .Select(x => new Vector2(x, point.Y))
                    .Union(Enumerable.Range(0, Height)
                        .Select(y => new Vector2(point.X, y)));

                DistancesCalculator.Update(subjectPoints, this);
            }

            int[] GetDirectionsByVectors(Vector2 point, IEnumerable<Vector2> dimensionShifts)
            {
                return dimensionShifts
                    .Select(shift => this[point].DirectionDistances[shift])
                    .ToArray();
            }

            public int[] GetHorizontalDistances(Vector2 point)
            {
                return GetDirectionsByVectors(point, Shifts.Horizontal);
            }

            public int[] GetVerticalDistances(Vector2 point)
            {
                return GetDirectionsByVectors(point, Shifts.Vertical);
            }

            public int[] GetLinesDistances(Vector2 point)
            {
                return GetDirectionsByVectors(point, Shifts.Lines);
            }

            public void MarkAsChecked(IEnumerable<Vector2> points)
            {
                Mark(points, Cell.States.Checked);
            }

            public void MarkAsChecked(Vector2 point)
            {
                Mark(point, Cell.States.Checked);
            }

            public void MarkAsWound(Vector2 point)
            {
                Mark(point, Cell.States.WoundedShip);
            }

            static class DistancesCalculator
            {
                public static void Update(IEnumerable<Vector2> points, Board map)
                {
                    foreach (var point in points)
                        Update(point, map);
                }

                static void Update(Vector2 changedCell, Board map)
                {
                    foreach (var shift in Shifts.Lines)
                        map[changedCell].DirectionDistances[shift] =
                            GetByDirection(changedCell, shift, map);
                }

                static int GetByDirection(Vector2 point, Vector2 shift, Board map)
                {
                    if (!map.IsInBounds(point) || map[point].IsChecked)
                        return -1;

                    return 1 + GetByDirection(point.Add(shift), shift, map);
                }
            }

            public static class Shifts
            {
                public static readonly Vector2[] Diagonals =
                    {
                        new Vector2(-1, -1),
                        new Vector2(-1, 1),
                        new Vector2(1, -1),
                        new Vector2(1, 1)
                    };

                public static readonly Vector2[] Horizontal =
                    {
                        new Vector2(-1, 0),
                        new Vector2(1, 0)
                    };

                public static readonly Vector2[] Vertical =
                    {
                        new Vector2(0, -1),
                        new Vector2(0, 1)
                    };

                public static readonly Vector2[] Lines = Vertical
                    .Union(Horizontal)
                    .ToArray();
            }

            public class Cell
            {
                public enum States
                {
                    Unknown,
                    Checked,
                    WoundedShip
                }

                public Cell()
                {
                    State = States.Unknown;
                }

                public States State;

                public List<Vector2> AllNearby;
                public List<Vector2> DiagonalsNearby;
                public Dictionary<Vector2, int> DirectionDistances;
                public List<Vector2> HorizontalNearby;
                public List<Vector2> LinesNearby;
                public List<Vector2> VerticalNearby;

                public bool IsWounded
                {
                    get { return State == States.WoundedShip; }
                }

                public bool IsKnown
                {
                    get { return State != States.Unknown; }
                }

                public bool IsChecked
                {
                    get { return State == States.Checked; }
                }
            }
        }
    }

    public static class Communicator
    {
        public static void SendTarget(Vector2 target)
        {
            Console.WriteLine(target.ToString());
        }

        public static Command ReceiveCommand()
        {
            return new Command(Console.ReadLine() ?? "Exit");
        }
    }

    public class Command
    {
        public Vector2 DimensionData;
        public String Name;
        public List<int> Ships;

        public Command(string inputString)
        {
            Parse(inputString);
        }

        void Parse(string inputString)
        {
            // Входная строка имеет один из следующих форматов:
            // Init <map_width> <map_height> <ship1_size> <ship2_size> ...
            // <command_name> <last_shot_X> <last_shot_Y>

            var tokens = inputString.Split(' ');
            var intParameters = ParseInts(tokens);

            Name = tokens[0];
            DimensionData = TryParseCell(intParameters);
            Ships = TryParseShips(intParameters);
        }

        static List<int> TryParseShips(int[] intParameters)
        {
            if (intParameters.Length < 2)
                return null;

            var ships = new List<int>();
            for (var i = 2; i < intParameters.Length; i++)
                ships.Add(intParameters[i]);

            return ships;
        }

        static Vector2 TryParseCell(int[] intParameters)
        {
            return (intParameters.Length >= 1)
                ? new Vector2(intParameters[0], intParameters[1])
                : null;
        }

        static int[] ParseInts(IEnumerable<string> tokens)
        {
            return tokens
                .Skip(1)
                .Select(Int32.Parse)
                .ToArray();
        }
    }

    public class Vector2
    {
        public Vector2(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; set; }
        public int Y { get; set; }

        public override string ToString()
        {
            return String.Format("{0} {1}", X, Y);
        }

        public Vector2 Add(Vector2 v)
        {
            return new Vector2(v.X + X, v.Y + Y);
        }

        bool Equals(Vector2 other)
        {
            return Y == other.Y
                   && X == other.X;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((Vector2)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Y * 397) ^ X;
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PSVRHistoryExporter
{
    class HandConverter
    {
        public HandConverter(string hhFile, string exportDir)
        {
            HandHistoryFile = hhFile;
            ExportDir = exportDir;
            if (Debugger.IsAttached)
            {
                ExportDir = ExportDir + "_Debug";
                if (!Directory.Exists(ExportDir))
                    Directory.CreateDirectory(ExportDir);
            }
        }
        private TimeSpan? _estOffset = null;
        private string _localTzAbbr = "";

        public string HandHistoryFile { get; set; }
        public string ExportDir { get; set; }
        public bool IsRunning { get; set; }
        public TimeSpan EstOffset {
            get
            {
                if (_estOffset == null)
                    _estOffset = GetTimeZoneOffsetDifference(TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"), TimeZoneInfo.Local);
                return (TimeSpan)_estOffset;
            }
        }

        public string LocalTzAbbr
        {
            get
            {
                if (_localTzAbbr == "")
                {
                    // Assume CET for now, needs nuget package for accurate abbr and i'd like to keep it a single exe for now
                    _localTzAbbr = "CET";
                }
                return _localTzAbbr;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>true on success, false on fail</returns>
        public bool Start()
        {
            if (!File.Exists(HandHistoryFile) || !Directory.Exists(ExportDir))
                return false;
            else
            {
                IsRunning = true;
                new Thread(() => RunConverter()).Start();
                return true;
            }
        }

        internal void Stop()
        {
            IsRunning = false;
        }

        private void RunConverter()
        {
            int emptyLines = 0; // non-standard line endings?
            using (var fs = new FileStream(HandHistoryFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (var sr = new StreamReader(fs, Encoding.Default))
                {
                    List<string> handData = new List<string>();
                    while (IsRunning)
                    {
                        try
                        {
                            string line = sr.ReadLine();
                            if (line == null) // end of file
                                Thread.Sleep(100);
                            else if (line != "")
                            {
                                if (rIsNewHand.IsMatch(line)) // hand was incomplete, can occur when client closes unexpectedly
                                {
                                    handData = new List<string>();
                                    handData.Add(line);
                                }
                                else if (rIsLastLine.IsMatch(line)) // Helps to filter out incomplete hands
                                {
                                    handData.Add(line);
                                    if (rIsNewHand.IsMatch(handData[0])) // Only process full hands
                                        ConvertHand(handData);
                                    handData = new List<string>();
                                }
                                else // Just another line in the hand
                                {
                                    handData.Add(line); // add the hand data
                                }
                                emptyLines = 0;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (Debugger.IsAttached)
                                throw ex;
                            else
                            {
                                System.Windows.MessageBox.Show(ex.Message, "An error has occurred",
                                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                            }
                        }
                    }
                }
            }
        }

        Regex rIsNewHand = new Regex(@"^PokerStars Hand +");
        Regex rIsLastLine = new Regex(@"^Board\[.*\]+");
        Regex rHandTime = new Regex(@"(\d+\/\d+\/\d+ \d+:\d+:\d+ (AM|PM))");
        Regex rStake = new Regex(@"(^.+) \(");
        Regex rTableName = new Regex(@"(^.+) \(");
        Regex rSeatLine = new Regex(@"^Seat +(\d):");
        Regex rTenCard = new Regex(@"(10)(h|d|s|c)");
        Regex rSidePot = new Regex(@"Side pot (\d+)\. ");
        Regex rSqBrackNeedsSpace = new Regex(@"\S(\[)");
        Regex rCommaInNumber = new Regex(@"\d+(,)\d+");

        /// <summary>
        /// Inputs a single hand, places it in the approperiate file
        /// </summary>
        /// <param name="handData"></param>
        private void ConvertHand(List<string> handData)
        {
            // In case they switch to multiple empty lines later on
            if (handData.Count == 0)
                return;

            // ensure we're handing hand data, not empty lines
            while (handData[0] == "")
                handData.RemoveAt(0);


            // Grab the hand's date
            DateTime handTime;
            if (!rHandTime.IsMatch(handData[0]))
                throw new Exception("Invalid hand format");
            if (!DateTime.TryParse(rHandTime.Match(handData[0]).Groups[1].Value, out handTime))
                throw new Exception("Failed to parse timestamp " + rHandTime.Match(handData[0]).Groups[1].Value);
            DateTime estHandTime = handTime.Add(EstOffset); // Get EST time

            // Hand already processed, return
            if (!Debugger.IsAttached && handTime <= Properties.Settings.Default.LastConvertedHandTime)
                return;

            // Update way time is displayed in L0
            // PSVR: PokerStars Hand #: Hold'em No Limit(5/10) - 2/5/2019 2:53:07 PM
            // Stars: PokerStars Hand #xxxxx:  Hold'em No Limit (50/100) - 2019/02/13 21:30:16 CET [2019/02/13 15:30:16 ET]
            string psTimeString = handTime.ToString("yyyy/MM/dd hh:mm:ss");
            string psEstTimeString = estHandTime.ToString("yyyy/MM/dd hh:mm:ss");
            string psTimeStringFinal = $"{psTimeString} {LocalTzAbbr} [{psEstTimeString} ET]";
            handData[0] = handData[0].Replace(rHandTime.Match(handData[0]).Groups[1].Value, psTimeStringFinal);

            // PSVR hands don't have an ID. Give it one.
            handData[0] = handData[0].Insert(17, (++Properties.Settings.Default.LastIdUsed).ToString());

            // Space between game time & stake
            handData[0] = handData[0].Replace("(", " (");

            // Change play money to dollars since HM2 (at least) doesn't support play money
            // todo: use HM3 beta for now

            // Line 1
            // PSVR: Patrick_Lucky's Cash Game (PlayMoney) Seat #2 is the button
            // Stars: Table 'Gacrux III' 9-max (Play Money) Seat #5 is the button
            if (!rTableName.IsMatch(handData[1]))
                throw new Exception("Failed to extract table name. File corrupt?");
            string tableName = rTableName.Match(handData[1]).Groups[1].Value; // get table name
            handData[1] = handData[1].Replace(tableName, $"Table '{tableName}' 8-max"); // Change table name structure
            handData[1] = handData[1].Replace("(PlayMoney)", "(Play Money)"); // Play money needs space

            // remove complication of ' in table name
            handData[1] = handData[1].Replace("'s", "");
            tableName = tableName.Replace("'s", "");

            // Seats are unorganized for some reason. Not sure if relevant but let's fix that just in case
            int seatLineIndex = 2; // starts at line 2
            List<string> seatLines = new List<string>();
            while (handData.Count - 1 > seatLineIndex && rSeatLine.IsMatch(handData[seatLineIndex]))
            {
                seatLines.Add(handData[seatLineIndex]);
                seatLineIndex++;
            }

            // Order by regex matched seat number and put them back in handData
            int seatRepositionIndex = 2;
            var orderedSeats = seatLines.OrderBy(s => Convert.ToInt32(rSeatLine.Match(s).Groups[1].Value));
            foreach (string line in orderedSeats)
            {
                handData[seatRepositionIndex] = line;
                seatRepositionIndex++;
            }

            // Turn and river spacing & 10 to T
            // PSVR: *** TURN *** [As Ah 10c][9d]
            // Stars: *** TURN *** [As Ah Tc] [9d]
            for (int j = 0; j < handData.Count; j++)
            {
                // Replace [ with space[ where needed
                if (rSqBrackNeedsSpace.IsMatch(handData[j]))
                    foreach (Match match in rSqBrackNeedsSpace.Matches(handData[j]).Cast<Match>().Reverse())
                        handData[j] = handData[j].Insert(match.Groups[1].Index, " "); // add space before [

                // Replace 10 with T
                int removedTens = 0; // Removing a ten in the same line removes a character
                if (rTenCard.IsMatch(handData[j]))
                    foreach (Match match in rTenCard.Matches(handData[j]))
                    {
                        int tenIndex = match.Groups[1].Index - removedTens;
                        handData[j] = handData[j].Remove(tenIndex, 2); // Remove 10
                        handData[j] = handData[j].Insert(tenIndex, "T"); // Replace with T
                        removedTens++;
                    }


                // Replace [ with space[ where needed
                if (rCommaInNumber.IsMatch(handData[j]))
                    foreach (Match match in rCommaInNumber.Matches(handData[j]).Cast<Match>().Reverse())
                        handData[j] = handData[j].Remove(match.Groups[1].Index, 1); //Remove comma
            }

            // Pot summary needs a space at the end
            handData[handData.Count - 2] += " ";


            // Fix pot size error. Occurs when multiple sidepots are mentioned - label them instead (still occurs but more hands import
            // PSVR: Total pot 35525 Main pot 1250. Side pot 12800. Side pot 21475. | Rake 0 
            // Stars: Total pot 38613 Main pot 34209. Side pot-1 1171. Side pot-2 1109. | Rake 2124
            string potSummary = handData[handData.Count - 2];
            if (rSidePot.IsMatch(potSummary))
            {
                var sidePotMatches = rSidePot.Matches(potSummary);
                if (sidePotMatches.Count > 1)
                {
                    // Add up all side pots & remove any mentions of side pot
                    int nextSidePot = sidePotMatches.Count;
                    foreach (Match match in sidePotMatches.Cast<Match>().Reverse()) // Reverse so we can replace using index (in case of same-size sidepots)
                    {
                        // Set label
                        string modifiedLabel = match.Value.Replace("Side pot", $"Side pot-{nextSidePot}");
                        potSummary = potSummary.Remove(match.Index, match.Length);
                        potSummary = potSummary.Insert(match.Index, modifiedLabel);
                        nextSidePot--;
                    }
                }

                // Save changes
                handData[handData.Count - 2] = potSummary;
            }

            // Add three new lines at the end to mirror a real PS hand history
            for (int i = 0; i < 3; i++)
                handData.Add("");
            
            // Save the hand
            File.AppendAllLines(Path.Combine(ExportDir, MakeValidFileName(tableName + ".txt")), handData);

            // Note the timestamp so it skips this hand in the future
            Properties.Settings.Default.LastConvertedHandTime = handTime;
            Properties.Settings.Default.Save();
        }

        public static TimeSpan GetTimeZoneOffsetDifference(TimeZoneInfo oldZone, TimeZoneInfo newZone)
        {
            var now = DateTimeOffset.UtcNow;
            TimeSpan oldOffset = oldZone.GetUtcOffset(now);
            TimeSpan newOffset = newZone.GetUtcOffset(now);
            TimeSpan difference = oldOffset - newOffset;
            return difference;
        }

        private static string MakeValidFileName(string name)
        {
            string invalidChars = System.Text.RegularExpressions.Regex.Escape(new string(System.IO.Path.GetInvalidFileNameChars()));
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

            return System.Text.RegularExpressions.Regex.Replace(name, invalidRegStr, "_");
        }
    }

}

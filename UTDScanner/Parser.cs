using EasyHttp.Http;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Renci.SshNet;
using System.Net;
using System.Collections.Specialized;
using System.Globalization;


namespace UTDScanner
{
    static class Parser
    {
        public static void Parse(bool checkAllFiles)
        {
            #region Download

            Console.WriteLine("Checking local PDF files against server");

            var modifiedfiles = new List<String>();

            var filenamestocheck = new List<String>();

            // Try to get all files
            if (checkAllFiles)
            {
                // Last file available: https://www.utdallas.edu/police/files/2011-01.pdf as of 7/23/14
                var months = Enumerable.Range(1, 12);
                var years = Enumerable.Range(DateTime.Now.Year - 1, 2);

                foreach (var year in years)
                {
                    foreach (var month in months)
                    {
                        filenamestocheck.Add(String.Format("{0}-{1:D2}.pdf", year, month)); // 2009-08.pdf
                    }
                }

                var archivedyears = Enumerable.Range(2010, DateTime.Now.Year - 2010 - 1);
                foreach (var year in archivedyears)
                {
                    filenamestocheck.Add(String.Format("{0}-crime-log.pdf", year));
                }
            }
            // Just check last n months
            else
            {
                var months = new List<DateTime>{
                    DateTime.Now,
                    DateTime.Now.AddMonths(-1),
                    DateTime.Now.AddMonths(-2)
                };

                foreach (var month in months)
                {
                    filenamestocheck.Add(String.Format("{0}-{1:D2}.pdf", month.Year, month.Month)); // 2009-08.pdf
                }
            }

            foreach (var filename in filenamestocheck)
            {
                var http = new HttpClient();
                http.Request.Accept = "application/pdf";
                http.StreamResponse = true;

                var localfile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), filename);
                var uri = "https://www.utdallas.edu/police/files/" + filename;

                try
                {
                    if (File.Exists(localfile))
                    {
                        // Compare date of our file to date on server
                        http.Request.IfModifiedSince = File.GetLastWriteTime(localfile);
                    }

                    var download = http.Get(uri);
                    if (download.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        Console.WriteLine("Downloading " + filename);
                        using (var fs = new FileStream(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), localfile), FileMode.Create))
                        {
                            download.ResponseStream.CopyTo(fs);
                        }
                        modifiedfiles.Add(localfile);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    Debugger.Break();
                }

            }

            Console.WriteLine("Downloading finished, " + modifiedfiles.Count + " modified files to parse");

            #endregion

            #region Parse

            Console.WriteLine("Parsing modified files");

            var incidents = new List<Incident>();

            var reportedline = new Regex("^Date/Time Reported: (.*) Incident Occurred Between: (.*?) and (.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var caseline = new Regex("^Case #:(.*)Int. Ref. #:(.*)Disposition: (.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var pageline = new Regex("^Page [0-9]+ of [0-9]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            foreach (var filename in modifiedfiles)
            {
                PdfReader reader = new PdfReader(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), filename));

                Console.WriteLine("Parsing " + Path.GetFileName(filename) + " (" + reader.NumberOfPages + " pages)");

                for (int page = 1; page <= reader.NumberOfPages; page++)
                {
                    var strategy = new LocationTextExtractionStrategy();
                    var text = PdfTextExtractor.GetTextFromPage(reader, page, strategy);
                    var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                    var currentIncident = new Incident();

                    bool started = false;
                    bool notes = false;
                    int unknownlines = 0;
                    foreach (var line in lines)
                    {
                        // Wait until first Incident Type
                        if (!started)
                        {
                            if (line.StartsWith("Incident Type: ", StringComparison.CurrentCultureIgnoreCase))
                            {
                                started = true;
                            }
                            else
                            {
                                continue;
                            }
                        }

                        if (line.StartsWith("Incident Type: ", StringComparison.CurrentCultureIgnoreCase))
                        {
                            if (currentIncident.Type != null)
                            {
                                incidents.Add(currentIncident);
                                currentIncident = new Incident();
                            }
                            currentIncident.Type = line.Substring("Incident Type: ".Length);
                        }
                        else if (line.StartsWith("Location: ", StringComparison.CurrentCultureIgnoreCase))
                        {
                            currentIncident.Location = line.Substring("Location: ".Length);
                        }
                        else if (line.StartsWith("Date/Time Reported: ", StringComparison.CurrentCultureIgnoreCase))
                        {
                            var match = reportedline.Match(line);

                            try
                            {
                                currentIncident.Reported = Convert.ToDateTime(match.Groups[1].Value);
                            }
                            catch (FormatException)
                            {
                                currentIncident.Reported = DateTime.MinValue;
                            }

                            try
                            {
                                currentIncident.OccurredStart = Convert.ToDateTime(match.Groups[2].Value);
                            }
                            catch (FormatException)
                            {
                                currentIncident.OccurredStart = DateTime.MinValue;
                            }

                            try
                            {
                                currentIncident.OccurredStop = Convert.ToDateTime(match.Groups[3].Value);
                            }
                            catch (FormatException)
                            {
                                currentIncident.OccurredStart = DateTime.MinValue;
                            }
                        }
                        else if (line.StartsWith("Case #: ", StringComparison.CurrentCultureIgnoreCase))
                        {
                            var match = caseline.Match(line);

                            currentIncident.CaseNumber = match.Groups[1].Value.Trim();
                            currentIncident.InternalReferenceNumber = match.Groups[2].Value.Trim();
                            currentIncident.Disposition = match.Groups[3].Value.Trim();
                        }
                        else if (pageline.IsMatch(line))
                        {
                            notes = false;
                            incidents.Add(currentIncident);
                        }
                        else if (line.StartsWith("Notes: ", StringComparison.CurrentCultureIgnoreCase))
                        {
                            notes = true;
                            currentIncident.Notes = line.Substring("Notes: ".Length);
                        }
                        else if (notes == true)
                        {
                            currentIncident.Notes += " " + line;
                        }
                        else
                        {
                            Console.WriteLine("Unknown line: " + line);
                            // Probably notes put in the Disposition field, one line is disposition, next is notes
                            if (unknownlines == 0)
                            {
                                currentIncident.Disposition += " " + line;
                                unknownlines++;
                            }
                            else
                            {
                                currentIncident.Notes += " " + line;
                            }
                            //Debugger.Break();
                        }
                    }
                }
            }

            Console.WriteLine("Parsing finished, " + incidents.Count + " incidents loaded");

            #endregion

            using (var db = new SQLiteConnection("Data Source = " + Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "database.s3db")))
            {
                db.Open();

                #region Store

                if (incidents.Count != 0)
                {
                    var begin = new SQLiteCommand("BEGIN TRANSACTION", db);
                    begin.ExecuteNonQuery();

                    var cmd = new SQLiteCommand(db);
                    cmd.CommandText = "SELECT COUNT(1) FROM Incidents WHERE CaseNumber = @casenumber AND InternalReferenceNumber = @internalreferencenumber";

                    var insert = new SQLiteCommand(db);
                    insert.CommandText = "INSERT INTO Incidents (Type, Location, Reported, OccurredStart, OccurredStop, CaseNumber, InternalReferenceNumber, Disposition, Notes)" +
                        "VALUES (@type, @location, @reported, @occurredstart, @occurredstop, @casenumber, @internalreferencenumber, @disposition, @notes);";

                    foreach (var incident in incidents)
                    {
                        // This will cause a lot of table scans. Oh well.
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@casenumber", incident.CaseNumber);
                        cmd.Parameters.AddWithValue("@internalreferencenumber", incident.InternalReferenceNumber);
                        int count = Convert.ToInt32(cmd.ExecuteScalar());

                        if (count == 0)
                        {
                            Console.WriteLine("Inserting incident " + incident.CaseNumber + " " + incident.InternalReferenceNumber);

                            // Insert
                            insert.Parameters.Clear();
                            insert.Parameters.AddWithValue("@Type", incident.Type);
                            insert.Parameters.AddWithValue("@Location", incident.Location);
                            insert.Parameters.AddWithValue("@Reported", (incident.Reported == DateTime.MinValue ? (object)DBNull.Value : incident.Reported));
                            insert.Parameters.AddWithValue("@OccurredStart", (incident.OccurredStart == DateTime.MinValue ? (object)DBNull.Value : incident.OccurredStart));
                            insert.Parameters.AddWithValue("@OccurredStop", (incident.OccurredStop == DateTime.MinValue ? (object)DBNull.Value : incident.OccurredStop));
                            insert.Parameters.AddWithValue("@CaseNumber", incident.CaseNumber);
                            insert.Parameters.AddWithValue("@InternalReferenceNumber", incident.InternalReferenceNumber);
                            insert.Parameters.AddWithValue("@Disposition", incident.Disposition);
                            insert.Parameters.AddWithValue("@Notes", incident.Notes);

                            int rows = insert.ExecuteNonQuery();
                            Debug.Assert(rows == 1);
                        }
                    }
                    var end = new SQLiteCommand("END TRANSACTION", db);
                    end.ExecuteNonQuery();

                    begin.Dispose();
                    cmd.Dispose();
                    insert.Dispose();
                    end.Dispose();
                }

                using (var numSentStatusesCmd = new SQLiteCommand(db))
                {
                    numSentStatusesCmd.CommandText = "SELECT COUNT(1) FROM Incidents WHERE SharedOnBuffer = 1";
                    int numSentStatuses = Convert.ToInt32(numSentStatusesCmd.ExecuteScalar());
                    if (numSentStatuses == 0)
                    {
                        // Probably a freshly loaded database
                        // Avoid sending thousands of tweets for old incidents
                        Console.WriteLine("Setting SharedOnBuffer = 1 for all Incidents");
                        using (var update = new SQLiteCommand("UPDATE Incidents SET SharedOnBuffer = 1", db))
                        {
                            update.ExecuteNonQuery();
                        }
                    }
                }

                Console.WriteLine("Finished storing incidents");
                #endregion

                #region Files table

                foreach (var filename in modifiedfiles)
                {
                    var localfile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), filename);
                    var exists = false;

                    using (var cmd = new SQLiteCommand(db))
                    {
                        cmd.CommandText = "SELECT COUNT(1) FROM Files WHERE Name = @name";
                        cmd.Parameters.AddWithValue("name", filename);

                        exists = Convert.ToBoolean(cmd.ExecuteScalar());
                    }

                    using (var cmd = new SQLiteCommand(db))
                    {
                        if (exists)
                        {
                            cmd.CommandText = "UPDATE FILES SET Name = @name, LastModified = @lastmodified WHERE Name = @name";
                        }
                        else
                        {
                            cmd.CommandText = "INSERT INTO Files (Name, LastModified) VALUES (@name, @lastmodified)";
                        }
                        cmd.Parameters.AddWithValue("name", filename);
                        cmd.Parameters.AddWithValue("lastmodified", File.GetLastWriteTime(localfile));

                        var rows = cmd.ExecuteNonQuery();
                        Debug.Assert(rows == 1);
                    }
                }

                #endregion

                #region Post

                using (var getToShare = new SQLiteCommand(db))
                {
                    getToShare.CommandText = "SELECT Id, CaseNumber, InternalReferenceNumber, Type, Location, Reported, OccurredStart, OccurredStart, Disposition, Notes, Location, Latitude, Longitude, FacebookPageId " +
                        "FROM IncidentsView WHERE SharedOnBuffer = 0 ORDER BY Reported ASC LIMIT 10";

                    using (var reader = getToShare.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // Time to tweet
                            const int httpsShortUrlLength = 21;
                            const int maxLength = 140 - httpsShortUrlLength - 1; // room for one space and an https t.co link

                            string text = Convert.ToString(reader["Notes"]);
                            var ti = CultureInfo.CurrentCulture.TextInfo;

                            // Ending ) and space is added at end!
                            if (text.Length <= maxLength)
                            {
                                string newtext = text + " (" + ti.ToTitleCase(Convert.ToString(reader["Location"]).ToLower());

                                if (newtext.Length + 1 <= maxLength) // +1 accounts for ending )
                                {
                                    text = newtext;
                                    newtext = text + " " + Convert.ToDateTime(reader["Reported"]).ToString(@"M/d h:mmt");

                                    if (newtext.Length + 1 <= maxLength)
                                    {
                                        text = newtext;
                                        newtext = text + " " + ti.ToTitleCase(Convert.ToString(reader["Disposition"]).ToLower());

                                        if (newtext.Length + 1 <= maxLength)
                                        {
                                            text = newtext;
                                        }
                                    }
                                }
                                text = text.Trim() + ")";
                            }
                            else
                            {
                                // Notes were already too long
                                text = text.Substring(0, maxLength - 3) + "...";
                            }

                            // Append url
                            if (reader["CaseNumber"] != DBNull.Value)
                            {
                                text = text + " http://utdscanner.com/case/" + Convert.ToString(reader["CaseNumber"]);
                            }
                            else
                            {
                                Debug.Fail("No casenumber for row", reader.ToString());
                            }


                            using (var web = new WebClient())
                            {
                                var data = new NameValueCollection();
                                data["text"] = text;
                                data["profile_ids[]"] = Properties.Settings.Default.BufferTwitterId;
                                data["shorten"] = "false";

                                web.Headers.Add("Authorization", "Bearer " + Properties.Settings.Default.OAuthAccessToken);
                                web.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
                                web.Headers.Add("User-Agent", "UTDScanner/1.0 (+http://utdscanner.com)");
                                try
                                {
                                    var response = web.UploadValues("https://api.bufferapp.com/1/updates/create.json", "POST", data);
                                    var json = Encoding.UTF8.GetString(response);
                                    if (Regex.IsMatch(json, @"success["" :]*true")) // lol
                                    {
                                        using (var update = new SQLiteCommand(db))
                                        {
                                            update.CommandText = "UPDATE Incidents SET SharedOnBuffer = 1 WHERE Id = @id";
                                            update.Parameters.AddWithValue("id", reader["Id"]);
                                            var rows = update.ExecuteNonQuery();
                                            Debug.Assert(rows == 1);
                                        }
                                    }
                                }
                                catch (WebException ex)
                                {
                                    if (ex.Message.Contains("400"))
                                    {
                                        Console.WriteLine("Reached Buffer limit? " + ex.Message);
                                    }
                                }

                            }
                        }
                    }
                }

                #endregion

                SQLiteConnection.ClearAllPools();
                db.Close();
            }

            // SQLiteConnectionHandle won't be released until GC collects it
            // http://stackoverflow.com/a/8513453
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.WaitForFullGCComplete(1000 * 15);

            #region Upload

            ConnectionInfo connectionInfo = new PasswordConnectionInfo(Properties.Settings.Default.SCPHost, Properties.Settings.Default.SCPUsername, System.Text.Encoding.UTF8.GetBytes(Properties.Settings.Default.SCPPassword));

            Console.WriteLine("Uploading to " + Properties.Settings.Default.SCPHost);

            using (var scpClient = new ScpClient(connectionInfo))
            {
                scpClient.Connect();
                scpClient.OperationTimeout = new TimeSpan(0, 5, 0);
                scpClient.BufferSize = 2 ^ 20;

                var database = new FileInfo(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "database.s3db"));

                scpClient.Upload(database, Properties.Settings.Default.SCPDestination);

                scpClient.Disconnect();
            }

            Console.WriteLine("Finished uploading");
            #endregion
        }
    }
}

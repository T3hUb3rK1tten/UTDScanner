using EasyHttp.Http;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq.Expressions;
using System.Net;
using System.Collections.Specialized;
using System.Globalization;
using System.Data.SqlClient;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage.Blob;


namespace UTDScanner
{
    static class Parser
    {
        public static void Parse(bool checkAllFiles)
        {
            #region Download

            Console.WriteLine("Checking local PDF files against server");

            var filenamestocheck = GetFilesToCheck(checkAllFiles);
            
            var storageAccount = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("StorageConnectionString"));
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference("utdpolicepdf");
            container.CreateIfNotExists();

            var modifiedfiles = GetModifiedFiles(filenamestocheck, container);

            Console.WriteLine("Downloading finished, " + modifiedfiles.Count + " modified files to parse");

            #endregion

            if (modifiedfiles.Count == 0)
            {
                return;
            }

            #region Parse

            Console.WriteLine("Parsing modified files");

            var incidents = new List<Incident>();

            foreach (var file in modifiedfiles)
            {
                Console.WriteLine("Parsing " + file.Key);
                
                var blockBlob = container.GetBlockBlobReference(file.Key);
                incidents.AddRange(ParseFile(blockBlob.OpenRead()));
            }

            Console.WriteLine("Parsing finished, " + incidents.Count + " incidents to load");

            #endregion

            if (incidents.Count == 0)
            {
                return;
            }

            DatabaseUpload(incidents, modifiedfiles);

            Post();
        }

        private static List<string> GetFilesToCheck(bool checkAllFiles)
        {
            var filenamestocheck = new List<string>();

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
            return filenamestocheck;
        }

        private static Dictionary<string, DateTime> GetModifiedFiles(List<string> filenamestocheck, CloudBlobContainer container)
        {
            var modifiedfiles = new Dictionary<string, DateTime>();

            using (var db = new SqlConnection(CloudConfigurationManager.GetSetting("DatabaseConnectionString")))
            {
                db.Open();

                foreach (var filename in filenamestocheck)
                {
                    DateTime lastmodified = DateTime.MinValue;

                    // Get the last modified date
                    using (var cmd = db.CreateCommand())
                    {
                        cmd.CommandText = "SELECT LastModified FROM Files WHERE Name = @Name";
                        cmd.Parameters.AddWithValue("@Name", filename);

                        var result = cmd.ExecuteScalar();
                        if (result != null)
                        {
                            lastmodified = (DateTime)result;
                        }
                    }

                    var http = new HttpClient();
                    http.Request.Accept = "application/pdf";
                    http.StreamResponse = true;

                    try
                    {
                        if (lastmodified != DateTime.MinValue)
                        {
                            // Compare date of our file to date on server
                            http.Request.IfModifiedSince = lastmodified;
                        }

                        var download = http.Get("https://www.utdallas.edu/police/files/" + filename);
                        if (download.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            Console.WriteLine("Downloading " + filename);
                            var blockBlob = container.GetBlockBlobReference(filename);
                            blockBlob.UploadFromStream(download.ResponseStream);

                            modifiedfiles.Add(filename, download.LastModified);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex.ToString());
                    }
                }
            }

            return modifiedfiles;
        }

        private static List<Incident> ParseFile(Stream stream)
        {
            PdfReader reader = new PdfReader(stream);

            List<Incident> incidents = new List<Incident>();

            var reportedline = new Regex("^Date/Time Reported: (.*) Incident Occurred Between: (.*?) and (.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var caseline = new Regex("^Case #:(.*)Int. Ref. #:(.*)Disposition: (.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var pageline = new Regex("^Page [0-9]+ of [0-9]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                    }
                }
            }

            return incidents;
        }

        private static void DatabaseUpload(List<Incident> incidents, Dictionary<string, DateTime> modifiedfiles)
        {
            using (var db = new SqlConnection(CloudConfigurationManager.GetSetting("DatabaseConnectionString")))
            {
                db.Open();

                using (var trans = db.BeginTransaction())
                {
                    using (var insert = db.CreateCommand())
                    {
                        insert.Transaction = trans;
                        insert.CommandText = "INSERT INTO Incidents (Type, Location, Reported, OccurredStart, OccurredStop, CaseNumber, InternalReferenceNumber, Disposition, Notes)" +
                            "VALUES (@type, @location, @reported, @occurredstart, @occurredstop, @casenumber, @internalreferencenumber, @disposition, @notes);";

                        foreach (var incident in incidents)
                        {
                            if (String.IsNullOrEmpty(incident.CaseNumber) && String.IsNullOrEmpty(incident.InternalReferenceNumber))
                            {
                                Console.WriteLine("Empty ID: " + incident.Notes);
                                continue;
                            }

                            insert.Parameters.Clear();
                            insert.Parameters.AddWithValue("@Type", (String.IsNullOrEmpty(incident.Type) ? (object)DBNull.Value : incident.Type));
                            insert.Parameters.AddWithValue("@Location", (String.IsNullOrEmpty(incident.Location) ? (object)DBNull.Value : incident.Location));
                            insert.Parameters.AddWithValue("@Reported", (incident.Reported == DateTime.MinValue ? (object)DBNull.Value : incident.Reported));
                            insert.Parameters.AddWithValue("@OccurredStart", (incident.OccurredStart == DateTime.MinValue ? (object)DBNull.Value : incident.OccurredStart));
                            insert.Parameters.AddWithValue("@OccurredStop", (incident.OccurredStop == DateTime.MinValue ? (object)DBNull.Value : incident.OccurredStop));
                            insert.Parameters.AddWithValue("@CaseNumber", (String.IsNullOrEmpty(incident.CaseNumber) ? (object)DBNull.Value : incident.CaseNumber));
                            insert.Parameters.AddWithValue("@InternalReferenceNumber", (String.IsNullOrEmpty(incident.InternalReferenceNumber) ? (object)DBNull.Value : incident.InternalReferenceNumber));
                            insert.Parameters.AddWithValue("@Disposition", (String.IsNullOrEmpty(incident.Disposition) ? (object)DBNull.Value : incident.Disposition));
                            insert.Parameters.AddWithValue("@Notes", (String.IsNullOrEmpty(incident.Notes) ? (object)DBNull.Value : incident.Notes));

                            try
                            {
                                int rows = insert.ExecuteNonQuery();
                                if (rows == 1)
                                {
                                    Console.WriteLine("Inserted incident " + incident.CaseNumber + " " + incident.InternalReferenceNumber);
                                }
                            }
                            catch (SqlException ex)
                            {
                                if (ex.Number == 2601) // Duplicate key row (this is okay)
                                {
                                    Debug.WriteLine("Duplicate key " + ex.Message);
                                }
                                else
                                {
                                    throw ex;
                                }
                            }
                        }
                    }

                    // Keep this in the same transaction because this is how we tell what we've worked on
                    foreach (var file in modifiedfiles)
                    {
                        using (var upsert = db.CreateCommand())
                        {
                            upsert.Transaction = trans;
                            upsert.CommandText = "MERGE INTO Files USING (SELECT @Name AS Name) AS SRC ON Files.Name = SRC.Name " +
                                "WHEN MATCHED THEN UPDATE SET LastModified = @LastModified " +
                                "WHEN NOT MATCHED THEN INSERT (Name, LastModified) VALUES (@Name, @LastModified);";

                            upsert.Parameters.AddWithValue("@Name", file.Key);
                            upsert.Parameters.AddWithValue("@LastModified", file.Value);

                            var rows = upsert.ExecuteNonQuery();
                            Debug.Assert(rows == 1);
                        }
                    }

                    // Might as well do this now too
                    using (var numSentStatusesCmd = db.CreateCommand())
                    {
                        numSentStatusesCmd.Transaction = trans;
                        numSentStatusesCmd.CommandText = "SELECT COUNT(1) FROM Incidents WHERE SharedOnBuffer = 1";
                        int numSentStatuses = Convert.ToInt32(numSentStatusesCmd.ExecuteScalar());

                        if (numSentStatuses == 0)
                        {
                            // Probably a freshly loaded database
                            // Avoid sending thousands of tweets for old incidents
                            Console.WriteLine("Setting SharedOnBuffer = 1 for all Incidents");
                            using (var update = db.CreateCommand())
                            {
                                update.Transaction = trans;
                                update.CommandText = "UPDATE Incidents SET SharedOnBuffer = 1";
                                update.ExecuteNonQuery();
                            }
                        }
                    }

                    trans.Commit();
                }
            }
        }

        private static void Post()
        {
            using (var db = new SqlConnection(CloudConfigurationManager.GetSetting("DatabaseConnectionString")))
            {
                db.Open();

                using (var getToShare = db.CreateCommand())
                {
                    getToShare.CommandText = "SELECT TOP 10 Id, CaseNumber, InternalReferenceNumber, Type, Location, Reported, OccurredStart, OccurredStart, Disposition, Notes, Location, Latitude, Longitude, FacebookPageId " +
                        "FROM IncidentsView WHERE SharedOnBuffer = 0 AND CaseNumber IS NOT NULL AND InternalReferenceNumber IS NOT NULL ORDER BY Reported ASC";

                    using (var reader = getToShare.ExecuteReader())
                    {
                        var results = new List<Dictionary<string, string>>();
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
                                data["profile_ids[]"] = CloudConfigurationManager.GetSetting("BufferTwitterId");
                                data["shorten"] = "false";

                                web.Headers.Add("Authorization", "Bearer " + CloudConfigurationManager.GetSetting("OAuthAccessToken"));
                                web.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
                                web.Headers.Add("User-Agent", "UTDScanner/1.0 (+http://utdscanner.com)");
                                try
                                {
                                    var response = web.UploadValues("https://api.bufferapp.com/1/updates/create.json", "POST", data);
                                    var json = Encoding.UTF8.GetString(response);
                                    if (Regex.IsMatch(json, @"success["" :]*true")) // lol
                                    {
                                        using (var db2 = new SqlConnection(CloudConfigurationManager.GetSetting("DatabaseConnectionString")))
                                        {
                                            using (var update = db2.CreateCommand())
                                            {
                                                update.CommandText = "UPDATE Incidents SET SharedOnBuffer = 1 WHERE Id = @id";
                                                update.Parameters.AddWithValue("id", reader["Id"]);
                                                var rows = update.ExecuteNonQuery();
                                                Debug.Assert(rows == 1);
                                            }
                                        }
                                    }
                                }
                                catch (WebException ex)
                                {
                                    if (ex.Message.Contains("400"))
                                    {
                                        Console.WriteLine("Reached Buffer limit? " + ex.Message);
                                        break;
                                    }
                                }

                            }
                        }
                    }
                }
            }
        }
    }
}

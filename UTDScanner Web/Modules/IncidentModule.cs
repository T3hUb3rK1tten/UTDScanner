using Nancy;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Web;
using UTDScanner_Web.Models;

namespace UTDScanner_Web.Modules
{
    public class IncidentModule : NancyModule
    {
        public IncidentModule()
        {
            Get["/incidents"] = Index;
            Get["/case/{casenumber}"] = ByCaseNumber;
            Get["/ref/{reference}"] = ByInternalReferenceNumber;
        }

        public dynamic Index(dynamic _)
        {
            var incidents = new List<IncidentModel>();
            using (var db = new SqlConnection(ConfigurationManager.AppSettings["DatabaseConnectionString"]))
            {
                db.Open();
                using (var cmd = db.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM IncidentsView ORDER BY Reported DESC";
                    var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        incidents.Add(reader.GetIncidentModel());
                    }
                }
            }
            return View["Index", incidents];
        }

        public dynamic ByCaseNumber(dynamic _)
        {
            using (var db = new SqlConnection(ConfigurationManager.AppSettings["DatabaseConnectionString"]))
            {
                db.Open();
                using (var cmd = db.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM IncidentsView WHERE CaseNumber=@CaseNumber";
                    cmd.Parameters.AddWithValue("@CaseNumber", (string)_.casenumber);
                    var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        return View[reader.GetIncidentModel()];
                    }
                    else
                    {
                        return HttpStatusCode.NotFound;
                    }
                }
            }
        }

        public dynamic ByInternalReferenceNumber(dynamic _)
        {
            using (var db = new SqlConnection(ConfigurationManager.AppSettings["DatabaseConnectionString"]))
            {
                db.Open();
                using (var cmd = db.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM IncidentsView WHERE InternalReferenceNumber=@Reference";
                    cmd.Parameters.AddWithValue("@Reference", _.reference);
                    var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        return View[reader.GetIncidentModel()];
                    }
                    else
                    {
                        return HttpStatusCode.NotFound;
                    }
                }
            }
        }

    }
}
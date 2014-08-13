using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Web;

namespace UTDScanner_Web.Models
{
    public class IncidentModel
    {
        public int Id { get; set; }
        public string CaseNumber { get; set; }
        public string InternalReferenceNumber { get; set; }
        public string Type { get; set; }
        public DateTime? Reported { get; set; }
        public DateTime? OccurredStart { get; set; }
        public DateTime? OccurredStop { get; set; }
        public string Disposition { get; set; }
        public string Notes { get; set; }
        public string Location { get; set; }
        public bool SharedOnBuffer { get; set; }
        public string Latitude { get; set; }
        public string Longitude { get; set; }
        public int FacebookPageId { get; set; }
    }

    public static partial class Extensions
    {
        public static IncidentModel GetIncidentModel(this IDataReader reader)
        {
            var model = new IncidentModel
            {
                Id = Convert.ToInt32(reader["Id"]),
                CaseNumber = Convert.ToString(reader["CaseNumber"]),
                InternalReferenceNumber = Convert.ToString(reader["InternalReferenceNumber"]),
                Type = Convert.ToString(reader["Type"]),
                Disposition = Convert.ToString(reader["Disposition"]),
                Notes = Convert.ToString(reader["Notes"]),
                Location = Convert.ToString(reader["Location"]),
                SharedOnBuffer = Convert.ToBoolean(reader["SharedOnBuffer"]),
                Latitude = Convert.ToString(reader["Latitude"]),
                Longitude = Convert.ToString(reader["Longitude"]),
            };
            
            if (reader["Reported"] != DBNull.Value)
            {
                model.Reported = Convert.ToDateTime(reader["Reported"]);
            }
            
            if (reader["OccurredStop"] != DBNull.Value)
            {
                model.OccurredStop = Convert.ToDateTime(reader["OccurredStop"]);
            }
            
            if (reader["OccurredStart"] != DBNull.Value)
            {
                model.OccurredStart = Convert.ToDateTime(reader["OccurredStart"]);
            }

            int facebookpageid;
            if (Int32.TryParse(Convert.ToString(reader["FacebookPageId"]), out facebookpageid))
            {
                model.FacebookPageId = facebookpageid;
            }
            return model;
        }
    }

}
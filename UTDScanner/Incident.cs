using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UTDScanner
{
    class Incident
    {
        public String Type { get; set; }
        public String Location { get; set; }
        public DateTime Reported { get; set; }
        public DateTime OccurredStart { get; set; }
        public DateTime OccurredStop { get; set; }
        public String CaseNumber { get; set; }
        public String InternalReferenceNumber { get; set; }
        public String Disposition { get; set; }
        public String Notes { get; set; }
    }
}

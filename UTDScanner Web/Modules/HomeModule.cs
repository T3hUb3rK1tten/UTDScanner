﻿using Nancy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace UTDScanner_Web.Modules
{
    public class HomeModule : NancyModule
    {
        public HomeModule()
        {
            Get["/"] = _ => View["Index"];
        }
    }
}
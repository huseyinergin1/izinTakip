using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace yillikizin.Models
{
    public class GecGelenPersonelModel
    {
        public string adi { get; set; }
        public string soyadi { get; set; }
        public string kartno { get; set; }
        public TimeSpan? Saat { get; set; } // Make Saat nullable
    }
}
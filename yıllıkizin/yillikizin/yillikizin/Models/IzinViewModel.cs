using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace yillikizin.Models
{
    public class IzinViewModel
    {
        public List<Izin> CurrentIzinList { get; set; }
        public List<Izin> PastIzinList { get; set; }
    }
}
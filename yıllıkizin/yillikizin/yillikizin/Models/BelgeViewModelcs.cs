using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace yillikizin.Models
{
    public class BelgeViewModel
    {
        public IEnumerable<personel> Personeller { get; set; }
        public IEnumerable<belge> Belgeler { get; set; }
    }

}
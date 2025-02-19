using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace yillikizin.Models
{
    public class IcmalViewModel
    {
        public int PersonelId { get; set; }
        public string PersonelAd { get; set; }
        public string PersonelSoyad { get; set; }
        public List<IcmalDay> Days { get; set; } = new List<IcmalDay>();
        public double TotalWorkingHours { get; set; }
        public double TotalOvertimeHours { get; set; }
    }

    public class IcmalDay
    {
        public DateTime Tarih { get; set; }
        public string GirisSaat { get; set; }
        public string CikisSaat { get; set; }
        public string CalismaSuresi { get; set; }
        public string Durum { get; set; } // "Devamsız" or other status
    }

}
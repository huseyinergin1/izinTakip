using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace yillikizin.Models
{
    public class DevamsizViewModel
    {
        public int PersonelId { get; set; }
        public string Adi { get; set; }
        public string Soyadi { get; set; }
        public string KartNo { get; set; }
        public string Departman { get; set; }
        public DateTime Tarih { get; set; }
        public string Bilgi { get; set; }
    }
}
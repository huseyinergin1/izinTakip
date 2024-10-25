using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using yillikizin.Models;
// Hareket.cs
namespace yillikizin.Models {
    public class Hareket
    {
        public DateTime Tarih { get; set; }
        public TimeSpan Saat { get; set; }
        public string TerminalNo { get; set; }
        public int PersonelId { get; set; } // İlişkilendirmek için personel ID
        public int kartno { get; set; } // Kart numarası string olarak tanımlandı
        public string IslemTipi { get; set; }
    }

}
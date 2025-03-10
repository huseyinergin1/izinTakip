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
        public string kartno { get; set; } // Kart numarası string olarak tanımlandı
        public string IslemTipi { get; set; }
        public string Bilgi { get; set; }
        public int id { get; set; }
        public string personelAdi { get; set; }
        public string personelSoyadi { get; set; }
        public string departman { get; set; }
        // Yeni eklenen özellik
        public Vardiya Vardiya { get; set; }
        // Giriş ve Çıkış saatlerini listeler olarak ekliyoruz
        public List<TimeSpan> GirişSaatleri { get; set; }
        public List<TimeSpan> ÇıkışSaatleri { get; set; }

        // Yeni özellik
        public TimeSpan CalismaBaslangic { get; set; }

        // Yeni eklenen özellik
        public string GirişDurumu { get; set; }
        public string ÇıkışDurumu { get; set; }
        public string Renk { get; set; } // Bu özelliği ekledik
        public string Degerlendirme { get; set; }
        public List<TimeSpan> NCS { get; set; } // Normal Çalışma Süreleri
        public List<TimeSpan> DVS { get; set; }
        public List<TimeSpan> Eksik { get; set; }
        public List<TimeSpan> Mesai { get; set; }
        public virtual personel Personel { get; set; }
        public TimeSpan CalismaSuresi { get; set; }
        public TimeSpan FazlaMesai { get; set; }
        public TimeSpan FazlaMesai1 { get; set; }
        public TimeSpan FazlaMesai3 { get; set; }
        public TimeSpan EksikSure { get; set; }
        public TimeSpan MCS { get; set; }
    }

}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace yillikizin.Models
{
    public class HareketRaporu
    {
        // Personel bilgileri
        public int PersonelId { get; set; }
        public string kartno { get; set; }
        public string PersonelAdi { get; set; }
        public string PersonelSoyadi { get; set; }

        // Hareket bilgileri
        public DateTime Tarih { get; set; }
        public string IslemTipi { get; set; }  // Giriş/Çıkış
        public TimeSpan Saat { get; set; }
        public int TerminalNo { get; set; }

        // Giriş ve çıkış saatleri listesi
        public List<TimeSpan> GirişSaatleri { get; set; }
        public List<TimeSpan> ÇıkışSaatleri { get; set; }

        // Toplam çalışma süresi
        public TimeSpan ToplamCalismaSuresi
        {
            get
            {
                if (GirişSaatleri.Count > 0 && ÇıkışSaatleri.Count > 0)
                {
                    // En son giriş saati ile çıkış saati arasındaki farkı alalım
                    var giriş = GirişSaatleri.Last();
                    var çıkış = ÇıkışSaatleri.Last();
                    return çıkış - giriş;
                }
                return TimeSpan.Zero;
            }
        }

        // Giriş/Çıkış sayıları
        public int GirişSayisi => GirişSaatleri.Count;
        public int ÇıkışSayisi => ÇıkışSaatleri.Count;

        // Geçerli izin durumu veya absente durumu
        public bool IzinliMi { get; set; }
        public bool DevamsizlikDurumu { get; set; }

        // Constructor
        public HareketRaporu()
        {
            GirişSaatleri = new List<TimeSpan>();
            ÇıkışSaatleri = new List<TimeSpan>();
        }
    }
}
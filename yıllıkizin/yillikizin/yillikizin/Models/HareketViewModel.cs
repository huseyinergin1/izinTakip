﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using yillikizin.Models;

namespace yillikizin.Models
{
    // HareketViewModel.cs
    public class HareketViewModel
    {
        public List<Vardiya> VardiyaListesi { get; set; }
        public List<personel> PersonelListesi { get; set; }
        public List<Hareket> HareketListesi { get; set; }
        public List<Izin> IzinListesi { get; set; }
        public int? SelectedPersonelId { get; set; }  // Seçilen personel ID
        public int? SelectedPersonelKartNo { get; set; } // Seçilen personelin kart no'su, int türünde
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<DateTime> DaysList { get; set; }
        public int? PersonelId { get; set; }  // PersonelId eklenmiş oldu
        public DateTime SelectedMonth { get; set; }  // Nullable yerine normal DateTime

        public string GirişDurumu { get; set; }
    }


}
